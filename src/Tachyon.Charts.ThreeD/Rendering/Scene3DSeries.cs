using System.Diagnostics;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.WebGPU;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>Brings the picture of a scene into a chart, as an image in the series pass.</summary>
/// <param name="scene">The scene.</param>
/// <param name="picker">The search for what lies under the pointer.</param>
/// <param name="xAxis">The hidden horizontal axis of the chart.</param>
/// <param name="yAxis">The hidden vertical axis of the chart.</param>
internal sealed class Scene3DSeries(Scene3D scene, ScenePicker picker, AxisBase xAxis, AxisBase yAxis) : ChartSeries(xAxis, yAxis), IDisposable, ICsvExport
{
    private readonly List<Vector3> _grid = [];
    private readonly List<Vector3> _frame = [];
    private readonly List<LineVertex> _lines = [];
    private readonly List<double> _linkMarkers = [];
    private readonly Dictionary<Surface3D, long> _surfaceVersions = [];
    private readonly Dictionary<Waterfall3D, long> _waterfallRows = [];
    private readonly Dictionary<Line3D, LineState> _lineStates = [];
    private readonly Dictionary<Scatter3D, LineState> _scatterStates = [];
    private readonly Dictionary<Bars3D, long> _barVersions = [];
    private readonly Dictionary<PointCloud3D, CloudLoader> _cloudLoaders = [];
    private readonly Dictionary<PointCloud3D, CloudBudget> _cloudBudgets = [];
    private long _lastCamera = -1;
    private ChartGroup? _group;
    private (Tachyon.Charts.Core.Numerics.DoubleRange, Tachyon.Charts.Core.Numerics.DoubleRange, Tachyon.Charts.Core.Numerics.DoubleRange) _ranges;
    private bool _meshesAreStale;
    private int _framesUntilMeasure;
    private double _gpuMilliseconds = double.NaN;
    private int _picturesBeforeMeasure = 2;
    private readonly Dictionary<Volume3D, VolumeLoader> _volumeLoaders = [];
    private readonly MarchQuality _quality = new();
    private readonly Vector4[] _transferTable = new Vector4[TransferFunction3D.TableSize];
    private long _lastView = -1;
    private BarInstance[] _barInstances = [];
    private float[] _heights = [];
    private float[] _row = [];
    private readonly Dictionary<MeasuredMap3D, Mesh3D> _mapMeshes = [];
    private readonly Dictionary<AntennaPattern3D, Mesh3D> _antennaMeshes = [];
    private (BoxWalls, Theme?, Tachyon.Charts.Core.Numerics.DoubleRange, Tachyon.Charts.Core.Numerics.DoubleRange, Tachyon.Charts.Core.Numerics.DoubleRange, long) _linesKey;
    private Renderer3D? _renderer;
    private IRenderDevice? _device;
    private WebGpuRenderDevice? _exportDevice;
    private IGpuTexture? _copy;
    private IRenderDevice? _copyDevice;
    private bool _drawsCopy;
    private ChartSurface? _chart;
    private long _renderedVersion = -1;
    private PixelSize _renderedSize;
    private Theme? _renderedTheme;

    /// <inheritdoc />
    protected override long Version => scene.Camera.Version + scene.Version + SpectrumLinks.Stamp(scene) + (_quality.WantsFinePicture(Environment.TickCount64) ? 1 : 0);

    /// <summary>Connects the chart, whose theme gives the colors, and the group it belongs to.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="group">The group, if any.</param>
    public void Bind(ChartSurface chart, ChartGroup? group = null)
    {
        _chart = chart;
        if (group is not null)
        {
            _group = group;
            group.Add(chart, scene.XAxis);
            group.CursorMoved += OnCursorMoved;
            scene.Group = group;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_group is { } group && _chart is { } chart)
        {
            group.CursorMoved -= OnCursorMoved;
            group.Remove(chart);
            scene.Group = null;
            _group = null;
        }

        ReleaseResources();
        _copy?.Dispose();
        _copy = null;
        _exportDevice?.Dispose();
        _exportDevice = null;
    }

    /// <inheritdoc />
    protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        _drawsCopy = device is not WebGpuRenderDevice;
        if (device is WebGpuRenderDevice webGpu)
        {
            Render(webGpu, frame);
        }
        else
        {
            PrepareCopy(device, frame);
        }
    }

    /// <inheritdoc />
    protected override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if ((_drawsCopy ? _copy : _renderer?.Picture) is { } picture)
        {
            commands.DrawImage(picture, frame.Bounds);
        }
    }

    /// <inheritdoc />
    public void WriteCsv(TextWriter writer) => SceneCsv.Write(scene, writer);

    // A device without a graphics card, as for SVG and PDF, draws a copy of the picture: the scene itself still renders on
    // the GPU, at the size of the export, and goes into the file as an embedded image. A chart that never ran on a
    // GPU, as in an export without a window, opens a device of its own for it.
    private void PrepareCopy(IRenderDevice device, in SeriesFrame frame)
    {
        var gpu = _device as WebGpuRenderDevice ?? (_exportDevice ??= OpenExportDevice());
        if (gpu is null)
        {
            return;
        }

        Render(gpu, frame);
        if (_renderer?.Picture is not { } picture)
        {
            return;
        }

        var size = picture.Size;
        using var target = gpu.CreateOffscreenTarget(size, 1);
        using (var commands = gpu.BeginFrame(target))
        {
            commands.DrawImage(picture, new PixelRect(0, 0, size.Width, size.Height));
            commands.Submit();
        }

        var pixels = new Rgba8[size.PixelCount];
        target.ReadPixels(pixels);
        if (_copy is null || !ReferenceEquals(_copyDevice, device) || _copy.Size != size)
        {
            _copy?.Dispose();
            _copy = device.CreateTexture(new TextureDesc(size, TextureFormat.Rgba8Unorm));
            _copyDevice = device;
        }

        _copy.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()));
    }

    private static WebGpuRenderDevice? OpenExportDevice()
    {
        try
        {
            return Gpu3d.Status.IsAvailable ? WebGpuRenderDevice.Create() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void Render(WebGpuRenderDevice webGpu, in SeriesFrame frame)
    {
        var device = webGpu;
        if (!ReferenceEquals(_device, device))
        {
            ReleaseResources();
            _device = device;
            _renderer = new Renderer3D(webGpu);
        }

        var size = new PixelSize((int)MathF.Round(frame.Bounds.Width), (int)MathF.Round(frame.Bounds.Height));
        var theme = _chart?.Theme;
        FollowLevels();
        var version = Version;
        if (size.Width <= 0 || size.Height <= 0 || (version == _renderedVersion && size == _renderedSize && ReferenceEquals(theme, _renderedTheme)))
        {
            return;
        }

        (_renderedVersion, _renderedSize, _renderedTheme) = (version, size, theme);
        TakeRanges();
        CollectLines(theme);
        UploadMeshes();

        foreach (var surface in scene.Surfaces)
        {
            UpdateSurface(surface);
        }

        foreach (var feed in scene.Feeds)
        {
            feed.Update(scene);
        }

        foreach (var waterfall in scene.Waterfalls)
        {
            UpdateWaterfall(waterfall);
        }

        foreach (var line in scene.Lines)
        {
            UpdateLine(line);
        }

        foreach (var scatter in scene.Scatters)
        {
            UpdateScatter(scatter);
        }

        foreach (var bars in scene.Bars)
        {
            UpdateBars(bars);
        }

        var eye = scene.Camera.Eye;
        var aspect = size.Width / (double)size.Height;
        foreach (var cloud in scene.Clouds)
        {
            UpdateCloud(cloud, size, aspect);
        }

        foreach (var volume in scene.Volumes)
        {
            UpdateVolume(volume, aspect);
        }

        var walls = AxisBox3D.SelectWalls(eye);
        var matrix = scene.Camera.CreateMatrix(aspect).ToGpu();
        var isMeasured = TakeMeasure();
        if (isMeasured)
        {
            webGpu.WaitForIdle();
        }

        var start = Stopwatch.GetTimestamp();
        _renderer!.Render(size, matrix, new Vector3((float)eye.X, (float)eye.Y, (float)eye.Z), theme?.Surface?.Background ?? new Rgba8(14, 17, 23, 255), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_lines), new Vector4(walls.X, walls.Y, walls.Z, 0), CutPlanes.From(scene));
        if (isMeasured)
        {
            webGpu.WaitForIdle();
            NoteTime(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }


    /// <inheritdoc />
    protected override void ReleaseResources()
    {
        _renderer?.Dispose();
        _renderer = null;
        _device = null;
        _renderedVersion = -1;
        _surfaceVersions.Clear();
        _waterfallRows.Clear();
        _lineStates.Clear();
        _scatterStates.Clear();
        _barVersions.Clear();
        _cloudLoaders.Clear();
        _cloudBudgets.Clear();
        _volumeLoaders.Clear();
        _mapMeshes.Clear();
        _antennaMeshes.Clear();
    }

    private static double[] ToBox(AxisBase axis)
    {
        var ticks = AxisBox3D.GetTicks(axis);
        var places = new double[ticks.Count];
        for (var i = 0; i < places.Length; i++)
        {
            places[i] = AxisBox3D.ToBox(axis, ticks[i].Value);
        }

        return places;
    }

    private void UpdateLine(Line3D line)
    {
        var ranges = (scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange);
        _lineStates.TryGetValue(line, out var state);
        var uploaded = state.Ranges == ranges ? state.Uploaded : 0;
        TraceRing.Upload(line.Points, ref uploaded, new BoxMap(scene), line.Color, _renderer!.GetTrace(line));
        _lineStates[line] = new LineState(uploaded, ranges);
    }

    private void UpdateScatter(Scatter3D scatter)
    {
        var ranges = (scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange);
        _scatterStates.TryGetValue(scatter, out var state);
        var uploaded = state.Ranges == ranges ? state.Uploaded : 0;
        TraceRing.Upload(scatter.Points, ref uploaded, new BoxMap(scene), scatter.Color, _renderer!.GetSpots(scatter), Points3D.ToSizeCode(scatter.Size));
        _scatterStates[scatter] = new LineState(uploaded, ranges);
    }

    // Waiting for the GPU costs a little, so only some frames do it, and only while the view moves.
    private bool TakeMeasure()
    {
        const int FramesPerMeasure = 30;
        var hasMoved = scene.Camera.Version != _lastCamera;
        _lastCamera = scene.Camera.Version;
        // The diagnostics overlay wants the time of the GPU also for a scene without clouds.
        var isWanted = _chart?.ShowDiagnostics == true || (hasMoved && _cloudBudgets.Count > 0);

        // The first pictures build the pipelines and upload the data, which says nothing about the frames after them.
        if (_picturesBeforeMeasure > 0)
        {
            _picturesBeforeMeasure--;
            return false;
        }

        if (!isWanted || --_framesUntilMeasure > 0)
        {
            return false;
        }

        _framesUntilMeasure = FramesPerMeasure;
        return true;
    }

    private void NoteTime(double milliseconds)
    {
        _gpuMilliseconds = milliseconds;
        foreach (var budget in _cloudBudgets.Values)
        {
            budget.Update(milliseconds);
        }
    }

    /// <inheritdoc />
    protected override void WriteDiagnostics(DiagnosticsText text) => Describe(text);

    /// <summary>Writes the lines of the 3D chart: time of the GPU, point budget, loading, graphics memory.</summary>
    /// <param name="text">Receives the lines.</param>
    internal void Describe(DiagnosticsText text)
    {
        if (double.IsFinite(_gpuMilliseconds))
        {
            text.Add("GPU 3D", _gpuMilliseconds, "F2", "ms");
        }

        foreach (var budget in _cloudBudgets.Values)
        {
            text.Add("Point budget", budget.Current, "N0", string.Empty);
        }

        for (var i = 0; i < scene.Clouds.Count; i++)
        {
            if (!scene.Clouds[i].IsLoaded)
            {
                text.Add("Loading", scene.Clouds[i].Progress * 100, "F0", "%");
            }
        }

        _renderer?.WriteMemory(text);
    }

    private void UpdateCloud(PointCloud3D cloud, PixelSize size, double aspect)
    {
        cloud.Start();
        if (!cloud.IsLoaded)
        {
            return;
        }

        if (!_cloudLoaders.TryGetValue(cloud, out var loader))
        {
            loader = new CloudLoader(cloud);
            _cloudLoaders[cloud] = loader;
            _cloudBudgets[cloud] = new CloudBudget(cloud.PointBudget);
        }

        loader.Load(scene, _renderer!.GetCloud(cloud));
        loader.Select(scene, scene.Camera.CreateMatrix(aspect), size, _renderer, _cloudBudgets[cloud].Current);
    }

    private VolumeView CreateView(Volume3D volume, double aspect)
    {
        const double FineSteps = 2;
        const double CoarseSteps = 0.75;
        var view = scene.Camera.Version + PlaneVersion() + (volume.Transfer?.Version ?? 0);
        var isCoarse = _quality.Update(_lastView >= 0 && view != _lastView, Environment.TickCount64);
        _lastView = view;

        // Steps per voxel: the view may lose quality while it moves, but not the picture its shape.
        var (low, high) = volume.GetBox(scene);
        var voxel = (high.X - low.X) / volume.Data.Width;
        // A surface must not slip through between two steps, so it never takes less than one step per voxel.
        var coarse = volume.Mode == VolumeMode.Surface ? 1 : CoarseSteps;
        var ratio = 1 / (isCoarse ? coarse : FineSteps);
        var matrix = scene.Camera.CreateMatrix(aspect);
        return new VolumeView(
            new Vector4((float)(voxel * ratio), (float)voxel, (float)ratio, 0),
            (ToCorner(matrix, -1, -1, 0), ToCorner(matrix, 3, -1, 0), ToCorner(matrix, -1, 3, 0)),
            (ToCorner(matrix, -1, -1, 1), ToCorner(matrix, 3, -1, 1), ToCorner(matrix, -1, 3, 1)));
    }

    private static Vector4 ToCorner(in Matrix3D matrix, double x, double y, double z)
    {
        var point = matrix.Unproject(x, y, z);
        return new Vector4((float)point.X, (float)point.Y, (float)point.Z, 1);
    }

    private void UpdateVolume(Volume3D volume, double aspect)
    {
        if (!_volumeLoaders.TryGetValue(volume, out var loader))
        {
            loader = new VolumeLoader(volume);
            _volumeLoaders[volume] = loader;
            _renderer!.GetVolume(volume, CreateColors(volume.Colormap));
        }

        loader.Load(_renderer!.GetVolume(volume, default));
        var (low, high) = volume.GetBox(scene);
        _renderer.SetPlace(
            volume,
            new Vector3((float)low.X, (float)low.Y, (float)low.Z),
            new Vector3((float)high.X, (float)high.Y, (float)high.Z),
            new Vector4((float)volume.SliceX.Offset, (float)volume.SliceY.Offset, (float)volume.SliceZ.Offset, (float)volume.SliceThreshold),
            volume.ShowsSlices);
        if (volume.Marches)
        {
            volume.Transfer?.WriteTable(_transferTable);
            var material = volume.Material;
            var eye = scene.Camera.Eye;
            _renderer.SetMarch(volume, _transferTable, CreateView(volume, aspect) with
            {
                Look = new Vector4((float)volume.Mode, (float)volume.IsoShare, 0, 0),
                Eye = new Vector4((float)eye.X, (float)eye.Y, (float)eye.Z, 1),
                Material = new Vector4((float)material.Gloss, (float)material.Sharpness, 1, material.IsLit ? 1 : 0),
            });
        }
    }

    private void UpdateBars(Bars3D bars)
    {
        var version = bars.Data.Version;
        if (_barVersions.TryGetValue(bars, out var known) && known == version)
        {
            return;
        }

        _barVersions[bars] = version;
        var count = bars.Data.Width * bars.Data.Height;
        if (_barInstances.Length != count)
        {
            _barInstances = new BarInstance[count];
        }

        bars.Write(scene, _barInstances);
        _renderer!.Update(bars, _barInstances, bars.Cells(scene));
    }

    private void UpdateSurface(Surface3D surface)
    {
        var version = surface.Data.Version;
        if (_surfaceVersions.TryGetValue(surface, out var known) && known == version)
        {
            return;
        }

        _surfaceVersions[surface] = version;
        var (columns, rows) = (surface.Data.Width, surface.Data.Height);
        if (_heights.Length != columns * rows)
        {
            _heights = new float[columns * rows];
        }

        surface.WriteHeights(scene.ZAxis, _heights);
        var (left, right) = (AxisBox3D.ToBox(scene.XAxis, surface.X.Min), AxisBox3D.ToBox(scene.XAxis, surface.X.Max));
        var (front, back) = (AxisBox3D.ToBox(scene.YAxis, surface.Y.Min), AxisBox3D.ToBox(scene.YAxis, surface.Y.Max));
        var range = surface.ColorRange ?? surface.Data.ValueRange;
        var shape = new SurfaceShape(
            columns,
            rows,
            new Vector4((float)left, (float)front, (float)((right - left) / (columns - 1)), (float)((back - front) / (rows - 1))),
            new Vector4((float)AxisBox3D.ToBox(scene.ZAxis, range.Min), (float)AxisBox3D.ToBox(scene.ZAxis, range.Max), 0, 0))
        {
            Levels = CreateLevels(surface.Contours),
            Contours = surface.Contours,
        };
        _renderer!.Update(surface, shape, _heights, CreateColors(surface.Colormap), surface.Material);
    }

    private static Vector4[] CreateColors(Tachyon.Charts.Core.Colors.Colormap colormap)
    {
        var entries = colormap.Entries;
        var colors = new Vector4[entries.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = new Vector4(entries[i].R, entries[i].G, entries[i].B, 255) / 255f;
        }

        return colors;
    }

    private float[] CreateLevels(Contours3D? contours)
    {
        if (contours is null)
        {
            return [];
        }

        var levels = new float[contours.Levels.Count];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = (float)AxisBox3D.ToBox(scene.ZAxis, contours.Levels[i]);
        }

        return levels;
    }

    private readonly record struct LineState(long Uploaded, (Tachyon.Charts.Core.Numerics.DoubleRange, Tachyon.Charts.Core.Numerics.DoubleRange, Tachyon.Charts.Core.Numerics.DoubleRange) Ranges);

    private void AddPointMarks(MeasuredMap3D map)
    {
        const float Size = 0.012f;
        var mark = new Rgba8(255, 255, 255, 230);
        var count = Math.Min(Math.Min(map.X.Length, map.Y.Length), map.Z.Length);
        for (var i = 0; i < count; i++)
        {
            var place = new Vector3((float)AxisBox3D.ToBox(scene.XAxis, map.X[i]), (float)AxisBox3D.ToBox(scene.YAxis, map.Y[i]), (float)AxisBox3D.ToBox(scene.ZAxis, map.Z[i]));
            if (float.IsFinite(place.X + place.Y + place.Z))
            {
                _lines.Add(new LineVertex(place - new Vector3(Size, 0, 0), mark));
                _lines.Add(new LineVertex(place + new Vector3(Size, 0, 0), mark));
                _lines.Add(new LineVertex(place - new Vector3(0, Size, 0), mark));
                _lines.Add(new LineVertex(place + new Vector3(0, Size, 0), mark));
                _lines.Add(new LineVertex(place - new Vector3(0, 0, Size), mark));
                _lines.Add(new LineVertex(place + new Vector3(0, 0, Size), mark));
            }
        }
    }

    // The cursor of the group as a thin frame across the box at its place on the first axis.
    private void AddCursor()
    {
        if (scene.Cursor is not { } value)
        {
            return;
        }

        var place = AxisBox3D.ToBox(scene.XAxis, value);
        if (Math.Abs(place) > 1)
        {
            return;
        }

        var mark = new Rgba8(255, 255, 255, 200);
        var corners = new List<Point3D>(4);
        new ClipPlane3D(new Point3D(1, 0, 0), place).AddOutline(corners);
        for (var i = 0; i < corners.Count; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % corners.Count]);
            _lines.Add(new LineVertex(new Vector3((float)a.X, (float)a.Y, (float)a.Z), mark));
            _lines.Add(new LineVertex(new Vector3((float)b.X, (float)b.Y, (float)b.Z), mark));
        }
    }

    private long PlaneVersion()
    {
        var version = (long)scene.Planes.Count + (scene.Cursor is { } cursor ? BitConverter.DoubleToInt64Bits(cursor) : 0);
        foreach (var plane in scene.Planes)
        {
            version += plane.Version;
        }

        return version;
    }

    private void AddOutline(ClipPlane3D plane)
    {
        var mark = new Rgba8(240, 150, 60, 255);
        var corners = new List<Point3D>(6);
        plane.AddOutline(corners);
        for (var i = 0; i < corners.Count; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % corners.Count]);
            _lines.Add(new LineVertex(new Vector3((float)a.X, (float)a.Y, (float)a.Z), mark));
            _lines.Add(new LineVertex(new Vector3((float)b.X, (float)b.Y, (float)b.Z), mark));
        }
    }

    private void AddLobeMarks(AntennaPattern3D antenna)
    {
        var mark = new Rgba8(255, 255, 255, 255);
        var ends = new List<Point3D>();
        antenna.AddMarks(ends);
        foreach (var end in ends)
        {
            _lines.Add(new LineVertex(new Vector3((float)AxisBox3D.ToBox(scene.XAxis, end.X), (float)AxisBox3D.ToBox(scene.YAxis, end.Y), (float)AxisBox3D.ToBox(scene.ZAxis, end.Z)), mark));
        }
    }

    private void UpdateWaterfall(Waterfall3D waterfall)
    {
        var (columns, rows) = (waterfall.Data.Columns, waterfall.Data.Rows);
        var total = waterfall.Data.TotalRows;
        if (!_renderer!.HoldsSurface(waterfall) || !_waterfallRows.ContainsKey(waterfall))
        {
            CreateWaterfall(waterfall);
            _waterfallRows[waterfall] = 0;
        }

        var row = Waterfall3D.FirstMissingRow(_waterfallRows[waterfall], total, rows);
        if (_row.Length != columns)
        {
            _row = new float[columns];
        }

        var transform = scene.ZAxis.CreateTransform(-1, 2);
        var sign = scene.ZAxis.IsHorizontal ? 1 : -1;
        for (; row < total; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var place = waterfall.Data.TryRead(row, column, out var value) && float.IsFinite(value) ? sign * transform.ToPixel(value) : double.NaN;
                _row[column] = double.IsFinite(place) ? (float)place : Surface3D.MissingHeight;
            }

            _renderer.WriteRow(waterfall, (int)(row % rows), _row, Waterfall3D.OldestSlot(row + 1, rows));
        }

        _waterfallRows[waterfall] = total;
    }

    private void CreateWaterfall(Waterfall3D waterfall)
    {
        var (columns, rows) = (waterfall.Data.Columns, waterfall.Data.Rows);
        var (left, right) = (AxisBox3D.ToBox(scene.XAxis, waterfall.X.Min), AxisBox3D.ToBox(scene.XAxis, waterfall.X.Max));
        var (front, back) = (AxisBox3D.ToBox(scene.YAxis, waterfall.Y.Min), AxisBox3D.ToBox(scene.YAxis, waterfall.Y.Max));
        var range = waterfall.ColorRange ?? scene.ZAxis.VisibleRange;
        var heights = new float[columns * rows];
        Array.Fill(heights, Surface3D.MissingHeight);

        // The oldest row comes first, at the far end, and the rows run towards the front.
        var shape = new SurfaceShape(
            columns,
            rows,
            new Vector4((float)left, (float)back, (float)((right - left) / (columns - 1)), (float)((front - back) / (rows - 1))),
            new Vector4((float)AxisBox3D.ToBox(scene.ZAxis, range.Min), (float)AxisBox3D.ToBox(scene.ZAxis, range.Max), 0, 0),
            LineSpacing: waterfall.Style == WaterfallStyle.Lines ? Math.Max(waterfall.LineSpacing, 1) : 0);
        _renderer!.Update(waterfall, shape, heights, CreateColors(waterfall.Colormap), waterfall.Material);
    }

    // The cursor of a 2D chart in the group. It comes from the UI thread, the next frame shows it.
    private void OnCursorMoved(double? value) => scene.Cursor = value;

    // New ranges move everything in the box: a chart group zooms from outside, so all data reach the GPU anew.
    private void TakeRanges()
    {
        var ranges = (scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange);
        if (ranges == _ranges)
        {
            return;
        }

        var isFirst = _ranges == default;
        _ranges = ranges;
        if (!isFirst)
        {
            _surfaceVersions.Clear();
            _waterfallRows.Clear();
            _barVersions.Clear();
            _mapMeshes.Clear();
            _antennaMeshes.Clear();
            _meshesAreStale = true;
        }
    }

    private void UploadMeshes()
    {
        foreach (var mesh in scene.Meshes)
        {
            if (!_renderer!.Holds(mesh) || _meshesAreStale)
            {
                Upload(mesh, mesh);
            }
        }

        _meshesAreStale = false;

        foreach (var map in scene.Maps)
        {
            if (!_mapMeshes.ContainsKey(map))
            {
                _mapMeshes[map] = map.CreateMesh(scene.XAxis, scene.YAxis);
                Upload(_mapMeshes[map], map);
            }
        }

        foreach (var antenna in scene.Antennas)
        {
            if (!_antennaMeshes.ContainsKey(antenna))
            {
                _antennaMeshes[antenna] = antenna.CreateMesh();
                Upload(_antennaMeshes[antenna], antenna);
            }
        }
    }

    private void Upload(Mesh3D mesh, object source)
    {
        var positions = new Point3D[mesh.Vertices.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            var point = mesh.Vertices[i].Position;
            positions[i] = new Point3D(AxisBox3D.ToBox(scene.XAxis, point.X), AxisBox3D.ToBox(scene.YAxis, point.Y), AxisBox3D.ToBox(scene.ZAxis, point.Z));
        }

        var normals = Mesh3D.CalculateNormals(positions, mesh.Indices);
        var vertices = new MeshVertex[positions.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var (p, n) = (positions[i], normals[i]);
            vertices[i] = new MeshVertex(new Vector3((float)p.X, (float)p.Y, (float)p.Z), new Vector3((float)n.X, (float)n.Y, (float)n.Z), mesh.Vertices[i].Color);
        }

        var indices = Mesh3D.ValidIndices(positions, mesh.Indices);
        _renderer!.Upload(mesh, vertices, indices);
        picker.Add(mesh, positions, indices, source);
    }

    // The lines only change when another wall comes to lie behind the data, so most frames reuse them.
    private void CollectLines(Theme? theme)
    {
        var key = (AxisBox3D.SelectWalls(scene.Camera.Eye), theme, scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange, scene.Maps.Count + scene.Antennas.Count + PlaneVersion() + SpectrumLinks.Stamp(scene));
        if (key == _linesKey)
        {
            return;
        }

        _linesKey = key;
        _grid.Clear();
        _frame.Clear();
        _lines.Clear();
        AxisBox3D.AddLines(_grid, _frame, AxisBox3D.SelectWalls(scene.Camera.Eye), ToBox(scene.XAxis), ToBox(scene.YAxis), ToBox(scene.ZAxis));
        Rgba8 gridColor = theme?.Grid?.Major ?? new Rgba8(255, 255, 255, 40);
        Rgba8 frameColor = theme?.Axis?.Line ?? new Rgba8(255, 255, 255, 110);
        foreach (var point in _grid)
        {
            _lines.Add(new LineVertex(point, gridColor));
        }

        foreach (var point in _frame)
        {
            _lines.Add(new LineVertex(point, frameColor));
        }

        foreach (var map in scene.Maps)
        {
            if (map.ShowsPoints)
            {
                AddPointMarks(map);
            }
        }

        foreach (var antenna in scene.Antennas)
        {
            AddLobeMarks(antenna);
        }

        foreach (var plane in scene.Planes)
        {
            AddOutline(plane);
        }

        AddCursor();
        for (var i = 0; i < scene.Waterfalls.Count; i++)
        {
            if (scene.Waterfalls[i] is { Display: { } display } waterfall)
            {
                SpectrumLinks.AddLines(scene, waterfall, display, _lines, _linkMarkers);
            }
        }
    }

    // The height of the box follows the levels of a spectrum behind a waterfall, as its reference level changes.
    private void FollowLevels()
    {
        for (var i = 0; i < scene.Waterfalls.Count; i++)
        {
            if (scene.Waterfalls[i].Display is { } display && SpectrumLinks.Levels(display) is var levels && scene.ZAxis.VisibleRange != levels)
            {
                scene.ZAxis.VisibleRange = levels;
            }
        }
    }
}
