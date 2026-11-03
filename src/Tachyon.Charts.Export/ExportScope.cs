using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export;

/// <summary>
/// Prepares a chart for a picture and puts it back afterwards, so that the display stays as it is.
/// </summary>
internal readonly ref struct ExportScope
{
    private const int Passes = 3;

    private readonly ChartSurface _chart;
    private readonly Theme? _theme;
    private readonly bool _showsDiagnostics;
    private readonly DipRect? _zoomRectangle;

    public ExportScope(ChartSurface chart, ExportOptions options)
    {
        _chart = chart;
        _theme = chart.Theme;
        _showsDiagnostics = chart.ShowDiagnostics;
        _zoomRectangle = chart.ZoomRectangle;
        chart.ShowDiagnostics = false;
        chart.ZoomRectangle = null;
        if (options.Theme is { } theme)
        {
            chart.Theme = theme;
        }
    }

    /// <summary>Renders until the layout has settled: a scale can depend on the size of the picture.</summary>
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        for (var pass = 0; pass < Passes; pass++)
        {
            _chart.Render(device, target);
        }
    }

    public void Dispose()
    {
        _chart.Theme = _theme;
        _chart.ShowDiagnostics = _showsDiagnostics;
        _chart.ZoomRectangle = _zoomRectangle;
        _chart.Invalidate();
    }
}
