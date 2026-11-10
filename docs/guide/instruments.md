# Instrument displays

The engine draws these displays itself, the same way in WPF and Avalonia. You describe them with `Configure` and deliver measurements from any thread.

## Oscilloscope

```csharp
var display = new ScopeDisplay { TimePerDivision = 1e-3 };
var channel = display.AddChannel();
channel.VoltsPerDivision = 0.5;
chart.Configure(c => c.Oscilloscope(display).Theme(BuiltInThemes.Instrument));

channel.SetWaveform(time, volts); // time in seconds, 0 is the trigger
```

| Gesture | Effect |
|---|---|
| Drag at the left edge | Move a channel |
| Drag at the right edge | Trigger level |
| Drag in the picture | Move the time |
| Wheel at the left edge, over "V/div" or Ctrl+wheel | Volts per division of the nearest channel, 1-2-5 |
| Wheel in the picture | Time per division, 1-2-5 |

`display.Persistence` knows `Off`, `Variable`, `Infinite` and `ColorGrade`. Changing a scale or position clears the stored traces, as on a real instrument.

For free-running signals there is `ScopeTrigger`. It compares the signal with the level and the edge (`Slope`), knows `HoldoffSeconds` and the modes `Auto`, `Normal` and `Single`, and aligns the traces at the edge:

```csharp
var trigger = new ScopeTrigger(display) { Slope = TriggerSlope.Rising };
trigger.Process(sampleTime, channels);
```

## Spectrum

```csharp
var display = new SpectrumDisplay { Mask = new FrequencyMask(frequencies, limits), ShowsDensity = true };
display.SetView(0, 1e7, 0);
display.MaskViolated += violation => { };
chart.Configure(c => c.Spectrum(display).Theme(BuiltInThemes.Instrument));

display.SetTrace(frequencies, levels);
```

`ShowsAxes = true` numbers the axes: the frequency below with its unit (Hz, kHz, MHz, GHz) and the level in dBm on the left, following reference level, division and zoom. Without it the readings in the corners name them, as on many instruments. The reference level may lie any number of divisions from 0 dBm.

| Gesture | Effect |
|---|---|
| Wheel | Zoom the frequency around the pointer |
| Wheel at the left edge | dB per division |
| Ctrl+wheel | Reference level |
| Drag | Move the frequency |
| Click | Set the marker, double click: back to the peak |
| Ctrl+click | Add a further marker, or remove the one at the pointer, up to five |

The spectrogram below it is a second chart with the same frequency axis. The newest trace is at the top:

```csharp
history.Configure(c => c.SpectrumHistory(display));
```

## Network analysis

```csharp
var display = new NetworkDisplay();
var s21 = display.AddTrace("S21");
s21.SetSweep(frequencies, complexValues);
s21.AddLimit(new LimitLine(LimitKind.Maximum, 1e9, -40, 1.25e9, -40));
display.AddMarker(1.5e9);
chart.Configure(c => c.NetworkAnalyzer(display, 1e9, 2e9, -70, 5)
    .Modifiers(m => m.ZoomPan().Custom(() => new NetworkMarkerModifier(display))));
```

With `NetworkMarkerModifier`, dragging a marker moves it, and Ctrl+click adds a marker or removes the one at the pointer.

## Language

`ChartText.Language = ChartLanguage.German;` switches all displays at once, including the numbers. English is the default. Another language is another column in `ChartText`.

## Signal tools

`Tachyon.Charts.Signal` depends on nothing: `Fft.Spectrum`, `FirFilter`, `BiquadFilter`, `SignalGenerator`. Pass the result of `Fft.Spectrum` to `SpectrumDisplay.SetTrace`.

## Your own theme

`BuiltInThemes.Instrument` is the template. Set your own values in your project without changing Tachyon. What you leave out comes from the base theme:

```csharp
var values = new Dictionary<string, string>
{
    ["Tachyon.surface.background"] = "#102030",
    ["Tachyon.grid.major"] = "#FFFFFF20",
    ["Tachyon.axis.label"] = "#E0E0E0",
};
chart.Theme = ThemeResources.Create("My theme", "Instrument", values);
```

In WPF the same works from XAML resources with `ChartThemes.FromResources(Resources, "My theme", "Instrument")`.

The traces of all three displays take their colors from the palette of the theme, see [Themes](themes.md).
