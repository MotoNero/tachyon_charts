using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A series that shows values as colors of a <see cref="ColorAxis"/>.
/// </summary>
/// <param name="xAxis">The horizontal axis.</param>
/// <param name="yAxis">The vertical axis.</param>
/// <param name="colorAxis">The value scale.</param>
public abstract class RasterSeries(AxisBase xAxis, AxisBase yAxis, ColorAxis colorAxis) : ChartSeries(xAxis, yAxis), IDisposable
{
    private readonly ColormapTexture _table = new();
    private readonly ColorAxis _colorAxis = colorAxis ?? throw new ArgumentNullException(nameof(colorAxis));

    /// <inheritdoc />
    public override ColorAxis ColorAxis => _colorAxis;

    /// <inheritdoc />
    public override Rgba8 AccentColor => _colorAxis.Colormap.Sample(0.75);

    /// <summary>Gets the table of the current colormap, valid after <see cref="ChartSeries.Prepare"/>.</summary>
    protected IGpuTexture? Table { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        _table.Dispose();
        Table = null;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources()
    {
        _table.Dispose();
        Table = null;
        DisposeCore();
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame) =>
        Table = _table.Update(device, _colorAxis.Colormap);

    /// <summary>
    /// Finds the part of a data rectangle inside the pane, and the part of a texture that belongs there.
    /// Texture coordinates grow with the values, whichever way the axes run on the screen.
    /// </summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="x">The horizontal extent of the data.</param>
    /// <param name="y">The vertical extent of the data.</param>
    /// <param name="filled">The part of the texture that holds the data, from 0 to 1.</param>
    /// <returns>The rectangle, and the origin and size of its section.</returns>
    protected static (PixelRect Bounds, Vector2 Origin, Vector2 Size) Place(in SeriesFrame frame, DoubleRange x, DoubleRange y, Vector2 filled)
    {
        var bounds = Clip(frame, x, y);
        var startX = frame.X.ToPixel(x.Min);
        var startY = frame.Y.ToPixel(y.Min);
        var perPixelX = filled.X / (frame.X.ToPixel(x.Max) - startX);
        var perPixelY = filled.Y / (frame.Y.ToPixel(y.Max) - startY);
        return (
            bounds,
            new Vector2((float)((bounds.X - startX) * perPixelX), (float)((bounds.Y - startY) * perPixelY)),
            new Vector2((float)(bounds.Width * perPixelX), (float)(bounds.Height * perPixelY)));
    }

    /// <summary>
    /// Finds the part of a data rectangle inside the pane. An end without a place on a logarithmic axis is the edge of the pane.
    /// </summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="x">The horizontal extent of the data.</param>
    /// <param name="y">The vertical extent of the data.</param>
    /// <returns>The rectangle, empty if nothing is visible.</returns>
    protected static PixelRect Clip(in SeriesFrame frame, DoubleRange x, DoubleRange y)
    {
        var pane = frame.Bounds;
        var (left, right) = Clip(frame.X.ToPixel(x.Min), frame.X.ToPixel(x.Max), pane.X, pane.Width);
        var (top, bottom) = Clip(frame.Y.ToPixel(y.Min), frame.Y.ToPixel(y.Max), pane.Y, pane.Height);
        return new PixelRect(left, top, Math.Max(right - left, 0), Math.Max(bottom - top, 0));
    }

    /// <summary>Frees the resources of the series.</summary>
    protected abstract void DisposeCore();

    private static (float Start, float End) Clip(double first, double second, float paneStart, float paneLength)
    {
        var missing = double.IsNaN(first) || double.IsNaN(second);
        var low = missing ? paneStart : Math.Max(Math.Min(first, second), paneStart);
        var high = missing ? paneStart + paneLength : Math.Min(Math.Max(first, second), paneStart + paneLength);
        return ((float)low, (float)high);
    }
}
