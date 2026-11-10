using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

/// <summary>
/// The measurements on a medical image or on one plane of a volume: distances and angles in millimetres and degrees,
/// regions with the area and the statistics of their pixels. Choose a <see cref="Tool"/> and drag on the image to
/// measure, or press D (distance), A (angle), C (circle), R (rectangle) or F (freehand) over the chart; without a tool,
/// drag a point of a measurement to change it. Delete removes the measurement last touched, Escape stops the tool. Every change can be undone with the navigation of the chart, and <see cref="Save"/> keeps all
/// measurements as JSON.
/// </summary>
public sealed class MedicalMeasurementLayer : ChartOverlay
{
    private const int FileVersion = 1;
    private const float HandleSize = 3;
    private static readonly Rgba8 _color = new(255, 214, 10, 255);
    private static readonly Rgba8 _selectedColor = new(80, 220, 255, 255);
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly IMedicalMeasurementSource _source;
    private readonly List<MedicalMeasurement> _measurements = [];
    private readonly Dictionary<MedicalMeasurement, string> _labels = [];
    private AxisBase? _xAxis;
    private AxisBase? _yAxis;
    private MedicalMeasurementKind? _tool;
    private bool _showsTools;
    private MedicalToolButton? _hovered;
    private MedicalPoint[]? _draft;

    internal MedicalMeasurementLayer(IMedicalMeasurementSource source) => _source = source;

    /// <summary>Occurs on the thread of the chart after a measurement was added, changed or removed.</summary>
    public event Action<MedicalMeasurement>? Changed;

    /// <summary>Gets all measurements, on every slice.</summary>
    public IReadOnlyList<MedicalMeasurement> Measurements => _measurements;

    /// <summary>Gets or sets the tool that the next drag on the image uses, or <see langword="null"/> to change measurements instead.</summary>
    public MedicalMeasurementKind? Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            _draft = null;
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a bar of tools shows in the lower left corner of the image: move, distance,
    /// angle, circle, rectangle, freehand and delete. A click chooses a tool and a second click puts it away, so that a drag
    /// moves points and the window again; the keys D, A, C, R, F do the same, Escape puts any tool away.
    /// </summary>
    public bool ShowsTools
    {
        get => _showsTools;
        set
        {
            _showsTools = value;
            OnChanged();
        }
    }

    /// <summary>Gets or sets the button of the tool bar under the pointer, whose name shows.</summary>
    internal MedicalToolButton? Hovered
    {
        get => _hovered;
        set
        {
            if (_hovered != value)
            {
                _hovered = value;
                OnChanged();
            }
        }
    }

    /// <summary>Gets the measurement last added or touched, which Delete removes.</summary>
    public MedicalMeasurement? Selected { get; private set; }

    /// <summary>Gets the unit of the pixel values.</summary>
    public string Unit => _source.Unit;

    /// <summary>Adds a measurement from code.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="points">The points in millimetres.</param>
    /// <param name="slice">The slice, or <see langword="null"/> for the one shown now.</param>
    /// <param name="name">The name; regions of the same name form a volume.</param>
    /// <returns>The measurement with its result.</returns>
    /// <exception cref="ArgumentException">The number of points does not fit the kind, or a point is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">There is no such slice.</exception>
    public MedicalMeasurement Add(MedicalMeasurementKind kind, IReadOnlyList<MedicalPoint> points, int? slice = null, string? name = null)
    {
        var index = slice ?? _source.Slice;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(slice));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _source.SliceCount, nameof(slice));
        var measurement = new MedicalMeasurement(kind, points, index, name ?? DefaultName(kind));

        // Only a measurement drawn or touched with the pointer counts as selected; those who listen see it so at once.
        measurement.Update(_source);
        _measurements.Add(measurement);
        Selected = null;
        Notify(measurement);
        return measurement;
    }

    /// <summary>Removes a measurement.</summary>
    /// <param name="measurement">The measurement.</param>
    /// <returns>Whether it was on the layer.</returns>
    public bool Remove(MedicalMeasurement measurement)
    {
        var index = _measurements.IndexOf(measurement);
        if (index < 0)
        {
            return false;
        }

        _measurements.RemoveAt(index);
        _labels.Remove(measurement);
        Selected = Selected == measurement ? null : Selected;
        Notify(measurement);
        return true;
    }

    /// <summary>
    /// Works out the volume of the regions with a name, as the sum of their areas times the distance between slices.
    /// A single image has no distance between slices and gives 0.
    /// </summary>
    /// <param name="name">The name of the regions.</param>
    /// <returns>The volume in cubic millimetres.</returns>
    public double Volume(string name)
    {
        var area = 0.0;
        foreach (var measurement in _measurements)
        {
            if (measurement.IsRegion && measurement.Name == name)
            {
                area += measurement.Result.Area;
            }
        }

        return area * _source.SliceDistance;
    }

    /// <summary>Writes all measurements as JSON.</summary>
    /// <returns>The JSON text.</returns>
    public string Save()
    {
        var items = new MeasurementDto[_measurements.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var measurement = _measurements[i];
            var points = measurement.Snapshot;
            items[i] = new MeasurementDto(measurement.Kind, measurement.Slice, measurement.Name, [.. points.Select(point => new[] { point.X, point.Y })]);
        }

        return JsonSerializer.Serialize(new FileDto(FileVersion, items), _options);
    }

    /// <summary>Replaces all measurements with those of a JSON text from <see cref="Save"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="FormatException">The text is no measurement file, comes from a newer version or holds invalid measurements.</exception>
    public void Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, _options);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The text is no measurement file: " + exception.Message, exception);
        }

        if (file?.Items is null)
        {
            throw new FormatException("The text holds no measurements.");
        }

        if (file.SavedVersion > FileVersion)
        {
            throw new FormatException($"The measurements were saved by a newer version ({file.SavedVersion}).");
        }

        var loaded = new List<MedicalMeasurement>(file.Items.Length);
        foreach (var item in file.Items)
        {
            try
            {
                if (item.Slice < 0 || item.Slice >= _source.SliceCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(json), item.Slice, "There is no such slice.");
                }

                var points = (item.Points ?? []).Select(pair => pair is [var x, var y] ? new MedicalPoint(x, y) : throw new ArgumentException("A point needs two numbers.")).ToArray();
                loaded.Add(new MedicalMeasurement(item.Kind, points, item.Slice, item.Name ?? DefaultName(item.Kind)));
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("A measurement is invalid: " + exception.Message, exception);
            }
        }

        _measurements.Clear();
        _labels.Clear();
        Selected = null;
        foreach (var measurement in loaded)
        {
            Insert(measurement, _measurements.Count);
        }

        OnChanged();
    }

    internal IMedicalMeasurementSource Source => _source;

    internal MedicalPoint[]? Draft
    {
        get => _draft;
        set
        {
            _draft = value;
            OnChanged();
        }
    }

    /// <summary>Joins a chart: the image lies on its first horizontal axis and the first axis of its pane, both in millimetres.</summary>
    internal MedicalMeasurementLayer Attach(Core.Pipeline.ChartSurface chart)
    {
        (_xAxis, _yAxis) = (chart.HorizontalAxes[0], chart.Panes[0].Axes[0]);
        return this;
    }

    internal void Insert(MedicalMeasurement measurement, int index)
    {
        measurement.Update(_source);
        _measurements.Insert(Math.Min(index, _measurements.Count), measurement);
        Selected = measurement;
        Notify(measurement);
    }

    internal void Move(MedicalMeasurement measurement, MedicalPoint[] points)
    {
        measurement.Move(points, _source);
        Selected = measurement;
        Notify(measurement);
    }

    internal void Select(MedicalMeasurement? measurement)
    {
        var changed = Selected != measurement;
        Selected = measurement;
        OnChanged();
        if (changed)
        {
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Occurs when another measurement becomes the selected one, without a measurement changing.</summary>
    internal event Action? SelectionChanged;

    // The nearest point of a measurement on the slice shown, within reach of the pointer.
    internal bool TryFindPoint(ChartLayoutResult layout, Vector2 position, float reach, out MedicalMeasurement measurement, out int index)
    {
        (measurement, index) = (null!, -1);
        var best = reach * reach;
        foreach (var candidate in _measurements)
        {
            if (candidate.Slice != _source.Slice)
            {
                continue;
            }

            var points = candidate.Snapshot;
            for (var i = 0; i < points.Length; i++)
            {
                if (TryMap(layout, points[i], out var at) && Vector2.DistanceSquared(at, position) <= best)
                {
                    (best, measurement, index) = (Vector2.DistanceSquared(at, position), candidate, i);
                }
            }
        }

        return index >= 0;
    }

    internal bool TryMap(ChartLayoutResult layout, MedicalPoint point, out Vector2 position)
    {
        position = default;
        if (_xAxis is null || _yAxis is null || !TryTransform(layout, _xAxis, out var x) || !TryTransform(layout, _yAxis, out var y))
        {
            return false;
        }

        position = new Vector2((float)x.ToPixel(point.X), (float)y.ToPixel(point.Y));
        return true;
    }

    internal bool TryUnmap(ChartLayoutResult layout, Vector2 position, out MedicalPoint point)
    {
        point = default;
        if (_xAxis is null || _yAxis is null || !TryTransform(layout, _xAxis, out var x) || !TryTransform(layout, _yAxis, out var y))
        {
            return false;
        }

        point = new MedicalPoint(x.ToData(position.X), y.ToData(position.Y));
        return true;
    }

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var layout = canvas.Layout;
        canvas.Clip = layout.PlotArea;
        foreach (var measurement in _measurements)
        {
            if (measurement.Slice == _source.Slice)
            {
                var color = measurement == Selected ? _selectedColor : _color;
                DrawShape(canvas, layout, measurement.Kind, measurement.Snapshot, measurement.OutlineSnapshot, color, isFinished: true);
                DrawLabel(canvas, layout, measurement, color);
            }
        }

        if (_tool is { } tool && _draft is { Length: > 0 } draft)
        {
            var outline = tool is MedicalMeasurementKind.Circle or MedicalMeasurementKind.Rectangle && draft.Length >= 2 ? MedicalMeasurement.Outline(tool, draft) : draft;
            DrawShape(canvas, layout, tool, draft, outline, _selectedColor, isFinished: false);
        }

        if (_showsTools)
        {
            MedicalToolBar.Draw(canvas, _tool, _hovered, Selected is not null, _selectedColor);
        }

        canvas.Clip = null;
    }

    internal static string DefaultName(MedicalMeasurementKind kind) => kind switch
    {
        MedicalMeasurementKind.Distance => "Distance",
        MedicalMeasurementKind.Angle => "Angle",
        _ => "Region",
    };

    private static bool TryTransform(ChartLayoutResult layout, AxisBase axis, out Core.Numerics.LinearAxisTransform transform)
    {
        var placements = layout.Axes;
        for (var i = 0; i < placements.Count; i++)
        {
            if (placements[i].Axis == axis)
            {
                var bounds = placements[i].Bounds;
                transform = axis.IsHorizontal ? axis.CreateTransform(bounds.X, bounds.Width) : axis.CreateTransform(bounds.Y, bounds.Height);
                return true;
            }
        }

        transform = default;
        return false;
    }

    private void DrawShape(OverlayCanvas canvas, ChartLayoutResult layout, MedicalMeasurementKind kind, MedicalPoint[] points, MedicalPoint[] outline, Rgba8 color, bool isFinished)
    {
        var isClosed = isFinished && kind is MedicalMeasurementKind.Circle or MedicalMeasurementKind.Rectangle or MedicalMeasurementKind.Freehand
            || (!isFinished && kind is MedicalMeasurementKind.Circle or MedicalMeasurementKind.Rectangle);
        for (var i = 1; i < outline.Length + (isClosed ? 1 : 0); i++)
        {
            if (TryMap(layout, outline[i - 1], out var from) && TryMap(layout, outline[i % outline.Length], out var to))
            {
                canvas.DrawLine(from, to, 1.5f, color);
            }
        }

        foreach (var point in points)
        {
            if (TryMap(layout, point, out var at))
            {
                canvas.FillRectangle(new DipRect(at.X - HandleSize, at.Y - HandleSize, HandleSize * 2, HandleSize * 2), color);
            }
        }
    }

    // A distance has its label over the middle of the line, an angle below its vertex, a region below its outline.
    private void DrawLabel(OverlayCanvas canvas, ChartLayoutResult layout, MedicalMeasurement measurement, Rgba8 color)
    {
        if (!_labels.TryGetValue(measurement, out var text))
        {
            return;
        }

        var points = measurement.Snapshot;
        var line = canvas.GetLineHeight(canvas.LabelSize) + 2;
        var background = canvas.BackgroundColor with { A = 220 };
        switch (measurement.Kind)
        {
            case MedicalMeasurementKind.Distance when TryMap(layout, points[0], out var a) && TryMap(layout, points[1], out var b):
                canvas.DrawLabel(text, ((a.X + b.X) / 2) - (canvas.MeasureWidth(text, canvas.LabelSize) / 2), ((a.Y + b.Y) / 2) - line - 4, background, color);
                break;
            case MedicalMeasurementKind.Angle when TryMap(layout, points[1], out var vertex):
                canvas.DrawLabel(text, vertex.X - (canvas.MeasureWidth(text, canvas.LabelSize) / 2), vertex.Y + 6, background, color);
                break;
            default:
                var (left, bottom, found) = (float.PositiveInfinity, float.NegativeInfinity, false);
                foreach (var point in measurement.OutlineSnapshot)
                {
                    if (TryMap(layout, point, out var at))
                    {
                        (left, bottom, found) = (Math.Min(left, at.X), Math.Max(bottom, at.Y), true);
                    }
                }

                if (found)
                {
                    canvas.DrawLabel(text, left, bottom + 4, background, color);
                }

                break;
        }
    }

    private void Notify(MedicalMeasurement measurement)
    {
        _labels[measurement] = Format(measurement.Kind, measurement.Result, _source.Unit);
        if (!_measurements.Contains(measurement))
        {
            _labels.Remove(measurement);
        }

        Changed?.Invoke(measurement);
        OnChanged();
    }

    private static string Format(MedicalMeasurementKind kind, MedicalMeasurementResult result, string unit) => kind switch
    {
        MedicalMeasurementKind.Distance => string.Create(CultureInfo.InvariantCulture, $"{result.Length:F1} mm"),
        MedicalMeasurementKind.Angle => string.Create(CultureInfo.InvariantCulture, $"{result.Angle:F1}°"),
        _ when result.PixelCount == 0 => string.Create(CultureInfo.InvariantCulture, $"{result.Area:F1} mm²"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{result.Area:F1} mm²  {result.Mean:F1} ± {result.StandardDeviation:F1} {unit}  ({result.Minimum:F0} to {result.Maximum:F0})"),
    };

    private sealed record FileDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("version")] int SavedVersion,
        [property: System.Text.Json.Serialization.JsonPropertyName("measurements")] MeasurementDto[]? Items);

    private sealed record MeasurementDto(MedicalMeasurementKind Kind, int Slice, string? Name, double[][]? Points);
}
