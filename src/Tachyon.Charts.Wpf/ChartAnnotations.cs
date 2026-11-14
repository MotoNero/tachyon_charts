using System.Windows;
using System.Windows.Media;
using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Rendering;
using CoreBox = Tachyon.Charts.Core.Annotations.BoxAnnotation;
using CoreHorizontal = Tachyon.Charts.Core.Annotations.HorizontalLineAnnotation;
using CoreLine = Tachyon.Charts.Core.Annotations.LineAnnotation;
using CoreText = Tachyon.Charts.Core.Annotations.TextAnnotation;
using CoreVertical = Tachyon.Charts.Core.Annotations.VerticalLineAnnotation;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// An annotation of a <see cref="Chart"/>. Positions are values of the axes and bind both ways, so
/// dragging reaches the view model. Further kinds of annotations exist in code, see
/// <see cref="Tachyon.Charts.Core.Annotations"/>.
/// </summary>
public abstract class ChartAnnotationElement : ChartElement
{
    /// <summary>Identifies the <see cref="IsEditable"/> property.</summary>
    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(ChartAnnotationElement), new PropertyMetadata(false, OnAppearanceChanged));

    /// <summary>Identifies the <see cref="Color"/> property.</summary>
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(ChartAnnotationElement), new PropertyMetadata(Color.FromRgb(240, 190, 90), OnAppearanceChanged));

    /// <summary>Identifies the <see cref="Appearance"/> property.</summary>
    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
        nameof(Appearance), typeof(AnnotationAppearance), typeof(ChartAnnotationElement), new PropertyMetadata(null, OnAppearanceChanged));

    /// <summary>Identifies the <see cref="XAxisId"/> property.</summary>
    public static readonly DependencyProperty XAxisIdProperty = DependencyProperty.Register(
        nameof(XAxisId), typeof(string), typeof(ChartAnnotationElement), new PropertyMetadata(null, OnStructureChanged));

    /// <summary>Identifies the <see cref="YAxisId"/> property.</summary>
    public static readonly DependencyProperty YAxisIdProperty = DependencyProperty.Register(
        nameof(YAxisId), typeof(string), typeof(ChartAnnotationElement), new PropertyMetadata(null, OnStructureChanged));

    private bool _isPublishing;

    /// <summary>Gets or sets a value indicating whether an edit modifier may change it.</summary>
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>Gets or sets the color.</summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Gets or sets how the label and the lines are drawn: place, size, colors, plate, border, turn and sign of the
    /// label, dash and width of the lines; <see langword="null"/> draws them as usual. Set a new object to change it.
    /// </summary>
    public AnnotationAppearance? Appearance
    {
        get => (AnnotationAppearance?)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    /// <summary>Gets or sets the name of the X axis, or <see langword="null"/> for the first one.</summary>
    public string? XAxisId
    {
        get => (string?)GetValue(XAxisIdProperty);
        set => SetValue(XAxisIdProperty, value);
    }

    /// <summary>Gets or sets the name of the Y axis, or <see langword="null"/> for the first one.</summary>
    public string? YAxisId
    {
        get => (string?)GetValue(YAxisIdProperty);
        set => SetValue(YAxisIdProperty, value);
    }

    /// <summary>Gets the annotation of the engine.</summary>
    internal Annotation? Core { get; private set; }

    /// <summary>Creates the annotation of the engine.</summary>
    /// <param name="x">The X axis.</param>
    /// <param name="y">The Y axis.</param>
    /// <returns>The annotation.</returns>
    internal Annotation Build(AxisBase x, AxisBase y)
    {
        var core = Create(x, y);
        ApplyTo(core, ReadState());
        core.IsEditable = IsEditable;
        core.Color = ToEngine(Color);
        core.Appearance = Appearance ?? new AnnotationAppearance();
        core.Edited += OnEdited;
        Core = core;
        return core;
    }

    /// <summary>Takes over the positions of the engine without sending them back.</summary>
    /// <param name="state">The positions.</param>
    internal void Publish(double[] state)
    {
        _isPublishing = true;
        try
        {
            WriteState(state);
        }
        finally
        {
            _isPublishing = false;
        }
    }

    /// <summary>Creates the annotation for its axes.</summary>
    private protected abstract Annotation Create(AxisBase x, AxisBase y);

    /// <summary>Reads the positions of this element on the UI thread.</summary>
    private protected abstract double[] ReadState();

    /// <summary>Writes positions to this element.</summary>
    private protected abstract void WriteState(double[] state);

    /// <summary>Reads the positions of the engine annotation.</summary>
    private protected abstract double[] ReadCore(Annotation core);

    /// <summary>Applies positions to the engine annotation.</summary>
    private protected abstract void ApplyTo(Annotation core, double[] state);

    /// <summary>Forwards a changed position.</summary>
    /// <param name="sender">The element.</param>
    /// <param name="args">The change.</param>
    private protected static void OnPositionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (ChartAnnotationElement)sender;
        if (element._isPublishing || element.Host is not { } host || element.Core is not { } core)
        {
            return;
        }

        var state = element.ReadState();
        host.Post(() => element.ApplyTo(core, state));
    }

    private static void OnAppearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (ChartAnnotationElement)sender;
        if (element.Host is not { } host || element.Core is not { } core)
        {
            return;
        }

        var (isEditable, color, appearance) = (element.IsEditable, ToEngine(element.Color), element.Appearance ?? new AnnotationAppearance());
        host.Post(() =>
        {
            core.IsEditable = isEditable;
            core.Color = color;
            core.Appearance = appearance;
            core.Move(0, 0);
        });
    }

    private static void OnStructureChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ChartAnnotationElement)sender).Host?.OnAxesChanged();

    private static Rgba8 ToEngine(Color color) => new(color.R, color.G, color.B, color.A);

    // On the render thread. The positions travel to the UI thread as a copy.
    private void OnEdited(Annotation core)
    {
        var state = ReadCore(core);
        Dispatcher.BeginInvoke(() => Publish(state));
    }
}

/// <summary>A line across the plot at a Y value.</summary>
public sealed class HorizontalLineAnnotation : ChartAnnotationElement
{
    /// <summary>Identifies the <see cref="Y"/> property.</summary>
    public static readonly DependencyProperty YProperty = DependencyProperty.Register(
        nameof(Y), typeof(double), typeof(HorizontalLineAnnotation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(HorizontalLineAnnotation), new PropertyMetadata(null));

    /// <summary>Gets or sets the value.</summary>
    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    /// <summary>Gets or sets the label.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private protected override Annotation Create(AxisBase x, AxisBase y) => new CoreHorizontal(y) { Label = Label };

    private protected override double[] ReadState() => [Y];

    private protected override void WriteState(double[] state) => SetCurrentValue(YProperty, state[0]);

    private protected override double[] ReadCore(Annotation core) => [((CoreHorizontal)core).Y];

    private protected override void ApplyTo(Annotation core, double[] state) => ((CoreHorizontal)core).Y = state[0];
}

/// <summary>A line across the plot at an X value.</summary>
public sealed class VerticalLineAnnotation : ChartAnnotationElement
{
    /// <summary>Identifies the <see cref="X"/> property.</summary>
    public static readonly DependencyProperty XProperty = DependencyProperty.Register(
        nameof(X), typeof(double), typeof(VerticalLineAnnotation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(VerticalLineAnnotation), new PropertyMetadata(null));

    /// <summary>Gets or sets the value.</summary>
    public double X
    {
        get => (double)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    /// <summary>Gets or sets the label.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private protected override Annotation Create(AxisBase x, AxisBase y) => new CoreVertical(x) { Label = Label };

    private protected override double[] ReadState() => [X];

    private protected override void WriteState(double[] state) => SetCurrentValue(XProperty, state[0]);

    private protected override double[] ReadCore(Annotation core) => [((CoreVertical)core).X];

    private protected override void ApplyTo(Annotation core, double[] state) => ((CoreVertical)core).X = state[0];
}

/// <summary>A rectangle between two corners.</summary>
public sealed class BoxAnnotation : ChartAnnotationElement
{
    /// <summary>Identifies the <see cref="X1"/> property.</summary>
    public static readonly DependencyProperty X1Property = Register(nameof(X1));

    /// <summary>Identifies the <see cref="Y1"/> property.</summary>
    public static readonly DependencyProperty Y1Property = Register(nameof(Y1));

    /// <summary>Identifies the <see cref="X2"/> property.</summary>
    public static readonly DependencyProperty X2Property = Register(nameof(X2));

    /// <summary>Identifies the <see cref="Y2"/> property.</summary>
    public static readonly DependencyProperty Y2Property = Register(nameof(Y2));

    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(BoxAnnotation), new PropertyMetadata(null));

    /// <summary>Gets or sets a label at the upper edge, or <see langword="null"/> for none.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Gets or sets the X value of the first corner.</summary>
    public double X1
    {
        get => (double)GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    /// <summary>Gets or sets the Y value of the first corner.</summary>
    public double Y1
    {
        get => (double)GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    /// <summary>Gets or sets the X value of the second corner.</summary>
    public double X2
    {
        get => (double)GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    /// <summary>Gets or sets the Y value of the second corner.</summary>
    public double Y2
    {
        get => (double)GetValue(Y2Property);
        set => SetValue(Y2Property, value);
    }

    private protected override Annotation Create(AxisBase x, AxisBase y) => new CoreBox(x, y) { Label = Label };

    private protected override double[] ReadState() => [X1, Y1, X2, Y2];

    private protected override void WriteState(double[] state)
    {
        SetCurrentValue(X1Property, state[0]);
        SetCurrentValue(Y1Property, state[1]);
        SetCurrentValue(X2Property, state[2]);
        SetCurrentValue(Y2Property, state[3]);
    }

    private protected override double[] ReadCore(Annotation core)
    {
        var box = (CoreBox)core;
        return [box.First.X, box.First.Y, box.Second.X, box.Second.Y];
    }

    private protected override void ApplyTo(Annotation core, double[] state) => ((CoreBox)core).SetCorners(state[0], state[1], state[2], state[3]);

    private static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(BoxAnnotation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));
}

/// <summary>A straight line between two points at any angle; a drag moves it, a drag at an end moves that end.</summary>
public sealed class LineAnnotation : ChartAnnotationElement
{
    /// <summary>Identifies the <see cref="X1"/> property.</summary>
    public static readonly DependencyProperty X1Property = Register(nameof(X1));

    /// <summary>Identifies the <see cref="Y1"/> property.</summary>
    public static readonly DependencyProperty Y1Property = Register(nameof(Y1));

    /// <summary>Identifies the <see cref="X2"/> property.</summary>
    public static readonly DependencyProperty X2Property = Register(nameof(X2));

    /// <summary>Identifies the <see cref="Y2"/> property.</summary>
    public static readonly DependencyProperty Y2Property = Register(nameof(Y2));

    /// <summary>Gets or sets the X value of the first end.</summary>
    public double X1
    {
        get => (double)GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    /// <summary>Gets or sets the Y value of the first end.</summary>
    public double Y1
    {
        get => (double)GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    /// <summary>Gets or sets the X value of the second end.</summary>
    public double X2
    {
        get => (double)GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    /// <summary>Gets or sets the Y value of the second end.</summary>
    public double Y2
    {
        get => (double)GetValue(Y2Property);
        set => SetValue(Y2Property, value);
    }

    private protected override Annotation Create(AxisBase x, AxisBase y) => new CoreLine(x, y);

    private protected override double[] ReadState() => [X1, Y1, X2, Y2];

    private protected override void WriteState(double[] state)
    {
        SetCurrentValue(X1Property, state[0]);
        SetCurrentValue(Y1Property, state[1]);
        SetCurrentValue(X2Property, state[2]);
        SetCurrentValue(Y2Property, state[3]);
    }

    private protected override double[] ReadCore(Annotation core)
    {
        var line = (CoreLine)core;
        return [line.Start.X, line.Start.Y, line.End.X, line.End.Y];
    }

    private protected override void ApplyTo(Annotation core, double[] state) => ((CoreLine)core).SetEnds(state[0], state[1], state[2], state[3]);

    private static DependencyProperty Register(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(LineAnnotation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));
}

/// <summary>Text at a place.</summary>
public sealed class TextAnnotation : ChartAnnotationElement
{
    /// <summary>Identifies the <see cref="X"/> property.</summary>
    public static readonly DependencyProperty XProperty = DependencyProperty.Register(
        nameof(X), typeof(double), typeof(TextAnnotation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    /// <summary>Identifies the <see cref="Y"/> property.</summary>
    public static readonly DependencyProperty YProperty = DependencyProperty.Register(
        nameof(Y), typeof(double), typeof(TextAnnotation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TextAnnotation), new PropertyMetadata(string.Empty));

    /// <summary>Gets or sets the X value.</summary>
    public double X
    {
        get => (double)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    /// <summary>Gets or sets the Y value.</summary>
    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    /// <summary>Gets or sets the text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private protected override Annotation Create(AxisBase x, AxisBase y) => new CoreText(x, y) { Text = Text };

    private protected override double[] ReadState() => [X, Y];

    private protected override void WriteState(double[] state)
    {
        SetCurrentValue(XProperty, state[0]);
        SetCurrentValue(YProperty, state[1]);
    }

    private protected override double[] ReadCore(Annotation core)
    {
        var text = (CoreText)core;
        return [text.Position.X, text.Position.Y];
    }

    private protected override void ApplyTo(Annotation core, double[] state) => ((CoreText)core).MoveTo(state[0], state[1]);
}

/// <summary>
/// Moves annotations marked as editable, changes their size at the bottom right corner, and removes the
/// last touched one with Delete.
/// </summary>
public sealed class AnnotationEditModifier : ChartModifierElement
{
    internal override Core.Input.ChartModifier Build() => new Core.Input.AnnotationEditModifier();
}
