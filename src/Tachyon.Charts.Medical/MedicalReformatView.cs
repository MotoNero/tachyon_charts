using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// The 2D chart of an oblique or curved section: the grayscale picture with the shared window of the MPR display,
/// window and level by dragging, and the wheel for what the section does with it.
/// </summary>
internal sealed class MedicalReformatView
{
    private readonly HeatmapData _scaleData = new(1, 1);

    public MedicalReformatView(MedicalMprDisplay display, MedicalReformatBuffer buffer, string title, Action<int>? wheel, Func<ChartKey, bool>? key = null)
    {
        (Display, Buffer, Title, Wheel, Key) = (display, buffer, title, wheel, key);
    }

    /// <summary>Occurs on the UI thread when the picture changed.</summary>
    public event Action? Changed;

    public MedicalMprDisplay Display { get; }

    public MedicalReformatBuffer Buffer { get; }

    public string Title { get; }

    public Action<int>? Wheel { get; }

    public Func<ChartKey, bool>? Key { get; }

    /// <summary>Gets the side of a square picture that holds the whole volume in any direction, and its pixel size.</summary>
    /// <param name="volume">The volume.</param>
    /// <param name="length">The length to cover in millimetres.</param>
    /// <returns>The number of pixels and their size.</returns>
    public static (int Count, double Spacing) Fit(MedicalVolume volume, double length)
    {
        const int MostPixels = 1024;
        var spacing = Math.Min(volume.VoxelSpacing.X, Math.Min(volume.VoxelSpacing.Y, volume.VoxelSpacing.Z));
        spacing = Math.Max(spacing, length / MostPixels);
        return (Math.Max(2, (int)Math.Ceiling(length / spacing)), spacing);
    }

    /// <summary>Gets the size of the volume in millimetres, from the middle of the first voxel to that of the last.</summary>
    /// <param name="volume">The volume.</param>
    /// <returns>The size.</returns>
    public static Point3D Extent(MedicalVolume volume) => new(
        (volume.Data.Width - 1) * volume.VoxelSpacing.X,
        (volume.Data.Height - 1) * volume.VoxelSpacing.Y,
        (volume.Data.Depth - 1) * volume.VoxelSpacing.Z);

    public void RaiseChanged() => Changed?.Invoke();

    public void Configure(ChartBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var window = Display.Window;
        var (width, height) = (Buffer.Width * Buffer.Spacing.Column, Buffer.Height * Buffer.Spacing.Row);
        var (across, down) = Filled();
        chart.Title(Title)
            .XAxis(axis => axis.Numeric("medical-reformat-x").Title("mm").Range(across.Min, across.Max).Limits(0, width))
            .YAxis(axis => axis.Numeric("medical-reformat-y").Title("mm").Range(down.Min, down.Max).Limits(0, height).Inverted())
            .AddHeatmap(_scaleData, series => series
                .ColorAxis(axis => axis.Colormap(Display.Options.Colormap).Range(window.Minimum, window.Maximum).Title(Display.Unit)))
            .AddSeries((x, y) => new MedicalMprSeries(Display, Buffer, x, y))
            .Modifiers(modifiers => modifiers
                .Custom(() => new Modifier(this))
                .Custom(() => new MedicalColorBarModifier(() => Display.Window, Display.ApplyPreset, Display.InitialSetting))
                .Custom(() => new ZoomPanModifier { PanButton = PointerButton.Middle }));
    }

    // The view opens on the part with values; zooming out shows the whole square.
    private (DoubleRange Across, DoubleRange Down) Filled()
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var row = 0; row < Buffer.Height; row++)
        {
            for (var column = 0; column < Buffer.Width; column++)
            {
                if (!float.IsNaN(Buffer.Read(column, row)))
                {
                    (left, right, top, bottom) = (Math.Min(left, column), Math.Max(right, column), Math.Min(top, row), Math.Max(bottom, row));
                }
            }
        }

        var spacing = Buffer.Spacing;
        return right < 0
            ? (new DoubleRange(0, Buffer.Width * spacing.Column), new DoubleRange(0, Buffer.Height * spacing.Row))
            : (new DoubleRange(left * spacing.Column, (right + 1) * spacing.Column), new DoubleRange(top * spacing.Row, (bottom + 1) * spacing.Row));
    }

    /// <summary>Left drag sets window and level, the wheel goes to the section; new pictures and windows redraw.</summary>
    private sealed class Modifier : ChartModifier
    {
        private readonly MedicalReformatView _view;
        private readonly Action _invalidate;
        private readonly Action _applyWindow;
        private ColorAxis? _axis;
        private MedicalWindow _startWindow;
        private MedicalWindow _pendingWindow;
        private Vector2 _start;
        private long _pointerId;
        private bool _isDragging;

        public Modifier(MedicalReformatView view)
            : base(50)
        {
            _view = view;
            _invalidate = () => Interaction?.Chart.Invalidate();
            _applyWindow = ApplyWindow;
        }

        protected override void OnAttached(ChartInteraction interaction)
        {
            // A chart described anew attaches its modifiers once before its new axes are there and once more after.
            _axis = interaction.Chart.Panes.SelectMany(pane => pane.Axes).OfType<ColorAxis>().FirstOrDefault();
            _view.Display.WindowChanged += OnWindowChanged;
            _view.Changed += OnChanged;
        }

        protected override void OnDetached(ChartInteraction interaction)
        {
            _view.Display.WindowChanged -= OnWindowChanged;
            _view.Changed -= OnChanged;
            _axis = null;
        }

        protected override ModifierResult OnPointer(in PointerInputEvent input)
        {
            if (!_isDragging)
            {
                if (input.Kind != PointerEventKind.Pressed || input.ChangedButton != PointerButton.Primary || input.Modifiers != InputModifiers.None
                    || Interaction is not { } interaction || !interaction.TryHitPane(input.Position, out _))
                {
                    return ModifierResult.Ignored;
                }

                (_isDragging, _pointerId, _start, _startWindow) = (true, input.PointerId, input.Position, _view.Display.Window);
                return ModifierResult.Capture;
            }

            if (input.PointerId != _pointerId)
            {
                return ModifierResult.Ignored;
            }

            if (input.Kind is PointerEventKind.Released or PointerEventKind.Canceled)
            {
                _isDragging = false;
                return ModifierResult.Release;
            }

            if (input.Kind == PointerEventKind.Moved)
            {
                var scale = Math.Max(_startWindow.Width / 256, 0.01);
                var width = Math.Max(1e-6, _startWindow.Width + ((input.Position.X - _start.X) * scale));
                _view.Display.SetWindow(_startWindow.Center - ((input.Position.Y - _start.Y) * scale), width);
            }

            return ModifierResult.Handled;
        }

        protected override ModifierResult OnWheel(in WheelInputEvent input)
        {
            if (_view.Wheel is not { } wheel || input.Modifiers != InputModifiers.None || input.Delta.Y == 0
                || Interaction is not { } interaction || !interaction.TryHitPane(input.Position, out _))
            {
                return ModifierResult.Ignored;
            }

            wheel(Math.Sign(input.Delta.Y));
            return ModifierResult.Handled;
        }

        protected override ModifierResult OnKey(in KeyInputEvent input)
        {
            if (input.Kind != KeyEventKind.Pressed || input.Modifiers != InputModifiers.None || _view.Key is not { } key)
            {
                return ModifierResult.Ignored;
            }

            return key(input.Key) ? ModifierResult.Handled : ModifierResult.Ignored;
        }

        protected override void OnCaptureLost(InputCapture capture) => _isDragging = false;

        private void OnWindowChanged(MedicalWindow window)
        {
            _pendingWindow = window;
            Interaction?.Dispatch(_applyWindow);
        }

        private void ApplyWindow()
        {
            if (_axis is { } axis)
            {
                axis.VisibleRange = new DoubleRange(_pendingWindow.Minimum, _pendingWindow.Maximum);
            }
        }

        private void OnChanged() => Interaction?.Dispatch(_invalidate);
    }
}
