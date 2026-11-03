using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Fluent;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Annotations“ of the showroom.</summary>
public static class AnnotationsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Annotations";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Edit;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, -1.5, 1.5)
        .AddLine(Sample(Math.Sin), s => s.Thickness(2))
        .AddOverlay(chart => new HorizontalLineAnnotation(chart.Series[0].YAxis) { Y = 1.1, Label = "Limit", IsEditable = true })
        .AddOverlay(chart => new VerticalLineAnnotation(chart.Series[0].XAxis) { X = 4, Label = "Start", IsEditable = true })
        .Modifiers(m => m.ZoomPan().AnnotationEdit());
}
