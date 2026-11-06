using Tachyon.Charts.Medical;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Creates a synthetic CT-like image without personal or external data.</summary>
public static class MedicalPhantom
{
    /// <summary>Creates an axial image.</summary>
    public static MedicalImageDisplay Axial() => Create(new PatientOrientation(new Point3D(1, 0, 0), new Point3D(0, 1, 0)));

    /// <summary>Creates a coronal image.</summary>
    public static MedicalImageDisplay Coronal() => Create(new PatientOrientation(new Point3D(1, 0, 0), new Point3D(0, 0, -1)));

    /// <summary>Creates a sagittal image.</summary>
    public static MedicalImageDisplay Sagittal() => Create(new PatientOrientation(new Point3D(0, 1, 0), new Point3D(0, 0, -1)));

    private static MedicalImageDisplay Create(PatientOrientation orientation)
    {
        const int width = 320;
        const int height = 256;
        var values = new float[width * height];
        for (var row = 0; row < height; row++)
        {
            var y = (row - (height / 2.0)) / (height * 0.42);
            for (var column = 0; column < width; column++)
            {
                var x = (column - (width / 2.0)) / (width * 0.34);
                var radius = (x * x) + (y * y);
                var value = -1000f;
                if (radius < 1)
                {
                    value = radius > 0.82 ? 1050 : 38;
                    if (Ellipse(x + 0.18, y, 0.13, 0.28) || Ellipse(x - 0.18, y, 0.13, 0.28))
                    {
                        value = 8;
                    }
                    else if (Ellipse(x + 0.48, y + 0.15, 0.18, 0.22))
                    {
                        value = 65;
                    }
                    else
                    {
                        value += (float)(12 * Math.Sin((x * 8) + (y * 5)));
                    }
                }

                values[(row * width) + column] = value;
            }
        }

        var display = new MedicalImageDisplay(values, width, height, new ImageSpacing(0.7, 0.7), orientation, "HU");
        display.ApplyPreset(CtWindowPresets.Brain);
        return display;
    }

    private static bool Ellipse(double x, double y, double horizontal, double vertical) =>
        ((x * x) / (horizontal * horizontal)) + ((y * y) / (vertical * vertical)) < 1;
}
