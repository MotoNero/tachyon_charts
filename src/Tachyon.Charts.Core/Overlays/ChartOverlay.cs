using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>
/// Something drawn above the series, in device-independent units. It belongs to the thread of the chart.
/// </summary>
public abstract class ChartOverlay
{
    /// <summary>Gets a version that grows with every change that needs a new frame.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a version of something else the overlay shows, which also needs new frames.</summary>
    protected internal virtual long ExternalVersion => 0;

    /// <summary>Draws the overlay in the overlay pass.</summary>
    /// <param name="canvas">Where to draw.</param>
    protected internal abstract void Draw(OverlayCanvas canvas);

    /// <summary>Requests a new frame after a change.</summary>
    protected void OnChanged() => Version++;
}

/// <summary>
/// The drawing surface of overlays. Coordinates are device-independent units relative to the chart.
/// </summary>
public sealed class OverlayCanvas
{
    private const float MinimumLineLength = 1e-3f;

    private readonly ITextEngine _textEngine;
    private ICommandList _commands = null!;
    private ChartLayoutResult _layout = null!;
    private double _scale = 1;
    private double _chartWidth;
    private IRenderDevice _device = null!;
    private readonly Dictionary<object, IGpuTexture> _textures = [];
    private GlyphInstance[] _glyphs = new GlyphInstance[512];
    private int _glyphCount;

    internal OverlayCanvas(ITextEngine textEngine)
    {
        _textEngine = textEngine;
    }

    /// <summary>Gets the layout of the chart.</summary>
    public ChartLayoutResult Layout => _layout;

    /// <summary>Gets or sets the area outside of which rectangles are cut off, or <see langword="null"/> for none.</summary>
    public DipRect? Clip { get; set; }

    /// <summary>Gets the color of lines and marks of the current theme.</summary>
    public Rgba8 AccentColor { get; private set; }

    /// <summary>Gets the color of text of the current theme.</summary>
    public Rgba8 TextColor { get; private set; }

    /// <summary>Gets the background of the chart in the current theme.</summary>
    public Rgba8 BackgroundColor { get; private set; }

    /// <summary>Gets the font size of labels.</summary>
    public float LabelSize { get; private set; }

    internal ReadOnlySpan<GlyphInstance> Glyphs => _glyphs.AsSpan(0, _glyphCount);

    /// <summary>Gets the height of a line of text.</summary>
    /// <param name="size">The font size.</param>
    /// <returns>The height.</returns>
    public float GetLineHeight(float size) => _textEngine.GetLineHeight(size);

    /// <summary>Measures text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The font size.</param>
    /// <returns>The width.</returns>
    public float MeasureWidth(string text, float size) => _textEngine.MeasureWidth(text, size);

    /// <summary>Measures text from a buffer, without a string.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The font size.</param>
    /// <returns>The width.</returns>
    public float MeasureWidth(ReadOnlySpan<char> text, float size) => _textEngine.MeasureWidth(text, size);

    /// <summary>Draws text from a buffer, without a string, for marks that change every frame.</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="baseline">The baseline.</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The color.</param>
    public void DrawText(ReadOnlySpan<char> text, double x, double baseline, float size, Rgba8 color)
    {
        if (_glyphCount + text.Length > _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(_glyphs.Length * 2, _glyphCount + text.Length));
        }

        var position = new Vector2((float)(x * _scale), (float)(baseline * _scale));
        _glyphCount += _textEngine.Layout(text, position, (float)(size * _scale), color, _glyphs.AsSpan(_glyphCount));
    }

    /// <summary>Finds where a value lies on an axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="value">The value.</param>
    /// <param name="position">Receives the position along the axis, X or Y in chart coordinates.</param>
    /// <returns>Whether the axis is part of the chart.</returns>
    public bool TryGetPosition(AxisBase axis, double value, out double position)
    {
        // An index loop: an enumerator of a list interface would allocate on every call.
        var axes = _layout.Axes;
        for (var i = 0; i < axes.Count; i++)
        {
            var placement = axes[i];
            if (placement.Axis == axis)
            {
                position = ToTransform(placement).ToPixel(value);
                return true;
            }
        }

        position = 0;
        return false;
    }

    /// <summary>
    /// Finds the place of a data point, also in a chart whose X axis runs down: each value goes along the direction of
    /// its own axis.
    /// </summary>
    /// <param name="xAxis">The X axis.</param>
    /// <param name="yAxis">The Y axis.</param>
    /// <param name="x">The X value.</param>
    /// <param name="y">The Y value.</param>
    /// <param name="point">Receives the place in device-independent units.</param>
    /// <returns>Whether both axes are part of the chart.</returns>
    public bool TryGetPoint(AxisBase xAxis, AxisBase yAxis, double x, double y, out Vector2 point)
    {
        ArgumentNullException.ThrowIfNull(xAxis);
        ArgumentNullException.ThrowIfNull(yAxis);
        point = default;
        if (!TryGetPosition(xAxis, x, out var along) || !TryGetPosition(yAxis, y, out var across))
        {
            return false;
        }

        point = xAxis.IsHorizontal ? new Vector2((float)along, (float)across) : new Vector2((float)across, (float)along);
        return true;
    }

    /// <summary>Finds the value at a position of an axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="position">The position along the axis, X or Y in chart coordinates.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns>Whether the axis is part of the chart.</returns>
    public bool TryGetValue(AxisBase axis, double position, out double value)
    {
        // An index loop: an enumerator of a list interface would allocate on every call.
        var axes = _layout.Axes;
        for (var i = 0; i < axes.Count; i++)
        {
            var placement = axes[i];
            if (placement.Axis == axis)
            {
                value = ToTransform(placement).ToData(position);
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Finds the pane at a position.</summary>
    /// <param name="position">The position.</param>
    /// <param name="pane">Receives the area of the pane.</param>
    /// <returns>Whether there is a pane.</returns>
    public bool TryGetPane(Vector2 position, out DipRect pane)
    {
        // An index loop: an enumerator of a list interface would allocate every frame.
        var panes = _layout.Panes;
        for (var i = 0; i < panes.Count; i++)
        {
            var candidate = panes[i];
            if (position.Y >= candidate.Y && position.Y <= candidate.Bottom && position.X >= candidate.X && position.X <= candidate.Right)
            {
                pane = candidate;
                return true;
            }
        }

        pane = default;
        return false;
    }

    /// <summary>Fills a rectangle.</summary>
    /// <param name="area">The rectangle.</param>
    /// <param name="color">The color.</param>
    public void FillRectangle(DipRect area, Rgba8 color)
    {
        if (Clip is { } clip)
        {
            var left = Math.Max(area.X, clip.X);
            var top = Math.Max(area.Y, clip.Y);
            var right = Math.Min(area.Right, clip.Right);
            var bottom = Math.Min(area.Bottom, clip.Bottom);
            if (right <= left || bottom <= top)
            {
                return;
            }

            area = new DipRect(left, top, right - left, bottom - top);
        }

        _commands.FillRectangle(area.ToPixels(_scale), color);
    }

    /// <summary>Draws the border of a rectangle.</summary>
    /// <param name="area">The rectangle.</param>
    /// <param name="color">The color.</param>
    public void DrawRectangle(DipRect area, Rgba8 color)
    {
        FillRectangle(new DipRect(area.X, area.Y, area.Width, 1), color);
        FillRectangle(new DipRect(area.X, area.Bottom - 1, area.Width, 1), color);
        FillRectangle(new DipRect(area.X, area.Y, 1, area.Height), color);
        FillRectangle(new DipRect(area.Right - 1, area.Y, 1, area.Height), color);
    }

    /// <summary>Fills a triangle. The clip does not apply.</summary>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <param name="color">The color.</param>
    public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color)
    {
        var scale = (float)_scale;
        _commands.FillTriangle(a * scale, b * scale, c * scale, color);
    }

    /// <summary>Draws a straight line. Lines along an axis stay on whole pixels and respect the clip.</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <param name="width">The width.</param>
    /// <param name="color">The color.</param>
    public void DrawLine(Vector2 from, Vector2 to, float width, Rgba8 color)
    {
        var delta = to - from;
        var length = delta.Length();
        if (length < MinimumLineLength)
        {
            return;
        }

        if (delta.X == 0 || delta.Y == 0)
        {
            var left = Math.Min(from.X, to.X);
            var top = Math.Min(from.Y, to.Y);
            FillRectangle(
                delta.X == 0
                    ? new DipRect(left, top, width, Math.Abs(delta.Y))
                    : new DipRect(left, top, Math.Abs(delta.X), width),
                color);
            return;
        }

        var normal = new Vector2(-delta.Y, delta.X) / length * (width / 2);
        FillTriangle(from + normal, to + normal, to - normal, color);
        FillTriangle(from + normal, to - normal, from - normal, color);
    }

    /// <summary>
    /// Draws pixels. The texture stays on the device for the owner, until the device or the chart ends.
    /// </summary>
    /// <param name="owner">What the pixels belong to.</param>
    /// <param name="pixels">The pixels, top row first.</param>
    /// <param name="size">The size of the image.</param>
    /// <param name="area">Where to draw.</param>
    public void DrawImage(object owner, Rgba8[] pixels, PixelSize size, DipRect area)
    {
        if (!_textures.TryGetValue(owner, out var texture))
        {
            texture = _device.CreateTexture(new TextureDesc(size, TextureFormat.Rgba8Unorm));
            texture.Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
            _textures.Add(owner, texture);
        }

        _commands.DrawImage(texture, area.ToPixels(_scale));
    }

    /// <summary>Draws text centered on a point and turned by an angle, for labels along a line.</summary>
    /// <param name="text">The text.</param>
    /// <param name="center">The middle of the text.</param>
    /// <param name="angle">The angle in radians, clockwise on the screen.</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The color.</param>
    internal void DrawTurnedText(string text, Vector2 center, float angle, float size, Rgba8 color)
    {
        var first = _glyphCount;
        var width = MeasureWidth(text, size);
        DrawText(text, center.X - (width / 2), center.Y + (GetLineHeight(size) * 0.3), size, color);
        var pivot = center * (float)_scale;
        for (var i = first; i < _glyphCount; i++)
        {
            _glyphs[i] = _glyphs[i] with { Rotation = angle, Pivot = pivot };
        }
    }

    /// <summary>Draws text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="baseline">The baseline.</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The color.</param>
    public void DrawText(string text, double x, double baseline, float size, Rgba8 color)
    {
        if (_glyphCount + text.Length > _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(_glyphs.Length * 2, _glyphCount + text.Length));
        }

        var position = new Vector2((float)(x * _scale), (float)(baseline * _scale));
        _glyphCount += _textEngine.Layout(text, position, (float)(size * _scale), color, _glyphs.AsSpan(_glyphCount));
    }

    /// <summary>Draws text on a colored plate, kept inside the chart.</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The left edge of the plate.</param>
    /// <param name="y">The top edge of the plate.</param>
    /// <param name="background">The plate color.</param>
    /// <param name="foreground">The text color.</param>
    /// <returns>Where the plate lies.</returns>
    public DipRect DrawLabel(string text, double x, double y, Rgba8 background, Rgba8 foreground)
    {
        const double Padding = 4;
        const double Margin = 2;
        var width = MeasureWidth(text, LabelSize) + (Padding * 2);
        var height = GetLineHeight(LabelSize) + 2;
        var left = Math.Max(Math.Min(x, _chartWidth - width - Margin), Margin);
        var plate = new DipRect(left, y, width, height);

        var clip = Clip;
        Clip = null;
        FillRectangle(plate, background);
        Clip = clip;
        DrawText(text, left + Padding, y + height - (height * 0.28), LabelSize, foreground);
        return plate;
    }

    /// <summary>Gets the width of the chart in device-independent units.</summary>
    internal double ChartWidth => _chartWidth;

    /// <summary>Gets or sets the height of the chart in device-independent units.</summary>
    internal double ChartHeight { get; set; }

    internal void Begin(IRenderDevice device, ICommandList commands, ChartLayoutResult layout, double scale, double chartWidth, Rgba8 accent, Rgba8 text, Rgba8 background, float labelSize)
    {
        if (_device != device)
        {
            _textures.Clear();
            _device = device;
        }

        _commands = commands;
        _layout = layout;
        _scale = scale;
        _chartWidth = chartWidth;
        _glyphCount = 0;
        AccentColor = accent;
        TextColor = text;
        BackgroundColor = background;
        LabelSize = labelSize;
        Clip = null;
    }

    private static Numerics.LinearAxisTransform ToTransform(AxisPlacement placement) => placement.Axis.IsHorizontal
        ? placement.Axis.CreateTransform(placement.Bounds.X, placement.Bounds.Width)
        : placement.Axis.CreateTransform(placement.Bounds.Y, placement.Bounds.Height);
}
