# Tachyon Charts

GPU-accelerated 2D and 3D charts for .NET 10, built for test and measurement, laboratory and scientific work. Runs in WPF and Avalonia on WebGPU, with a CPU fallback through Skia.

![The showroom app with a four-channel oscilloscope](docs/images/app-oscilloscope.png)

> **Status:** preview, see [Releases](../../releases). The API can still change.

## At a glance

Measured on a laptop with an RTX 4070 Laptop GPU, see [Performance](#performance):

| | |
|---|---:|
| Line with 100 million points, zoom and pan (4K) | **158 FPS** |
| Spectrogram 4096 × 4096 (4K) | **270 FPS** |
| Point cloud of 100 million points, turning, full-screen window | **115 FPS** |
| Surface of 4096 × 4096 values, turning, full-screen window | **115 FPS** |
| Volume 512³, turning, full-screen window | **up to 118 FPS** |
| CT series 1024 × 1024 × 1000 (2 GB): slices, windowing and 3D | **62 FPS** at 60 steps per second |
| Nearest of 100 million points under the pointer | **0.4 ms** |
| Chart at rest | **no frames, 0.02 % CPU** |

## Highlights

- **Fast:** 100 million points per line, 10 million scatter points, 4096 × 4096 spectrograms, all at interactive rates; every 3D type above 60 frames per second in a full-screen window
- **Live:** stream from any thread without blocking the display; the newest value of each series as a mark at the axis
- **Instruments included:** oscilloscope with trigger and persistence, spectrum analyzer with spectrogram and masks, network analyzer, sweep and stepping monitors
- **Scientific types:** Smith chart, ternary plot, parallel coordinates, polar, radar, heatmap with cells of their own sizes, contour with labels along the lines
- **Finance:** candles, OHLC bars, volume, market profile and drawing tools
- **3D as an add-on:** surfaces, waterfalls, antenna patterns, point clouds of 100 million points, volumes and models from STL and OBJ files, live and with values under the pointer
- **Medical data as an add-on:** privacy-first DICOM and NIfTI reading, linked axial, coronal, sagittal and 3D views, segmentations, measurements in millimetres, profiles and histograms, oblique and curved sections, CT series up to 1024 × 1024 × 1000, phantoms and freely licensed sample data on request, without patient-information APIs
- **Runs everywhere:** GPU, software adapter or CPU, chosen automatically, also over remote desktop
- **Ready for reports:** export to PNG, SVG, PDF and CSV, clipboard and printing, MP4 videos of 2D and 3D charts

## Gallery

### Instruments

| Spectrum analyzer | Network analyzer |
|---|---|
| ![Spectrum analyzer with a spectrogram below it](docs/images/app-spectrum.png) | ![S-parameters of a band-pass filter with limit lines and markers](docs/images/network.png) |

| Color grading | Smith chart |
|---|---|
| ![Oscilloscope trace with a color-graded density](docs/images/color-grade.png) | ![Smith chart with a marker](docs/images/smith.png) |

| Sweep monitor |
|---|
| ![Four channels of a patient monitor writing over the previous pass with a gap ahead](docs/images/sweep.png) |

### Large data

| Heatmap | Filled contour | 50 million points |
|---|---|---|
| ![Heatmap of 4096 by 4096 cells](docs/images/heatmap.png) | ![Filled contour plot](docs/images/contour.png) | ![Point cloud shown as density](docs/images/point-cloud.png) |

| Labelled contour lines | Heatmap inside an area |
|---|---|
| ![Filled contours with the value of each line written along the line](docs/images/contour-labels.png) | ![A measurement shown only inside a region, not on its lake](docs/images/heatmap-area.png) |

### Chart types

| Bars | Stacked areas | Bubbles |
|---|---|---|
| ![Grouped bars](docs/images/bars.png) | ![Stacked areas](docs/images/stacked-area.png) | ![Bubble chart](docs/images/bubble.png) |

| Violin | Error bars | Parallel coordinates |
|---|---|---|
| ![Violin plot](docs/images/violin.png) | ![Measurements with error bars](docs/images/error-bars.png) | ![Parallel coordinates with 20,000 rows](docs/images/app-parallel.png) |

| Polar | Radar | Ternary |
|---|---|---|
| ![Polar chart](docs/images/polar.png) | ![Radar chart](docs/images/radar.png) | ![Ternary plot](docs/images/ternary.png) |

| Pie | Donut |
|---|---|
| ![Pie of a power mix with a pulled-out slice and small slices labelled on lines](docs/images/pie.png) | ![Donut of the same power mix](docs/images/donut.png) |

| Gantt | Polygons |
|---|---|
| ![Test plan over three stations with groups, dependencies and milestones](docs/images/gantt.png) | ![A ring, a star and an island with two lakes](docs/images/polygons.png) |

### Finance

| Candles and volume | OHLC bars |
|---|---|
| ![Daily candles of a share with volume bars in a pane below](docs/images/candles.png) | ![The same prices as OHLC bars in the light theme](docs/images/ohlc.png) |

| Several legends | Crosshair with values |
|---|---|
| ![Temperatures and pressures, each with a legend of its own, one inside the plot and one below it](docs/images/legends.png) | ![A crosshair with a point on each of six curves and a box with the time and all six values in the colors of the series](docs/images/crosshair-values.png) |

| Last value at the axis | Axes in the origin | X axis down |
|---|---|---|
| ![Three live temperatures with their newest values as colored marks at the axis, moved apart where they meet](docs/images/last-value.png) | ![A function whose axes cross at zero](docs/images/axes-origin.png) | ![A borehole log with the depth running down: gamma rays as an area, temperature as a line, core samples as points](docs/images/depth-profile.png) |

| Scale break | Candles without weekends | Values at the points |
|---|---|---|
| ![A line with small values and one high peak, the empty range between left out](docs/images/scale-break.png) | ![Daily candles on working days, the weekends take no room](docs/images/candles-weekends.png) | ![Sales per quarter as grouped bars with their values on top and a target line with its values](docs/images/value-labels.png) |

| Market profile | Drawing tools |
|---|---|
| ![Volume per price level of the visible days with point of control and value area](docs/images/market-profile.png) | ![Fibonacci retracement, trend channel, pitchfork and a harmonic pattern on candles](docs/images/drawings.png) |

### 3D

| Surface with height lines | Waterfall | Antenna pattern |
|---|---|---|
| ![Surface of 4096 by 4096 values with height lines](docs/images/3d-surface.png) | ![Waterfall of a spectrum](docs/images/3d-waterfall.png) | ![Antenna gain as a shape with main lobe and half-power ring](docs/images/3d-antenna.png) |

| Map from measurements | Lines | Scatter |
|---|---|---|
| ![Temperature over speed and load from uneven measured points](docs/images/3d-map.png) | ![Path of a particle as a line in space](docs/images/3d-lines.png) | ![Three clouds with a size and color per point](docs/images/3d-scatter.png) |

| Columns | Point cloud | Cut planes |
|---|---|---|
| ![Columns over a raster with a color by value](docs/images/3d-bars.png) | ![Scanned landscape as a point cloud](docs/images/3d-cloud.png) | ![One plane cuts a line, a cloud and columns](docs/images/3d-cut-planes.png) |

| Volume slices | Through a volume | Objects inside a volume |
|---|---|---|
| ![Three draggable slices through a body](docs/images/3d-volume-slices.png) | ![Looking through a body with bright spots inside](docs/images/3d-volume-through.png) | ![A surface and a line inside a volume](docs/images/3d-volume-objects.png) |

| Maximum projection | Surface of equal value | Waterfall with mask and markers |
|---|---|---|
| ![The highest value along each ray of a volume](docs/images/3d-volume-maximum.png) | ![A lit surface where the values of a volume cross a level, cut open by a plane](docs/images/3d-volume-surface.png) | ![Waterfall of a spectrum with the limit line of the 2D chart as a fence and two markers](docs/images/3d-waterfall-mask.png) |

| Model with measured values | Model of 1 million triangles |
|---|---|
| ![A pipe bend read from an STL file, colored by wall temperature, cut open, with sensors](docs/images/3d-model.png) | ![A knot of tube with a million triangles, colored by height](docs/images/3d-knot.png) |

| Bubbles | 1 million bubbles | Planes and limits |
|---|---|---|
| ![Wells of three fields as lit spheres, the volume shows the yield](docs/images/3d-bubbles.png) | ![A spiral of a million lit spheres with a size in screen points](docs/images/3d-bubbles-1m.png) | ![A surface against a transparent limit plane with red lines where it crosses, a test zone and a slanted area with a hole](docs/images/3d-planes.png) |

| Pie 3D | Donut 3D |
|---|---|
| ![The power mix as a lit pie with one slice pulled out](docs/images/3d-pie.png) | ![The power mix as a lit ring](docs/images/3d-donut.png) |

### Themes

Seven built-in themes, plus your own from JSON or XAML resources. Themes apply to every chart type, including the instruments.

| Neon | Paper | Instrument |
|---|---|---|
| ![Neon theme](docs/images/theme-neon.png) | ![Paper theme](docs/images/theme-paper.png) | ![Instrument theme](docs/images/theme-instrument.png) |

## Features

- Lines, scatter and density, bubbles, area, band, bars, error bars, stems, histogram, box plot, violin, pie and donut
- Gantt and time lines: rows in collapsible groups, dependencies, milestones, 100 000 tasks at 60 frames per second
- Polygons with holes, 10 000 of them at 60 frames per second, and heatmaps cut out to an area
- Finance: candles and OHLC bars from a thread-safe candle store, a million candles at 60 frames per second, volume in a pane of its own, market profile with point of control and value area
- Drawing tools: trend line, Fibonacci retracement and extension, channel, pitchfork, Elliott waves, XABCD, head and shoulders and a free brush, fixed to values, saved as JSON
- Numeric, time, logarithmic and category axes, stacked panes, linked charts, scale breaks and hidden times such as weekends, axes that cross in the origin, charts turned with the X axis down for depth profiles
- Zoom, pan, crosshair with the values of all series at the pointer, tooltip, lasso selection, measuring cursors, annotations, one undo chain for navigation and drawings
- Several legends per chart, inside or outside the plot, each with its own series; a click switches a series off and on
- Signal tools without dependencies: FFT, FIR and biquad filters, generators
- English and German texts, numbers and month names, switchable at runtime

### 3D

3D is an add-on: the packages `Tachyon.Charts.ThreeD`, `.ThreeD.Wpf` and `.ThreeD.Avalonia` build on the 2D charts, and the 2D packages know nothing about them. It needs a real GPU. Without one, the chart shows a clear note.

- **Surfaces:** up to 4096 × 4096 values, gaps, light and gloss, transparent surfaces without sorting, height lines on the surface, the floor and the walls
- **Waterfall:** live from the spectrum analyzer, as a surface or as single lines
- **Antenna pattern:** gain over azimuth and elevation as a closed shape, dB scale with an adjustable floor, main lobe and half-power marks, cuts for 2D polar charts
- **Maps from measurements:** uneven points become a surface through a triangulation of its own
- **Lines:** constant width on the screen, clean bends, gaps at `NaN`, live streams
- **Scatter:** a size and color per point, a million new points per second from a measuring thread, oldest points fall out
- **Bubbles:** lit spheres with a diameter per point in data units or screen points, sizes from values by volume, exact depth where they meet other objects; a million at the frame rate of the screen
- **Planes and flat shapes:** transparent limit planes that show where a surface crosses them, rectangles at any angle, polygons with holes on a plane in space
- **Pie and donut:** lit bodies that turn, a click pulls a slice out, name, value and share under the pointer as in 2D
- **Columns:** over a raster, with a color by value and the same light as surfaces
- **Point clouds:** an octree forms in the background, a coarse picture shows at once, and the density never jumps. Files in XYZ, PLY and LAS through `Tachyon.Charts.ThreeD.Files`
- **Models:** STL (text and binary) and OBJ through `Tachyon.Charts.ThreeD.Files`, with sharp edges kept, a value per point that colors the model and shows under the pointer, cut open by cut planes; a million triangles at the frame rate of the screen
- **Volumes:** 8 bit, 16 bit and floating point, three draggable slices, looking through with a transfer function that you edit in the chart, the highest value along each ray, lit surfaces of equal value, correct depth against other objects. Files in NIfTI-1 and raw through `Tachyon.Charts.ThreeD.Files`
- **Operation:** turn, move and zoom in perspective or parallel view, values under the pointer with unit and language, markers, up to four cut planes at any angle, cuts as data for 2D charts
- **Together with 2D:** markers and the limit mask of the spectrum analyzer appear in the 2D and the 3D chart at once; a 2D chart narrows the range of a 3D chart
- **Export:** PNG, SVG and PDF with the 3D picture, CSV with all data of the scene, MP4 videos that turn the view or record what the user does

### Medical data

`Tachyon.Charts.Medical` is a separate add-on for analytical image and volume applications. Its reader is deliberately limited to pixels, image geometry and value interpretation; it does not expose patient information.

It reads uncompressed single-frame grayscale DICOM Part 10 data in the three standard uncompressed transfer syntaxes, including signed and unsigned 8- or 16-bit pixels. A series reader sorts slices by patient-space position and produces a regularly spaced volume with its physical geometry.

![A synthetic axial medical image with millimetre axes, orientation marks and the value under the pointer](docs/images/medical-image.png)

`MedicalImageDisplay` turns an image or rescaled array into an interactive grayscale chart with bilinear display filtering. Left-drag adjusts window and level without rebuilding the image, common CT presets are included, and the pointer reports the original value with its unit and physical position.

![A synthetic head volume shown as linked 3D, axial, coronal and sagittal charts](docs/images/medical-mpr.png)

`MedicalMprDisplay` supplies three regular 2D chart descriptions and one 3D chart description for an application-defined layout. A click in any slice or on the volume moves all crosshairs and 3D planes to the same physical position. The wheel and arrow keys step through slices, Space starts cyclic cine playback, and +/- changes its speed. Every slice shows its number and physical position. Window and level are shared by all four views without uploading the volume again.

![Synthetic categorical structures over three medical slices and as translucent 3D surfaces](docs/images/medical-segmentation.png)

Integer segmentation maps can come from arrays, affine NIfTI-1 files or uncompressed binary DICOM-SEG objects. They retain their own patient-space geometry and are aligned to the image automatically. Each label has an independent name, color, opacity and visibility and can be shown in 3D as a categorical volume or a lit boundary surface.

![A distance, an angle, a circle and a freehand region on a synthetic head image, each with its result](docs/images/medical-measurements.png)

| Profile along a line | Histogram of a region |
|---|---|
| ![The values along a line through a synthetic head: air, bone, tissue, ventricles](docs/images/medical-profile.png) | ![The values of a round region in bins of about 4 HU, with the grayscale window as a band](docs/images/medical-histogram.png) |

Measurements work on every image and MPR plane: distances in millimetres, angles, and circles, rectangles and freehand regions with area, mean, standard deviation, minimum and maximum in the unit of the image. Uneven pixel spacing is part of every result, regions of one name on several slices give a volume, and all measurements can be edited, undone and saved as JSON. A profile along a line and a histogram of a region or slice follow their measurement, the slice and the window live.

![An axial slice of the synthetic thorax in the lung window: lungs with branching vessels, heart, aorta and spine](docs/images/medical-thorax-phantom.png)

| Oblique plane through a line | Curved section along the ribs |
|---|---|
| ![A plane standing upright on a line across the synthetic thorax: lungs, heart and ribs](docs/images/medical-oblique.png) | ![The chest wall of the synthetic thorax stretched flat: ribs as bands, the sternum in the middle](docs/images/medical-curved.png) |

Planes at any angle follow a line drawn on a slice or face the 3D view, and a curved section follows a drawn line through all slices, stretched flat.

Synthetic head and thorax volumes come with the package (`MedicalPhantoms`), free of any personal data. For real data, `MedicalSampleDataset` downloads a chest CT (LIDC-IDRI, CC BY 3.0) or a defaced head MRI (OpenNeuro ds000001, CC0) on request, with progress and a SHA-256 check; nothing is bundled. `NiftiImageReader` reads NIfTI-1 research images. Large series up to 1024 × 1024 × 1000 values stay at two bytes per value, and each volume fits itself to the graphics memory and says how.

#### With real data

The same tools on freely licensed, anonymized datasets: in the showroom, choose a dataset in the data list of the pages "Medical CT and MRI", "Medical analysis" or "Medical oblique and curved". It is downloaded once, checked against its checksum and kept in the local application data; nothing is bundled with the packages.

![A chest CT in 3D and as an axial slice with a line and a circle in the right lung; the profile along the line shows lung, heart and chest wall, the histogram the lung tissue in the circle](docs/images/medical-real-analysis.png)

![The measuring tools on a chest CT: a distance across the right lung, an angle, a rectangle on the aorta and a freehand region around the heart, each with its result, beside the linked coronal and sagittal planes](docs/images/medical-real-measurements.png)

![An oblique plane through the chest CT along a drawn line, showing both lungs with their vessels, the heart and the upper abdomen, and a curved section along the front of the chest](docs/images/medical-real-sections.png)

![A head MRI in 3D and as an axial slice with a line and an arc; the oblique plane shows the brain from the side and the curved section the folded cortex stretched flat. The face was removed before publication](docs/images/medical-real-mri.png)

*Chest CT:* Armato III, S. G. et al. (2015). Data From LIDC-IDRI, The Cancer Imaging Archive, [doi:10.7937/K9/TCIA.2015.LO9QL9SX](https://doi.org/10.7937/K9/TCIA.2015.LO9QL9SX), licensed [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/); the [data usage policy](https://www.cancerimagingarchive.net/data-usage-policies-and-restrictions/) of the archive applies. *Head MRI:* Schonberg, T. et al. (2012). Balloon Analog Risk-taking Task, OpenNeuro ds000001, [doi:10.18112/openneuro.ds000001.v1.0.0](https://doi.org/10.18112/openneuro.ds000001.v1.0.0), [CC0](https://creativecommons.org/publicdomain/zero/1.0/), defaced. Both datasets were published anonymized; never try to identify a person. The pictures show cross-sections only, no 3D surfaces of a head.

Tachyon Charts is not a medical device and is not approved or certified for diagnosis or therapy under the EU Medical Device Regulation (MDR). The application developer and operator remain responsible for the intended use, validation, privacy and every applicable regulatory requirement. See [Medical data and privacy](docs/guide/medical.md).

## Quick start

```csharp
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Wpf; // or Tachyon.Charts.Avalonia

var data = new DataSource<double, double>();
for (var i = 0; i < 1000; i++)
{
    data.Append(i * 0.01, Math.Sin(i * 0.01));
}

var chart = new Chart().Configure(c => c
    .Title("Signal")
    .XAxis(a => a.Numeric("x").Title("Time in s").AutoRange())
    .YAxis(a => a.Numeric("y").Title("Value").AutoRange())
    .AddLine(data, s => s.Thickness(2))
    .Modifiers(m => m.ZoomPan().RubberBand().Crosshair().Tooltip()));
```

An oscilloscope is just as short:

```csharp
var display = new ScopeDisplay { TimePerDivision = 1e-3 };
var channel = display.AddChannel();
chart.Configure(c => c.Oscilloscope(display).Theme(BuiltInThemes.Instrument));

channel.SetWaveform(time, volts); // from your measuring thread
```

A surface in 3D:

```csharp
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD.Scene;
using Tachyon.Charts.ThreeD.Wpf; // or Tachyon.Charts.ThreeD.Avalonia

var heights = new HeatmapData(512, 512);
heights.SetValues(values); // 512 × 512 floats

var chart = new Chart3D().Configure(c => c
    .Title("Surface")
    .XAxis(0, 10, "x in mm").YAxis(0, 10, "y in mm").ZAxis(-1, 1, "Height in µm")
    .AddSurface(new Surface3D(heights, new DoubleRange(0, 10), new DoubleRange(0, 10))
    {
        Contours = Contours3D.Even(new DoubleRange(-1, 1), 12),
    }));
```

## Try it

The easiest way: download the showroom from [Releases](../../releases), unpack the zip and start the `.exe`. No .NET needed.

From source, with the .NET 10 SDK:

```bash
dotnet run --project samples/Tachyon.Charts.Samples.Wpf -c Release
dotnet run --project samples/Tachyon.Charts.Samples.Avalonia -c Release
```

The showroom lists the interactive pages, the instruments and all scenes. Theme and language are at the top right. F11 shows the source code of a page, the red button records what the page shows as an MP4 video.

### Configurator (preview)

Put a chart together and copy its code: the configurator shows the chart and its C# side by side, and every change shows at once in both. It writes the fluent C#, a view model with the data, the window in XAML and a complete WPF or Avalonia project. It offers line (straight, step, spline), scatter, bubbles, area, stacked area, band, bars (grouped, stacked, horizontal), error bars, stems, histogram, box plot, violin, pie, donut, heatmap (also uneven and inside a region), contour, spectrogram, polar, radar, ternary, Smith chart, parallel coordinates, polygons, a scatter colored by density, candles or OHLC bars with volume, market profile and drawing tools, Gantt charts, and the instruments oscilloscope, spectrum analyzer, network analyzer and sweep monitor with their controls, each with its options. Axes can show categories, run through the origin, leave ranges out or hide; panes stack, two legends list chosen series, annotations mark lines, bands, boxes and text, lines get dashes and a glow, areas colors by value, bars round corners and a shadow, and a time axis can leave out the weekends. Data come from samples, from CSV files (example files included; delimiter, header and decimal comma are recognized, the columns are offered by name, and several Y columns draw several lines), from HDF5 datasets of one to three dimensions, from files of points, volumes and models in 3D, or live at a set rate; the code reads or makes them the same way. The generated window can bring a context menu that saves the chart as PNG, SVG or PDF, its data as CSV, or records a video and a switch for the values at the points. With the medical package it adds a medical image, a plane of a volume and the volume in 3D, and linked charts: the planes with the volume, an analysis with profile and histogram, and oblique and curved sections, each with windowing, segmentations and measurements, on head and thorax phantoms or on own DICOM and NIfTI files that are read for the preview only and never saved. With the 3D package it adds surface, columns, waterfall, line, scatter, bubbles, point cloud, map from measurements, volume, antenna pattern and pie in 3D, with camera, cut plane, light and a group shared with 2D charts; without the package it leaves 3D out. More types follow with each release. `--type boxplot` starts with a given type.

```bash
dotnet run --project src/Tachyon.Charts.Configurator.Wpf -c Release
dotnet run --project src/Tachyon.Charts.Configurator.Avalonia -c Release
```

![The configurator with a box plot: settings on the left, the chart in the middle, the generated C# on the right](docs/images/configurator.png)

![The configurator reading an example CSV file: two columns chosen draw two lines](docs/images/configurator-csv.png)

![The configurator with a medical plane of the head phantom and its segmentation](docs/images/configurator-medical.png)

![The configurator with linked medical charts: three planes and the volume in 3D](docs/images/configurator-medical-mpr.png)

![The configurator with a 3D surface and the scene settings: camera, cut plane](docs/images/configurator-3d.png)

## Performance

Offscreen on an RTX 4070 Laptop GPU at 4K.

| Scenario | Result |
|---|---:|
| Line, 100 million points, zoom and pan | 158 FPS |
| Scatter, 10 million points with own colors | 79 FPS |
| Streaming, 10 series at 100k points/s | 147 FPS |
| Spectrogram 4096 × 4096 | 270 FPS |
| Nearest point, 100 million points | 0.4 ms |

3D in a real full-screen window (2560 × 1440) on the same GPU while the view turns, with the frame rate cap of the chart raised from 60 to 120:

| Scenario | Result |
|---|---:|
| Point cloud, 100 million points | 115 FPS |
| Surface 4096 × 4096 | 115 FPS |
| Waterfall, 60 new rows per second | 117 FPS |
| Volume 512³, slices, look-through, maximum or surface | 114 to 118 FPS |
| Lines, 10 million points | 95 FPS |
| Scatter, 10 million points | 117 FPS |
| Columns, 1 million | 99 FPS |
| Bubbles, 1 million | 59 FPS, 3 ms per frame (screen at 60 Hz) |
| Model, 1 million triangles | 60 FPS, 5.8 ms per frame (screen at 60 Hz) |
| Four large 3D charts at once | 52 FPS |
| Chart at rest | no frames, 0.02 % CPU |

A synthetic CT series of 1024 × 1024 × 1000 values of 16 bits (2.1 GB) in the medical page with three planes and 3D, at 60 steps per second: reading 1000 DICOM files takes 1.8 s, the way to the graphics card 3.1 s, and turning in 3D, moving through the slices and windowing each run at 62 FPS.

By default the charts draw as many frames per second as the fastest screen shows; every single scenario above reaches 60.

3D, offscreen on the same GPU at full HD while the view turns:

| Scenario | Result |
|---|---:|
| Surface 4096 × 4096 | 7.1 ms per frame |
| Lines, 10 million points | 83 FPS |
| Scatter, 10 million points with own colors | 85 FPS |
| Columns, 1 million | 102 FPS |
| Point cloud, 100 million points | 60 FPS, in a real window as well |
| Live cloud, 20 million points, a million new per second | 67 FPS |
| Through a volume, 512³ with 16 bits | 185 FPS |
| Value under the pointer, surface of 4096 × 4096 | 25 µs |

Measured on a laptop GPU only. A point cloud draws at most 30 million points per frame and lowers this number by itself when the GPU needs more time than a frame has.

To measure a window on your own machine, set `TACHYON_FRAME_LOG` to a file and start the showroom with a page and `--turn`, which turns the view without a mouse. `--near` zooms in first.

In real windows with 8 live charts: 56 FPS on the GPU, 21.6 FPS on the CPU.

Known gaps: a stress test of 50 charts that all change per frame reaches about 21 FPS in a real window instead of the targeted 60, and the CPU fallback misses its targets for streaming (23 instead of 30 FPS at full HD) and for large scatter plots (9 instead of 10 FPS). Both are planned.

## Build

```bash
dotnet build Tachyon.Charts.sln -c Release
dotnet run --project samples/Tachyon.Charts.Samples.Wpf -c Release
dotnet run --project samples/Tachyon.Charts.Samples.Avalonia -c Release
```

## Documentation

| Document | Content |
|---|---|
| [docs/guide](docs/guide/README.md) | User guide |
| [3D charts](docs/guide/3d.md) | 3D types, one page each: surfaces and planes, waterfall, lines, points and bubbles, columns, point clouds, volumes, models, antennas, cuts |
| [Pie and donut](docs/guide/pie.md) | Slices, labels on lines, pulling a slice out |
| [Finance](docs/guide/finance.md) | Candles, OHLC bars, volume and market profile |
| [Drawing tools](docs/guide/drawings.md) | Fibonacci, channels, pitchfork, patterns, saving as JSON |
| [Gantt](docs/guide/gantt.md) | Rows, groups, dependencies, milestones |
| [Sweep monitors](docs/guide/sweep.md) | Writing over the previous pass or page by page, channels in step |
| [Polygons](docs/guide/polygons.md) | Polygons with holes, heatmaps inside an area |
| [Moving from OxyPlot](docs/guide/migration-oxyplot.md) | Concepts and code side by side for a quick switch |
| [Code signing policy](docs/guide/code-signing.md) | How the showroom apps are signed, who approves, privacy |
| [Medical data and privacy](docs/guide/medical.md) | Reading, MPR, segmentations, measurements, oblique and curved sections, sample and real data, large series, data minimisation and regulatory scope |
| [docs/api](docs/api) | Public API lists |

## License

Licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE).
