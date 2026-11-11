using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator.Avalonia;

/// <summary>
/// A swatch beside a color field. A click opens a picker: the palette, sliders for red, green, blue and opacity, and the
/// usual color. The picker writes the hex text into the field, so that typing works as before and each choice reaches
/// the preview and the code at once.
/// </summary>
internal sealed class ColorPickerButton : Button
{
    private static readonly string[] _channels = ["Red", "Green", "Blue", "Opacity"];

    private readonly TextBox _text;
    private readonly Slider[] _sliders = new Slider[4];
    private bool _isShowing;

    /// <summary>Initializes a new instance of the <see cref="ColorPickerButton"/> class.</summary>
    /// <param name="text">The hex field it writes into.</param>
    public ColorPickerButton(TextBox text)
    {
        _text = text;
        (Width, Margin, Padding) = (28, new Thickness(0, 0, 4, 0), new Thickness(0));
        ToolTip.SetTip(this, "Choose a color");
        Picker = new Flyout { Content = CreatePicker(), Placement = PlacementMode.Bottom };
        Flyout = Picker;
        Picker.Opening += (_, _) => ShowCurrent();
        _text.TextChanged += (_, _) =>
        {
            ShowSwatch();
            ShowCurrent();
        };
        ShowSwatch();
        ShowCurrent();
    }

    /// <summary>Gets the picker that opens at a click.</summary>
    internal Flyout Picker { get; }

    /// <summary>Sets a color, or the usual color with <see langword="null"/>.</summary>
    /// <param name="color">The color.</param>
    internal void Choose(Rgba8? color)
    {
        _text.Text = color is { } chosen ? SpecColor.Format(chosen) : string.Empty;

        // Not every framework reports the change of the text at once; the picker follows its own choice directly.
        ShowSwatch();
        ShowCurrent();
    }

    /// <summary>Moves one slider: 0 red, 1 green, 2 blue, 3 opacity.</summary>
    /// <param name="channel">The slider.</param>
    /// <param name="value">The value from 0 to 255.</param>
    internal void Slide(int channel, double value) => _sliders[channel].Value = value;

    private static SolidColorBrush Brush(Rgba8 color) => new(Color.FromArgb(color.A, color.R, color.G, color.B));

    private Rgba8 Current => SpecColor.TryParse(_text.Text, out var color) ? color : new Rgba8(80, 170, 240, 255);

    private StackPanel CreatePicker()
    {
        var panel = new StackPanel { Width = 240 };
        var palette = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var color in SpecColor.Palette)
        {
            var swatch = new Button { Width = 22, Height = 22, Margin = new Thickness(2), Padding = new Thickness(0), Background = Brush(color) };
            ToolTip.SetTip(swatch, SpecColor.Format(color));

            // A color of the palette keeps the opacity chosen, so that a band stays see-through.
            swatch.Click += (_, _) =>
            {
                Choose(new Rgba8(color.R, color.G, color.B, (byte)Math.Round(_sliders[3].Value)));
                ShowCurrent();
            };
            palette.Children.Add(swatch);
        }

        panel.Children.Add(palette);
        for (var i = 0; i < _sliders.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock { Text = _channels[i], Width = 56, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
            DockPanel.SetDock(label, Dock.Left);
            var slider = new Slider { Minimum = 0, Maximum = 255, SmallChange = 1, LargeChange = 16, IsSnapToTickEnabled = true, TickFrequency = 1 };
            slider.ValueChanged += (_, _) =>
            {
                if (!_isShowing)
                {
                    Choose(new Rgba8((byte)_sliders[0].Value, (byte)_sliders[1].Value, (byte)_sliders[2].Value, (byte)_sliders[3].Value));
                }
            };
            _sliders[i] = slider;
            row.Children.Add(label);
            row.Children.Add(slider);
            panel.Children.Add(row);
        }

        var usual = new Button { Content = "Usual color", Margin = new Thickness(0, 8, 0, 0) };
        ToolTip.SetTip(usual, "Leaves the color empty: the chart takes the color of the theme.");
        usual.Click += (_, _) =>
        {
            Choose(null);
            Picker.Hide();
        };
        panel.Children.Add(usual);
        return panel;
    }

    // The sliders follow the field, also while the picker is open, without writing it back.
    private void ShowCurrent()
    {
        if (_isShowing || _sliders[0] is null)
        {
            return;
        }

        _isShowing = true;
        var color = Current;
        (_sliders[0].Value, _sliders[1].Value, _sliders[2].Value, _sliders[3].Value) = (color.R, color.G, color.B, color.A);
        _isShowing = false;
    }

    // The swatch shows the color; the usual color shows as a dash.
    private void ShowSwatch()
    {
        if (SpecColor.TryParse(_text.Text, out var color))
        {
            (Background, Content) = (Brush(color), null);
        }
        else
        {
            Background = Brushes.Transparent;
            Content = "–";
        }
    }
}
