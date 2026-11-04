# Sweep and stepping monitors

A monitor display does not scroll: the trace writes from the left over the previous one and clears a gap ahead of itself, as on a patient monitor or an ECG. In stepping mode the trace fills a page, and at the right edge the next page begins empty.

```csharp
// Four channels, 25 000 samples per second each, a screen of 6 seconds.
var monitor = new SweepData(channels: 4, capacity: 25_000 * 7, window: 6) { GapShare = 0.03 };

chart.Configure(c => c.Title("Monitor")
    .Sweep(monitor, [new(-0.6, 1.4), new(-0.6, 1.4), new(-0.1, 1.1), new(-1.2, 1.2)], ["ECG I", "ECG II", "Pleth", "Resp"])
    .Modifiers(m => m.AxisDrag()));

// From the measuring thread: one time per sample, the values of all channels after each other.
monitor.Append(times, values);
```

`Sweep` puts each channel into a pane of its own with its value range; the horizontal axis runs from 0 to the window. All channels share the time of the write head, so they always run in step. `Mode = SweepMode.Stepping` switches to pages, `GapShare` sets the gap as a share of the window, `Window` the width of the screen.

Each pixel column shows the lowest and highest value of its samples, so 100 000 samples per second run at 60 frames per second in a full-screen window. The ring keeps `capacity` samples per channel; give it at least one window at the sample rate.

In the showroom: *Sweep monitor*, *Stepping monitor* among the instruments.
