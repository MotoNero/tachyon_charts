using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>An entry of the page list of the sample apps.</summary>
/// <param name="Id">The fixed name, also on the command line.</param>
/// <param name="Text">The English text, the key of the translation.</param>
/// <param name="IsHeader">Whether this is a group heading.</param>
public sealed record SamplePage(string Id, string Text, bool IsHeader = false)
{
    /// <inheritdoc />
    public override string ToString() => SampleText.T(Text);
}

/// <summary>The texts of the sample apps. English is the key, each further language a table.</summary>
public static class SampleText
{
    private static readonly Dictionary<string, string> _german = new()
    {
        ["Showroom"] = "Showroom",
        ["Interactive"] = "Interaktiv",
        ["Instruments"] = "Messgeräte",
        ["Gallery"] = "Galerie",
        ["Chart types"] = "Diagrammtypen",
        ["3D"] = "3D",
        ["Axis box"] = "Achsenbox",
        ["Cut, live"] = "Schnitt, live",
        ["Drag the orange outline: the curve below follows | Click in 3D or Ctrl+click below: marker in both, on a marker removes it | Delete: newest marker | Drag: turn"] = "Orangen Umriss ziehen: die Kurve unten folgt | Klick in 3D oder Strg+Klick unten: Marker in beiden, auf einem Marker entfernt ihn | Entf: neuester Marker | Ziehen: drehen",
        ["Drag the red mask line below: the fence in 3D follows | Click in 3D: marker in both, on a marker removes it | Ctrl+wheel below: level, the box follows | Drag: turn"] = "Rote Maskenlinie unten ziehen: der Zaun in 3D folgt | Klick in 3D: Marker in beiden, auf einem Marker entfernt ihn | Strg+Rad unten: Pegel, die Box folgt | Ziehen: drehen",
        ["Surface of equal value 512³"] = "Isofläche 512³",
        ["Drag the white bar at the top right: value of the surface | Drag the orange outline: move the plane | Drag: turn | Wheel: zoom"] = "Weißen Strich oben rechts ziehen: Wert der Fläche | Orangen Umriss ziehen: Ebene verschieben | Ziehen: drehen | Rad: Zoom",
        ["Maximum projection 512³"] = "Maximum-Projektion 512³",
        ["Through a volume 512³"] = "Durchleuchtung 512³",
        ["Objects inside a volume"] = "Objekte im Volumen",
        ["Drag the white points at the top right: color and opacity | Drag: turn | Wheel: zoom | Pointer: values"] = "Weiße Punkte oben rechts ziehen: Farbe und Deckkraft | Ziehen: drehen | Rad: Zoom | Zeiger: Werte",
        ["Volume slices 512³"] = "Volumen-Schnittbilder 512³",
        ["Cloud, live"] = "Punktwolke, live",
        ["Cloud of 20 million points"] = "Punktwolke mit 20 Millionen Punkten",
        ["Cloud of 100 million points"] = "Punktwolke mit 100 Millionen Punkten",
        ["Cut planes"] = "Schnittebenen",
        ["One plane, all kinds"] = "Eine Ebene, alle Typen",
        ["Cut through a surface"] = "Schnitt durch eine Fläche",
        ["Drag the orange outline: move the plane | Drag: turn | Wheel: zoom | Pointer: values"] = "Orangen Umriss ziehen: Ebene verschieben | Ziehen: drehen | Rad: Zoom | Zeiger: Werte",
        ["Columns"] = "Säulen",
        ["1 million columns"] = "1 Million Säulen",
        ["Scatter of 3000 points"] = "Punktwolke mit 3000 Punkten",
        ["Scatter of 10 million points"] = "Punktwolke mit 10 Millionen Punkten",
        ["Path, live"] = "Bahnkurve, live",
        ["Path of 200 000 points"] = "Bahnkurve mit 200 000 Punkten",
        ["Antenna pattern"] = "Antennendiagramm",
        ["Antenna cut"] = "Antennenschnitt",
        ["Map from measurements"] = "Kennfeld aus Messpunkten",
        ["Height lines"] = "Höhenlinien",
        ["Waterfall, live"] = "Wasserfall, live",
        ["Surface 4096 × 4096"] = "Fläche 4096 × 4096",
        ["Surface with light"] = "Fläche mit Licht",
        ["Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it"] = "Ziehen: drehen | Shift+Ziehen: verschieben | Rad: Zoom | Pfeiltasten: drehen | Doppelklick, Pos1: Start | Zeiger: Werte | Klick: Marker, Entf entfernt ihn",
        ["Line"] = "Linie",
        ["Step line"] = "Stufenlinie",
        ["Spline"] = "Spline",
        ["Scatter"] = "Punktwolke",
        ["Bubble"] = "Blasen",
        ["Area"] = "Fläche",
        ["Stacked area"] = "Gestapelte Fläche",
        ["Band"] = "Band",
        ["Bars"] = "Balken",
        ["Stacked bars"] = "Gestapelte Balken",
        ["Horizontal bars"] = "Waagerechte Balken",
        ["Error bars"] = "Fehlerbalken",
        ["Stems"] = "Stem",
        ["Histogram"] = "Histogramm",
        ["Box plot"] = "Box-Plot",
        ["Violin"] = "Violin",
        ["Categories"] = "Kategorien",
        ["Logarithmic axis"] = "Log-Achse",
        ["Polar"] = "Polar",
        ["Radar"] = "Radar",
        ["Ternary"] = "Ternary",
        ["Annotations"] = "Annotationen",
        ["Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all"] = "Rad: Zoom | Ziehen: Pan | Strg+Ziehen: in Rechteck zoomen | Doppelklick: alles",
        ["Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | Pointer: values"] = "Rad: Zoom | Ziehen: Pan | Strg+Ziehen: in Rechteck zoomen | Doppelklick: alles | Zeiger: Werte",
        ["Wheel: zoom the radius"] = "Rad: Radius zoomen",
        ["Pointer: values"] = "Zeiger: Werte",
        ["Drag an annotation: move it | Wheel: zoom | Drag: pan"] = "Annotation ziehen: verschieben | Rad: Zoom | Ziehen: Pan",
        ["Live chart"] = "Live-Chart",
        ["Linked charts"] = "Gekoppelte Charts",
        ["Rasters"] = "Raster",
        ["Oscilloscope"] = "Oszilloskop",
        ["Spectrum analyzer"] = "Spektrumanalysator",
        ["Network analyzer"] = "Netzwerkanalysator",
        ["Drag a marker: move it | Ctrl+click: add or remove a marker | Wheel: zoom | Drag: pan"] =
            "Marker ziehen: verschieben | Strg+Klick: Marker hinzufügen oder entfernen | Rad: Zoom | Ziehen: Pan",
        ["Select points: off"] = "Punkte auswählen: aus",
        ["Select points: on"] = "Punkte auswählen: an",
        ["{0:N0} points selected. CSV exports only these. Esc clears the selection."] = "{0:N0} Punkte ausgewählt. CSV exportiert nur diese. Esc hebt die Auswahl auf.",
        ["Circle the points to select with the mouse. Then CSV exports only these."] = "Punkte mit der Maus umkreisen. Danach exportiert CSV nur diese.",
        ["Smith chart"] = "Smith-Chart",
        ["Parallel coordinates"] = "Parallele Koordinaten",
        ["Theme"] = "Theme",
        ["Language"] = "Sprache",
        ["Scene default"] = "Standard der Szene",
        ["Reset Y range"] = "Y-Bereich zurücksetzen",
        ["PNG 300 dpi"] = "PNG 300 dpi",
        ["Copy image"] = "Bild kopieren",
        ["Copy data"] = "Daten kopieren",
        ["Print"] = "Drucken",
        ["Save as PNG"] = "Als PNG speichern",
        ["Save as SVG"] = "Als SVG speichern",
        ["Save as PDF"] = "Als PDF speichern",
        ["Save as CSV"] = "Als CSV speichern",
        ["Saved: "] = "Gespeichert: ",
        ["Saved"] = "Gespeichert",
        ["Show"] = "Anzeigen",
        ["Video"] = "Video",
        ["Recording the video …"] = "Video wird aufgenommen …",
        ["OK"] = "OK",
        ["The file is saved:"] = "Die Datei ist gespeichert:",
        ["Source code (F11)"] = "Quellcode (F11)",
        ["Source code"] = "Quellcode",
        ["Copy"] = "Kopieren",
        ["Save file"] = "Datei speichern",
        ["The code is on the clipboard"] = "Der Code ist in der Zwischenablage",
        ["Export failed: "] = "Export fehlgeschlagen: ",
        ["Failed: "] = "Fehlgeschlagen: ",
        ["The image is on the clipboard"] = "Das Bild liegt in der Zwischenablage",
        ["The CSV is on the clipboard"] = "Die CSV liegt in der Zwischenablage",
        ["Printed or canceled"] = "Gedruckt oder abgebrochen",
        ["Trigger Auto"] = "Trigger Auto",
        ["Trigger Normal"] = "Trigger Normal",
        ["Trigger Single"] = "Trigger Single",
        ["Persistence off"] = "Persistenz aus",
        ["Variable"] = "Variabel",
        ["Infinite"] = "Unendlich",
        ["Color grade"] = "Color Grade",
        ["Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Shift+drag: select points | Alt+drag: measure | Double click: all | Right click: menu"] =
            "Rad: Zoom | Ziehen: Pan | Strg+Ziehen: in Rechteck zoomen | Shift+Ziehen: Punkte auswählen | Alt+Ziehen: Messen | Doppelklick: alles | Rechtsklick: Menü",
        ["Wheel: zoom | Drag: pan | Wheel over the color bar: color range | List: colormap"] =
            "Rad: Zoom | Ziehen: Pan | Rad über der Farbleiste: Farbbereich | Liste: Farbkarte",
        ["Zoom and crosshair apply to all three charts"] = "Zoom und Fadenkreuz gelten für alle drei Charts",
        ["Still image of the scene. The pages above it are interactive."] = "Standbild der Szene. Die Seiten darüber sind interaktiv.",
        ["Drag at the left: channel | at the right: trigger | else: time | Wheel: time/div | Left, on V/div or Ctrl+wheel: volts/div"] =
            "Links ziehen: Kanal | Rechts ziehen: Trigger | Sonst: Zeit | Rad: Zeit/div | Links, auf V/div oder Strg+Rad: Volt/div",
        ["Wheel: frequency | Left: dB/div | Ctrl+wheel: reference | Drag: pan | Click: marker | Ctrl+click: add or remove a marker | Double click: peak"] =
            "Rad: Frequenz | Links: dB/div | Strg+Rad: Referenz | Ziehen: Pan | Klick: Marker | Strg+Klick: Marker hinzufügen oder entfernen | Doppelklick: Peak",
        ["Click or drag: the marker follows the curve"] = "Klicken oder ziehen: Der Marker folgt der Kurve",
        ["Drag a name: move the axis | Drag along an axis: filter | Double click: all rows"] =
            "Am Namen ziehen: Achse verschieben | An einer Achse ziehen: filtern | Doppelklick: alle Zeilen",
        ["Live: 50,000 points per second"] = "Live: 50 000 Punkte pro Sekunde",
        ["Time in s"] = "Zeit in s",
        ["Value"] = "Wert",
        ["Fixed"] = "Fest",
        ["Level"] = "Pegel",
        ["Height"] = "Höhe",
        ["Points"] = "Punkte",
        ["Heatmap 4096 × 4096"] = "Heatmap 4096 × 4096",
        ["Spectrogram, 60 rows per second"] = "Spektrogramm, 60 Zeilen pro Sekunde",
        ["Contour, filled"] = "Contour, gefüllt",
        ["5 million points: zoom in for markers"] = "5 Mio. Punkte: Hineinzoomen zeigt Marker",
    };

    /// <summary>Gets the English names of the languages, in the order of <see cref="ChartLanguage"/>.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["English", "Deutsch"];

    /// <summary>Translates a text into the current language.</summary>
    /// <param name="english">The English text.</param>
    /// <returns>The text.</returns>
    public static string T(string english) =>
        ChartText.Language == ChartLanguage.German && _german.TryGetValue(english, out var text) ? text : english;

    /// <summary>Gets a value indicating whether a text has a translation.</summary>
    /// <param name="english">The English text.</param>
    /// <returns>Whether it has one.</returns>
    public static bool Knows(string english) => _german.ContainsKey(english);

    /// <summary>Creates the page list.</summary>
    /// <param name="hasLinkedCharts">Whether the app has the page with linked charts.</param>
    /// <returns>The entries with their headings.</returns>
    public static IReadOnlyList<SamplePage> CreatePages(bool hasLinkedCharts)
    {
        var pages = new List<SamplePage>
        {
            new("Interactive", "Interactive", true),
            new("Live-Chart (interaktiv)", "Live chart"),
        };
        if (hasLinkedCharts)
        {
            pages.Add(new SamplePage("Gruppe (3 Charts)", "Linked charts"));
        }

        pages.Add(new SamplePage("Raster (interaktiv)", "Rasters"));
        pages.Add(new SamplePage("Instruments", "Instruments", true));
        pages.Add(new SamplePage("Oszilloskop (interaktiv)", "Oscilloscope"));
        pages.Add(new SamplePage("Spektrum (interaktiv)", "Spectrum analyzer"));
        pages.Add(new SamplePage("Network (interaktiv)", "Network analyzer"));
        pages.Add(new SamplePage("Smith (interaktiv)", "Smith chart"));
        pages.Add(new SamplePage("Parallel (interaktiv)", "Parallel coordinates"));
        pages.Add(new SamplePage("Chart types", "Chart types", true));
        pages.AddRange(ChartTypePages.All.Select(page => new SamplePage(page.Name, page.Name)));
        pages.Add(new SamplePage("3D", "3D", true));
        pages.AddRange(Pages3D.All.Select(page => new SamplePage(page.Name, page.Name)));
        pages.Add(new SamplePage("Gallery", "Gallery", true));
        pages.AddRange(SceneCatalog.All.Select(scene => new SamplePage(scene.Name, scene.Name)));
        return pages;
    }
}
