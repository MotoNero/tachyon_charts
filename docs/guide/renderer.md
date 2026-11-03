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
