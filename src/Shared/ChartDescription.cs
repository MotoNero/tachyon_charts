using System.Globalization;
using System.Text;

#if AVALONIA
namespace Tachyon.Charts.Avalonia;
#else
namespace Tachyon.Charts.Wpf;
#endif

/// <summary>Puts a chart into words for screen readers and test tools.</summary>
internal static class ChartDescription
{
    public static string Describe(IEnumerable<ChartAxis> axes, int seriesCount)
    {
        var text = new StringBuilder();
        foreach (var axis in axes)
        {
            var range = axis.VisibleRange;
            text.Append(CultureInfo.InvariantCulture, $"{axis.Title ?? axis.Id ?? "Axis"}: {range.Min:G6} to {range.Max:G6}. ");
        }

        return text.Append(CultureInfo.InvariantCulture, $"{seriesCount} series.").ToString();
    }
}
