using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
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

    internal MedicalMprSlice(MedicalMprDisplay display, MedicalMprSliceBuffer buffer)
    {
        _display = display;
        _buffer = buffer;
        OrientationMarks = MedicalImageDisplay.Marks(buffer.Orientation);
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
            .AddHeatmap(_buffer.Data, series => series
                .Name(_display.Unit)
                .Cells(0, Spacing.Column, 0, Spacing.Row)
                .Interpolated()
                .ColorAxis(axis => axis.Colormap(Colormaps.Grayscale).Range(window.Minimum, window.Maximum).Title(_display.Unit)))
            .AddOverlay(surface => new MedicalMprOverlay(this, surface.HorizontalAxes[0], surface.Panes[0].Axes[0]))
            .Modifiers(modifiers => modifiers
                .Custom(() => new MedicalMprModifier(this))
                .Custom(() => new ZoomPanModifier { PanButton = PointerButton.Middle }));
    }

    internal void Update(Point3D position) => _buffer.Update(position);

    internal int FixedIndex => _buffer.FixedIndex;

    internal Point3D ToVolumePosition(double x, double y) => _buffer.ToVolumePosition(x, y, _display.Position);

    internal (double X, double Y) FromVolumePosition(Point3D position) => _buffer.FromVolumePosition(position);

    private sealed class MedicalMprModifier : ChartModifier
    {
        private const float ClickReach = 4;

        private readonly MedicalMprSlice _slice;
        private readonly Action _applyPendingWindow;
        private readonly Action _invalidate;
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
            _invalidate = Invalidate;
        }

        protected override void OnAttached(ChartInteraction interaction)
        {
            _axis = interaction.Chart.Panes.SelectMany(pane => pane.Axes).OfType<ColorAxis>().Single();
            _slice._display.WindowChanged += OnWindowChanged;
            _slice._display.PositionChanged += OnPositionChanged;
        }

        protected override void OnDetached(ChartInteraction interaction)
        {
            _slice._display.WindowChanged -= OnWindowChanged;
            _slice._display.PositionChanged -= OnPositionChanged;
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
                return ModifierResult.Ignored;
            }

            _slice._display.ApplyPreset(preset);
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

        private void ApplyPendingWindow()
        {
            if (_axis is { } axis)
            {
                axis.VisibleRange = new DoubleRange(_pendingWindow.Minimum, _pendingWindow.Maximum);
            }
        }

        private void Invalidate() => Interaction?.Chart.Invalidate();
    }

    private sealed class MedicalMprOverlay(MedicalMprSlice slice, AxisBase xAxis, AxisBase yAxis) : ChartOverlay
    {
        protected override long ExternalVersion => slice._display.PositionVersion;

        protected override void Draw(OverlayCanvas canvas)
        {
            var plot = canvas.Layout.PlotArea;
            var (x, y) = slice.FromVolumePosition(slice._display.Position);
            var color = new Rgba8(255, 160, 50, 220);
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
}
