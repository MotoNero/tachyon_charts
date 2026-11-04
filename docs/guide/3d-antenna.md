# Antenna patterns

An antenna pattern shows the gain in each direction as a body: far out where the antenna sends strongly, close to the centre where it is weak.

```csharp
// Gains in dB, one row per elevation, one column per azimuth, both in degrees.
var pattern = new AntennaPattern3D(azimuths, elevations, gains)
{
    DynamicRange = 40,                // dB below the peak where the floor lies; weaker directions shrink to the centre
    Colormap = Colormaps.Viridis,
};
chart.Configure(c => c.XAxis(-1, 1).YAxis(-1, 1).ZAxis(-1, 1).AddAntenna(pattern));
```

Under the pointer the chart shows azimuth, elevation and gain. `MainLobe` gives the direction of the main lobe with its width at half power. `AzimuthCut(elevation)` and `ElevationCut(azimuth)` give the cuts as angles and values, ready for a 2D polar chart.

In the showroom: *Antenna pattern*, and *Antenna cut* among the chart types.
