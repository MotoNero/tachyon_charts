using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>One orthogonal image of a <see cref="MedicalMprDisplay"/>, ready to configure a normal 2D chart.</summary>
public sealed class MedicalMprSlice
{
    private readonly MedicalMprDisplay _display;
    private readonly MedicalMprSliceBuffer _buffer;
    private readonly HeatmapData _scaleData = new(1, 1);
    private readonly string[] _sliceTexts;
    private MedicalMeasurementLayer? _measurements;

    /// <summary>Gets the distances, angles and regions measured on this plane, each on its own slice. Regions of one name on several slices form a volume.</summary>
    public MedicalMeasurementLayer Measurements => _measurements ??= new MedicalMeasurementLayer(new MeasurementSource(_buffer, _display));

    internal MedicalMprSlice(MedicalMprDisplay display, MedicalMprSliceBuffer buffer)
    {
        _display = display;
        _buffer = buffer;
        OrientationMarks = MedicalImageDisplay.Marks(buffer.Orientation);
        _sliceTexts = CreateSliceTexts(display, buffer.Plane);
    }

    /// <summary>Gets the direction of this slice.</summary>
    public MedicalMprPlane Plane => _buffer.Plane;

    /// <summary>Gets the number of image columns.</summary>
    public int Width => _buffer.Width;

    /// <summary>Gets the number of image rows.</summary>
    public int Height => _buffer.Height;

    /// <summary>Gets the pixel spacing shown by this slice.</summary>
    public ImageSpacing Spacing => _buffer.Spacing;

    /// <summary>Gets the patient-space orientation of this slice.</summary>
    public PatientOrientation Orientation => _buffer.Orientation;

    /// <summary>Gets the letters shown at the four image edges.</summary>
    public MedicalOrientationMarks OrientationMarks { get; }

    /// <summary>Adds the slice and its linked interactions to a 2D chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure(ChartBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var window = _display.Window;
        chart.Title(Plane.ToString())
            .XAxis(axis => axis.Numeric("medical-mpr-x").Title("mm").Range(0, Width * Spacing.Column).Limits(0, Width * Spacing.Column))
            .YAxis(axis => axis.Numeric("medical-mpr-y").Title("mm").Range(0, Height * Spacing.Row).Limits(0, Height * Spacing.Row).Inverted())
            .AddHeatmap(_scaleData, series => series
                .ColorAxis(axis => axis.Colormap(Colormaps.Grayscale).Range(window.Minimum, window.Maximum).Title(_display.Unit)))
            .AddSeries((x, y) => new MedicalMprSeries(_display, _buffer, x, y))
            .AddSeries((x, y) => new MedicalSegmentationSeries(_display, this, x, y))
            .AddOverlay(surface => new MedicalMprOverlay(this, surface.HorizontalAxes[0], surface.Panes[0].Axes[0]))
            .AddOverlay(Measurements.Attach)
            .Modifiers(modifiers => modifiers
                .Custom(() => new MedicalMeasurementModifier(Measurements))
                .Custom(() => new MedicalMprModifier(this))
                .Custom(() => new MedicalColorBarModifier(() => _display.Window, _display.ApplyPreset, _display.InitialSetting))
                .Custom(() => new ZoomPanModifier { PanButton = PointerButton.Middle }));
    }

    /// <summary>Copies the values of the slice shown now, row by row from the top left, for example to export or analyse them.</summary>
    /// <param name="target">Receives <see cref="Width"/> × <see cref="Height"/> values.</param>
    /// <exception cref="ArgumentException">The target is too short.</exception>
    public void CopyValues(Span<float> target)
    {
        if (target.Length < Width * Height)
        {
            throw new ArgumentException("The target is shorter than the slice.", nameof(target));
        }

        for (var row = 0; row < Height; row++)
        {
            for (var column = 0; column < Width; column++)
            {
                target[(row * Width) + column] = _buffer.Read(column, row);
            }
        }
    }

    internal void Update(Point3D position) => _buffer.Update(position);

    internal MedicalMprSliceBuffer Buffer => _buffer;

    internal MedicalMprDisplay Display => _display;

    // The slices of the plane, read straight from the volume.
    private sealed class MeasurementSource : IMedicalMeasurementSource
    {
        private readonly MedicalMprSliceBuffer _buffer;
        private readonly MedicalMprDisplay _display;

        public MeasurementSource(MedicalMprSliceBuffer buffer, MedicalMprDisplay display)
        {
            (_buffer, _display) = (buffer, display);
            display.PositionChanged += _ => SliceChanged?.Invoke();
        }

        public int Width => _buffer.Width;

        public int Height => _buffer.Height;

        public ImageSpacing Spacing => _buffer.Spacing;

        public int Slice => Math.Max(_buffer.FixedIndex, 0);

        public int SliceCount => _buffer.SliceCount;

        public double SliceDistance => _buffer.SliceDistance;

        public string Unit => _display.Unit;

        public double Read(int slice, int column, int row) => _buffer.Read(slice, column, row);

        public Core.Numerics.DoubleRange ValueRange => _display.Source.Data.ValueRange;

        public MedicalWindow Window => _display.Window;

        public event Action<MedicalWindow>? WindowChanged
        {
            add => _display.WindowChanged += value;
            remove => _display.WindowChanged -= value;
        }

        // The slice of a plane follows the position of the whole display.
        public event Action? SliceChanged;
    }

    internal int FixedIndex => _buffer.FixedIndex;

    internal float Read(int column, int row) => _buffer.Read(column, row);

    internal string SliceText => _sliceTexts[_buffer.FixedIndex];

    internal Point3D ToVolumePosition(double x, double y) => _buffer.ToVolumePosition(x, y, _display.Position);

    internal (double X, double Y) FromVolumePosition(Point3D position) => _buffer.FromVolumePosition(position);

    private sealed class MedicalMprModifier : ChartModifier
    {
        private const float ClickReach = 4;

        private readonly MedicalMprSlice _slice;
        private readonly Action _applyPendingWindow;
        private readonly Action _faster;
        private readonly Action _first;
        private readonly Action _invalidate;
        private readonly Action _lastSlice;
        private readonly Action _slower;
        private readonly Action _stepBackward;
        private readonly Action _stepForward;
        private readonly Action _toggle;
        private ColorAxis? _axis;
        private MedicalWindow _pendingWindow;
        private MedicalWindow _startWindow;
        private Vector2 _start;
        private Vector2 _last;
        private long _pointerId;
        private bool _isDragging;
        private bool _isClick;

        public MedicalMprModifier(MedicalMprSlice slice)
            : base(50)
        {
            _slice = slice;
            _applyPendingWindow = ApplyPendingWindow;
            _faster = Faster;
            _first = First;
            _invalidate = Invalidate;
            _lastSlice = Last;
            _slower = Slower;
            _stepBackward = StepBackward;
            _stepForward = StepForward;
            _toggle = Toggle;
        }

        protected override void OnAttached(ChartInteraction interaction)
        {
            _axis = interaction.Chart.Panes.SelectMany(pane => pane.Axes).OfType<ColorAxis>().Single();
            _slice._display.WindowChanged += OnWindowChanged;
            _slice._display.PositionChanged += OnPositionChanged;
            _slice._display.Cine.StateChanged += OnCineChanged;
            _slice._display.SegmentationChanged += OnCineChanged;
            _slice._display.Cine.Attach(_slice.Plane, interaction);
        }

        protected override void OnDetached(ChartInteraction interaction)
        {
            _slice._display.WindowChanged -= OnWindowChanged;
            _slice._display.PositionChanged -= OnPositionChanged;
            _slice._display.Cine.StateChanged -= OnCineChanged;
            _slice._display.SegmentationChanged -= OnCineChanged;
            _slice._display.Cine.Detach(_slice.Plane, interaction);
            _axis = null;
        }

        protected override ModifierResult OnPointer(in PointerInputEvent input)
        {
            if (!_isDragging)
            {
                if (input.Kind != PointerEventKind.Pressed
                    || input.ChangedButton != PointerButton.Primary
                    || input.Modifiers != InputModifiers.None
                    || Interaction is not { } interaction
                    || !interaction.TryHitPane(input.Position, out _))
                {
                    return ModifierResult.Ignored;
                }

                _isDragging = true;
                _isClick = true;
                _pointerId = input.PointerId;
                _start = input.Position;
                _last = input.Position;
                _startWindow = _slice._display.Window;
                return ModifierResult.Capture;
            }

            if (input.PointerId != _pointerId)
            {
                return ModifierResult.Ignored;
            }

            if (input.Kind == PointerEventKind.Moved)
            {
                _last = input.Position;
                _isClick &= Vector2.Distance(_start, input.Position) <= ClickReach;
                if (!_isClick)
                {
                    var scale = Math.Max(_startWindow.Width / 256, 0.01);
                    var width = Math.Max(1e-6, _startWindow.Width + ((input.Position.X - _start.X) * scale));
                    var center = _startWindow.Center - ((input.Position.Y - _start.Y) * scale);
                    _slice._display.SetWindow(center, width);
                }

                return ModifierResult.Handled;
            }

            if (input.Kind is PointerEventKind.Released or PointerEventKind.Canceled)
            {
                _isDragging = false;
                if (_isClick && input.Kind == PointerEventKind.Released)
                {
                    SetPosition(_last);
                }

                return ModifierResult.Release;
            }

            return ModifierResult.Handled;
        }

        protected override ModifierResult OnKey(in KeyInputEvent input)
        {
            if (input.Kind != KeyEventKind.Pressed || input.Modifiers != InputModifiers.None)
            {
                return ModifierResult.Ignored;
            }

            var preset = input.Key switch
            {
                ChartKey.D1 => CtWindowPresets.Brain,
                ChartKey.D2 => CtWindowPresets.Bone,
                ChartKey.D3 => CtWindowPresets.Lung,
                _ => default,
            };
            if (preset.Width == 0)
            {
                var action = input.Key switch
                {
                    ChartKey.Up or ChartKey.Right or ChartKey.PageUp => _stepForward,
                    ChartKey.Down or ChartKey.Left or ChartKey.PageDown => _stepBackward,
                    ChartKey.Home => _first,
                    ChartKey.End => _lastSlice,
                    ChartKey.Space => _toggle,
                    ChartKey.Add => _faster,
                    ChartKey.Subtract => _slower,
                    _ => null,
                };
                if (action is null || Interaction is not { } interaction)
                {
                    return ModifierResult.Ignored;
                }

                interaction.Dispatch(action);
                return ModifierResult.Handled;
            }

            _slice._display.ApplyPreset(preset);
            return ModifierResult.Handled;
        }

        protected override ModifierResult OnWheel(in WheelInputEvent input)
        {
            if (input.Modifiers != InputModifiers.None
                || input.Delta.Y == 0
                || Interaction is not { } interaction
                || !interaction.TryHitPane(input.Position, out _))
            {
                return ModifierResult.Ignored;
            }

            interaction.Dispatch(input.Delta.Y > 0 ? _stepForward : _stepBackward);
            return ModifierResult.Handled;
        }

        protected override void OnCaptureLost(InputCapture capture) => _isDragging = false;

        private void SetPosition(Vector2 pointer)
        {
            if (Interaction?.Chart is not { } chart
                || !chart.TryHitTest(pointer, double.MaxValue, HitTestMode.Nearest, out var hit))
            {
                return;
            }

            _slice._display.SetPosition(_slice.ToVolumePosition(hit.X, hit.Y));
        }

        private void OnWindowChanged(MedicalWindow window)
        {
            if (Interaction is not { } interaction)
            {
                return;
            }

            _pendingWindow = window;
            interaction.Dispatch(_applyPendingWindow);
        }

        private void OnPositionChanged(Point3D position) => Interaction?.Dispatch(_invalidate);

        private void OnCineChanged() => Interaction?.Dispatch(_invalidate);

        private void ApplyPendingWindow()
        {
            if (_axis is { } axis)
            {
                axis.VisibleRange = new DoubleRange(_pendingWindow.Minimum, _pendingWindow.Maximum);
            }
        }

        private void Invalidate() => Interaction?.Chart.Invalidate();

        private void StepForward()
        {
            _slice._display.Cine.Pause();
            _slice._display.Cine.Step(_slice.Plane, 1);
        }

        private void StepBackward()
        {
            _slice._display.Cine.Pause();
            _slice._display.Cine.Step(_slice.Plane, -1);
        }

        private void First()
        {
            _slice._display.Cine.Pause();
            _slice._display.Cine.ShowBoundary(_slice.Plane, last: false);
        }

        private void Last()
        {
            _slice._display.Cine.Pause();
            _slice._display.Cine.ShowBoundary(_slice.Plane, last: true);
        }

        private void Toggle() => _slice._display.Cine.Toggle(_slice.Plane);

        private void Faster() => _slice._display.Cine.FramesPerSecond = Math.Min(_slice._display.Cine.FramesPerSecond * 1.25, 120);

        private void Slower() => _slice._display.Cine.FramesPerSecond = Math.Max(_slice._display.Cine.FramesPerSecond / 1.25, 0.5);
    }

    private sealed class MedicalMprOverlay(MedicalMprSlice slice, AxisBase xAxis, AxisBase yAxis) : ChartOverlay
    {
        protected override long ExternalVersion => slice._display.PositionVersion + slice._display.Cine.Version;

        protected override void Draw(OverlayCanvas canvas)
        {
            var plot = canvas.Layout.PlotArea;
            var (x, y) = slice.FromVolumePosition(slice._display.Position);
            var color = new Rgba8(255, 160, 50, 220);
            var label = canvas.DrawLabel(slice.SliceText, plot.X + 6, plot.Y + 6, new Rgba8(12, 16, 24, 220), new Rgba8(235, 240, 248, 255));
            if (slice._display.Cine.Plane == slice.Plane && slice._display.Cine.StatusText is { } status)
            {
                canvas.DrawLabel(status, plot.X + 6, label.Bottom + 3, new Rgba8(12, 16, 24, 220), color);
            }

            if (canvas.TryGetPosition(xAxis, x, out var pixelX))
            {
                canvas.DrawLine(new Vector2((float)pixelX, (float)plot.Y), new Vector2((float)pixelX, (float)plot.Bottom), 1, color);
            }

            if (canvas.TryGetPosition(yAxis, y, out var pixelY))
            {
                canvas.DrawLine(new Vector2((float)plot.X, (float)pixelY), new Vector2((float)plot.Right, (float)pixelY), 1, color);
            }

            const float size = 15;
            var height = canvas.GetLineHeight(size);
            DrawCentered(canvas, slice.OrientationMarks.Top, plot.X + (plot.Width / 2), plot.Y + height + 5, size);
            DrawCentered(canvas, slice.OrientationMarks.Bottom, plot.X + (plot.Width / 2), plot.Bottom - 6, size);
            DrawCentered(canvas, slice.OrientationMarks.Left, plot.X + 12, plot.Y + (plot.Height / 2), size);
            DrawCentered(canvas, slice.OrientationMarks.Right, plot.Right - 12, plot.Y + (plot.Height / 2), size);
        }

        private static void DrawCentered(OverlayCanvas canvas, string text, double x, double baseline, float size) =>
            canvas.DrawText(text, x - (canvas.MeasureWidth(text, size) / 2), baseline, size, new Rgba8(255, 220, 90, 255));
    }

    private static string[] CreateSliceTexts(MedicalMprDisplay display, MedicalMprPlane plane)
    {
        var source = display.Source;
        var (count, spacing) = plane switch
        {
            MedicalMprPlane.Axial => (source.Data.Depth, source.VoxelSpacing.Z),
            MedicalMprPlane.Coronal => (source.Data.Height, source.VoxelSpacing.Y),
            MedicalMprPlane.Sagittal => (source.Data.Width, source.VoxelSpacing.X),
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, null),
        };
        var texts = new string[count];
        for (var index = 0; index < count; index++)
        {
            texts[index] = string.Create(CultureInfo.InvariantCulture, $"{index + 1} / {count} · {index * spacing:0.###} mm");
        }

        return texts;
    }
}
