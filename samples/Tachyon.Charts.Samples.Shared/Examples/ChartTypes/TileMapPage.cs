using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Geo;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „Tile map“ of the showroom: tiles of a server over the countries of the world. Tachyon Charts sets no
/// server: the page takes it from the environment variables <c>TACHYON_TILE_URL</c> (with <c>{z}</c>, <c>{x}</c> and
/// <c>{y}</c>) and <c>TACHYON_TILE_ATTRIBUTION</c>, and keeps its tiles beside the other local data of the showroom.
/// Without them it shows a grid of tiles drawn locally; without a network the countries below show.
/// </summary>
public static class TileMapPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Tile map";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | Server: TACHYON_TILE_URL and TACHYON_TILE_ATTRIBUTION";

    /// <summary>Gets the source of the tiles: the server of the environment, or the grid drawn locally.</summary>
    /// <returns>The source.</returns>
    public static TileSource Source()
    {
        var (url, attribution) = (Environment.GetEnvironmentVariable("TACHYON_TILE_URL"), Environment.GetEnvironmentVariable("TACHYON_TILE_ATTRIBUTION"));
        return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(attribution)
            ? TileSource.Grid()
            : new TileSource(url, attribution) { CacheFolder = Path.Combine(Common.LargeFileDemo.Folder, "..", "Tiles"), UserAgent = "Tachyon.Charts.Showroom" };
    }

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title(SampleText.T("Tiles from a server of your choice"))
        .Map(GeoProjection.WebMercator)
        .Tiles(Source())
        .Modifiers(m => m.ZoomPan().RubberBand());
}
