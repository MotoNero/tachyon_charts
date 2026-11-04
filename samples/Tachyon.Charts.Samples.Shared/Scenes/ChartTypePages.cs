using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>The chart types of the showroom, in the order of the list. Each one lives in a file of its own under Examples/ChartTypes.</summary>
public static class ChartTypePages
{
    /// <summary>Gets the pages by their English name, in the order of the list.</summary>
    public static IReadOnlyList<(string Name, string Hint, Action<ChartBuilder> Configure)> All { get; } =
    [
        (LinePage.Name, LinePage.Hint, LinePage.Configure),
        (StepLinePage.Name, StepLinePage.Hint, StepLinePage.Configure),
        (SplinePage.Name, SplinePage.Hint, SplinePage.Configure),
        (ScatterPage.Name, ScatterPage.Hint, ScatterPage.Configure),
        (BubblePage.Name, BubblePage.Hint, BubblePage.Configure),
        (AreaPage.Name, AreaPage.Hint, AreaPage.Configure),
        (StackedAreaPage.Name, StackedAreaPage.Hint, StackedAreaPage.Configure),
        (BandPage.Name, BandPage.Hint, BandPage.Configure),
        (BarsPage.Name, BarsPage.Hint, BarsPage.Configure),
        (StackedBarsPage.Name, StackedBarsPage.Hint, StackedBarsPage.Configure),
        (HorizontalBarsPage.Name, HorizontalBarsPage.Hint, HorizontalBarsPage.Configure),
        (ErrorBarsPage.Name, ErrorBarsPage.Hint, ErrorBarsPage.Configure),
        (StemsPage.Name, StemsPage.Hint, StemsPage.Configure),
        (HistogramPage.Name, HistogramPage.Hint, HistogramPage.Configure),
        (BoxPlotPage.Name, BoxPlotPage.Hint, BoxPlotPage.Configure),
        (ViolinPage.Name, ViolinPage.Hint, ViolinPage.Configure),
        (CategoriesPage.Name, CategoriesPage.Hint, CategoriesPage.Configure),
        (LogarithmicAxisPage.Name, LogarithmicAxisPage.Hint, LogarithmicAxisPage.Configure),
        (PolarPage.Name, PolarPage.Hint, PolarPage.Configure),
        (AntennaCutPage.Name, AntennaCutPage.Hint, AntennaCutPage.Configure),
        (CutThroughASurfacePage.Name, CutThroughASurfacePage.Hint, CutThroughASurfacePage.Configure),
        (RadarPage.Name, RadarPage.Hint, RadarPage.Configure),
        (PiePage.Name, PiePage.Hint, PiePage.Configure),
        (DonutPage.Name, DonutPage.Hint, DonutPage.Configure),
        (TernaryPage.Name, TernaryPage.Hint, TernaryPage.Configure),
        (AnnotationsPage.Name, AnnotationsPage.Hint, AnnotationsPage.Configure),
    ];
}
