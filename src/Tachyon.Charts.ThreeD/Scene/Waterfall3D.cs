using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>How a waterfall is drawn.</summary>
public enum WaterfallStyle
{
    /// <summary>A closed surface.</summary>
    Surface,

    /// <summary>A curve per row, which hides what lies behind it.</summary>
    Lines,
}

/// <summary>
/// Rows one behind the other: the newest row stands in front, older ones move back. Rows arrive from any
/// thread through the data, and only new rows reach the GPU.
/// </summary>
/// <param name="data">The ring of rows. Its length gives the depth.</param>
/// <param name="x">The range of the columns on the first axis.</param>
/// <param name="y">The range of the depth on the second axis: the oldest row at the end, the newest at the start.</param>
public sealed class Waterfall3D(SpectrogramData data, DoubleRange x, DoubleRange y)
{
    /// <summary>Gets the ring of rows.</summary>
    public SpectrogramData Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets the range of the columns.</summary>
    public DoubleRange X { get; } = x;

    /// <summary>Gets the range of the depth.</summary>
    public DoubleRange Y { get; } = y;

    /// <summary>Gets the kind of drawing.</summary>
    public WaterfallStyle Style { get; init; }

    /// <summary>Gets the distance of the curves in rows, for <see cref="WaterfallStyle.Lines"/>.</summary>
    public int LineSpacing { get; init; } = 8;

    /// <summary>Gets the colors.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Inferno;

    /// <summary>Gets the values at the ends of the colormap, or <see langword="null"/> for the range of the third axis.</summary>
    public DoubleRange? ColorRange { get; init; }

    /// <summary>Gets how the surface takes the light.</summary>
    public Material3D Material { get; init; } = new();

    /// <summary>
    /// Gets the spectrum display the rows come from, or <see langword="null"/>. With a display the waterfall shows its
    /// mask and markers, its levels set the height of the box, and a click in 3D sets a marker of the display.
    /// </summary>
    public SpectrumDisplay? Display { get; init; }

    /// <summary>Connects to a spectrum display: every trace becomes a row, the first axis takes its frequency range.</summary>
    /// <param name="display">The display.</param>
    /// <param name="depth">The range of the depth.</param>
    /// <returns>The waterfall.</returns>
    public static Waterfall3D From(SpectrumDisplay display, DoubleRange depth)
    {
        ArgumentNullException.ThrowIfNull(display);
        return new Waterfall3D(display.History, new DoubleRange(display.HistoryStart, display.HistoryStop), depth) { Display = display };
    }

    /// <summary>Gets the slot of the ring that the first row of the picture shows, which is the oldest row.</summary>
    /// <param name="total">The number of rows appended so far.</param>
    /// <param name="rows">The length of the ring.</param>
    /// <returns>The slot.</returns>
    internal static uint OldestSlot(long total, int rows) => (uint)(total % rows);

    /// <summary>Gets the first row that still has to reach the GPU.</summary>
    /// <param name="uploaded">The number of rows that reached it.</param>
    /// <param name="total">The number of rows appended so far.</param>
    /// <param name="rows">The length of the ring.</param>
    /// <returns>The row: rows that the ring has dropped already are skipped.</returns>
    internal static long FirstMissingRow(long uploaded, long total, int rows) => Math.Max(uploaded, total - rows);
}
