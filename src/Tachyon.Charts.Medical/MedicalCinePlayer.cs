using Tachyon.Charts.Core.Input;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>Steps through the orthogonal slices of a medical MPR display and plays them at a chosen rate.</summary>
public sealed class MedicalCinePlayer
{
    private const double MaximumFramesPerSecond = 120;

    private readonly MedicalMprDisplay _display;
    private readonly object _gate = new();
    private readonly ChartInteraction?[] _interactions = new ChartInteraction?[3];
    private readonly Action _advance;
    private Timer? _timer;
    private double _framesPerSecond = 24;
    private int _isPlaying;
    private int _plane;
    private int _pending;
    private long _version;
    private string? _statusText;

    internal MedicalCinePlayer(MedicalMprDisplay display)
    {
        _display = display;
        _advance = AdvanceOnChart;
    }

    /// <summary>Occurs after the active plane, speed or playback state changes.</summary>
    public event Action? StateChanged;

    /// <summary>Gets the plane selected for playback and keyboard control.</summary>
    public MedicalMprPlane Plane => (MedicalMprPlane)Volatile.Read(ref _plane);

    /// <summary>Gets or sets the playback speed, greater than zero and no more than 120 frames per second.</summary>
    public double FramesPerSecond
    {
        get => Volatile.Read(ref _framesPerSecond);
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value > MaximumFramesPerSecond)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The speed must be finite, positive and no greater than 120 frames per second.");
            }

            if (Volatile.Read(ref _framesPerSecond) == value)
            {
                return;
            }

            Volatile.Write(ref _framesPerSecond, value);
            lock (_gate)
            {
                if (_timer is not null)
                {
                    _timer.Change(Period, Period);
                }
            }

            Changed();
        }
    }

    /// <summary>Gets a value indicating whether automatic playback is active.</summary>
    public bool IsPlaying => Volatile.Read(ref _isPlaying) != 0;

    internal long Version => Interlocked.Read(ref _version);

    internal string? StatusText => Volatile.Read(ref _statusText);

    private TimeSpan Period => TimeSpan.FromSeconds(1 / FramesPerSecond);

    /// <summary>Moves a plane by a number of slices and stops at the first or last slice.</summary>
    /// <param name="plane">The plane to move.</param>
    /// <param name="slices">The signed number of slices.</param>
    public void Step(MedicalMprPlane plane, int slices)
    {
        Validate(plane);
        var changed = SetPlane(plane);
        if (slices == 0)
        {
            if (changed)
            {
                Changed();
            }

            return;
        }

        var (index, count) = IndexAndCount(plane);
        var target = (int)Math.Clamp((long)index + slices, 0, count - 1L);
        SetIndex(plane, target);
        if (changed)
        {
            Changed();
        }
    }

    /// <summary>Starts cyclic playback of one orthogonal plane.</summary>
    /// <param name="plane">The plane to play.</param>
    public void Play(MedicalMprPlane plane)
    {
        Validate(plane);
        var changed = SetPlane(plane);
        if (Interlocked.Exchange(ref _isPlaying, 1) == 0)
        {
            changed = true;
        }

        lock (_gate)
        {
            StartTimerIfAttached();
        }

        if (changed)
        {
            Changed();
        }
    }

    /// <summary>Stops automatic playback.</summary>
    public void Pause()
    {
        if (Interlocked.Exchange(ref _isPlaying, 0) == 0)
        {
            return;
        }

        lock (_gate)
        {
            StopTimer();
        }

        Changed();
    }

    /// <summary>Starts playback of a plane, or pauses when that plane is already playing.</summary>
    /// <param name="plane">The plane to toggle.</param>
    public void Toggle(MedicalMprPlane plane)
    {
        Validate(plane);
        if (IsPlaying && Plane == plane)
        {
            Pause();
        }
        else
        {
            Play(plane);
        }
    }

    internal void ShowBoundary(MedicalMprPlane plane, bool last)
    {
        Validate(plane);
        var changed = SetPlane(plane);
        var (_, count) = IndexAndCount(plane);
        SetIndex(plane, last ? count - 1 : 0);
        if (changed)
        {
            Changed();
        }
    }

    internal void AdvanceFrame()
    {
        var plane = Plane;
        var (index, count) = IndexAndCount(plane);
        SetIndex(plane, index + 1 == count ? 0 : index + 1);
    }

    internal void Attach(MedicalMprPlane plane, ChartInteraction interaction)
    {
        lock (_gate)
        {
            _interactions[(int)plane] = interaction;
            StartTimerIfAttached();
        }
    }

    internal void Detach(MedicalMprPlane plane, ChartInteraction interaction)
    {
        var pauses = false;
        lock (_gate)
        {
            if (ReferenceEquals(_interactions[(int)plane], interaction))
            {
                _interactions[(int)plane] = null;
            }

            if (!HasInteraction())
            {
                StopTimer();
                pauses = Interlocked.Exchange(ref _isPlaying, 0) != 0;
            }
        }

        if (pauses)
        {
            Changed();
        }
    }

    private void OnTimer(object? state)
    {
        if (!IsPlaying || Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
        {
            return;
        }

        ChartInteraction? interaction;
        lock (_gate)
        {
            interaction = _interactions[(int)Plane] ?? FirstInteraction();
        }

        if (interaction is null)
        {
            Volatile.Write(ref _pending, 0);
            return;
        }

        interaction.Dispatch(_advance);
    }

    private void AdvanceOnChart()
    {
        try
        {
            if (IsPlaying)
            {
                AdvanceFrame();
            }
        }
        finally
        {
            Volatile.Write(ref _pending, 0);
        }
    }

    private bool SetPlane(MedicalMprPlane plane)
    {
        if (Plane == plane)
        {
            return false;
        }

        Volatile.Write(ref _plane, (int)plane);
        return true;
    }

    private (int Index, int Count) IndexAndCount(MedicalMprPlane plane)
    {
        var position = _display.Position;
        var source = _display.Source;
        return plane switch
        {
            MedicalMprPlane.Axial => (Index(position.Z, source.VoxelSpacing.Z, source.Data.Depth), source.Data.Depth),
            MedicalMprPlane.Coronal => (Index(position.Y, source.VoxelSpacing.Y, source.Data.Height), source.Data.Height),
            MedicalMprPlane.Sagittal => (Index(position.X, source.VoxelSpacing.X, source.Data.Width), source.Data.Width),
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, null),
        };
    }

    private void SetIndex(MedicalMprPlane plane, int index)
    {
        var position = _display.Position;
        var spacing = _display.Source.VoxelSpacing;
        _display.SetPosition(plane switch
        {
            MedicalMprPlane.Axial => position with { Z = index * spacing.Z },
            MedicalMprPlane.Coronal => position with { Y = index * spacing.Y },
            MedicalMprPlane.Sagittal => position with { X = index * spacing.X },
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, null),
        });
    }

    private void StartTimerIfAttached()
    {
        if (!IsPlaying || !HasInteraction())
        {
            return;
        }

        _timer ??= new Timer(OnTimer);
        _timer.Change(Period, Period);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
        Volatile.Write(ref _pending, 0);
    }

    private bool HasInteraction() => _interactions[0] is not null || _interactions[1] is not null || _interactions[2] is not null;

    private ChartInteraction? FirstInteraction() => _interactions[0] ?? _interactions[1] ?? _interactions[2];

    private void Changed()
    {
        Volatile.Write(ref _statusText, IsPlaying ? FormattableString.Invariant($"▶ {FramesPerSecond:0.#} fps") : null);
        Interlocked.Increment(ref _version);
        StateChanged?.Invoke();
    }

    private static int Index(double position, double spacing, int count) =>
        Math.Clamp((int)Math.Floor((position / spacing) + 0.5), 0, count - 1);

    private static void Validate(MedicalMprPlane plane)
    {
        if (!Enum.IsDefined(plane))
        {
            throw new ArgumentOutOfRangeException(nameof(plane), plane, null);
        }
    }
}
