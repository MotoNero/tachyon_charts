# GPU, fallback, diagnostics

Tachyon chooses on its own: GPU, then software adapter, then CPU (Skia). A chart therefore also runs over RDP or without suitable drivers, only slower.

```csharp
Chart.Renderer = RendererPreference.Skia; // before the first chart
```

The environment variable `TACHYON_WEBGPU_ADAPTER=hardware|software` forces the adapter.

If the driver crashes, the chart rebuilds itself. If that happens several times in a short period, it falls back to the next backend.

The diagnostics overlay (F12 with the `Diagnostics` block, or `chart.ShowsDiagnostics = true` from code) shows the frame rate, the frame time as median and 99th percentile, the CPU time per frame, draw calls, points drawn and points in the data, uploaded bytes, the backend with the graphics card, and warnings. A 3D chart adds the measured GPU time, the point budget, the loading progress of clouds and the graphics memory of each surface, cloud and volume. Writing the lines allocates nothing. It does not appear in exports.

A series of your own adds lines by overriding `WriteDiagnostics(DiagnosticsText text)`.

In the showroom, F12 switches the overlay on every page, also when no chart has the focus, and it stays on when you change the page. Still images in the gallery show how long they took to render. `--diagnostics` starts the apps with the overlay on.

The measured values are in the README.

## Smooth edges

Every chart type draws smooth edges, on the GPU, in the CPU fallback and in exported pictures alike:

- **2D:** lines, markers and text smooth their own edges. Filled shapes with slanted edges (pie and donut, areas, polygons without an outline, milestones) get a soft band of one pixel along the outside, which costs a few triangles instead of multisampling the whole picture. Edges along the pixel grid, such as bars and candles, stay crisp, and edges where two filled shapes meet, such as the layers of stacked areas, get no band, so no seam shows.
- **3D:** the picture is multisampled four times; points and bubbles smooth their outline on top.

## Frame rate

Charts draw at most as many frames per second as the fastest screen shows: up to 120 at 120 Hz, up to 60 at 60 Hz. The diagnostics (F12) show the limit as *FPS limit*. A fixed limit saves power, for all charts of the process:

```csharp
Chart.MaximumFramesPerSecond = 30;    // null follows the screens again
```

With screens of different refresh rates, Windows may show windows at the rate of the slowest screen; the chart then draws no more frames than are shown.
