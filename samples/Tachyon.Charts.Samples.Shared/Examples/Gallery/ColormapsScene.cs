using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// One strip per colormap, a custom map with a missing part, and a strip with out-of-range colors.
/// The color bar on the right shows Turbo.
/// </summary>
public sealed class ColormapsScene : IScene
{
    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Colormaps";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 100), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 8), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Turbo, VisibleRange = new DoubleRange(0, 100) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var strips = new StripSeries(x, y);
        chart.AddSeries(strips);
        chart.Render(device, target);
        device.WaitForIdle();
    }

    private sealed class StripSeries(AxisBase x, AxisBase y) : ChartSeries(x, y), IDisposable
    {
        private const int Width = 256;

        private readonly List<(ColormapTexture Table, Colormap Map)> _tables = [];
        private IRenderDevice _device = null!;
        private IGpuTexture? _values;

        protected override long Version => 0;

        public void Dispose()
        {
            _values?.Dispose();
            foreach (var (table, _) in _tables)
            {
                table.Dispose();
            }
        }

        protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
        {
            // The tables are written while drawing is prepared, so the frame only binds them.
            _device = device;
            if (_values is not null)
            {
                return;
            }

            var values = new float[Width];
            for (var i = 0; i < Width; i++)
            {
                values[i] = i is >= 120 and < 136 ? float.NaN : i / (float)(Width - 1);
            }

            _values = device.CreateTexture(new TextureDesc(new PixelSize(Width, 1), TextureFormat.R32Float));
            _values.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
            Colormap custom = Colormap.FromColors("Custom", [new Rgba8(20, 40, 120, 255), new Rgba8(240, 240, 240, 255), new Rgba8(170, 30, 30, 255)]);
            foreach (var map in Colormaps.All.Append(custom).Append(Colormaps.Viridis))
            {
                _tables.Add((new ColormapTexture(), map));
                _tables[^1].Table.Update(device, map);
            }
        }

        protected override void Draw(ICommandList commands, in SeriesFrame frame)
        {
            for (var i = 0; i < _tables.Count; i++)
            {
                var (table, map) = _tables[i];
                var style = i == _tables.Count - 1
                    ? new RasterStyle(0.25f, 0.75f) { BelowColor = new Rgba8(255, 255, 255, 255), AboveColor = new Rgba8(255, 0, 255, 255), MissingColor = new Rgba8(128, 128, 128, 255) }
                    : new RasterStyle(0, 1) { MissingColor = new Rgba8(128, 128, 128, 255) };
                var top = (float)frame.Y.ToPixel(8 - i - 0.1);
                var bottom = (float)frame.Y.ToPixel(8 - i - 0.9);
                commands.DrawRaster(_values!, table.Update(_device, map), new PixelRect(frame.Bounds.X, top, frame.Bounds.Width, bottom - top), style);
            }
        }

    }
}
