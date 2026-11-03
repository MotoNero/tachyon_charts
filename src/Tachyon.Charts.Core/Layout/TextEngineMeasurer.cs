using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Layout;

/// <summary>
/// Measures labels with a text engine at a fixed size.
/// </summary>
public sealed class TextEngineMeasurer : ITextMeasurer
{
    private readonly ITextEngine _engine;
    private float _size;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextEngineMeasurer"/> class.
    /// </summary>
    /// <param name="engine">The text engine.</param>
    /// <param name="size">The em size in device-independent units.</param>
    public TextEngineMeasurer(ITextEngine engine, float size)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Size = size;
    }

    /// <summary>Gets or sets the em size in device-independent units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public float Size
    {
        get => _size;
        set
        {
            if (!float.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The text size must be positive and finite.");
            }

            _size = value;
        }
    }

    /// <inheritdoc />
    public double LineHeight => _engine.GetLineHeight(_size);

    /// <inheritdoc />
    public double MeasureWidth(string text) => _engine.MeasureWidth(text, _size);
}
