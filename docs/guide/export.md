# Export, clipboard, printing

All ways show what the chart shows at this moment, with annotations. The display does not change.

```csharp
var options = new ExportOptions { Width = 1200, Height = 800, Dpi = 300, Theme = BuiltInThemes.Paper };
await File.WriteAllBytesAsync("chart.png", await chart.ExportPngAsync(options));
await File.WriteAllBytesAsync("chart.svg", await chart.ExportSvgAsync(options));
await File.WriteAllBytesAsync("chart.pdf", await chart.ExportPdfAsync(options));
await File.WriteAllBytesAsync("data.csv", await chart.ExportCsvAsync());
```

`Width` and `Height` count in units of 1/96 inch. `Dpi` gives more pixels with the same layout. `Theme` only applies to the image.

| Format | Content |
|---|---|
| PNG | Pixels from the active backend, the resolution is stored in the file |
| SVG, PDF | Lines and areas as vectors, text and rasters as images of at least 288 dpi |
| CSV | `series,x,y`, numbers with a dot, visible or selected points |

## Clipboard and printing

```csharp
await chart.CopyImageAsync();
await chart.CopyDataAsync();
await chart.PrintAsync("My report"); // WPF only
```

In Avalonia, print with `ExportPdfAsync` and hand the file to the system.

## Limits

- Text in SVG and PDF cannot be selected.
- Bars, areas and rasters have no CSV rows.
- A chart must be visible, otherwise you get an `InvalidOperationException`.
- SVG and PDF cost one rebuild of the graphics buffers. The display stays identical but can stutter briefly.
- Many markers without density mode make SVG and PDF large.

## 3D charts

A 3D chart exports with the same calls. PNG renders the scene on the graphics card at the size you ask for, 4K and more, in the theme of the options; the display does not change. SVG and PDF embed the 3D picture as an image of at least 288 dpi, with axes, title and text around it as in 2D. CSV writes one table with the columns `object, x, y, z, value`: markers, lines, points, surfaces, meshes, maps, columns, waterfalls and point clouds of up to five million points, all inside the axes, and antenna patterns with azimuth, elevation and gain in x, y and z. Volumes and larger clouds stay out, they hold far more values than a table program opens.

Your own series can write a table of its own by implementing `ICsvExport`.

## Video

```csharp
await chart.ExportVideoAsync("orbit.mp4", new VideoOptions { Width = 1920, Height = 1080, FrameRate = 30, Duration = TimeSpan.FromSeconds(6) },
    (frame, time) => chart3D.Scene.Camera.Orbit(2, 0)); // turn the camera a little before each frame
```

Each frame belongs to a fixed time, frame number divided by frame rate, so the video plays evenly however long a frame takes to render. The step before each frame moves a camera, adds data or shifts a range; it runs on the render thread. On Windows Media Foundation encodes H.264 in MP4, which every Windows desktop has. Elsewhere the video needs an FFmpeg that you install yourself: put it on the PATH or set `VideoOptions.FFmpegPath`; without it you get a `FileNotFoundException` that says so. FFmpeg is not part of the package. Odd sizes are rounded down to even ones, as H.264 needs them. In the showroom the button **Video** records six seconds: a 3D page turns once around, a 2D page records what keeps arriving.

## Recording like a video recorder

```csharp
chart.StartRecording("session.mp4");   // the size of the chart, 30 frames per second
// … the user zooms, pans, turns the 3D view, drags planes and sets markers …
await chart.StopRecordingAsync();       // completes the file
```

The recording takes what the chart shows, while the user works with it. Pictures are taken on the render thread and encoded on a thread of their own; when a picture comes late, the previous one is shown longer, so the video plays evenly and lasts as long as the recording. A picture at 1080p costs about 3.5 ms on the render thread. Pass `VideoOptions` for a fixed size or another frame rate; the encoders are the same as for planned videos. Both chart controls implement `IRecordableChart`, so code for videos works with WPF and Avalonia alike.

A window often shows several charts that belong together: a spectrum with its history, a 3D chart with a 2D chart below it, a page of rasters. `ChartAreaRecorder` records all charts inside a panel at their places, as the window shows them, in the size of the panel; WPF and Avalonia call it the same way:

```csharp
var recording = ChartAreaRecorder.Start(panel, "session.mp4");
// … work with the charts …
await recording.StopAsync();

await ChartAreaRecorder.ExportVideoAsync(panel, "turn.mp4", options, step); // a planned video of the panel
```

The places are taken when the recording starts. In the showroom, the red button records the whole stage on every page, and the page **Video recording** shows the code of both ways.

