namespace Tachyon.Charts.Configurator;

/// <summary>
/// The pages of the showroom as descriptions, so that "Open in configurator" opens each example completely: the same
/// charts, series, axes, interactions and data of the same form.
/// </summary>
internal static class ShowroomPages
{
    private const string Thickness = "thickness";
    private const string Zoompan = "zoompan";
    private const string Color50AAF0 = "#50AAF0";
    private const string Color = "color";
    private const string TimeInS = "Time in s";
    private const string Rubberband = "rubberband";
    private const string ValueName = "Value";
    private const string TurboName = "Turbo";
    private const string Colormap = "colormap";
    private const string InfernoName = "Inferno";
    private const string Tooltip = "tooltip";
    private const string Hline = "hline";
    private const string Label = "label";
    private const string WaveKey = "wave13";
    private const string ColorF05A5AFF = "#F05A5AFF";
    private const string LabelPlace = "labelPlace";
    private const string Color6ED282FF = "#6ED282FF";
    private const string TachyonDarkName = "TachyonDark";
    private const string CrosshairKey = "crosshair";
    private const string ScatterKey = "scatter";
    private const string Levels = "levels";
    private const string Color6ED282 = "#6ED282";
    private const string ColorF0825A = "#F0825A";
    private const string Axisdrag = "axisdrag";
    private const string Marker = "marker";
    private const string PureSine = "pureSine";
    private const string Style = "style";
    private const string Layout = "layout";
    private const string ThreeGroups = "threeGroups";
    private const string CandlesKey = "candles";
    private const string VolumeKey = "volume";
    private const string DrawingTools = "Drawing tools";
    private const string BarRowsGap = "barRowsGap";
    private const string ColorF72585 = "#F72585";
    private const string Color4CC9F0 = "#4CC9F0";
    private const string TemperatureKey = "temperature";
    private const string TachyonLightName = "TachyonLight";
    private const string BottomKey = "bottom";

    /// <summary>Gets the pages of 2D charts and of the interactive part.</summary>
    public static IReadOnlyList<PageInfo> Core { get; } = WithGallery(
    [
        new("Live-Chart (interaktiv)", LiveChart),

        // The chart types, with the instrument theme, fixed ranges and the interactions of their pages.
        new("Line", _ => Types(0, 10, -1.5, 1.5, true, Series("line", "Sine", PureSine, 300, (Thickness, 2.0)))),
        new("Step line", _ => Types(0, 10, -1.5, 1.5, true, Series("line", string.Empty, "steps", 60, (Thickness, 2.0), (Style, "step")))),
        new("Spline", _ => Types(0, 10, -1.5, 1.5, true, Series("line", string.Empty, PureSine, 14, (Thickness, 2.0), (Style, "spline")))),
        new("Scatter", _ => Types(0, 10, 0, 10, true, Series(ScatterKey, string.Empty, "cloud", 40, ("size", 6.0)))),
        new("Bubble", _ => Types(0, 10, 0, 10, true, Series("bubbles", string.Empty, "cloudSizes", 40))),
        new("Area", _ => Types(0, 10, -1, 3, false, Series("area", string.Empty, "raisedSine", 300))),
        new("Stacked area", _ => Types(0, 10, 0, 9, false, Series("stackedarea", string.Empty, "threeLayers", 300))),
        new("Band", _ => Types(0, 10, -1.5, 1.5, false, Series("band", string.Empty, "band", 300))),
        new("Bars", _ => Types(-0.5, 5.5, 0, 8, false, Series("bars", string.Empty, "barRows", 6))),
        new("Stacked bars", _ => Types(-0.5, 5.5, 0, 14, false, Series("bars", string.Empty, "barRows", 6, (Layout, "Stacked")))),
        new("Horizontal bars", _ => Types(0, 8, -0.5, 5.5, false, Series("bars", string.Empty, "barRows", 6, ("horizontal", true)))),
        new("Error bars", _ => Types(-0.5, 12.5, 0, 4, false, Series("errorbars", string.Empty, "fiveErrors", 13, (Thickness, 1.5)))),
        new("Stems", _ => Types(-1, 41, -0.5, 3, false, Series("stems", string.Empty, "fadingPulses", 41, ("headSize", 6.0)))),
        new("Histogram", _ => Types(0, 12, 0, 1200, false, Series("histogram", string.Empty, "exponential", 5000, ("rule", "FreedmanDiaconis")))),
        new("Box plot", _ => Categories(Types(0, 1, 0, 8, false, Series("boxplot", string.Empty, ThreeGroups, 300)), "Normal, Skewed, Two peaks")),
        new("Violin", _ => Categories(Types(0, 1, 0, 8, false, Series("violin", string.Empty, ThreeGroups, 300)), "Normal, Skewed, Two peaks")),
        new("Categories", _ => Categories(Types(0, 1, 0, 10, false, Series("bars", string.Empty, "months", 12)), "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec")),
        new("Scale break", ScaleBreak),
        new("Last value at the axis", LastValue),
        new("Several legends", SeveralLegends),
        new("Own theme", OwnTheme),
        new("Axes in the origin", AxesInTheOrigin),
        new("Depth profile", DepthProfile),
        new("Polygons", _ => Titled("Polygons with holes", Plain(0, 10, 0, 10, Series("polygons", string.Empty, "holedShapes", 3, (Thickness, 1.5))), Zoompan, CrosshairKey, Tooltip)),
        new("10 000 polygons", _ => Titled("10 000 hexagons", Plain(-1, 150, -1, 44, Series("polygons", string.Empty, "honeycomb", 10_000, (Thickness, 0.5))), Zoompan, Tooltip)),
        new("Heatmap in an area", _ => Titled("A measurement inside a region", Plain(0, 10, 0, 10, Series("maskedheatmap", string.Empty, "hillsInRegion", 400, (Colormap, TurboName))), Zoompan, Tooltip)),
        new("Uneven heatmap 4096²", _ =>
        {
            var spec = Titled("4096 × 4096 cells of their own sizes", Plain(1, 10_000, 0, 100, Series("unevenheatmap", string.Empty, "fineCells", 4096, (Colormap, TurboName))), Zoompan, CrosshairKey, Tooltip);
            (spec.XAxes[0].Id, spec.XAxes[0].Title, spec.XAxes[0].Kind) = ("frequency", "Frequency in Hz", AxisKind.Logarithmic);
            (spec.YAxes[0].Id, spec.YAxes[0].Title) = ("depth", "Depth in m");
            return spec;
        }),
        new("Labelled contours", _ => Titled("Height lines with their values", Plain(0, 6, 0, 6, Series("contour", "Height", "hills", 512, ("from", -1.0), ("to", 1.25), (Levels, 10.0), ("filled", true), ("labels", true), ("size", 6.0))), Zoompan)),
        new("Polar", _ => Instrument(Series("polar", string.Empty, "lobe", 361, ("radius", 10.0), ("spokes", 8.0)))),
        new("Antenna cut", _ => Instrument(Series("polar", string.Empty, "azimuthCut", 361, ("radius", 40.0), ("spokes", 8.0)))),
        new("Cut through a surface", _ => Types(-1, 1, -1, 1, true, Series("line", "Cut", "hillCut", 256, (Thickness, 2.0)))),
        new("Radar", _ => Instrument(Series("radar", string.Empty, "scores", 6, ("radius", 100.0)))),
        new("Pie", _ => new ChartSpec { Title = "Power mix", XAxes = { new AxisSpec { Id = "x" } }, YAxes = { new AxisSpec { Id = "y" } }, Series = { Series("pie", string.Empty, "tenSources", 10, ("exploded", 1.0)) } }),
        new("Donut", _ => new ChartSpec { Title = "Power mix", XAxes = { new AxisSpec { Id = "x" } }, YAxes = { new AxisSpec { Id = "y" } }, Series = { Series("donut", string.Empty, "tenSources", 10, ("innerRadius", 0.55)) } }),
        new("Candles", _ =>
        {
            var spec = Prices("Share price, the last day trades live", Series(CandlesKey, string.Empty, "dailyWalk", 250));
            spec.Series[0].Data.Kind = DataKind.Live;
            return spec;
        }),
        new("OHLC bars", _ => Prices("Share price as OHLC bars", Series(CandlesKey, string.Empty, "dailyWalk3", 120, (Style, "Ohlc"), ("rising", "#0072B2"), ("falling", "#E69F00"), (VolumeKey, false)))),
        new("1 million candles", _ => Prices("1 million minute candles", Series(CandlesKey, string.Empty, "minuteWalk", 1_000_000, ("hollow", true), ("volumeShare", 0.25)))),
        new("Large file", _ => LargeFile()),
        new("Recording", _ => Recording()),
        new("Digital channels", _ => DigitalChannels()),
        new("Scatter from a file", _ => ScatterFromAFile()),
        new("Heatmap from a file", _ => HeatmapFromAFile()),
        new("Candles without weekends", _ =>
        {
            var spec = Prices("Working days only, the weekends take no room", Series(CandlesKey, string.Empty, "workdayWalk", 60, (VolumeKey, false)));
            spec.XAxes[0].SkipsWeekends = true;
            return spec;
        }),
        new("Market profile", _ => Prices("Volume per price level of the visible days", Series(CandlesKey, string.Empty, "dailyWalk11", 400, (VolumeKey, false), ("profile", true), (Levels, 40.0)))),
        new(DrawingTools, _ =>
        {
            var spec = Prices(DrawingTools, Series(CandlesKey, string.Empty, "dailyWalk5", 160, (VolumeKey, false), ("drawings", true), ("examples", true)));
            spec.Modifiers.Clear();
            Interactions(spec, Zoompan, "keyboard");
            return spec;
        }),
        new("Gantt", _ => Titled("Test plan", Plainly(Instrument(Series("gantt", string.Empty, "testPlan", 13))), Zoompan)),
        new("Gantt, 100 000 tasks", _ => Titled("Test hall: 100 stations, 100 000 tests", Plainly(Instrument(Series("gantt", string.Empty, "testHall", 100_000))), Zoompan)),
        new("Ternary", _ => Instrument(Series("ternary", string.Empty, "randomMixture", 300))),
        new("Annotations", _ =>
        {
            var spec = Types(0, 10, -1.5, 1.5, false, Series("line", string.Empty, PureSine, 300, (Thickness, 2.0)));
            spec.Modifiers.Clear();
            Interactions(spec, Zoompan, "annotationedit");
            foreach (var (type, place, value, label) in new[] { (Hline, "y", 1.1, "Limit"), ("vline", "x", 4.0, "Start") })
            {
                var annotation = new AnnotationSpec { Type = type };
                annotation.Parameters.Set(place, value);
                annotation.Parameters.Set(Label, label);
                spec.Annotations.Add(annotation);
            }

            return spec;
        }),
        // The gallery: scenes of lines with the time below, as their pages draw them.
        new("LineBasic", _ => Scene("LineBasic", "Time", -1.5, 1.5, Line(WaveKey, 240, Color50AAF0, 1.5), Line("wave13Cosine", 240, ColorF0825A, 4), Line("circle", 121, Color6ED282, 2))),
        new("LineStep", _ => Scene("LineStep", "Time", -1.5, 1.5, Line("wave11", 25, Color50AAF0, 1), Line("wave11", 25, ColorF0825A, 2, (Style, "step")))),
        new("LineGaps", _ => Scene("LineGaps", "Time", -1.5, 1.5, Line("wavesWithHoles", 240, Color50AAF0, 2), Line("wavesWithHolesTurned", 60, ColorF0825A, 2, (Style, "step")))),
        new("LineDash", _ => Scene("LineDash", "Time", -1.5, 1.5, Line(WaveKey, 240, Color50AAF0, 2, ("dash", "Long dashes")), Line("fastCosineHalf", 240, ColorF0825A, 3, ("dash", "Sparse dots")), Line("ramp", 240, Color6ED282, 2, ("dash", "Long dash dot")))),
        new("AnnotationStyles", _ =>
        {
            var spec = Scene("AnnotationStyles", "Time", -1.5, 1.5, Line(WaveKey, 240, Color50AAF0, 2));
            Interactions(spec, "annotationedit");
            spec.Annotations.Add(Annotation(Hline, ("y", 1.1), (Label, "Upper limit"), (Color, ColorF05A5AFF), (LabelPlace, "End"), ("labelIcon", "Warning"), ("labelColor", "#FFFFFFFF"), ("dash", "Dashed")));
            spec.Annotations.Add(Annotation("vline", ("x", 6.0), (Label, "Trigger"), (Color, Color6ED282FF), ("labelTurn", "AlongLine"), ("labelFill", "#00000000"), ("labelSize", 14.0), (Thickness, 2.0)));
            spec.Annotations.Add(Annotation("band", ("from", 7.5), ("to", 9.0), (Label, "Window"), (Color, "#6ED2822D"), ("labelFill", "#00000000"), ("labelBorder", Color6ED282FF), ("labelColor", Color6ED282FF), ("dash", "Dotted"), (Thickness, 1.5)));
            spec.Annotations.Add(Annotation("box", ("x1", 1.9), ("y1", 1.25), ("x2", 3.6), ("y2", 0.55), (Label, "Peak"), (Color, "#F0825AFF"), ("labelIcon", "Info"), ("labelBorder", "#1E1E1EFF"), ("labelBorderWidth", 1.5), ("dash", "Dashed")));
            spec.Annotations.Add(Annotation("line", ("x1", 0.4), ("y1", -1.3), ("x2", 4.6), ("y2", -0.5), (Label, "Trend"), (Color, "#966EE6FF"), ("labelTurn", "AlongLine"), (LabelPlace, "Middle"), ("labelIcon", "Arrow"), ("dash", "Dotted")));
            spec.Annotations.Add(Annotation("text", ("x", 6.6), ("y", -1.05), ("text", "Turned note"), (Color, ColorF05A5AFF), ("labelTurn", "Angle"), ("labelAngle", 15.0), ("labelFill", "#F05A5A3C"), ("labelBorder", ColorF05A5AFF), ("labelColor", ColorF05A5AFF)));
            return spec;
        }),
        new("TextFonts", _ =>
        {
            var spec = Scene("Fonts: Roboto Slab, bold and italic", TimeInS, -1.5, 1.5, Named(Line(WaveKey, 240, Color50AAF0, 2), "Wave"));
            (spec.Theme, spec.YAxes[0].Title) = (TachyonDarkName, "Level");
            spec.TitleFont = new FontSpec { Family = "Roboto Slab", Bold = true };
            spec.AxisFont = new FontSpec { Italic = true };
            Interactions(spec, "annotationedit");
            spec.Annotations.Add(Annotation(Hline, ("y", 1.1), (Label, "Bold label"), (Color, ColorF05A5AFF), (LabelPlace, "End"), ("labelBold", true)));
            spec.Annotations.Add(Annotation("vline", ("x", 6.0), (Label, "Italic label"), (Color, Color6ED282FF), ("labelItalic", true)));
            spec.Annotations.Add(Annotation(Hline, ("y", -1.2), (Label, "Roboto Slab, bold italic"), (Color, "#F0A03CFF"), (LabelPlace, "Start"), ("labelFont", "Roboto Slab"), ("labelBold", true), ("labelItalic", true)));
            return spec;
        }),
        new("ZoomPreview", _ => Interacting(Scene("ZoomPreview", "Time", -1.5, 1.5, Line(WaveKey, 240, Color50AAF0, 2)), Rubberband)),
        new("LineFifoWrap", _ => Scene("LineFifoWrap", "Time", -1.5, 1.5, Line(WaveKey, 301, Color50AAF0, 2), Line("vibration", 200_000, ColorF0825A, 1))),
        new("Crosshair", _ => Interacting(Scene("Crosshair", "Time", -1.5, 1.5, Line(WaveKey, 40, Color50AAF0, 2)), CrosshairKey)),
        new("CrosshairSnap", _ =>
        {
            var spec = Interacting(Scene("CrosshairSnap", "Time", -1.5, 1.5, Line(WaveKey, 40, Color50AAF0, 2)), CrosshairKey);
            spec.Modifiers[0].Parameters.Set("snap", true);
            return spec;
        }),
        new("Rollover", _ => Interacting(Scene("Rollover", "Time", -1.5, 1.5, Named(Line(WaveKey, 240, Color50AAF0, 2), "Druck"), Named(Line("wave13Raised", 240, Color6ED282, 2), "Soll"), Named(Line("slowCosine", 240, ColorF0825A, 2), "Temperatur")), "rollover")),
        new("MeasureCursors", _ => Interacting(Scene("MeasureCursors", "Time", -1.5, 1.5, Line(WaveKey, 240, Color50AAF0, 2)), "measure")),
        new("Legend", _ => Interacting(Scene("Legend", "Time", -1.5, 1.5, Named(Line(WaveKey, 240, Color50AAF0, 2), "Druck"), Named(Line("wave13Cosine", 240, ColorF0825A, 2), "Versteckt"), Named(Line("slowCosineHalf", 240, Color6ED282, 2), "Temperatur")), "legend")),
        new("Selection", _ =>
        {
            var spec = Interacting(Scene("Selection", "Time", -1.5, 1.5, Series(ScatterKey, string.Empty, "randomPoints", 400, (Color, Color50AAF0), ("size", 5.0))), "selection");
            spec.Modifiers[0].Parameters.Set("lasso", true);
            return spec;
        }),

        // The gallery: scenes of shapes with fixed ranges and their name as the title.
        new("BarsGrouped", _ => Shape("BarsGrouped", -0.5, 5.5, 0, 8, Series("bars", string.Empty, BarRowsGap, 6))),
        new("BarsStacked", _ => Shape("BarsStacked", -0.5, 5.5, 0, 14, Series("bars", string.Empty, BarRowsGap, 6, (Layout, "Stacked")))),
        new("BarsPercent", _ => Shape("BarsPercent", -0.5, 5.5, 0, 100, Series("bars", string.Empty, BarRowsGap, 6, (Layout, "Percent")))),
        new("BarsHorizontal", _ => Shape("BarsHorizontal", 0, 14, -0.5, 5.5, Series("bars", string.Empty, BarRowsGap, 6, (Layout, "Stacked"), ("horizontal", true)))),
        new("ErrorBars", _ => Shape("ErrorBars", -0.5, 12.5, 0, 4, Series("errorbars", string.Empty, "gappedErrors", 13, (Color, Color50AAF0), (Thickness, 1.5)))),
        new("Stem", _ => Shape("Stem", -1, 41, -0.5, 3, Series("stems", string.Empty, "fadingPulses", 41, (Color, ColorF0825A), ("headSize", 6.0)))),
        new("GlowNeon", _ => Shape("GlowNeon", 0, 10, -1.5, 1.5, Line(PureSine, 300, Color4CC9F0, 2, ("glow", 10.0), ("glowIntensity", 0.8)), Line("shortCosine", 300, ColorF72585, 2, ("glow", 6.0), ("glowIntensity", 0.6)))),
        Theme("ThemeHighContrast", "HighContrast", "#FFFF00", "#00FFFF"),
        Theme("ThemeNeon", "Neon", Color4CC9F0, ColorF72585),
        Theme("ThemePaper", "Paper", "#000000", "#555555"),
        Theme("ThemeInstrument", "Instrument", "#F5D63C", "#46D778"),
        Theme("ThemeSignal", "Signal", "#E90029", "#333333"),
        // The gallery: scenes that show the chart types once more, in other themes or with other data.
        new("LastValue", _ =>
        {
            var spec = new ChartSpec
            {
                Title = "Temperatures",
                Theme = TachyonDarkName,
                XAxes = { new AxisSpec { Id = "t", Title = TimeInS, Minimum = 0, Maximum = 28.4 } },
                YAxes = { new AxisSpec { Id = TemperatureKey, Title = "Temperature in °C", Minimum = 45, Maximum = 75, IsOpposite = true } },
            };
            string[] names = ["Inlet", "Core", "Outlet"];
            string[] colors = [Color4CC9F0, "#FFBE0B", ColorF72585];
            for (var i = 0; i < names.Length; i++)
            {
                var line = Series("line", names[i], $"recordedTemperature{i + 1}", 285, (Thickness, 1.5), (Color, colors[i]), ("lastValue", true));
                (line.XAxis, line.YAxis) = ("t", TemperatureKey);
                spec.Series.Add(line);
            }

            return spec;
        }),
        new("ValueLabels", _ =>
        {
            var spec = new ChartSpec
            {
                Title = "Sales per quarter",
                Theme = TachyonDarkName,
                XAxes = { new AxisSpec { Id = "quarter", Title = "Quarter", Minimum = -0.6, Maximum = 7.6 } },
                YAxes = { new AxisSpec { Id = "sales", Title = "Sales in k€", Minimum = 0, Maximum = 110 } },
                Series = { Series("bars", "North and South", "quarters", 8, ("values", true)), Series("line", "Target", "quarterTarget", 8, (Thickness, 2.0), ("values", true)) },
            };
            return spec;
        }),
        new("CategoryThinning", _ => Categories(Shape("CategoryThinning", 0, 1, 0, 2, Series("bars", string.Empty, "sensors", 200)), string.Join(", ", Enumerable.Range(0, 200).Select(i => $"Sensor {i}")))),
        new("Contour", _ => Shape("Contour", 0, 6, 0, 6, Series("contour", string.Empty, "hillsWithGap", 128, ("from", -1.0), ("to", 1.25), (Levels, 10.0), ("filled", true), ("labels", false), ("size", 6.0)))),
        new("DonutLight", _ => new ChartSpec { Title = "Power mix", Theme = TachyonLightName, XAxes = { new AxisSpec { Id = "x" } }, YAxes = { new AxisSpec { Id = "y" } }, Series = { Series("donut", string.Empty, "tenSources", 10, ("innerRadius", 0.55)) } }),
        new("BoxViolin", _ =>
        {
            var spec = Categories(Shape("BoxViolin", 0, 1, 0, 8, Series("boxplot", string.Empty, ThreeGroups, 300)), "Normal, Schief, Zweigipflig");
            spec.YAxes[0].Id = "top";
            spec.YAxes.Add(new AxisSpec { Id = BottomKey, Minimum = 0, Maximum = 8, Pane = 1 });
            var violin = Series("violin", string.Empty, ThreeGroups, 300, (Color, "#F7258540"));
            violin.YAxis = BottomKey;
            spec.Series[0].YAxis = "top";
            spec.Series.Add(violin);
            return spec;
        }),
        new("OhlcLight", _ =>
        {
            var spec = Prices("Share price", Series(CandlesKey, string.Empty, "dailyWalk", 90, (Style, "Ohlc")));
            spec.Theme = TachyonLightName;
            spec.Modifiers.Clear();
            return spec;
        }),
        new("Drawings", _ =>
        {
            var spec = Prices(DrawingTools, Series(CandlesKey, string.Empty, "dailyWalk5", 160, (VolumeKey, false), ("examples", true)));
            spec.Theme = TachyonDarkName;
            spec.Modifiers.Clear();
            return spec;
        }),
        new("AreaStacked", _ => Shape("AreaStacked", 0, 10, 0, 9, Series("stackedarea", string.Empty, "gappedLayers", 300, ("colors", "#4CC9F040, #F7258540, #7CFC9A40")))),
        new("DrawingPatterns", _ =>
        {
            var spec = Prices(DrawingTools, Series(CandlesKey, string.Empty, "dailyWalk5", 160, (VolumeKey, false), ("patterns", true)));
            spec.Theme = TachyonDarkName;
            spec.Modifiers.Clear();
            return spec;
        }),
        Preview("ThemeDark", TachyonDarkName, Color4CC9F0, ColorF72585, "#0E1117"),
        Preview("ThemeLight", TachyonLightName, "#0077B6", "#D81B60", "#F4F6FA"),
        new("CategoryAxis", _ =>
        {
            var spec = Categories(Shape("CategoryAxis", 0, 1, 0, 10, Series("bars", string.Empty, "months", 12)), "Januar, Februar, März, April, Mai, Juni, Juli, August, September, Oktober, November, Dezember");
            spec.XAxes[0].LabelRotation = -45;
            return spec;
        }),
        new("Overview", _ => new ChartSpec
        {
            Title = "Overview",
            Theme = TachyonDarkName,
            Overview = true,
            XAxes = { new AxisSpec { Id = "x", Title = "Time", Minimum = 3, Maximum = 6 } },
            YAxes = { new AxisSpec { Id = "y", Minimum = -1.5, Maximum = 1.5 } },
            Series = { Series("line", "Signal", "overviewSignal", 240, (Thickness, 2.0), (Color, Color50AAF0)) },
        }),
        new("GridBands", _ => new ChartSpec
        {
            Title = "GridBands",
            OwnTheme = new ThemeSpec
            {
                Name = "Grid bands",
                Base = TachyonDarkName,
                Values =
                {
                    ["surface.background"] = "#0B0E16",
                    ["surface.plotArea"] = "linear(#202D42, #0C121F)",
                    ["grid.major"] = "#AABEDC48",
                    ["grid.minor"] = "#AABEDC1C",
                    ["grid.band"] = "#6EA0DC14",
                },
            },
            XAxes = { new AxisSpec { Id = "x", Title = "Major and minor grid", Minimum = 0, Maximum = 12 } },
            YAxes = { new AxisSpec { Id = "y", Minimum = 0, Maximum = 8 } },
            Series = { Series("line", "Wave", "bandsWave", 300, (Thickness, 2.0), (Color, "#5CC8FF")) },
        }),
        new("CrosshairValues", catalog =>
        {
            var spec = Pages(catalog, "Several legends");
            spec.Theme = TachyonDarkName;
            return spec;
        }),
        new("CrosshairValuesTurned", catalog =>
        {
            var spec = Pages(catalog, "Depth profile");
            spec.Theme = TachyonDarkName;
            return spec;
        }),
        new("GanttCollapsedLight", _ => new ChartSpec
        {
            Title = "Test plan",
            Theme = TachyonLightName,
            XAxes = { new AxisSpec { Id = "x" } },
            YAxes = { new AxisSpec { Id = "y" } },
            Series = { Series("gantt", string.Empty, "testPlan", 13, ("collapsed", "Vibration")) },
        }),
        new("MarketProfile", _ =>
        {
            var spec = Prices("Market profile", Series(CandlesKey, string.Empty, "dailyWalk11", 200, (VolumeKey, false), ("profile", true), (Levels, 40.0)));
            spec.Theme = TachyonDarkName;
            spec.Modifiers.Clear();
            return spec;
        }),
        new("Smith", _ => Untitled(Instrument(Series("smith", string.Empty, "seriesCircuit", 200, (Marker, 100.0))), "Smith")),
        new("Smith (interaktiv)", _ => Instrument(Series("smith", string.Empty, "seriesCircuit", 200, (Marker, 100.0)))),
        new("ParallelCoordinates", _ => Untitled(Instrument(Series("parallel", string.Empty, "sixColumns", 2000, ("names", "Volt, Strom, Temp, Druck, Hz, dB"), (Color, "#50AAF032"))), "ParallelCoordinates")),
        new("Parallel (interaktiv)", _ => Instrument(Series("parallel", string.Empty, "loads", 20_000, ("names", "V, A, °C, hPa, Hz, dB")))),
        // Several charts: a group of three, four rasters, three stacked charts of one group.
        new("Gruppe (3 Charts)", _ => Several(1, GroupChart("Small", "groupSmall"), GroupChart("Large", "groupLarge"), GroupChart("Medium", "groupMedium"))),
        new("Raster (interaktiv)", _ => Several(
            2,
            Raster("Heatmap 4096 × 4096", Series("heatmap", ValueName, "fineField", 4096, (Colormap, InfernoName), ("size", 8.0)), Zoompan, Rubberband, Axisdrag, Tooltip),
            Raster("Spectrogram, 60 rows per second", Series("spectrogram", "Level", "steadyTone", 1024, (Colormap, "Plasma"), ("colorRange", "-0.1..1.1"), ("live", true), ("rate", 60.0)), 0, 1024, -512, 0, Zoompan, Axisdrag, Tooltip),
            Raster("Contour, filled", Series("contour", "Height", "hills", 256, ("from", -1.0), ("to", 1.25), (Levels, 10.0), ("filled", true), ("labels", false), ("size", 6.0)), Zoompan, Rubberband),
            Raster("5 million points: zoom in for markers", Series(ScatterKey, "Points", "cloud50M", 5_000_000, ("size", 4.0), ("density", true), ("densityColormap", InfernoName)), 0, 10, 0, 10, Zoompan, Rubberband, Tooltip))),
        new("GroupStacked", _ => Several(1, Stacked("stackedFirst", Color50AAF0, 1, 2, 8), Stacked("stackedSecond", ColorF0825A, 100_000), Stacked("stackedThird", Color6ED282, 25))),

        // The instruments: live on their pages, as still pictures in the gallery.
        new("Oszilloskop (interaktiv)", _ => Instrument(Series("oscilloscope", string.Empty, "scope", 5000, [.. FourChannels, ("live", true)]))),
        new("Oscilloscope", _ => Instrument(Series("oscilloscope", string.Empty, "scope", 5000, FourChannels))),
        new("ColorGrade", _ => Instrument(Series("oscilloscope", string.Empty, "scope", 3000, ("channels", 1.0), ("signals", "Sine"), ("volts", "1"), ("positions", "0"), ("trigger", 0.0), ("persistence", "ColorGrade"), ("live", true), ("noise", 0.3)))),
        new("Spektrum (interaktiv)", _ => Instrument(Series("spectrum", string.Empty, "noisyCarriers", 1500, ("maskLevel", -9.0), (Marker, -1.0), ("historyBelow", true), ("live", true)))),
        new("Spectrum", _ => Instrument(Series("spectrum", string.Empty, "noisyCarriers", 1500, (Marker, -1.0)))),
        new("Network (interaktiv)", _ => Instrument(Series("network", string.Empty, "filter", 801, ("secondMarker", 1.7e9)))),
        new("Network", _ => Instrument(Series("network", string.Empty, "filter", 801, ("secondMarker", 1.7e9), ("failing", true)))),
        new("Sweep monitor", _ => Monitor("Sweep", live: true)),
        new("Stepping monitor", _ => Monitor("Stepping", live: true)),
        new("Sweep", _ => Monitor("Sweep", live: false)),
        new("Stepping", _ => Monitor("Stepping", live: false)),
        // The gallery: scenes that check single abilities of the charts.
        new("ScatterMarkers", _ =>
        {
            string[] shapes = ["Circle", "Square", "Triangle", "Diamond", "Cross", "Plus"];
            string[] colors = [Color50AAF0, ColorF0825A, Color6ED282, "#DCB446", "#B478E6", "#50D2CD"];
            var spec = Shape("ScatterMarkers", 0, 10, 0, 7);
            for (var shape = 0; shape < shapes.Length; shape++)
            {
                spec.Series.Add(Series(ScatterKey, string.Empty, $"markerRow{shape + 1}", 9, (Marker, shapes[shape]), (Color, colors[shape]), ("size", 16.0), ("borderColor", "#0A0F18"), ("border", shape == 0 ? 2.0 : 0.0)));
            }

            return spec;
        }),
        new("PointCloud50M", _ => Shape("PointCloud50M", 0, 10, 0, 10, Series(ScatterKey, string.Empty, "cloud50M", 50_000_000, (Color, Color50AAF0), ("size", 7.0), ("density", true), ("densityColormap", InfernoName)))),
        new("Heatmap4k", _ => Shape("Heatmap4k", 0, 8, 0, 8, Series("heatmap", string.Empty, "fineFieldWithHole", 4096, (Colormap, InfernoName), ("size", 8.0), ("missingColor", "#5AC85A")))),
        new("HeatmapNonUniform", _ =>
        {
            var spec = Shape("HeatmapNonUniform", 0, 10, 1, 10_000, Series("unevenheatmap", string.Empty, "decades", 6, (Colormap, "Viridis"), ("logColors", true), ("missingColor", "#C83C3C")));
            spec.YAxes[0].Kind = AxisKind.Logarithmic;
            return spec;
        }),
        new("Spectrogram", _ =>
        {
            var spec = Shape("Spectrogram", 0, 256, -256, 0, Series("spectrogram", string.Empty, "wanderingTone", 256, (Colormap, "Plasma"), ("colorRange", "-0.1..1.1"), ("missingColor", "#282828")));
            spec.XAxes[0].Title = "Frequency";
            return spec;
        }),
        new("LogAxis", _ =>
        {
            var spec = Scene("LogAxis", "Time", 0.01, 10_000);
            spec.YAxes[0] = new AxisSpec { Id = "hide", Kind = AxisKind.Logarithmic, Minimum = 0.01, Maximum = 10_000 };
            spec.YAxes.Add(new AxisSpec { Id = "clamp", Kind = AxisKind.Logarithmic, LogBase = 2, NonPositive = "Clamp", Floor = 1, Minimum = 1, Maximum = 1024, Pane = 1 });
            spec.YAxes.Add(new AxisSpec { Id = "sym", Kind = AxisKind.Logarithmic, NonPositive = "SymLog", Minimum = -1000, Maximum = 1000, Pane = 2 });
            spec.Series.Add(OnAxis(Line("logDecay", 200, Color50AAF0, 2), "hide"));
            spec.Series.Add(OnAxis(Series(ScatterKey, string.Empty, "logSteps", 200, (Color, ColorF0825A), ("size", 4.0)), "clamp"));
            spec.Series.Add(OnAxis(Line("logBoth", 200, Color6ED282, 2), "sym"));
            return spec;
        }),
        new("Gradients", _ =>
        {
            var spec = Shape("Gradients", -0.5, 9.5, 0, 40);
            spec.YAxes[0].Id = "top";
            spec.YAxes.Add(new AxisSpec { Id = BottomKey, Minimum = 0, Maximum = 10, Pane = 1 });
            spec.Series.Add(OnAxis(Series("area", string.Empty, "gradientWave", 200, ("valueColors", true), (Colormap, TurboName), ("colorFrom", 0.0), ("colorTo", 40.0)), "top"));
            spec.Series.Add(OnAxis(Series("bars", string.Empty, "gradientRow", 10, ("corners", 8.0), ("width", 0.6), ("gradientFrom", "#FFB703"), ("gradientTo", ColorF72585), ("shadowColor", "#0000003C")), BottomKey));
            return spec;
        }),
        new("ScatterPerPointColor", _ => Shape("ScatterPerPointColor", 0, 19, 0, 11, Series("bubbles", string.Empty, "colorGrid", 180, (Color, Color50AAF0)))),
        new("Logarithmic axis", _ =>
        {
            var spec = Types(0, 10, 0.01, 10000, true, Series("line", string.Empty, "fallingPower", 300, (Thickness, 2.0)));
            spec.YAxes[0].Kind = AxisKind.Logarithmic;
            return spec;
        }),
    ]);

    // Pages of the gallery that show a page of the chart types once more, under their own name.
    private static (string Gallery, string Page)[] Same =>
    [
        ("ScaleBreak", "Scale break"),
        ("CandlesWithoutWeekends", "Candles without weekends"),
        ("ContourLabels", "Labelled contours"),
        ("AxesInTheOrigin", "Axes in the origin"),
        ("DepthProfile", "Depth profile"),
        ("Legends", "Several legends"),
        ("HeatmapInAnArea", "Heatmap in an area"),
    ];

    private static IReadOnlyList<PageInfo> WithGallery(PageInfo[] pages) =>
        [.. pages, .. Same.Select(same => new PageInfo(same.Gallery, pages.Single(page => page.Page == same.Page).Create))];

    // A tall peak beside small values, with the range between left out.
    private static ChartSpec ScaleBreak(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "A peak and the small values beside it",
            XAxes = { new AxisSpec { Id = "x", Title = TimeInS, Minimum = 0, Maximum = 20 } },
            YAxes = { new AxisSpec { Id = "y", Title = "Counts", Minimum = 0, Maximum = 1010, Breaks = "45..960" } },
            Series = { Series("line", "Counts", "peakBeside", 2000, (Thickness, 1.5)) },
        };
        Interactions(spec, Zoompan, CrosshairKey, Tooltip);
        return spec;
    }

    // Three live temperatures, each with its newest value at the axis on the right.
    private static ChartSpec LastValue(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Temperatures, live",
            XAxes = { new AxisSpec { Id = "t", Title = TimeInS } },
            YAxes = { new AxisSpec { Id = TemperatureKey, Title = "Temperature in °C", Minimum = 45, Maximum = 75, IsOpposite = true } },
        };
        string[] names = ["Inlet", "Core", "Outlet"];
        string[] colors = [Color4CC9F0, "#FFBE0B", ColorF72585];
        for (var i = 0; i < names.Length; i++)
        {
            var line = Series("line", names[i], $"temperature{i + 1}", 3000, (Thickness, 1.5), (Color, colors[i]), ("lastValue", true));
            (line.Data.Kind, line.XAxis, line.YAxis) = (DataKind.Live, "t", TemperatureKey);
            line.Data.Options.Set("rate", 50.0);
            spec.Series.Add(line);
        }

        Interactions(spec, Zoompan);
        return spec;
    }

    // Temperatures on the left, pressures on the right, each group with a legend of its own.
    private static ChartSpec SeveralLegends(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Test bench",
            XAxes = { new AxisSpec { Id = "t", Title = TimeInS, Minimum = 0, Maximum = 60 } },
            YAxes =
            {
                new AxisSpec { Id = TemperatureKey, Title = "Temperature in °C", Minimum = 0, Maximum = 120 },
                new AxisSpec { Id = "pressure", Title = "Pressure in bar", Minimum = 0, Maximum = 12, IsOpposite = true },
            },
            Legend = new LegendSpec { Title = "Temperatures", Placement = "TopLeft", Series = "Inlet, Core, Outlet" },
            SecondLegend = new LegendSpec { Title = "Pressures", Placement = "Bottom", Series = "Pump, Filter, Valve" },
        };
        string[] names = ["Inlet", "Core", "Outlet", "Pump", "Filter", "Valve"];
        string[] colors = ["#FFBE0B", "#FB5607", ColorF72585, Color4CC9F0, "#3A86FF", "#06D6A0"];
        for (var i = 0; i < names.Length; i++)
        {
            var line = Series("line", names[i], $"bench{i + 1}", 601, (Thickness, 1.5), (Color, colors[i]));
            (line.XAxis, line.YAxis) = ("t", i < 3 ? TemperatureKey : "pressure");
            spec.Series.Add(line);
        }

        Interactions(spec, Zoompan, CrosshairKey);
        spec.Modifiers[^1].Parameters.Set("values", true);
        return spec;
    }

    // Three lines in a theme of their own, that of harbor.json in the showroom: a dark base with a gradient, bands and its colors.
    private static ChartSpec OwnTheme(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Harbor, a theme from JSON",
            OwnTheme = new ThemeSpec
            {
                Name = "Harbor",
                Base = TachyonDarkName,
                Values =
                {
                    ["surface.background"] = "#0B0E16",
                    ["surface.plotArea"] = "linear(#202D42, #0C121F)",
                    ["surface.border"] = "#2C3A52",
                    ["grid.major"] = "#AABEDC48",
                    ["grid.minor"] = "#AABEDC1C",
                    ["grid.band"] = "#6EA0DC14",
                    ["axis.line"] = "#4A5A74",
                    ["axis.tick"] = "#4A5A74",
                    ["axis.label"] = "#A9B8CF",
                    ["axis.title"] = "#E3EAF5",
                    ["palette"] = "#5CC8FF, #FFB454, #7EE0A1, #FF7A90",
                },
            },
            XAxes = { new AxisSpec { Id = "x", Title = "Time (s)", Minimum = 0, Maximum = 10 } },
            YAxes = { new AxisSpec { Id = "y", Title = "Level", Minimum = -1.5, Maximum = 1.5 } },
            Legend = new LegendSpec(),
        };
        spec.Series.Add(Series("line", "Tide", "tide", 300, (Thickness, 2.0), (Color, "#5CC8FF")));
        spec.Series.Add(Series("line", "Swell", "swell", 300, (Thickness, 2.0), (Color, "#FFB454")));
        spec.Series.Add(Series("line", "Ripple", "ripple", 300, (Thickness, 1.5), (Color, "#7EE0A1")));
        Interactions(spec, Zoompan, Rubberband, CrosshairKey, Tooltip);
        return spec;
    }

    // A curve through the origin with both axes crossing there.
    private static ChartSpec AxesInTheOrigin(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "y = x · sin(x) / 2 − 1",
            XAxes = { new AxisSpec { Id = "x", Minimum = -10, Maximum = 10, CrossesZero = true } },
            YAxes = { new AxisSpec { Id = "y", Minimum = -6, Maximum = 6, CrossesZero = true } },
            Series = { Series("line", "y", "waveAroundZero", 1001, (Thickness, 2.0)) },
        };
        Interactions(spec, Zoompan, CrosshairKey, Tooltip);
        return spec;
    }

    // A borehole log: depth runs down, gamma as an area, temperature as a line and core samples as points.
    private static ChartSpec DepthProfile(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Borehole log",
            XDown = true,
            XAxes = { new AxisSpec { Id = "depth", Title = "Depth in m", Minimum = 0, Maximum = 1500 } },
            YAxes = { new AxisSpec { Id = "value", Title = "Gamma in API, temperature in °C", Minimum = 0, Maximum = 120 } },
            Series =
            {
                Series("area", "Gamma", "gammaLog", 1501, (Color, "#FFBE0B5A")),
                Series("line", "Temperature", "boreholeTemperature", 1501, (Thickness, 2.0), (Color, ColorF72585)),
                Series(ScatterKey, "Core samples", "coreSamples", 16, ("size", 9.0), (Color, Color4CC9F0)),
            },
        };
        Interactions(spec, Zoompan, CrosshairKey, Tooltip);
        return spec;
    }

    private static void Interactions(ChartSpec spec, params string[] types)
    {
        foreach (var type in types)
        {
            spec.Modifiers.Add(new ModifierSpec { Type = type });
        }
    }

    // A page of the chart types: the instrument theme, axes with fixed ranges and no titles, panning and zooming, with
    // the values under the pointer where the page shows them.
    private static ChartSpec Types(double left, double right, double bottom, double top, bool values, SeriesSpec series)
    {
        var spec = new ChartSpec
        {
            Theme = "Instrument",
            XAxes = { new AxisSpec { Id = "x", Minimum = left, Maximum = right } },
            YAxes = { new AxisSpec { Id = "y", Minimum = bottom, Maximum = top } },
            Series = { series },
        };
        foreach (var modifier in values ? [Zoompan, Rubberband, CrosshairKey, Tooltip] : new[] { Zoompan, Rubberband })
        {
            spec.Modifiers.Add(new ModifierSpec { Type = modifier });
        }

        return spec;
    }

    // A chart with fixed ranges, no titles at the axes and the theme of the application.
    private static ChartSpec Plain(double left, double right, double bottom, double top, SeriesSpec series) => new()
    {
        XAxes = { new AxisSpec { Id = "x", Minimum = left, Maximum = right } },
        YAxes = { new AxisSpec { Id = "y", Minimum = bottom, Maximum = top } },
        Series = { series },
    };

    private static ChartSpec Titled(string title, ChartSpec spec, params string[] interactions)
    {
        spec.Title = title;
        Interactions(spec, interactions);
        return spec;
    }

    // A description of several charts in columns: the first chart with the others as its further charts.
    private static ChartSpec Several(int columns, params ChartSpec[] charts)
    {
        var first = charts[0];
        first.Charts.AddRange(charts.Skip(1));
        first.Columns = columns;
        return first;
    }

    // A chart of the group of three: one curve over 0 to 20, the values followed, zoom and crosshair shared.
    private static ChartSpec GroupChart(string title, string sample)
    {
        var spec = new ChartSpec
        {
            Title = title,
            Theme = TachyonDarkName,
            Group = "showroom",
            XAxes = { new AxisSpec { Id = "x", Minimum = 0, Maximum = 20 } },
            YAxes = { new AxisSpec { Id = "y" } },
            Series = { Series("line", string.Empty, sample, 2000, (Thickness, 1.5)) },
        };
        Interactions(spec, Zoompan, CrosshairKey);
        return spec;
    }

    // A chart of the rasters: its title, the theme of the page, axes that follow the data.
    private static ChartSpec Raster(string title, SeriesSpec series, params string[] interactions) => Raster(title, series, double.NaN, double.NaN, double.NaN, double.NaN, interactions);

    private static ChartSpec Raster(string title, SeriesSpec series, double left, double right, double bottom, double top, params string[] interactions)
    {
        var spec = new ChartSpec
        {
            Title = title,
            Theme = TachyonDarkName,
            XAxes = { new AxisSpec { Id = "x", Minimum = double.IsNaN(left) ? null : left, Maximum = double.IsNaN(right) ? null : right } },
            YAxes = { new AxisSpec { Id = "y", Minimum = double.IsNaN(bottom) ? null : bottom, Maximum = double.IsNaN(top) ? null : top } },
            Series = { series },
        };
        Interactions(spec, interactions);
        return spec;
    }

    // A chart of the stacked group: one curve between ±1.2 times its height; the first shows 2 to 8, which the group shares.
    private static ChartSpec Stacked(string sample, string color, double height, double from = 0, double to = 10) => new()
    {
        Group = "stacked",
        XAxes = { new AxisSpec { Id = "x", Minimum = from, Maximum = to } },
        YAxes = { new AxisSpec { Id = "y", Minimum = -1.2 * height, Maximum = 1.2 * height } },
        Series = { Series("line", string.Empty, sample, 400, (Color, color), (Thickness, 1.5)) },
    };

    // The four channels of the oscilloscope of the showroom: sine, square, burst and triangle, spread from top to bottom.
    private static (string Name, object Value)[] FourChannels =>
        [("channels", 4.0), ("signals", "Sine, Square, Burst, Triangle"), ("volts", "1, 0.5, 0.5, 0.5"), ("positions", "3, 1, -1, -3")];

    // The patient monitor: live on its page from the start, as 8.4 s recorded in the gallery.
    private static ChartSpec Monitor(string mode, bool live)
    {
        var spec = Instrument(Series("monitor", string.Empty, "patient", live ? 2 : 210_000,
            ("mode", mode), ("gap", 0.03), ("names", "ECG I, ECG II, Pleth, Resp"), ("ranges", "-0.6..1.4, -0.6..1.4, -0.1..1.1, -1.2..1.2"), ("live", live), ("rate", 25_000.0)));
        if (live)
        {
            (spec.Title, spec.Theme) = ("Monitor, 4 channels × 25 000 samples per second", null);
            Interactions(spec, Axisdrag);
        }
        else
        {
            spec.Title = mode;
        }

        return spec;
    }

    // A type that brings its own axes and interactions, in the instrument theme.
    private static ChartSpec Instrument(SeriesSpec series) => new()
    {
        Theme = "Instrument",
        XAxes = { new AxisSpec { Id = "x" } },
        YAxes = { new AxisSpec { Id = "y" } },
        Series = { series },
    };

    // Prices over time: a time axis, the price axis on the left, panning and the crosshair.
    private static ChartSpec Prices(string title, SeriesSpec series)
    {
        var spec = new ChartSpec
        {
            Title = title,
            XAxes = { new AxisSpec { Id = "time", Kind = AxisKind.DateTime } },
            YAxes = { new AxisSpec { Id = "price", Title = "Price" } },
            Series = { series },
        };
        Interactions(spec, Zoompan, CrosshairKey);
        return spec;
    }

    // A billion values from a test file of 4 GB, coarse at once and refining while the pyramid grows.
    private static ChartSpec LargeFile()
    {
        var spec = new ChartSpec
        {
            Title = "1 billion values from a 4 GB file",
            XAxes = { new AxisSpec { Id = "x", Title = TimeInS } },
            YAxes = { new AxisSpec { Id = "y", Title = ValueName } },
            Series = { Series(LargeFileType.Id, "Measurement", LargeFileType.SampleId, 1_000_000_000, (Thickness, 1.0), (Color, Color50AAF0)) },
        };
        Interactions(spec, Zoompan, Rubberband);
        return spec;
    }

    // Ten million values per second recorded into a ring of files on the disk, the view rolling over the whole ring.
    private static ChartSpec Recording()
    {
        var spec = new ChartSpec
        {
            Title = "10 million values per second, recorded on the disk",
            XAxes = { new AxisSpec { Id = "x", Title = TimeInS } },
            YAxes = { new AxisSpec { Id = "y", Title = ValueName } },
            Series = { Series(LargeFileType.Id, "Measurement", RecordingSample.Id, RecordingSample.Sample.DefaultCount, (Thickness, 1.0), (Color, Color50AAF0), ("refining", false)) },
        };
        Interactions(spec, Zoompan, Rubberband);
        return spec;
    }

    // Sixteen channels of a logic analyzer, a billion points each, one above the other with their names at the axis.
    private static ChartSpec DigitalChannels()
    {
        var spec = new ChartSpec
        {
            Title = "16 channels of 1 billion points each",
            XAxes = { new AxisSpec { Id = "x", Title = TimeInS } },
            YAxes = { new AxisSpec { Id = "y", Kind = AxisKind.Category, Categories = string.Join(", ", DigitalType.Names.Reverse()) } },
        };
        string[] colors = ["#F0C850", "#F09646", "#F09646", .. Enumerable.Repeat("#5AC878", 8), "#F05A5A", "#F05A5A", "#5AA0F0", "#5AA0F0", "#5AA0F0"];
        for (var channel = 0; channel < DigitalType.Names.Length; channel++)
        {
            var lane = DigitalType.Names.Length - 1 - channel;
            spec.Series.Add(Series(DigitalType.Id, DigitalType.Names[channel], DigitalType.SampleId, 1_000_000_000, ("channel", (double)channel), ("low", lane - 0.3), ("high", lane + 0.3), (Thickness, 1.0), (Color, colors[channel])));
        }

        Interactions(spec, Zoompan, Rubberband, Tooltip);
        return spec;
    }

    // 200 million points from a test file as a density, refining while the tiles are sorted.
    private static ChartSpec ScatterFromAFile()
    {
        var spec = new ChartSpec
        {
            Title = "200 million points from a file",
            XAxes = { new AxisSpec { Id = "x", Title = "X" } },
            YAxes = { new AxisSpec { Id = "y", Title = "Y" } },
            Series = { Series(PointDensityType.Id, "Points", PointDensityType.SampleId, 200_000_000, (Colormap, InfernoName), ("log", true)) },
        };
        Interactions(spec, Zoompan, Rubberband);
        return spec;
    }

    // 268 million cells from a test raster, read through stages that keep the highest value.
    private static ChartSpec HeatmapFromAFile()
    {
        var spec = new ChartSpec
        {
            Title = "268 million cells from a file",
            XAxes = { new AxisSpec { Id = "x", Title = "Column" } },
            YAxes = { new AxisSpec { Id = "y", Title = "Row" } },
            Series = { Series(FileRasterType.Id, ValueName, FileRasterType.SampleId, 16_384, (Colormap, TurboName), ("reduction", "Maximum")) },
        };
        Interactions(spec, Zoompan, Rubberband);
        return spec;
    }

    // The theme of the application instead of the instrument theme.
    private static ChartSpec Plainly(ChartSpec spec)
    {
        spec.Theme = null;
        return spec;
    }

    // A line scene of the gallery: its name as the title, the time below, the theme of the application.
    private static ChartSpec Scene(string name, string time, double bottom, double top, params SeriesSpec[] series)
    {
        var spec = new ChartSpec
        {
            Title = name,
            XAxes = { new AxisSpec { Id = "x", Title = time, Minimum = 0, Maximum = 10 } },
            YAxes = { new AxisSpec { Id = "y", Minimum = bottom, Maximum = top } },
        };
        spec.Series.AddRange(series);
        return spec;
    }

    // A shape scene of the gallery: its name as the title, fixed ranges, no titles at the axes.
    private static ChartSpec Shape(string name, double left, double right, double bottom, double top, params SeriesSpec[] series)
    {
        var spec = Scene(name, string.Empty, bottom, top, series);
        (spec.XAxes[0].Minimum, spec.XAxes[0].Maximum) = (left, right);
        return spec;
    }

    // A wave and marks with a border in the background color, in the first colors of a theme.
    private static PageInfo Preview(string page, string theme, string line, string marks, string background) => new(page, _ =>
    {
        var spec = Scene("Theme preview", "Time", -1.5, 1.5, Line("previewWave", 180, line, 2.5), Series(ScatterKey, string.Empty, "previewMarks", 18, (Color, marks), ("size", 9.0), ("borderColor", background), ("border", 1.5)));
        (spec.Theme, spec.YAxes[0].Title) = (theme, "Signal");
        return spec;
    });

    // A scene of the gallery: its name as the title and the theme of the application.
    private static ChartSpec Untitled(ChartSpec spec, string name)
    {
        (spec.Title, spec.Theme) = (name, null);
        return spec;
    }

    // The description of another page, to show it once more.
    private static ChartSpec Pages(ChartCatalog catalog, string page) => catalog.CreatePage(page)!;

    // Two curves in the first colors of a theme.
    private static PageInfo Theme(string page, string theme, string first, string second) => new(page, _ =>
    {
        var spec = Shape(page, 0, 10, -1.5, 1.5, Line(PureSine, 300, first, 2), Line("shortCosine", 300, second, 2));
        spec.Theme = theme;
        return spec;
    });

    private static AnnotationSpec Annotation(string type, params (string Name, object Value)[] values)
    {
        var annotation = new AnnotationSpec { Type = type };
        foreach (var (name, value) in values)
        {
            if (value is double number)
            {
                annotation.Parameters.Set(name, number);
            }
            else if (value is bool flag)
            {
                annotation.Parameters.Set(name, flag);
            }
            else
            {
                annotation.Parameters.Set(name, (string)value);
            }
        }

        return annotation;
    }

    private static ChartSpec Interacting(ChartSpec spec, params string[] interactions)
    {
        Interactions(spec, interactions);
        return spec;
    }

    private static SeriesSpec Line(string sample, int count, string color, double thickness, params (string Name, object Value)[] more) =>
        Series("line", string.Empty, sample, count, [(Color, color), (Thickness, thickness), .. more]);

    private static SeriesSpec Line(string name, string sample, int count, string color, double thickness)
    {
        var series = new SeriesSpec { Type = "line", Name = name, Data = new DataSpec { Sample = sample, Count = count } };
        series.Parameters.Set(Color, color);
        series.Parameters.Set(Thickness, thickness);
        return series;
    }

    private static SeriesSpec OnAxis(SeriesSpec series, string yAxis)
    {
        series.YAxis = yAxis;
        return series;
    }

    private static SeriesSpec Named(SeriesSpec series, string name)
    {
        series.Name = name;
        return series;
    }

    // Names on the X axis instead of numbers.
    private static ChartSpec Categories(ChartSpec spec, string names)
    {
        (spec.XAxes[0].Kind, spec.XAxes[0].Categories, spec.XAxes[0].Minimum, spec.XAxes[0].Maximum) = (AxisKind.Category, names, null, null);
        return spec;
    }

    private static SeriesSpec Series(string type, string name, string sample, int count, params (string Name, object Value)[] parameters)
    {
        var series = new SeriesSpec { Type = type, Name = name, Data = new DataSpec { Sample = sample, Count = count } };
        foreach (var (parameter, value) in parameters)
        {
            switch (value)
            {
                case double number:
                    series.Parameters.Set(parameter, number);
                    break;
                case bool flag:
                    series.Parameters.Set(parameter, flag);
                    break;
                default:
                    series.Parameters.Set(parameter, (string)value);
                    break;
            }
        }

        return series;
    }

    // A fixed curve, 50,000 new points per second and an overview below, with all interactions of the page.
    private static ChartSpec LiveChart(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Live: 50,000 points per second",
            Theme = TachyonDarkName,
            Overview = true,
            XAxes = { new AxisSpec { Id = "x", Title = TimeInS } },
            YAxes = { new AxisSpec { Id = "y", Title = ValueName, Minimum = -2, Maximum = 2 } },
        };
        spec.Series.Add(Line("Fixed", "fixedCurve", 2000, Color50AAF0, 2));
        var live = Line("Live", "liveNoisy", 1_000_000, ColorF0825A, 1);
        live.Data.Kind = DataKind.Live;
        live.Data.Options.Set("rate", 50_000.0);
        spec.Series.Add(live);
        foreach (var modifier in new[] { Zoompan, Rubberband, Axisdrag, CrosshairKey, Tooltip, "legend", "keyboard", "selection", "measure", "diagnostics" })
        {
            spec.Modifiers.Add(new ModifierSpec { Type = modifier });
        }

        spec.Modifiers.Single(modifier => modifier.Type == "selection").Parameters.Set("lasso", true);
        return spec;
    }

}
