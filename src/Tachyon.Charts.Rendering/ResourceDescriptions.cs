namespace Tachyon.Charts.Rendering;

/// <summary>
/// The ways a buffer can be used.
/// </summary>
[Flags]
public enum BufferUsage // NOSONAR: the name is public API
{
    /// <summary>
    /// The buffer is not usable.
    /// </summary>
    None = 0,

    /// <summary>
    /// The buffer provides vertex data, for example points for <see cref="ICommandList.DrawPolyline(IGpuBuffer, int, int, in PolylineStyle)"/>.
    /// </summary>
    Vertex = 1,

    /// <summary>
    /// The buffer provides uniform data.
    /// </summary>
    Uniform = 2,

    /// <summary>
    /// The buffer provides storage data, for example for compute shaders.
    /// </summary>
    Storage = 4,
}

/// <summary>
/// The pixel format of a texture.
/// </summary>
public enum TextureFormat
{
    /// <summary>
    /// Four 8-bit normalized channels, for images.
    /// </summary>
    Rgba8Unorm,

    /// <summary>
    /// One 8-bit normalized channel, for glyph atlases.
    /// </summary>
    R8Unorm,

    /// <summary>
    /// One 32-bit float channel, for heatmap values.
    /// </summary>
    R32Float,
}

/// <summary>
/// Describes a buffer to create.
/// </summary>
public sealed class BufferDesc
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BufferDesc"/> class.
    /// </summary>
    /// <param name="sizeInBytes">The buffer size in bytes.</param>
    /// <param name="usage">The intended usage.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive, or the usage is empty or unknown.</exception>
    public BufferDesc(long sizeInBytes, BufferUsage usage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        if (usage == BufferUsage.None || (usage & ~(BufferUsage.Vertex | BufferUsage.Uniform | BufferUsage.Storage)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usage), usage, "The usage must be a combination of known flags.");
        }

        SizeInBytes = sizeInBytes;
        Usage = usage;
    }

    /// <summary>
    /// Gets the buffer size in bytes.
    /// </summary>
    public long SizeInBytes { get; }

    /// <summary>
    /// Gets the intended usage.
    /// </summary>
    public BufferUsage Usage { get; }
}

/// <summary>
/// Describes a texture to create.
/// </summary>
public sealed class TextureDesc
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextureDesc"/> class.
    /// </summary>
    /// <param name="size">The texture size.</param>
    /// <param name="format">The pixel format.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive, or the format is unknown.</exception>
    public TextureDesc(PixelSize size, TextureFormat format)
    {
        RenderGuard.PositiveSize(size, int.MaxValue);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown texture format.");
        }

        Size = size;
        Format = format;
    }

    /// <summary>
    /// Gets the texture size.
    /// </summary>
    public PixelSize Size { get; }

    /// <summary>
    /// Gets the pixel format.
    /// </summary>
    public TextureFormat Format { get; }

    /// <summary>
    /// Gets the size of the complete pixel data in bytes.
    /// </summary>
    public long SizeInBytes => Size.PixelCount * (Format == TextureFormat.R8Unorm ? 1 : 4);
}

/// <summary>
/// The native window system a surface belongs to.
/// </summary>
public enum NativeSurfaceKind
{
    /// <summary>
    /// A Win32 <c>HWND</c>.
    /// </summary>
    Win32Window,

    /// <summary>
    /// A Metal <c>CAMetalLayer</c>.
    /// </summary>
    MetalLayer,

    /// <summary>
    /// An Xlib window with its display.
    /// </summary>
    XlibWindow,

    /// <summary>
    /// A Wayland surface with its display.
    /// </summary>
    WaylandSurface,
}

/// <summary>
/// A window-system handle to render into, provided by a UI adapter.
/// </summary>
/// <param name="Kind">The window system.</param>
/// <param name="Window">The window, layer or surface handle.</param>
/// <param name="Display">The display handle for Xlib and Wayland, otherwise zero.</param>
public readonly record struct NativeSurfaceHandle(NativeSurfaceKind Kind, nint Window, nint Display = 0);

/// <summary>
/// What a render device supports.
/// </summary>
public sealed record DeviceCapabilities
{
    /// <summary>
    /// Gets the backend name for diagnostics.
    /// </summary>
    public required string BackendName { get; init; }

    /// <summary>
    /// Gets a value indicating whether compute shaders are available.
    /// </summary>
    public required bool SupportsCompute { get; init; }

    /// <summary>
    /// Gets the largest allowed buffer size in bytes.
    /// </summary>
    public required long MaxBufferSize { get; init; }

    /// <summary>
    /// Gets the largest allowed width or height of a texture or render target.
    /// </summary>
    public required int MaxTextureDimension { get; init; }

    /// <summary>
    /// Gets the highest supported MSAA sample count, 1 means no MSAA.
    /// </summary>
    public required int MaxSampleCount { get; init; }
}
