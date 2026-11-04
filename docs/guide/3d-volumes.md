# Volumes

A volume is a block of values, for example from a CT or MRI scan or a simulation: `VolumeData` with 8 bit, 16 bit or floating point values, up to 512³ and more, as far as the graphics card holds them.

```csharp
var data = new VolumeData(values, width: 512, height: 512, depth: 512);
var volume = new Volume3D(data, new DoubleRange(-1, 1), new DoubleRange(-1, 1), new DoubleRange(-1, 1))
{
    Colormap = Colormaps.Inferno,
};
chart.Configure(c => c.XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddVolume(volume));
```

## Slices

`ShowsSlices` shows three slices, one across each axis; drag their outlines to move them, or set `SliceX.Offset` and the others from code. Values below the share `SliceThreshold` of the value range stay clear.

## Looking through

```csharp
volume.Transfer = new TransferFunction3D(
[
    new TransferPoint3D(0.0, new Rgba8(0, 0, 0, 255), 0),
    new TransferPoint3D(0.4, new Rgba8(200, 80, 40, 255), 0.05),
    new TransferPoint3D(1.0, new Rgba8(255, 240, 200, 255), 0.6),
]);
```

The transfer function gives each value a color and an opacity; the chart shows it at the top right, and dragging its points changes it live.

## Maximum projection and surfaces of equal value

`Mode = VolumeMode.Maximum` shows the highest value along each ray, as in angiography. `Mode = VolumeMode.Surface` shows the surface where the values cross `IsoValue`, lit by `Material`; the bar at the top right moves it live.

## Files

`Tachyon.Charts.ThreeD.Files` reads NIfTI-1 (also packed with gzip) and raw volumes:

```csharp
var file = VolumeFile.Read("head.nii.gz");
var volume = file.ToVolume();
chart3D.Configure(c => file.AddToScale(c, volume));   // axes in millimetres, from the spacing in the file
```

`VolumeFile.ReadRaw(stream, new RawVolumeLayout(512, 512, 300, VolumeValueType.UInt16) { Offset = 1024 })` reads raw data. DICOM is not supported. `VolumeData.EstimateMemory` tells beforehand what a volume needs.

In the showroom: *Volume slices 512³*, *Through a volume 512³*, *Objects inside a volume*, *Maximum projection 512³*, *Surface of equal value 512³*.
