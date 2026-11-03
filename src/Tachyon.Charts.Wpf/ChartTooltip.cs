using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// What a tooltip template binds to.
/// </summary>
public sealed class ChartTooltipContent : INotifyPropertyChanged
{
    private TooltipData _data;
    private Brush _background = Brushes.Black;
    private Brush _foreground = Brushes.White;
    private Brush _accent = Brushes.Gray;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the name of the series.</summary>
    public string? SeriesName => _data.SeriesName;

    /// <summary>Gets the formatted X value.</summary>
    public string? XText => _data.XText;

    /// <summary>Gets the formatted Y value.</summary>
    public string? YText => _data.YText;

    /// <summary>Gets the X value in units of the axis.</summary>
    public double X => _data.X;

    /// <summary>Gets the Y value.</summary>
    public double Y => _data.Y;

    /// <summary>Gets the index of the point.</summary>
    public long Index => _data.Index;

    /// <summary>Gets the background of the theme.</summary>
    public Brush Background
    {
        get => _background;
        private set => Set(ref _background, value);
    }

    /// <summary>Gets the text color of the theme.</summary>
    public Brush Foreground
    {
        get => _foreground;
        private set => Set(ref _foreground, value);
    }

    /// <summary>Gets the color of the series.</summary>
    public Brush Accent
    {
        get => _accent;
        private set => Set(ref _accent, value);
    }

    internal void Update(in TooltipData data, Theme? theme)
    {
        _data = data;
        Background = ToBrush(theme?.Surface?.Background?.Value ?? new Rgba8(24, 28, 36, 255));
        Foreground = ToBrush(theme?.Axis?.Label?.Value ?? new Rgba8(200, 208, 220, 255));
        Accent = ToBrush(data.Color);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static SolidColorBrush ToBrush(Rgba8 color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private void Set(ref Brush field, Brush value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Shows the nearest point in a popup, which lies above the chart despite its own window.
/// </summary>
public sealed class TooltipModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="ItemTemplate"/> property.</summary>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(TooltipModifier), new PropertyMetadata(null, OnTemplateChanged));

    private const double CursorOffset = 16;

    private readonly ChartTooltipContent _content = new();
    private readonly ContentPresenter _presenter = new();
    private readonly Popup _popup;

    /// <summary>Initializes a new instance of the <see cref="TooltipModifier"/> class.</summary>
    public TooltipModifier()
    {
        _presenter.Content = _content;
        _presenter.ContentTemplate = CreateDefaultTemplate();
        _popup = new Popup
        {
            Child = _presenter,
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            StaysOpen = true,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Gets or sets the template of the content, bound to a <see cref="ChartTooltipContent"/>, or
    /// <see langword="null"/> for the default one in the colors of the theme.
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    internal bool IsOpen => _popup.IsOpen;

    internal ChartTooltipContent Content => _content;

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.TooltipModifier();
        modifier.Changed += OnChanged;
        return modifier;
    }

    private static void OnTemplateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var tooltip = (TooltipModifier)sender;
        tooltip._presenter.ContentTemplate = (DataTemplate?)args.NewValue ?? CreateDefaultTemplate();
    }

    private static DataTemplate CreateDefaultTemplate()
    {
        static FrameworkElementFactory Line(string path, double size)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(path));
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ChartTooltipContent.Foreground)));
            text.SetValue(TextBlock.FontSizeProperty, size);
            return text;
        }

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.AppendChild(Line(nameof(ChartTooltipContent.SeriesName), 12));
        stack.AppendChild(Line(nameof(ChartTooltipContent.XText), 11));
        stack.AppendChild(Line(nameof(ChartTooltipContent.YText), 11));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(ChartTooltipContent.Background)));
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(ChartTooltipContent.Accent)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.AppendChild(stack);

        var template = new DataTemplate(typeof(ChartTooltipContent)) { VisualTree = border };
        template.Seal();
        return template;
    }

    private void OnChanged(TooltipData? data)
    {
        if (data is not { } tooltip)
        {
            _popup.IsOpen = false;
            return;
        }

        var chart = Parent as Chart;
        _content.Update(tooltip, chart?.Theme);
        _popup.PlacementTarget = chart;
        _popup.HorizontalOffset = tooltip.Position.X + CursorOffset;
        _popup.VerticalOffset = tooltip.Position.Y + CursorOffset;
        _popup.IsOpen = true;
    }
}
