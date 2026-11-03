using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Instruments;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>Charts for the sample apps that can be operated with the mouse.</summary>
public static class InstrumentPages
{
    /// <summary>Gets the pages by name, with their hints.</summary>
    public static IReadOnlyDictionary<string, (string Hint, Action<ChartBuilder> Configure)> All { get; } = new Dictionary<string, (string, Action<ChartBuilder>)>
    {
        [NetworkPage.Name] = (NetworkPage.Hint, NetworkPage.Configure),
        [SmithPage.Name] = (SmithPage.Hint, SmithPage.Configure),
        [ParallelPage.Name] = (ParallelPage.Hint, ParallelPage.Configure),
    };

    /// <summary>Gets the configuration of a page, of the instruments or of a chart type.</summary>
    /// <param name="name">The page.</param>
    /// <param name="page">The page.</param>
    /// <returns>Whether the page exists.</returns>
    public static bool TryGet(string name, out (string Hint, Action<ChartBuilder> Configure) page)
    {
        if (All.TryGetValue(name, out page))
        {
            return true;
        }

        foreach (var (typeName, hint, configure) in ChartTypePages.All)
        {
            if (typeName == name)
            {
                page = (hint, configure);
                return true;
            }
        }

        return false;
    }

}
