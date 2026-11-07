using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// Holds the shared position and grayscale window of three orthogonal slice charts and one 3D volume chart.
/// Coordinates are millimetres from the first voxel along the volume's column, row and slice directions.
/// </summary>
public sealed class MedicalMprDisplay
{
    private const double MinimumWindowWidth = 1e-6;

    private Point3D _position;
    private MedicalWindow _window;
    private long _positionVersion;
    private long _segmentationVersion;
    private readonly List<MedicalSegmentationLayer> _segmentations = [];
    private readonly List<MedicalSegmentation> _segmentationSources = [];
    private readonly List<Scene3D> _scenes = [];

    /// <summary>Initializes linked orthogonal views of a regularly spaced medical volume.</summary>
    /// <param name="volume">The volume.</param>
    public MedicalMprDisplay(MedicalVolume volume)
    {
        Source = volume ?? throw new ArgumentNullException(nameof(volume));
        Unit = string.Equals(volume.Modality, "CT", StringComparison.OrdinalIgnoreCase) ? "HU" : "value";
        _window = MedicalImageDisplay.InitialWindow(volume.WindowCenter, volume.WindowWidth, volume.Data.ValueRange);
        var maximum = MaximumPosition;
        _position = new Point3D(maximum.X / 2, maximum.Y / 2, maximum.Z / 2);

        Axial = new MedicalMprSlice(this, new MedicalMprSliceBuffer(volume, MedicalMprPlane.Axial));
        Coronal = new MedicalMprSlice(this, new MedicalMprSliceBuffer(volume, MedicalMprPlane.Coronal));
        Sagittal = new MedicalMprSlice(this, new MedicalMprSliceBuffer(volume, MedicalMprPlane.Sagittal));
        Cine = new MedicalCinePlayer(this);
        Axial.Update(_position);
        Coronal.Update(_position);
        Sagittal.Update(_position);

        Volume = new Volume3D(volume.Data, new DoubleRange(0, maximum.X), new DoubleRange(0, maximum.Y), new DoubleRange(0, maximum.Z))
        {
            Colormap = Colormaps.Grayscale,
            ColorRange = new DoubleRange(_window.Minimum, _window.Maximum),
        };
        MovePlanes(_position);
    }

    /// <summary>Occurs after the shared position changes.</summary>
    public event Action<Point3D>? PositionChanged;

    /// <summary>Occurs after the shared grayscale window changes.</summary>
    public event Action<MedicalWindow>? WindowChanged;

    internal event Action? SegmentationChanged;

    /// <summary>Gets the source volume.</summary>
    public MedicalVolume Source { get; }

    /// <summary>Gets the axial slice.</summary>
    public MedicalMprSlice Axial { get; }

    /// <summary>Gets the coronal slice.</summary>
    public MedicalMprSlice Coronal { get; }

    /// <summary>Gets the sagittal slice.</summary>
    public MedicalMprSlice Sagittal { get; }

    /// <summary>Gets the controller for stepping through slices and automatic playback.</summary>
    public MedicalCinePlayer Cine { get; }

    /// <summary>Gets the 3D volume whose planes follow <see cref="Position"/>.</summary>
    public Volume3D Volume { get; }

    /// <summary>Gets the unit shown for values.</summary>
    public string Unit { get; }

    /// <summary>Gets the segmentation layers in drawing order.</summary>
    public IReadOnlyList<MedicalSegmentation> Segmentations => _segmentationSources;

    /// <summary>Gets the shared position in volume-local millimetres.</summary>
    public Point3D Position => _position;

    /// <summary>Gets the active grayscale window.</summary>
    public MedicalWindow Window => _window;

    internal long PositionVersion => Interlocked.Read(ref _positionVersion);

    internal long SegmentationVersion => Interlocked.Read(ref _segmentationVersion);

    internal IReadOnlyList<MedicalSegmentationLayer> SegmentationLayers => _segmentations;

    private Point3D MaximumPosition => new(
        (Source.Data.Width - 1) * Source.VoxelSpacing.X,
        (Source.Data.Height - 1) * Source.VoxelSpacing.Y,
        (Source.Data.Depth - 1) * Source.VoxelSpacing.Z);

    /// <summary>Sets the shared position in volume-local millimetres, clamped to the volume.</summary>
    /// <param name="position">The position.</param>
    public void SetPosition(Point3D position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "The position must be finite.");
        }

        var maximum = MaximumPosition;
        var clamped = new Point3D(
            Math.Clamp(position.X, 0, maximum.X),
            Math.Clamp(position.Y, 0, maximum.Y),
            Math.Clamp(position.Z, 0, maximum.Z));
        if (_position == clamped)
        {
            return;
        }

        _position = clamped;
        Axial.Update(clamped);
        Coronal.Update(clamped);
        Sagittal.Update(clamped);
        MovePlanes(clamped);
        Interlocked.Increment(ref _positionVersion);
        PositionChanged?.Invoke(clamped);
    }

    /// <summary>Sets the shared grayscale window.</summary>
    /// <param name="center">The value in the middle.</param>
    /// <param name="width">The positive width.</param>
    public void SetWindow(double center, double width) => SetWindow(new MedicalWindow("Custom", center, width));

    /// <summary>Applies a named grayscale window to all four views.</summary>
    /// <param name="window">The window.</param>
    public void ApplyPreset(MedicalWindow window) => SetWindow(window);

    /// <summary>Adds a categorical segmentation to all linked 2D and 3D views.</summary>
    /// <param name="segmentation">The label map and its patient-space geometry.</param>
    public void AddSegmentation(MedicalSegmentation segmentation)
    {
        ArgumentNullException.ThrowIfNull(segmentation);
        if (_segmentations.Any(layer => ReferenceEquals(layer.Source, segmentation)))
        {
            throw new ArgumentException("The segmentation is already part of this display.", nameof(segmentation));
        }

        var layer = new MedicalSegmentationLayer(Source, segmentation);
        _segmentations.Add(layer);
        _segmentationSources.Add(segmentation);
        foreach (var scene in _scenes) layer.AddTo(scene);
        segmentation.Changed += OnSegmentationChanged;
        OnSegmentationChanged();
    }

    /// <summary>Adds the coupled volume to a 3D chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure3D(Chart3DBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var maximum = MaximumPosition;
        chart.Title("Medical MPR")
            .XAxis(0, maximum.X, "mm")
            .YAxis(0, maximum.Y, "mm")
            .ZAxis(0, maximum.Z, "mm");
        chart.AddVolume(Volume);
        foreach (var layer in _segmentations) layer.AddTo(chart.Scene);
        _scenes.Add(chart.Scene);
        chart.Scene.MarkerAdded += OnMarkerAdded;
    }

    private void SetWindow(MedicalWindow window)
    {
        if (!double.IsFinite(window.Center) || !double.IsFinite(window.Width) || window.Width < MinimumWindowWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "The window must have a finite centre and a positive finite width.");
        }

        if (_window == window)
        {
            return;
        }

        _window = window;
        Volume.ColorRange = new DoubleRange(window.Minimum, window.Maximum);
        WindowChanged?.Invoke(window);
    }

    private void OnMarkerAdded(Pick3D pick)
    {
        if (ReferenceEquals(pick.Source, Volume) || _segmentations.Any(layer => layer.Owns(pick.Source)))
        {
            SetPosition(pick.Position);
        }
    }

    private void MovePlanes(Point3D position)
    {
        var maximum = MaximumPosition;
        Volume.SliceX.Offset = ToBox(position.X, maximum.X);
        Volume.SliceY.Offset = ToBox(position.Y, maximum.Y);
        Volume.SliceZ.Offset = ToBox(position.Z, maximum.Z);
    }

    private static double ToBox(double value, double maximum) => ((value / maximum) * 2) - 1;

    private void OnSegmentationChanged()
    {
        foreach (var layer in _segmentations) layer.Refresh3D();
        Interlocked.Increment(ref _segmentationVersion);
        SegmentationChanged?.Invoke();
    }
}
