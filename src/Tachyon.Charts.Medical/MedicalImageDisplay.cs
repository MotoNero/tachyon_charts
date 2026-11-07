using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

/// <summary>A centre and width used to map medical values to grayscale.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Center">The value in the middle of the grayscale range.</param>
/// <param name="Width">The width of the grayscale range.</param>
public readonly record struct MedicalWindow(string Name, double Center, double Width)
{
    /// <summary>Gets the lowest visible value.</summary>
    public double Minimum => Center - (Width / 2);

    /// <summary>Gets the highest visible value.</summary>
    public double Maximum => Center + (Width / 2);
}

/// <summary>Common CT display windows, expressed in Hounsfield units.</summary>
public static class CtWindowPresets
{
    /// <summary>Gets a soft-tissue brain window.</summary>
    public static MedicalWindow Brain { get; } = new("Brain", 40, 80);

    /// <summary>Gets a bone window.</summary>
    public static MedicalWindow Bone { get; } = new("Bone", 400, 1800);

    /// <summary>Gets a lung window.</summary>
    public static MedicalWindow Lung { get; } = new("Lung", -600, 1500);
}

/// <summary>The patient-direction letters at the four edges of an image.</summary>
/// <param name="Top">The direction at the top.</param>
/// <param name="Right">The direction at the right.</param>
/// <param name="Bottom">The direction at the bottom.</param>
/// <param name="Left">The direction at the left.</param>
public readonly record struct MedicalOrientationMarks(string Top, string Right, string Bottom, string Left);

/// <summary>A value found in a displayed medical image.</summary>
/// <param name="Column">The zero-based column.</param>
/// <param name="Row">The zero-based row.</param>
/// <param name="X">The horizontal position in millimetres.</param>
/// <param name="Y">The vertical position in millimetres.</param>
/// <param name="Value">The rescaled image value.</param>
/// <param name="Unit">The unit of the value.</param>
public readonly record struct MedicalImageProbe(int Column, int Row, double X, double Y, double Value, string Unit);

/// <summary>
/// Configures a platform-neutral 2D chart for one medical image, including physical axes, grayscale windowing,
/// a value probe and patient-orientation marks.
/// </summary>
public sealed class MedicalImageDisplay
{
    private const string XAxisId = "medical-x";
    private const string YAxisId = "medical-y";
    private const double MinimumWindowWidth = 1e-6;

    private readonly HeatmapData _data;
    private MedicalWindow _window;

    /// <summary>Initializes a display from an image read by <see cref="DicomReader"/>.</summary>
    /// <param name="image">The image.</param>
    public MedicalImageDisplay(MedicalImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Spacing is not { } spacing || image.Orientation is not { } orientation)
        {
            throw new ArgumentException("The image needs pixel spacing and patient orientation.", nameof(image));
        }

        Width = image.Width;
        Height = image.Height;
        ValidateSpacing(spacing);
        ValidateOrientation(orientation);
        Spacing = spacing;
        Orientation = orientation;
        Unit = string.Equals(image.Modality, "CT", StringComparison.OrdinalIgnoreCase) ? "HU" : "value";
        _data = new HeatmapData(Width, Height);
        var values = new float[checked(Width * Height)];
        image.CopyValues(values);
        _data.SetValues(values);
        _window = InitialWindow(image.WindowCenter, image.WindowWidth, _data.ValueRange);
        OrientationMarks = Marks(orientation);
    }

    /// <summary>Initializes a display from rescaled values in row-major order.</summary>
    /// <param name="values">The values, row by row.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="spacing">The pixel spacing in millimetres.</param>
    /// <param name="orientation">The patient-space orientation.</param>
    /// <param name="unit">The value unit shown under the pointer.</param>
    public MedicalImageDisplay(
        ReadOnlySpan<float> values,
        int width,
        int height,
        ImageSpacing spacing,
        PatientOrientation orientation,
        string unit = "value")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (values.Length != checked(width * height))
        {
            throw new ArgumentException("The values do not fill the image.", nameof(values));
        }

        ValidateSpacing(spacing);
        ValidateOrientation(orientation);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        Width = width;
        Height = height;
        Spacing = spacing;
        Orientation = orientation;
        Unit = unit;
        _data = new HeatmapData(width, height);
        _data.SetValues(values);
        _window = InitialWindow(null, null, _data.ValueRange);
        OrientationMarks = Marks(orientation);
    }

    /// <summary>Occurs after the grayscale window changes.</summary>
    public event Action<MedicalWindow>? WindowChanged;

    /// <summary>Gets the number of columns.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }

    /// <summary>Gets the pixel spacing.</summary>
    public ImageSpacing Spacing { get; }

    /// <summary>Gets the patient-space orientation.</summary>
    public PatientOrientation Orientation { get; }

    /// <summary>Gets the letters shown at the four image edges.</summary>
    public MedicalOrientationMarks OrientationMarks { get; }

    /// <summary>Gets the unit shown for image values.</summary>
    public string Unit { get; }

    /// <summary>Gets the active grayscale window.</summary>
    public MedicalWindow Window => _window;

    /// <summary>Sets the active grayscale window.</summary>
    /// <param name="center">The value in the middle.</param>
    /// <param name="width">The width, greater than zero.</param>
    public void SetWindow(double center, double width) => SetWindow(new MedicalWindow("Custom", center, width));

    /// <summary>Applies a named grayscale window.</summary>
    /// <param name="window">The window.</param>
    public void ApplyPreset(MedicalWindow window) => SetWindow(window);

    /// <summary>Finds the value at a position measured from the upper-left image corner.</summary>
    /// <param name="x">The horizontal position in millimetres.</param>
    /// <param name="y">The vertical position in millimetres.</param>
    /// <param name="probe">Receives the image position and value.</param>
    /// <returns>Whether the position lies in the image.</returns>
    public bool TryGetValue(double x, double y, out MedicalImageProbe probe)
    {
        var column = (int)Math.Floor(x / Spacing.Column);
        var row = (int)Math.Floor(y / Spacing.Row);
        if (column < 0 || column >= Width || row < 0 || row >= Height)
        {
            probe = default;
            return false;
        }

        probe = new MedicalImageProbe(column, row, x, y, _data[column, row], Unit);
        return true;
    }

    /// <summary>Adds the image and its interactions to a chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure(ChartBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var xLength = Width * Spacing.Column;
        var yLength = Height * Spacing.Row;
        var window = _window;
        chart.Title("Medical image")
            .XAxis(axis => axis.Numeric(XAxisId).Title("mm").Range(0, xLength).Limits(0, xLength))
            .YAxis(axis => axis.Numeric(YAxisId).Title("mm").Range(0, yLength).Limits(0, yLength).Inverted())
            .AddHeatmap(_data, series => series
                .Name(Unit)
                .Cells(0, Spacing.Column, 0, Spacing.Row)
                .Interpolated()
                .ColorAxis(axis => axis.Colormap(Colormaps.Grayscale).Range(window.Minimum, window.Maximum).Title(Unit)))
            .Modifiers(modifiers => modifiers
                .Custom(() => new MedicalImageModifier(this))
                .Custom(() => new ZoomPanModifier { PanButton = PointerButton.Middle }));
    }

    private void SetWindow(MedicalWindow window)
    {
        if (!double.IsFinite(window.Center) || !double.IsFinite(window.Width) || window.Width < MinimumWindowWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "The window must have a finite centre and a positive finite width.");
        }

        if (_window == window)
        {
            return;
        }

        _window = window;
        WindowChanged?.Invoke(window);
    }

    internal static MedicalWindow InitialWindow(double? center, double? width, DoubleRange range)
    {
        if (center is { } validCenter && width is > 0 and { } validWidth && double.IsFinite(validCenter) && double.IsFinite(validWidth))
        {
            return new MedicalWindow("File", validCenter, validWidth);
        }

        var fallbackWidth = range.Length > 0 && double.IsFinite(range.Length) ? range.Length : 1;
        var fallbackCenter = double.IsFinite(range.Center) ? range.Center : 0;
        return new MedicalWindow("Data", fallbackCenter, fallbackWidth);
    }

    private static void ValidateSpacing(ImageSpacing spacing)
    {
        if (!double.IsFinite(spacing.Row) || spacing.Row <= 0 || !double.IsFinite(spacing.Column) || spacing.Column <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Pixel spacing must be finite and positive.");
        }
    }

    private static void ValidateOrientation(PatientOrientation orientation)
    {
        if (!double.IsFinite(orientation.Across.Length)
            || !double.IsFinite(orientation.Down.Length)
            || orientation.Across.Length == 0
            || orientation.Down.Length == 0
            || orientation.Normal.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation needs two finite, independent directions.");
        }
    }

    internal static MedicalOrientationMarks Marks(PatientOrientation orientation)
    {
        var right = Direction(orientation.Across);
        var bottom = Direction(orientation.Down);
        return new MedicalOrientationMarks(Opposite(bottom), right, bottom, Opposite(right));
    }

    private static string Direction(ThreeD.Scene.Point3D direction)
    {
        var x = Math.Abs(direction.X);
        var y = Math.Abs(direction.Y);
        var z = Math.Abs(direction.Z);
        if (x >= y && x >= z)
        {
            return direction.X >= 0 ? "L" : "R";
        }

        if (y >= z)
        {
            return direction.Y >= 0 ? "P" : "A";
        }

        return direction.Z >= 0 ? "S" : "I";
    }

    private static string Opposite(string direction) => direction switch
    {
        "L" => "R",
        "R" => "L",
        "P" => "A",
        "A" => "P",
        "S" => "I",
        _ => "S",
    };

    private sealed class MedicalImageModifier : ChartModifier
    {
        private readonly MedicalImageDisplay _display;
        private readonly MedicalImageOverlay _overlay;
        private readonly Action _applyPendingWindow;
        private ColorAxis? _axis;
        private MedicalWindow _pendingWindow;
        private Vector2 _start;
        private MedicalWindow _startWindow;
        private long _pointerId;
        private bool _isDragging;

        public MedicalImageModifier(MedicalImageDisplay display)
            : base(50)
        {
            _display = display;
            _overlay = new MedicalImageOverlay(display.OrientationMarks);
            _applyPendingWindow = ApplyPendingWindow;
        }

        protected override void OnAttached(ChartInteraction interaction)
        {
            _axis = interaction.Chart.Panes.SelectMany(pane => pane.Axes).OfType<ColorAxis>().Single();
            _display.WindowChanged += OnWindowChanged;
            interaction.Dispatch(() => interaction.Chart.AddOverlay(_overlay));
        }

        protected override void OnDetached(ChartInteraction interaction)
        {
            _display.WindowChanged -= OnWindowChanged;
            interaction.Dispatch(() => interaction.Chart.RemoveOverlay(_overlay));
            _axis = null;
        }

        protected override ModifierResult OnPointer(in PointerInputEvent input)
        {
            if (_isDragging)
            {
                return input.PointerId == _pointerId ? ContinueWindowing(input) : ModifierResult.Ignored;
            }

            UpdateProbe(input);
            if (input.Kind != PointerEventKind.Pressed
                || input.ChangedButton != PointerButton.Primary
                || input.Modifiers != InputModifiers.None
                || Interaction is not { } interaction
                || !interaction.TryHitPane(input.Position, out _))
            {
                return ModifierResult.Ignored;
            }

            _isDragging = true;
            _pointerId = input.PointerId;
            _start = input.Position;
            _startWindow = _display.Window;
            return ModifierResult.Capture;
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

            _display.ApplyPreset(preset);
            return ModifierResult.Handled;
        }

        protected override void OnCaptureLost(InputCapture capture) => _isDragging = false;

        private ModifierResult ContinueWindowing(in PointerInputEvent input)
        {
            if (input.Kind == PointerEventKind.Moved)
            {
                var scale = Math.Max(_startWindow.Width / 256, 0.01);
                var width = Math.Max(MinimumWindowWidth, _startWindow.Width + ((input.Position.X - _start.X) * scale));
                var center = _startWindow.Center - ((input.Position.Y - _start.Y) * scale);
                _display.SetWindow(center, width);
                return ModifierResult.Handled;
            }

            if (input.Kind is PointerEventKind.Released or PointerEventKind.Canceled)
            {
                _isDragging = false;
                return ModifierResult.Release;
            }

            return ModifierResult.Handled;
        }

        private void UpdateProbe(in PointerInputEvent input)
        {
            if (Interaction is not { } interaction
                || input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled
                || !interaction.TryHitPane(input.Position, out _)
                || !interaction.Chart.TryHitTest(input.Position, double.MaxValue, Core.HitTesting.HitTestMode.Nearest, out var hit)
                || hit.Value is not { } value)
            {
                _overlay.HideProbe();
                return;
            }

            var text = string.Create(CultureInfo.InvariantCulture, $"{value:F1} {_display.Unit}   x {hit.X:F1} mm   y {hit.Y:F1} mm");
            _overlay.ShowProbe(input.Position, text);
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

        private void ApplyPendingWindow()
        {
            if (_axis is { } axis)
            {
                axis.VisibleRange = new DoubleRange(_pendingWindow.Minimum, _pendingWindow.Maximum);
            }
        }
    }

    private sealed class MedicalImageOverlay(MedicalOrientationMarks marks) : ChartOverlay
    {
        private Vector2 _probePosition;
        private string? _probeText;

        public void ShowProbe(Vector2 position, string text)
        {
            _probePosition = position;
            _probeText = text;
            OnChanged();
        }

        public void HideProbe()
        {
            if (_probeText is null)
            {
                return;
            }

            _probeText = null;
            OnChanged();
        }

        protected override void Draw(OverlayCanvas canvas)
        {
            var plot = canvas.Layout.PlotArea;
            const float size = 15;
            var height = canvas.GetLineHeight(size);
            DrawCentered(canvas, marks.Top, plot.X + (plot.Width / 2), plot.Y + height + 5, size);
            DrawCentered(canvas, marks.Bottom, plot.X + (plot.Width / 2), plot.Bottom - 6, size);
            DrawCentered(canvas, marks.Left, plot.X + 12, plot.Y + (plot.Height / 2), size);
            DrawCentered(canvas, marks.Right, plot.Right - 12, plot.Y + (plot.Height / 2), size);

            if (_probeText is { } text)
            {
                canvas.DrawLabel(text, _probePosition.X + 12, _probePosition.Y + 12, new Rgba8(20, 24, 32, 225), canvas.TextColor);
            }
        }

        private static void DrawCentered(OverlayCanvas canvas, string text, double x, double baseline, float size) =>
            canvas.DrawText(text, x - (canvas.MeasureWidth(text, size) / 2), baseline, size, new Rgba8(255, 220, 90, 255));
    }
}
