using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Tachyon.Charts.Samples.Avalonia;

/// <summary>The logo of the showroom and the configurator, embedded in the program that shows it.</summary>
internal static class AppLogo
{
    /// <summary>The icon of a window: the logo, from small to large.</summary>
    /// <returns>The icon.</returns>
    public static WindowIcon Icon()
    {
        using var stream = Open("Logo.ico");
        return new WindowIcon(stream);
    }

    /// <summary>The logo with its name, light on the dark sidebar.</summary>
    /// <returns>The logo.</returns>
    public static Bitmap Logo()
    {
        using var stream = Open("Logo.Sidebar.png");
        return new Bitmap(stream);
    }

    private static Stream Open(string name) =>
        typeof(AppLogo).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The picture {name} is missing.");
}
