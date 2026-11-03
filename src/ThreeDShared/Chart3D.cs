#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tachyon.Charts.Avalonia;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tachyon.Charts.Wpf;
#endif
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.ThreeD.Scene;

#if AVALONIA
namespace Tachyon.Charts.ThreeD.Avalonia;
#else
namespace Tachyon.Charts.ThreeD.Wpf;
#endif

/// <summary>
/// A 3D chart. It rests on a 2D chart, which brings the render thread, input, themes and export.
/// Without a graphics card it shows a hint and raises <see cref="GpuUnavailable"/>, and the application goes on.
/// </summary>
public sealed class Chart3D : Border
{
    private static readonly Color _background = Color.FromRgb(0x0E, 0x11, 0x17);
    private static readonly Color _foreground = Color.FromRgb(0xC4, 0xCD, 0xD9);

    private readonly TextBlock _hint = new()
    {
        Foreground = new SolidColorBrush(_foreground),
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(24),
    };

    /// <summary>Initializes a new instance of the <see cref="Chart3D"/> class with an empty box.</summary>
    public Chart3D()
    {
        Background = new SolidColorBrush(_background);
        Configure(_ => { });
#if AVALONIA
        AttachedToVisualTree += OnShown;
        DetachedFromVisualTree += OnHidden;
#else
        Loaded += OnShown;
        Unloaded += OnHidden;
#endif
    }

    /// <summary>Occurs on the UI thread when this chart cannot show 3D.</summary>
    public event EventHandler? GpuUnavailable;

    /// <summary>Gets a value indicating whether this chart can show 3D. It is true until the check has failed.</summary>
    public bool IsGpuAvailable { get; private set; } = true;

    /// <summary>Gets the 2D chart that carries the picture, for export and context menus.</summary>
    public Chart Surface { get; } = new();

    /// <summary>Gets the scene.</summary>
    public Scene3D Scene { get; private set; } = new();

    /// <summary>Gets or sets the title.</summary>
    public string? Title
    {
        get => Surface.Title;
        set => Surface.Title = value;
    }

    /// <summary>Gets or sets the theme.</summary>
    public Theme? ChartTheme
    {
#if AVALONIA
        get => Surface.ChartTheme;
        set => Surface.ChartTheme = value;
#else
        get => Surface.Theme;
        set => Surface.Theme = value;
#endif
    }

    /// <summary>Gets or sets how the depth is shown.</summary>
    public CameraProjection Projection
    {
        get => Scene.Camera.Projection;
        set => Scene.Camera.Projection = value;
    }

    /// <summary>Describes the chart.</summary>
    /// <param name="configure">Fills the builder.</param>
    /// <returns>This chart.</returns>
    public Chart3D Configure(Action<Chart3DBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new Chart3DBuilder();
        configure(builder);
        Scene = builder.Scene;
        Surface.Configure(builder.Apply);
        return this;
    }

    internal void Apply(Gpu3dStatus status)
    {
        IsGpuAvailable = status.IsAvailable;
        Child = status.IsAvailable ? Surface : _hint;
        if (!status.IsAvailable)
        {
            _hint.Text = Gpu3d.UnavailableText;
            GpuUnavailable?.Invoke(this, EventArgs.Empty);
        }
    }

    // The check creates a device, which takes too long for the UI thread.
    private async void OnShown(object? sender, EventArgs e)
    {
        ChartText.LanguageChanged += OnLanguageChanged;
        Apply(await Task.Run(() => Gpu3d.Status));
    }

    private void OnHidden(object? sender, EventArgs e) => ChartText.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged() => _hint.Text = IsGpuAvailable ? string.Empty : Gpu3d.UnavailableText;
}
