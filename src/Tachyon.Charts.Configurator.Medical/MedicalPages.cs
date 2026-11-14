namespace Tachyon.Charts.Configurator.Medical;

/// <summary>
/// The medical pages of the showroom as descriptions. They start with the phantoms the showroom starts with; the real
/// data the showroom can download are left out, since a description never holds medical files.
/// </summary>
internal static class MedicalPages
{
    private const string Dark = "TachyonDark";

    // The 3D charts of the pages with a choice of data name the data shown; the showroom starts with a phantom.
    private const string Head = "Head phantom (synthetic)";
    private const string Thorax = "Thorax phantom (synthetic)";

    public static IReadOnlyList<PageInfo> All { get; } =
    [
        new("Medical image", catalog => Image(catalog, "headAxial")),
        new("Medical measurements", catalog => Image(catalog, "headAxial", ("measurements", true))),
        new("Medical MPR", catalog => Spec(catalog, "medicalMpr", "headPhantom")),
        new("Medical analysis", catalog => Spec(catalog, "medicalAnalysis", "headPhantom", ("segmentation", false), ("window", "Brain"), ("title3d", Head))),
        new("Medical CT and MRI", catalog => Spec(catalog, "medicalMpr", "thoraxPhantom", ("segmentation", false), ("title3d", Thorax))),
        new("Medical oblique and curved", catalog => Spec(catalog, "medicalReformat", "thoraxPhantom", ("title3d", Thorax))),

        // The pictures of the gallery: one chart each, on the dark theme where the gallery draws one.
        new("MedicalAxial", catalog => Themed(Image(catalog, "headAxial"))),
        new("MedicalCoronal", catalog => Themed(Image(catalog, "headCoronal"))),
        new("MedicalSagittal", catalog => Themed(Image(catalog, "headSagittal"))),
        new("MedicalMeasurements", catalog => Themed(Image(catalog, "headAxial", ("measurements", true)))),
        new("MedicalAnalysisSlice", catalog => Analysis(catalog, "slice")),
        new("MedicalProfile", catalog => Analysis(catalog, "profile")),
        new("MedicalHistogram", catalog => Analysis(catalog, "histogram")),
        new("MedicalThoraxAxial", catalog => Plane(catalog, "Axial")),
        new("MedicalThoraxCoronal", catalog => Plane(catalog, "Coronal")),
        new("MedicalReformatSlice", catalog => Reformat(catalog, "axial")),
        new("MedicalOblique", catalog => Reformat(catalog, "oblique")),
        new("MedicalCurved", catalog => Reformat(catalog, "curved")),
    ];

    private static readonly string[] _analysisViews = ["volume", "slice", "profile", "histogram"];
    private static readonly string[] _reformatViews = ["volume", "axial", "oblique", "curved"];

    private static ChartSpec Spec(ChartCatalog catalog, string type, string sample, params (string Name, object Value)[] values)
    {
        var spec = catalog.CreateSpec(type);
        var series = spec.Series[0];
        series.Data.Sample = sample;

        // The showroom works its tools with keys; the bar of buttons in the images stays away.
        series.Parameters.Set("toolBar", false);
        foreach (var (name, value) in values)
        {
            switch (value)
            {
                case bool flag:
                    series.Parameters.Set(name, flag);
                    break;
                case double number:
                    series.Parameters.Set(name, number);
                    break;
                default:
                    series.Parameters.Set(name, (string)value);
                    break;
            }
        }

        return spec;
    }

    // An image of the head with the brain window, as the showroom shows it.
    private static ChartSpec Image(ChartCatalog catalog, string sample, params (string Name, object Value)[] values) =>
        Spec(catalog, "medicalImage", sample, [("window", "Brain"), .. values]);

    private static ChartSpec Themed(ChartSpec spec)
    {
        spec.Theme = Dark;
        return spec;
    }

    // One chart of the analysis on the head phantom with the brain window, as the gallery shows it.
    private static ChartSpec Analysis(ChartCatalog catalog, string view) =>
        Only(Spec(catalog, "medicalAnalysis", "headPhantom", ("segmentation", false), ("window", "Brain")), _analysisViews, view);

    // One plane of the thorax phantom with the lung window.
    private static ChartSpec Plane(ChartCatalog catalog, string plane) =>
        Spec(catalog, "medicalSlice", "thoraxPhantom", ("plane", plane), ("window", "Lung"), ("segmentation", false));

    // One chart of the oblique and curved sections on the thorax phantom.
    private static ChartSpec Reformat(ChartCatalog catalog, string view) => Only(Spec(catalog, "medicalReformat", "thoraxPhantom"), _reformatViews, view);

    private static ChartSpec Only(ChartSpec spec, string[] views, string shown)
    {
        foreach (var view in views)
        {
            spec.Series[0].Parameters.Set(view, view == shown);
        }

        return spec;
    }
}
