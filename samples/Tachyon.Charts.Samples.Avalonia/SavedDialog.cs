using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tachyon.Charts.Samples.Shared;

namespace Tachyon.Charts.Samples.Avalonia;

/// <summary>Tells that a file is saved, with a button that shows it in its folder.</summary>
internal sealed class SavedDialog : Window
{
    private SavedDialog(string path)
    {
        Title = SampleText.T("Saved");
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        Background = new SolidColorBrush(Color.FromRgb(15, 19, 25));

        var show = new Button { Content = SampleText.T("Show"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        show.Click += (_, _) =>
        {
            FileLocation.Show(path);
            Close();
        };
        var ok = new Button { Content = SampleText.T("OK"), MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(show);
        buttons.Children.Add(ok);

        var content = new StackPanel { Margin = new Thickness(20), MaxWidth = 640 };
        content.Children.Add(new TextBlock { Text = SampleText.T("The file is saved:"), FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = path, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        content.Children.Add(buttons);
        Content = content;
    }

    /// <summary>Shows the dialog and waits until it is closed.</summary>
    /// <param name="owner">The window it belongs to.</param>
    /// <param name="path">The saved file.</param>
    /// <returns>When the dialog is closed.</returns>
    public static Task Show(Window owner, string path) => new SavedDialog(path).ShowDialog(owner);
}
