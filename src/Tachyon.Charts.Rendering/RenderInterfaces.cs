using System.Numerics;
using System.Runtime.InteropServices;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// Creates GPU resources and starts frames.
/// </summary>
/// <remarks>
/// A device and its resources are not thread-safe and belong to the render thread. Disposing is
/// idempotent. Any other member of a disposed object throws <see cref="ObjectDisposedException"/>,
/// and disposing a device makes all its resources unusable. The resources must still be disposed.
/// </remarks>
public interface IRenderDevice : IDisposable
{
    /// <summary>
    /// Gets what this device supports.
    /// </summary>
    DeviceCapabilities Capabilities { get; }

    /// <summary>
    /// Gets a value indicating whether the device stopped working, for example after a driver reset.
    /// A lost device stays lost: its owner creates a new one and builds all resources again. Safe from any thread.
    /// </summary>
    bool IsLost { get; }

    /// <summary>
    /// Makes the device behave as if it were lost, as a test hook.
    /// </summary>
    void SimulateLoss();

    /// <summary>
    /// Gets the work done by compute shaders, or <see langword="null"/> on a device without them.
    /// Callers then do the work on the CPU.
    /// </summary>
    IComputeOperations? Compute { get; }

    /// <summary>
    /// Creates a buffer.
    /// </summary>
    /// <param name="desc">The buffer description.</param>
    /// <returns>The new buffer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size exceeds <see cref="DeviceCapabilities.MaxBufferSize"/>.</exception>
    IGpuBuffer CreateBuffer(BufferDesc desc);

    /// <summary>
    /// Creates a texture.
    /// </summary>
    /// <param name="desc">The texture description.</param>
    /// <returns>The new texture.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size exceeds <see cref="DeviceCapabilities.MaxTextureDimension"/>.</exception>
    IGpuTexture CreateTexture(TextureDesc desc);

    /// <summary>
    /// Creates a render target that is not attached to a window and can be read back.
    /// </summary>
    /// <param name="size">The size in physical pixels.</param>
    /// <param name="dpiScale">Physical pixels per device-independent unit.</param>
    /// <returns>The new render target.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size or the DPI scale is out of range.</exception>
    IOffscreenRenderTarget CreateOffscreenTarget(PixelSize size, double dpiScale);

    /// <summary>
    /// Creates a render target that presents to a native surface.
    /// </summary>
    /// <param name="handle">The native surface.</param>
    /// <param name="size">The size in physical pixels.</param>
    /// <param name="dpiScale">Physical pixels per device-independent unit.</param>
    /// <returns>The new render target.</returns>
    /// <exception cref="ArgumentException">The handle is zero.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size or the DPI scale is out of range.</exception>
    ISurfaceRenderTarget CreateSurfaceTarget(NativeSurfaceHandle handle, PixelSize size, double dpiScale);

    /// <summary>
    /// Starts a frame on a render target.
    /// </summary>
    /// <param name="target">A render target of this device.</param>
    /// <returns>
    /// The command list of the frame. Dispose it after <see cref="ICommandList.Submit"/>. The next frame may
    /// reuse the object, so that frames do not allocate.
    /// </returns>
    /// <exception cref="ArgumentException">The target belongs to another device.</exception>
    /// <exception cref="InvalidOperationException">Another frame is still open.</exception>
    /// <exception cref="DeviceLostException">The device is lost.</exception>
    ICommandList BeginFrame(IRenderTarget target);

    /// <summary>
    /// Gets the number of draw calls issued so far, a diagnostic counter.
    /// </summary>
    long DrawCallCount { get; }

    /// <summary>
    /// Gets the number of bytes written to buffers and textures so far, a diagnostic counter.
    /// </summary>
    long UploadedByteCount { get; }

    /// <summary>
    /// Blocks until all submitted frames are finished.
    /// </summary>
    void WaitForIdle();

    /// <summary>
    /// Starts collecting frames: a device can then hand them over together, which is much cheaper with
    /// many charts. Submitted frames reach their targets at <see cref="EndBatch"/> at the latest.
    /// </summary>
    void BeginBatch()
    {
    }

    /// <summary>Hands over all frames collected since <see cref="BeginBatch"/>.</summary>
    void EndBatch()
    {
    }
}

/// <summary>
/// Thrown when a device stops working. It is no error of the caller: the device has to be replaced.
/// </summary>
public sealed class DeviceLostException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DeviceLostException"/> class.</summary>
    public DeviceLostException()
        : base("The render device was lost.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DeviceLostException"/> class.</summary>
    /// <param name="message">What happened.</param>
    public DeviceLostException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DeviceLostException"/> class.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">The cause.</param>
    public DeviceLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Work for compute shaders. It runs outside of frames, on the render thread, and waits for the GPU,
/// so it belongs to changes of the data and not to every frame.
/// </summary>
public interface IComputeOperations
{
    /// <summary>The size of a contour segment: its two ends and a gap, as <see cref="ICommandList.DrawPolyline(IGpuBuffer, int, int, in PolylineStyle)"/> reads them.</summary>
    const int ContourSegmentSize = 24;

    /// <summary>
    /// Finds isolines by marching squares and writes them as a line with gaps, in grid coordinates.
    /// </summary>
    /// <param name="values">An <see cref="TextureFormat.R32Float"/> texture. Rows run with Y.</param>
    /// <param name="levels">The levels.</param>
    /// <param name="segments">A storage buffer. Segments beyond its size are counted, but not written.</param>
    /// <returns>The number of segments, in any order.</returns>
    /// <exception cref="ArgumentException">A resource belongs to another device or has the wrong kind.</exception>
    long GenerateContours(IGpuTexture values, ReadOnlySpan<float> levels, IGpuBuffer segments);

    /// <summary>
    /// Counts points per pixel and writes the counts to a texture.
    /// </summary>
    /// <param name="points">A storage buffer of tightly packed <see cref="Vector2"/> positions.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="scale">The factors from positions to pixels of the texture.</param>
    /// <param name="offset">The pixel of the position zero.</param>
    /// <param name="target">An <see cref="TextureFormat.R32Float"/> texture of this device.</param>
    /// <returns>The highest count.</returns>
    /// <exception cref="ArgumentException">A resource belongs to another device or has the wrong kind.</exception>
    long BinPoints(IGpuBuffer points, int count, Vector2 scale, Vector2 offset, IGpuTexture target);

    /// <summary>
    /// Copies the start of a storage buffer to the CPU, waiting for the GPU.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="buffer">The buffer.</param>
    /// <param name="destination">Receives the data.</param>
    void ReadBuffer<T>(IGpuBuffer buffer, Span<T> destination)
        where T : unmanaged;
}

/// <summary>
/// A block of GPU memory.
/// </summary>
public interface IGpuBuffer : IDisposable
{
    /// <summary>
    /// Gets the buffer size in bytes.
    /// </summary>
    long SizeInBytes { get; }

    /// <summary>
    /// Gets the intended usage.
    /// </summary>
    BufferUsage Usage { get; }

    /// <summary>
    /// Copies data into the buffer.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="offsetInBytes">The position in the buffer, a multiple of 4.</param>
    /// <param name="data">The data to copy, its size in bytes a multiple of 4.</param>
    /// <exception cref="ArgumentOutOfRangeException">The data does not fit at this offset, or is not aligned.</exception>
    void Write<T>(long offsetInBytes, ReadOnlySpan<T> data)
        where T : unmanaged;
}

/// <summary>
/// A two-dimensional image in GPU memory.
/// </summary>
public interface IGpuTexture : IDisposable
{
    /// <summary>
    /// Gets the texture size.
    /// </summary>
    PixelSize Size { get; }

    /// <summary>
    /// Gets the pixel format.
    /// </summary>
    TextureFormat Format { get; }

    /// <summary>
    /// Replaces the complete texture content.
    /// </summary>
    /// <param name="pixels">The pixels, tightly packed and top row first.</param>
    /// <exception cref="ArgumentException">The length does not match <see cref="TextureDesc.SizeInBytes"/>.</exception>
    void Write(ReadOnlySpan<byte> pixels);

    /// <summary>
    /// Replaces one row of the texture.
    /// </summary>
    /// <param name="row">The row, zero at the top.</param>
    /// <param name="pixels">The pixels of the row, tightly packed.</param>
    /// <exception cref="ArgumentOutOfRangeException">The row does not exist.</exception>
    /// <exception cref="ArgumentException">The length does not match a row.</exception>
    void WriteRow(int row, ReadOnlySpan<byte> pixels);
}

/// <summary>
/// A surface that frames are rendered into.
/// </summary>
public interface IRenderTarget : IDisposable
{
    /// <summary>
    /// Gets the size in physical pixels.
    /// </summary>
    PixelSize Size { get; }

    /// <summary>
    /// Gets the physical pixels per device-independent unit.
    /// </summary>
    double DpiScale { get; }
}

/// <summary>
/// A render target whose content can be read back.
/// </summary>
public interface IOffscreenRenderTarget : IRenderTarget
{
    /// <summary>
    /// Copies the content of the target to the CPU, waiting for submitted frames to finish.
    /// </summary>
    /// <param name="destination">Receives the pixels, tightly packed and top row first.</param>
    /// <exception cref="ArgumentException">The length does not match <see cref="PixelSize.PixelCount"/>.</exception>
    /// <exception cref="InvalidOperationException">A frame on this target is still open.</exception>
    void ReadPixels(Span<Rgba8> destination);
}

/// <summary>
/// A render target on a native window. <see cref="ICommandList.Submit"/> presents the frame.
/// </summary>
public interface ISurfaceRenderTarget : IRenderTarget
{
    /// <summary>
    /// Gets or sets a value indicating whether presenting waits for the display. The default is true.
    /// </summary>
    /// <exception cref="InvalidOperationException">A frame on this target is still open.</exception>
    bool VSync { get; set; }

    /// <summary>
    /// Adapts the target to a new window size or scale. A minimized window has no size, callers
    /// then stop rendering instead of resizing.
    /// </summary>
    /// <param name="size">The size in physical pixels, positive in both dimensions.</param>
    /// <param name="dpiScale">Physical pixels per device-independent unit.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size or the DPI scale is out of range.</exception>
    /// <exception cref="InvalidOperationException">A frame on this target is still open.</exception>
    void Resize(PixelSize size, double dpiScale);
}

/// <summary>
/// The appearance of a line.
/// </summary>
/// <param name="WidthInPixels">The line width, positive and finite.</param>
/// <param name="Color">The line color.</param>
public readonly record struct PolylineStyle(float WidthInPixels, Rgba8 Color)
{
    /// <summary>
    /// The X value of a point that interrupts a line. Real coordinates stay far below it.
    /// </summary>
    public const float Gap = 1e30f;

    /// <summary>
    /// Gets the factors from buffer values to pixels.
    /// </summary>
    public Vector2 Scale { get; init; } = Vector2.One;

    /// <summary>
    /// Gets the pixel position of the buffer value zero.
    /// </summary>
    public Vector2 Offset { get; init; }

    /// <summary>
    /// Gets the rectangle outside of which nothing is drawn, or <see langword="null"/> for the whole target.
    /// </summary>
    public PixelRect? Clip { get; init; }

    /// <summary>Gets the reach of a glow around the line in pixels, zero for none.</summary>
    public float GlowRadius { get; init; }

    /// <summary>Gets the strength of the glow, from 0 to 1.</summary>
    public float GlowIntensity { get; init; }
}

/// <summary>
/// A marker shape evaluated as a signed distance field by GPU backends.
/// </summary>
public enum MarkerShape
{
    /// <summary>A circle.</summary>
    Circle,
    /// <summary>An axis-aligned square.</summary>
    Square,
    /// <summary>An upward-pointing triangle.</summary>
    Triangle,
    /// <summary>A diamond.</summary>
    Diamond,
    /// <summary>A diagonal cross.</summary>
    Cross,
    /// <summary>An axis-aligned plus sign.</summary>
    Plus,
}

/// <summary>
/// One marker stored in a vertex buffer. Position is transformed by <see cref="MarkerStyle.Scale"/> and
/// <see cref="MarkerStyle.Offset"/>; size is multiplied by <see cref="MarkerStyle.SizeScale"/>.
/// </summary>
/// <param name="Position">The position relative to the series origin.</param>
/// <param name="Size">The marker diameter before the size scale is applied.</param>
/// <param name="Color">The fill color.</param>
public readonly record struct MarkerInstance(Vector2 Position, float Size, Rgba8 Color);

/// <summary>
/// The appearance and data-to-pixel transform of a marker batch.
/// </summary>
/// <param name="Shape">The marker shape.</param>
/// <param name="BorderWidthInPixels">The inward border width, zero for no border.</param>
/// <param name="BorderColor">The border color.</param>
public readonly record struct MarkerStyle(MarkerShape Shape, float BorderWidthInPixels, Rgba8 BorderColor)
{
    /// <summary>Gets the factors from buffer positions to pixels.</summary>
    public Vector2 Scale { get; init; } = Vector2.One;

    /// <summary>Gets the pixel position of buffer position zero.</summary>
    public Vector2 Offset { get; init; }

    /// <summary>Gets the factor from stored marker sizes to physical pixels.</summary>
    public float SizeScale { get; init; } = 1;

    /// <summary>Gets the rectangle outside of which nothing is drawn, or <see langword="null"/>.</summary>
    public PixelRect? Clip { get; init; }
}

/// <summary>
/// What a pixel shows when several texels fall on it.
/// </summary>
public enum RasterReduction
{
    /// <summary>The nearest texel, the cheapest way. Narrow features can fall through.</summary>
    Nearest,

    /// <summary>The mean of the texels, for smooth fields.</summary>
    Mean,

    /// <summary>The highest of the texels, so that narrow peaks stay visible.</summary>
    Maximum,
}

/// <summary>
/// How a texture of values becomes colors.
/// </summary>
/// <param name="Minimum">The value of the first color.</param>
/// <param name="Maximum">The value of the last color, greater than the minimum.</param>
public readonly record struct RasterStyle(float Minimum, float Maximum)
{
    /// <summary>Gets a value indicating whether colors follow the logarithm of the values. The minimum must then be positive.</summary>
    public bool IsLogarithmic { get; init; }

    /// <summary>Gets the color of NaN.</summary>
    public Rgba8 MissingColor { get; init; }

    /// <summary>Gets the color of values below the minimum, or <see langword="null"/> for the first color of the table.</summary>
    public Rgba8? BelowColor { get; init; }

    /// <summary>Gets the color of values above the maximum, or <see langword="null"/> for the last color of the table.</summary>
    public Rgba8? AboveColor { get; init; }

    /// <summary>Gets the point of the texture at the top left of the rectangle, from 0 to 1.</summary>
    public Vector2 SectionOrigin { get; init; }

    /// <summary>Gets the part of the texture on the rectangle, from 0 to 1. A negative part mirrors it.</summary>
    public Vector2 SectionSize { get; init; } = Vector2.One;

    /// <summary>Gets a value indicating whether values between the texels are blended, with the texels in the middle of their cells. A missing neighbor makes the result missing.</summary>
    public bool IsInterpolated { get; init; }

    /// <summary>Gets what a pixel shows when several texels fall on it. A backend may read only some of them.</summary>
    public RasterReduction Reduction { get; init; }

    /// <summary>Gets the number of rows the texture is rotated by, for a ring of rows.</summary>
    public int RowShift { get; init; }
}

/// <summary>
/// A corner of a triangle in <see cref="ICommandList.FillMesh"/>.
/// </summary>
/// <param name="Position">The position in physical pixels.</param>
/// <param name="Color">The color, blended across the triangle.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct MeshVertex(Vector2 Position, Rgba8 Color);

/// <summary>
/// Records the drawing of one frame. Coordinates are physical pixels relative to the top left of the target.
/// </summary>
/// <remarks>
/// The commands take effect in order with <see cref="Submit"/>. Disposing without submitting discards them.
/// After <see cref="Submit"/>, recording throws <see cref="InvalidOperationException"/>.
/// </remarks>
public interface ICommandList : IDisposable
{
    /// <summary>
    /// Fills the complete target, ignoring what was drawn before.
    /// </summary>
    /// <param name="color">The fill color.</param>
    void Clear(Rgba8 color);

    /// <summary>
    /// Fills a rectangle. A rectangle with empty or non-finite extent draws nothing.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <param name="color">The fill color.</param>
    void FillRectangle(PixelRect bounds, Rgba8 color);

    /// <summary>
    /// Fills a rectangle with a linear gradient. Invalid or empty bounds draw nothing.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <param name="startColor">The color at the top or left edge.</param>
    /// <param name="endColor">The color at the bottom or right edge.</param>
    /// <param name="vertical">Whether the gradient runs from top to bottom instead of left to right.</param>
    void FillLinearGradient(PixelRect bounds, Rgba8 startColor, Rgba8 endColor, bool vertical);

    /// <summary>
    /// Fills a triangle. A triangle with a non-finite corner draws nothing.
    /// </summary>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <param name="color">The fill color.</param>
    void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color);

    /// <summary>
    /// Fills triangles with a color per corner, in one draw call however many there are.
    /// </summary>
    /// <param name="vertices">Three corners per triangle.</param>
    /// <param name="clip">The rectangle outside of which nothing is drawn, or <see langword="null"/> for the whole target.</param>
    void FillMesh(ReadOnlySpan<MeshVertex> vertices, PixelRect? clip);

    /// <summary>
    /// Draws an anti-aliased connected line through consecutive points, with round joins and caps.
    /// Widths below one pixel render with proportionally reduced coverage.
    /// </summary>
    /// <param name="points">A vertex buffer of tightly packed <see cref="Vector2"/> positions.</param>
    /// <param name="firstPoint">The index of the first point.</param>
    /// <param name="pointCount">The number of points, at least 2.</param>
    /// <param name="widthInPixels">The line width, positive and finite.</param>
    /// <param name="color">The line color.</param>
    /// <exception cref="ArgumentException">
    /// The buffer belongs to another device or lacks <see cref="BufferUsage.Vertex"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The point range or the width is out of range.</exception>
    void DrawPolyline(IGpuBuffer points, int firstPoint, int pointCount, float widthInPixels, Rgba8 color) =>
        DrawPolyline(points, firstPoint, pointCount, new PolylineStyle(widthInPixels, color));

    /// <summary>
    /// Draws a line whose points are transformed by the GPU, so that a persistent buffer can follow the
    /// axes without new uploads. Segments that touch a <see cref="PolylineStyle.Gap"/> point are skipped.
    /// </summary>
    /// <param name="points">A vertex buffer of tightly packed <see cref="Vector2"/> positions.</param>
    /// <param name="firstPoint">The index of the first point.</param>
    /// <param name="pointCount">The number of points, at least 2.</param>
    /// <param name="style">The appearance.</param>
    /// <exception cref="ArgumentException">
    /// The buffer belongs to another device or lacks <see cref="BufferUsage.Vertex"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The point range or the width is out of range.</exception>
    void DrawPolyline(IGpuBuffer points, int firstPoint, int pointCount, in PolylineStyle style);

    /// <summary>
    /// Draws marker instances in one draw call.
    /// </summary>
    /// <param name="markers">A vertex buffer of tightly packed <see cref="MarkerInstance"/> values.</param>
    /// <param name="firstMarker">The index of the first marker.</param>
    /// <param name="markerCount">The number of markers. Zero draws nothing.</param>
    /// <param name="style">The shape, border and transform.</param>
    /// <exception cref="ArgumentException">The buffer belongs to another device or lacks vertex usage.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The range or style is invalid.</exception>
    void DrawMarkers(IGpuBuffer markers, int firstMarker, int markerCount, in MarkerStyle style);

    /// <summary>
    /// Marks the area of a text label. Stands in for real text until the text pass exists.
    /// </summary>
    /// <param name="bounds">The area of the label.</param>
    /// <param name="color">The placeholder color.</param>
    void DrawTextPlaceholder(PixelRect bounds, Rgba8 color);

    /// <summary>
    /// Draws glyphs from a distance field atlas, in one draw call however many there are.
    /// </summary>
    /// <param name="atlas">An <see cref="TextureFormat.Rgba8Unorm"/> texture of this device.</param>
    /// <param name="glyphs">The glyphs. An empty span draws nothing.</param>
    /// <exception cref="ArgumentException">The texture belongs to another device or has another format.</exception>
    void DrawGlyphs(IGpuTexture atlas, ReadOnlySpan<GlyphInstance> glyphs);

    /// <summary>
    /// Draws a texture on a rectangle, with the nearest texel and blended by its alpha.
    /// </summary>
    /// <param name="image">An <see cref="TextureFormat.Rgba8Unorm"/> texture of this device.</param>
    /// <param name="bounds">The rectangle. An empty one draws nothing.</param>
    /// <exception cref="ArgumentException">The texture belongs to another device or has another format.</exception>
    void DrawImage(IGpuTexture image, PixelRect bounds);

    /// <summary>
    /// Draws a texture of values on a rectangle, colored by a table, with the nearest texel.
    /// </summary>
    /// <param name="values">An <see cref="TextureFormat.R32Float"/> texture of this device.</param>
    /// <param name="colormap">An <see cref="TextureFormat.Rgba8Unorm"/> texture with the table in its first row.</param>
    /// <param name="bounds">The rectangle. An empty one draws nothing.</param>
    /// <param name="style">The mapping.</param>
    /// <exception cref="ArgumentException">A texture belongs to another device or has another format.</exception>
    void DrawRaster(IGpuTexture values, IGpuTexture colormap, PixelRect bounds, in RasterStyle style);

    /// <summary>
    /// Ends the frame and hands its commands to the device. A surface target presents the frame, or
    /// drops it if the swapchain has no image available in time.
    /// </summary>
    void Submit();
}
