using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// Shows a layout with colored areas and real axis labels.
/// </summary>
public abstract class LayoutScene : IScene
{
    private const float LabelSize = 11;
    private const float TitleSize = 14;

    private static readonly Rgba8 _background = new(24, 28, 36, 255);
    private static readonly Rgba8 _plot = new(40, 48, 62, 255);
    private static readonly Rgba8 _axis = new(32, 38, 50, 255);
    private static readonly Rgba8 _tick = new(150, 160, 175, 255);
    private static readonly Rgba8 _title = new(230, 190, 90, 255);
    private static readonly Rgba8[] _accents =
    [
        new(80, 170, 240, 255), new(240, 130, 90, 255), new(110, 210, 130, 255), new(190, 130, 230, 255),
    ];

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <summary>
    /// Gets the chart title.
    /// </summary>
    protected abstract string? Title { get; }

    /// <summary>
    /// Gets the axes at the top and bottom.
    /// </summary>
    protected abstract IReadOnlyList<AxisBase> HorizontalAxes { get; }

    /// <summary>
    /// Gets the panes.
    /// </summary>
    protected abstract IReadOnlyList<LayoutPane> Panes { get; }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var scale = target.DpiScale;
        var engine = SkiaTextEngine.Default;
        var measurer = new TextEngineMeasurer(engine, LabelSize);
        var layout = ChartLayout.Arrange(target.Size.ToDips(scale), Title, HorizontalAxes, Panes, measurer);
        var text = new TextBatch(engine, scale);

        using var frame = device.BeginFrame(target);
        frame.Clear(_background);

        if (Title is { } title)
        {
            var left = layout.Title.X + ((layout.Title.Width - engine.MeasureWidth(title, TitleSize)) / 2);
            text.Add(title, left, layout.Title.Bottom - 2, TitleSize, _title);
        }

        foreach (var pane in layout.Panes)
        {
            frame.FillRectangle(pane.ToPixels(scale), _plot);
        }

        for (var i = 0; i < layout.Axes.Count; i++)
        {
            DrawAxis(frame, layout.Axes[i], _accents[i % _accents.Length], measurer, text, scale);
        }

        frame.DrawGlyphs(engine.GetAtlas(device), text.Glyphs);
        frame.Submit();
    }

    /// <summary>
    /// Creates an axis with a fixed range and culture, so that snapshots are reproducible.
    /// </summary>
    protected static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max, string? title = null) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
        Title = title,
    };

    private static void DrawAxis(ICommandList frame, AxisPlacement placement, Rgba8 color, ITextMeasurer measurer, TextBatch text, double scale)
    {
        var bounds = placement.Bounds;
        var axis = placement.Axis;
        frame.FillRectangle(bounds.ToPixels(scale), _axis);

        if (axis.Title is { } title)
        {
            var width = measurer.MeasureWidth(title);
            var area = TitleBounds(axis.Position, bounds, width);
            if (axis.IsHorizontal)
            {
                text.Add(title, area.X, area.Bottom - 2, LabelSize, _title);
            }
            else
            {
                // Rotated text does not exist yet.
                frame.DrawTextPlaceholder(area.ToPixels(scale), _title);
            }
        }

        var length = axis.IsHorizontal ? bounds.Width : bounds.Height;
        var transform = axis.CreateTransform(axis.IsHorizontal ? bounds.X : bounds.Y, length);
        foreach (var tick in axis.GenerateTicks(length, measurer).Major)
        {
            var position = transform.ToPixel(tick.Value);
            var width = measurer.MeasureWidth(tick.Label);
            var (mark, labelX, baseline) = axis.Position switch
            {
                AxisPosition.Bottom => (new DipRect(position, bounds.Y, 1, 5), position - (width / 2), bounds.Y + 8 + LabelSize),
                AxisPosition.Top => (new DipRect(position, bounds.Bottom - 5, 1, 5), position - (width / 2), bounds.Bottom - 9),
                AxisPosition.Left => (new DipRect(bounds.Right - 5, position, 5, 1), bounds.Right - 8 - width, position + (LabelSize * 0.36)),
                _ => (new DipRect(bounds.X, position, 5, 1), bounds.X + 8, position + (LabelSize * 0.36)),
            };

            frame.FillRectangle(mark.ToPixels(scale), _tick);
            text.Add(tick.Label, labelX, baseline, LabelSize, color);
        }
    }

    // The title sits at the outer edge of the axis, along it.
    private static DipRect TitleBounds(AxisPosition position, DipRect bounds, double length)
    {
        var alongX = bounds.X + ((bounds.Width - length) / 2);
        var alongY = bounds.Y + ((bounds.Height - length) / 2);
        return position switch
        {
            AxisPosition.Bottom => new DipRect(alongX, bounds.Bottom - 12, length, 10),
            AxisPosition.Top => new DipRect(alongX, bounds.Y + 2, length, 10),
            AxisPosition.Left => new DipRect(bounds.X + 2, alongY, 10, length),
            _ => new DipRect(bounds.Right - 12, alongY, 10, length),
        };
    }

    /// <summary>
    /// Collects the text of a frame, which then is one draw call.
    /// </summary>
    private sealed class TextBatch(ITextEngine engine, double scale)
    {
        private GlyphInstance[] _glyphs = new GlyphInstance[1024];
        private int _count;

        public ReadOnlySpan<GlyphInstance> Glyphs => _glyphs.AsSpan(0, _count);

        public void Add(string text, double x, double baseline, float size, Rgba8 color)
        {
            if (_count + text.Length > _glyphs.Length)
            {
                Array.Resize(ref _glyphs, _glyphs.Length * 2);
            }

            var position = new Vector2((float)(x * scale), (float)(baseline * scale));
            _count += engine.Layout(text, position, (float)(size * scale), color, _glyphs.AsSpan(_count));
        }
    }
}
