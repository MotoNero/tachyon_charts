# Waterfall

A waterfall shows traces one behind the other: the newest in front, older ones further back. It reads a `SpectrogramData`, the ring of rows the 2D spectrogram uses, so new rows keep arriving without copies.

```csharp
var rows = new SpectrogramData(columns: 1024, rows: 400);
var waterfall = new Waterfall3D(rows, new DoubleRange(0, 10e6), new DoubleRange(0, 400))
{
    Style = WaterfallStyle.Surface,   // or Lines, one line per trace
    Colormap = Colormaps.Inferno,
    ColorRange = new DoubleRange(-90, 0),
};
chart.Configure(c => c.XAxis(0, 10e6, "Frequency").YAxis(0, 400, "Traces ago").ZAxis(-90, 0, "Level in dBm").AddWaterfall(waterfall));

rows.AppendRow(trace); // from any thread
```

## Behind a spectrum analyzer

`Waterfall3D.From(display, depth)` connects the waterfall to a `SpectrumDisplay`: every trace becomes a row. With the display connected, the waterfall shows the mask of the spectrum as a red fence and its markers as frames across the box, the levels of the display set the height of the box, and a click on the waterfall sets a marker in the spectrum. Put the 3D chart and the 2D spectrum into one group, so that zoom and cursor stay in step:

```csharp
var waterfall = Waterfall3D.From(spectrum.Display, new DoubleRange(0, 400));
chart3D.Configure(c => c.AddWaterfall(waterfall).Group("spectrum"));
chart2D.Configure(c => c.Spectrum(spectrum.Display).Group("spectrum"));
```

In the showroom: *Waterfall, live*.
