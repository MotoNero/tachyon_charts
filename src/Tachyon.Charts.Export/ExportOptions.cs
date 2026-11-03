using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Export;

/// <summary>What an exported picture looks like.</summary>
public sealed record ExportOptions
{
    /// <summary>Gets the width in device-independent units, 96 per inch.</summary>
    public double Width { get; init; } = 1200;

    /// <summary>Gets the height in device-independent units, 96 per inch.</summary>
    public double Height { get; init; } = 800;

    /// <summary>Gets the resolution. The picture keeps its layout and gets more pixels.</summary>
    public double Dpi { get; init; } = 96;

    /// <summary>Gets a theme for this picture only, or <see langword="null"/> for the one of the chart.</summary>
    public Theme? Theme { get; init; }
}
