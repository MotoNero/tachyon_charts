# Medical data

`Tachyon.Charts.Medical` is an optional add-on for displaying and analysing image volumes with the 2D and 3D charts. Install it separately:

```bash
dotnet add package Tachyon.Charts.Medical
```

The add-on is intended as a technical building block for research, education and analytical applications. It does not provide clinical workflows, diagnoses or treatment recommendations.

> **Not a medical device:** Tachyon Charts and its medical add-on are not medical devices and are not approved or certified for diagnosis or therapy under the EU Medical Device Regulation (MDR). The application developer and operator must assess the intended use, validate the complete application and meet every regulatory requirement that applies to it.

## Privacy and data minimisation

Medical images can contain health data, which is special-category personal data under Article 9 of the General Data Protection Regulation (GDPR). An application using the add-on remains responsible for its lawful basis, access control, retention periods, encryption, audit logging, exports and every other organisational or technical safeguard.

The built-in DICOM reader follows these limits:

- It reads only pixel data and tags needed to interpret image geometry and values.
- It may use the modality and Series Instance UID to interpret or group images.
- It skips patient names, dates of birth, patient and study identifiers and every other unrelated tag.
- Skipped values are never exposed by the API, retained in memory, written to a log or included in an export.

These limits apply data minimisation under GDPR Article 5 and privacy by design and by default under Article 25. They do not make a file anonymous: text and identifiers can be burned into the pixels themselves. Applications that accept external files must inspect and handle pixel data according to their own privacy policy.

The reader deliberately has no switch for loading patient information. If an application has a lawful reason to process additional tags, it must do so through a separate component under its own responsibility.

## Reading an image

The reader accepts DICOM Part 10 streams with uncompressed Implicit VR Little Endian, Explicit VR Little Endian or Explicit VR Big Endian data. Pixels can be signed or unsigned with 8 or 16 allocated bits. Each stream contains one grayscale frame.

```csharp
using Tachyon.Charts.Medical;

using var stream = File.OpenRead("slice.dcm");
var image = DicomReader.Read(stream);

double value = image[column: 120, row: 80]; // rescale slope and intercept are already applied
double? centre = image.WindowCenter;
double? width = image.WindowWidth;
```

`MedicalImage` carries only image information: dimensions, values, spacing, position, orientation, modality, window values and value rescaling. The stream stays owned by the application.

## Showing an image in 2D

`MedicalImageDisplay` configures the regular 2D chart surface, so the same code works in WPF and Avalonia. Axes and pointer positions use millimetres. The image keeps DICOM row order on screen and derives the L/R, A/P and S/I edge marks from its patient-space orientation.

```csharp
var display = new MedicalImageDisplay(image);
chart.Configure(display.Configure);

display.ApplyPreset(CtWindowPresets.Brain); // also Bone and Lung
display.SetWindow(center: 40, width: 80);

if (display.TryGetValue(x: 42.5, y: 18.0, out var probe))
{
    Console.WriteLine($"{probe.Value:F1} {probe.Unit}");
}
```

The chart adds these interactions without platform-specific code:

- left-drag changes width horizontally and centre vertically;
- keys 1, 2 and 3 select the brain, bone and lung CT windows;
- middle-drag pans and the wheel zooms;
- moving the pointer shows the rescaled value, its unit and the physical X/Y position.

Window changes update only the color range. Pixel values and their GPU texture stay unchanged, which keeps the interaction responsive even for large images. Bilinear display filtering smooths enlarged pixels, while the value probe continues to report the original sample. Applications can also construct `MedicalImageDisplay` directly from a row-major span of rescaled values, spacing and orientation.

## Building a volume

Read a series from streams in any order. The builder verifies one Series Instance UID and common dimensions, pixel format, spacing and orientation. It sorts slices by their patient-space position and requires regular spacing:

```csharp
var volume = DicomSeriesReader.Read(streams);

var values = volume.Data;       // VolumeData, ready for Volume3D
var spacing = volume.VoxelSpacing;
var origin = volume.Origin;
var orientation = volume.Orientation;
```

Compressed transfer syntaxes, color images, multi-frame data and irregular slice stacks return a `DicomReadException` with a description that contains neither source values nor file paths. An application can use another decoder and construct its own chart data when it needs encodings outside this deliberately small reader.

In a chain of the chart builder, `MedicalCharts` does the same: `.MedicalImage(display)` for an image, `.MedicalSlice(mpr.Axial)` for one plane of a volume and `.MedicalVolume3D(mpr)` for the volume in 3D. The configurator writes its code this way.

```csharp
chart.Configure(c => c.Title("Axial").MedicalSlice(mpr.Axial));
```

## Linking orthogonal slices and 3D

`MedicalMprDisplay` is shared state rather than a platform control. It supplies axial, coronal and sagittal descriptions for normal 2D charts plus a 3D volume description. The application remains free to arrange those four charts, replace one of them or add its own overlays and controls.

```csharp
var volume = DicomSeriesReader.Read(streams);
var mpr = new MedicalMprDisplay(volume);

axialChart.Configure(mpr.Axial.Configure);
coronalChart.Configure(mpr.Coronal.Configure);
sagittalChart.Configure(mpr.Sagittal.Configure);
volumeChart.Configure(mpr.Configure3D);

mpr.SetPosition(new Point3D(x: 40, y: 35, z: 60)); // volume-local millimetres
mpr.SetWindow(center: 40, width: 80);
mpr.Cine.FramesPerSecond = 20;
mpr.Cine.Play(MedicalMprPlane.Axial);
```

The axes use physical millimetres. A primary click in a 2D slice changes the shared position; a pick on the 3D volume does the same in the other direction. Each view shows the corresponding crosshair or slice plane, slice number and physical position. The wheel, arrow keys and Page Up/Page Down step through the slice under the pointer; Home and End select its boundary slices. Space toggles cyclic cine playback for that plane and +/- changes its speed. Primary dragging changes the shared window and level, while middle dragging pans. Keys 1, 2 and 3 apply the CT presets to every view.

`MedicalMprDisplay.Cine` can also be controlled from application UI through `Step`, `Play`, `Pause` and `Toggle`. Its `StateChanged` event reports changes to the active plane, speed or playback state.

## Segmentation overlays

`MedicalSegmentation` stores positive integer labels together with their own voxel spacing, origin and patient orientation. Label zero is transparent background. The display resamples labels with nearest-neighbour selection in patient space, so image and segmentation may use different raster sizes or orientations.

```csharp
var segmentation = NiftiSegmentationReader.Read(niftiStream);
segmentation.Segments[0].Color = new Rgba8(240, 70, 85, 255);
segmentation.Segments[0].Opacity = 0.6;
segmentation.Segments[0].IsVisible = true;

var mpr = new MedicalMprDisplay(volume);
mpr.AddSegmentation(segmentation);
```

Use `DicomSegReader.Read` for uncompressed binary DICOM-SEG objects, or construct `MedicalSegmentation` from `VolumeData` and explicit geometry. Set `ThreeDMode` in an object initializer to `MedicalSegmentation3DMode.Surface` for lit boundaries; the default is a categorical volume. Fractional and overlapping DICOM segments cannot be represented by one integer label map and are rejected rather than silently losing information. Volume mode supports up to 255 labels at once; surface mode has no categorical table limit.

The constructor also accepts a `MedicalVolume` made from an application's own `VolumeData`, voxel spacing, origin and patient orientation. All geometry values must be finite, spacing must be positive and the two orientation directions must be independent.

The grayscale bar beside every image sets the window too: the wheel on it widens or narrows the window around its centre, dragging it moves the window, and a double click brings back the window the image came with.

## Measurements

Each image and each MPR plane has a `Measurements` layer: distances in millimetres, angles in degrees, and circles, rectangles and freehand outlines with their area and the mean, standard deviation, minimum and maximum of the pixels inside. Points are in millimetres from the upper-left image corner, so a pixel spacing that differs across and down is part of every result. A pixel counts for a region when its middle lies inside.

```csharp
var display = new MedicalImageDisplay(image);
var circle = display.Measurements.Add(MedicalMeasurementKind.Circle, [new(75, 78), new(84, 78)], name: "Focus");
Console.WriteLine($"{circle.Result.Area:F1} mm², {circle.Result.Mean:F1} ± {circle.Result.StandardDeviation:F1} {display.Unit}");

display.Measurements.Tool = MedicalMeasurementKind.Distance;   // the next drag on the image measures a distance
string json = display.Measurements.Save();                     // and Load(json) brings them back
```

- **On the chart:** set `Tool`, or press D, A, C, R or F over the chart, then drag: two points for a distance, circle (middle, then edge) and rectangle, three for an angle (the first arm by dragging, the second end by a click), any number for a freehand outline. Without a tool, dragging a point changes a measurement; the middle of a circle moves the whole circle. Delete removes the measurement last touched, Escape stops the tool, and every change is a step of the undo of the chart.
- **Volumes:** on an MPR plane every measurement belongs to its slice. `Measurements.Volume("Lesion")` adds the areas of the regions named so on all slices and multiplies them by the distance between slices.
- **Changes:** the `Changed` event reports each added, changed or removed measurement, for example to show the results in a table of the application.

## Profile and histogram

`MedicalProfile` shows the values along a distance measurement as a 2D chart beside the image, and `MedicalHistogram` the distribution of the values of a region, or of the whole slice when no region is chosen. Both follow their measurement while it is drawn or moved, and the slice while it changes.

```csharp
var profile = new MedicalProfile(mpr.Axial);      // follows the distance last touched, or set profile.Line
var histogram = new MedicalHistogram(mpr.Axial);  // follows the region last touched, or set histogram.Region
profileChart.Configure(profile.Configure);
histogramChart.Configure(histogram.Configure);
```

- **Profile:** samples every half pixel along the line, blended from the four nearest pixels; the distance from the start of the line runs across in millimetres. `Data` holds the samples for the application.
- **Histogram:** 512 bins over all values of the image, so histograms of different regions compare directly. The grayscale window shows as a band, and the view shows the window and half its width on each side; both follow every change of the window. `Current` holds the counts.

## Oblique and curved sections

`MedicalObliqueSlice` shows a plane at any angle through the volume of an MPR display, and `MedicalCurvedSlice` shows a section along a line drawn on an orthogonal plane, stretched flat. Both sample the eight nearest voxels and share the window of the display; dragging in their charts sets window and level.

```csharp
var oblique = new MedicalObliqueSlice(mpr);
oblique.FollowLine(mpr.Axial);            // stands upright on the axial plane through the distance last touched
oblique.ViewCamera = chart3D.Scene.Camera; // key V on the oblique chart turns the plane to face the 3D view
obliqueChart.Configure(oblique.Configure);
chart3D.Configure(c => { mpr.Configure3D(c); oblique.Configure3D(c); }); // outline of the plane in 3D

var curved = new MedicalCurvedSlice(mpr.Axial); // follows the distance, angle or freehand line last touched
curvedChart.Configure(curved.Configure);
```

- **Oblique plane:** `SetPlane(center, normal)` places it, `Move(mm)` and the wheel move it along its normal, `AlignToView(camera)` turns it to face the eye of a 3D chart. Its picture is a square that holds the volume in any direction; the chart opens on the part with values.
- **Curved section:** across goes the length along the line, down goes the depth of the volume across the plane, through all of its slices, for example the ribs along the chest wall or a vessel. `Length` gives the length of the line.

## Sample data

`MedicalPhantoms.Head()` (256 × 256 × 192 at 0.75 mm) and `MedicalPhantoms.Thorax()` (384 × 288 × 200 at 1 mm) build synthetic CT volumes from smooth shapes with typical Hounsfield units, with edges that blend over about one voxel and a little noise, as in a real scan: scalp, skull, grey and folded white matter, ventricles and a lesion, and a chest with lungs, branching vessels, a nodule, heart, aorta, spine, ribs and sternum. They hold no data of any person and need no download.

Real data are downloaded only on request, into a folder the application chooses, and checked against a SHA-256 checksum:

| Dataset | Content | License and source |
|---|---|---|
| `MedicalSampleDataset.ThoraxCt` | Chest CT, 512 × 512 × 133, DICOM, 35 MB download | CC BY 3.0. Armato III, S. G. et al. (2015), Data From LIDC-IDRI, The Cancer Imaging Archive, [doi:10.7937/K9/TCIA.2015.LO9QL9SX](https://doi.org/10.7937/K9/TCIA.2015.LO9QL9SX). The [data usage policy](https://www.cancerimagingarchive.net/data-usage-policies-and-restrictions/) of the archive applies: cite the data and never try to identify a person. |
| `MedicalSampleDataset.HeadMri` | Head MRI T1, 160 × 192 × 192, NIfTI, 5.7 MB, face removed | CC0. Schonberg, T. et al. (2012), Balloon Analog Risk-taking Task, OpenNeuro ds000001, [doi:10.18112/openneuro.ds000001.v1.0.0](https://doi.org/10.18112/openneuro.ds000001.v1.0.0). |

```csharp
var dataset = MedicalSampleDataset.ThoraxCt;
// Show dataset.License, dataset.Citation and dataset.TermsUri to the user before the download.
var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp", "SampleData");
var volume = await dataset.LoadAsync(folder, new Progress<MedicalDownloadProgress>(p => status.Text = $"{p.Share:P0}"));
var mpr = new MedicalMprDisplay(volume);
```

A download that does not match its checksum is deleted and reported as `InvalidDataException`. `IsDownloaded(folder)` and `Read(folder)` reuse data that is already there. For the archive, which packs its files anew on each request, the checksum covers the names and bytes of the files rather than the archive itself. `NiftiImageReader.Read` reads NIfTI-1 images such as the head MRI; free-text header fields are never read. In the showroom, the pages "Medical CT and MRI", "Medical analysis" and "Medical oblique and curved" have a data list: the phantoms, or the real chest CT and head MRI, downloaded when chosen. Source and license show when the data are chosen; all measuring and analysis tools work on every choice.

## Large series

A CT series of 1024 × 1024 × 1000 values of 16 bits takes about 2.1 GB of memory. Series with a rescale slope and intercept stay at two bytes per value; the scale is applied when a value is read. On the graphics card a volume takes four bytes per value when the card filters 32-bit floats, so the same series needs 4.2 GB there.

Each volume gets a budget of 60 % of the graphics memory, or 2 GiB where the system does not report it (the memory is detected on Windows). A volume above its budget first keeps its resolution with 16-bit floats, about 11 bits per value. If it still does not fit, it shows every second, third … value along each axis. The 3D chart then says so in a line above the volume and shows a progress bar while the slices arrive. Slices, measurements, profiles and histograms always read the full volume on the CPU.

```csharp
var volume = new Volume3D(data, x, y, z) { GpuMemoryLimit = 3_000_000_000 };  // optional, bytes
volume.GpuFitChosen += fit => log.Information(fit.Message);                   // also volume.GpuFit
long gpuBytes = Volume3D.EstimateMemory(1024, 1024, 1000, 4);                  // before loading
```

On a laptop with an 8 GB card, the 1024 × 1024 × 1000 series loads from 1000 DICOM files in a few seconds. It reaches the card exactly, and turning in 3D, moving through the slices and windowing all run at the refresh rate of a 60 Hz screen.

## Package boundary

The medical package builds on `Tachyon.Charts.ThreeD`, which in turn builds on the platform-neutral chart core. The regular 2D and 3D packages do not reference the medical package, so applications that do not install it receive no medical-data API.
