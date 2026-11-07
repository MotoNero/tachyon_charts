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

## Package boundary

The medical package builds on `Tachyon.Charts.ThreeD`, which in turn builds on the platform-neutral chart core. The regular 2D and 3D packages do not reference the medical package, so applications that do not install it receive no medical-data API.
