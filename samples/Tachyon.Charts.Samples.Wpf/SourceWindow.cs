using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Tachyon.Charts.Samples.Shared;

namespace Tachyon.Charts.Samples.Wpf;

/// <summary>Shows the code of a showroom page, a tab for each file, with buttons to copy and save it.</summary>
internal sealed class SourceWindow : Window
{
    private const double LineHeight = 18;

    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };

    public SourceWindow(string page, IReadOnlyList<SourceFile> files)
    {
        Title = SampleText.T("Source code") + " – " + page;
        Width = 1000;
        Height = 760;
        Background = TryFindResource("Shell") as Brush ?? new SolidColorBrush(Color.FromRgb(15, 19, 25));
        Foreground = TryFindResource("Ink") as Brush ?? Brushes.White;
        SourceInitialized += (_, _) => SampleStyle.DarkenTitleBar(this);
        foreach (var file in files)
        {
            _tabs.Items.Add(new TabItem { Header = Path.GetFileName(file.Name), ToolTip = file.Name, Tag = file, Content = CreateView(file) });
        }

        _tabs.SelectedIndex = 0;
        var copy = new Button { Content = SampleText.T("Copy"), Margin = new Thickness(0, 0, 6, 0) };
        copy.Click += (_, _) => Copy();
        var save = new Button { Content = SampleText.T("Save file"), Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) => Save();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 10) };
        bar.Children.Add(copy);
        bar.Children.Add(save);
        bar.Children.Add(_status);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_tabs);
        Content = root;
    }

    private SourceFile? Selected => (_tabs.SelectedItem as TabItem)?.Tag as SourceFile;

    // Each line ends in a break of its own, so that the lines keep their height.
    private static RichTextBox CreateView(SourceFile file)
    {
        var brushes = CodeTokens.DarkColors.ToDictionary(pair => pair.Key, pair => (Brush)Freeze(new SolidColorBrush(Color.FromRgb(pair.Value.R, pair.Value.G, pair.Value.B))));
        var paragraph = new Paragraph { LineHeight = LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0) };
        var text = file.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var token in CodeTokens.Split(text))
        {
            var parts = text.Substring(token.Start, token.Length).Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }

                if (parts[i].Length > 0)
                {
                    paragraph.Inlines.Add(new Run(parts[i]) { Foreground = brushes[token.Kind] });
                }
            }
        }

        var document = new FlowDocument(paragraph)
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 13,
            PageWidth = 4000,
            PagePadding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(11, 14, 19)),
        };
        var view = new RichTextBox(document)
        {
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = document.Background,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        return view;
    }

    private static Freezable Freeze(Freezable value)
    {
        value.Freeze();
        return value;
    }

    private void Copy()
    {
        if (Selected is { } file)
        {
            Clipboard.SetText(file.Text);
            _status.Text = SampleText.T("The code is on the clipboard");
        }
    }

    private void Save()
    {
        if (Selected is not { } file)
        {
            return;
        }

        var dialog = new SaveFileDialog { FileName = Path.GetFileName(file.Name), Filter = "C# (*.cs)|*.cs|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, file.Text);
            _status.Text = SampleText.T("Saved: ") + dialog.FileName;
        }
    }
}
