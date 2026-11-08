using System.Numerics;
using Tachyon.Charts.Core.Input;

namespace Tachyon.Charts.Medical;

/// <summary>
/// Measures with the tool of a layer, or moves a point of a measurement when the pointer grabs one. Everything else,
/// such as dragging for the window, goes on to the modifiers below. Each finished change is one step of undo.
/// </summary>
internal sealed class MedicalMeasurementModifier(MedicalMeasurementLayer layer) : ChartModifier(60)
{
    private const float GrabReach = 7;
    private const double FreehandStep = 1.5;

    private readonly MedicalMeasurementLayer _layer = layer;
    private MedicalMeasurement? _moving;
    private int _movingIndex;
    private MedicalPoint[] _before = [];
    private MedicalPoint _grabbed;
    private long _pointerId;
    private bool _isDrawing;

    /// <inheritdoc />

    /// <inheritdoc />
    protected override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { } interaction || interaction.Chart.CurrentLayout is not { } layout)
        {
            return ModifierResult.Ignored;
        }

        if (!_layer.TryUnmap(layout, input.Position, out var point))
        {
            return ModifierResult.Ignored;
        }

        if (_moving is not null || _isDrawing)
        {
            return input.PointerId == _pointerId ? Continue(interaction, input, point) : ModifierResult.Ignored;
        }

        // An angle waits for its third point between two presses.
        if (_layer.Tool == MedicalMeasurementKind.Angle && _layer.Draft is { Length: 3 } waiting)
        {
            if (input.Kind == PointerEventKind.Moved)
            {
                _layer.Draft = [waiting[0], waiting[1], point];
                return ModifierResult.Handled;
            }

            if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary)
            {
                Finish(interaction, [waiting[0], waiting[1], point]);
                return ModifierResult.Handled;
            }
        }

        if (input.Kind != PointerEventKind.Pressed || input.ChangedButton != PointerButton.Primary
            || input.Modifiers != InputModifiers.None || !interaction.TryHitPane(input.Position, out _))
        {
            return ModifierResult.Ignored;
        }

        _pointerId = input.PointerId;
        if (_layer.Tool is not null)
        {
            _isDrawing = true;
            _layer.Draft = [point, point];
            return ModifierResult.Capture;
        }

        if (_layer.TryFindPoint(layout, input.Position, GrabReach, out var measurement, out var index))
        {
            (_moving, _movingIndex, _before, _grabbed) = (measurement, index, measurement.Snapshot, point);
            _layer.Select(measurement);
            return ModifierResult.Capture;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (input.Kind != KeyEventKind.Pressed || Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        // Letters choose a tool, so that a host without a tool bar can measure.
        if (input.Modifiers == InputModifiers.None && ToolOf(input.Key) is { } tool)
        {
            _layer.Tool = tool;
            return ModifierResult.Handled;
        }

        if (input.Key == ChartKey.Escape && (_layer.Tool is not null || _layer.Draft is not null))
        {
            (_isDrawing, _layer.Tool) = (false, null);
            return ModifierResult.Handled;
        }

        if (input.Key == ChartKey.Delete && _layer.Selected is { } selected)
        {
            var index = _layer.Measurements.ToList().IndexOf(selected);
            _layer.Remove(selected);
            interaction.History.Record(new ListEdit(_layer, selected, index, isAdded: false));
            return ModifierResult.Handled;
        }

        return ModifierResult.Ignored;
    }

    private ModifierResult Continue(ChartInteraction interaction, in PointerInputEvent input, MedicalPoint point)
    {
        if (_moving is { } moving)
        {
            var points = Moved(moving, _before, _movingIndex, _grabbed, point);
            if (input.Kind == PointerEventKind.Released)
            {
                _layer.Move(moving, points);
                interaction.History.Record(new MoveEdit(_layer, moving, _before, points));
                _moving = null;
                return ModifierResult.Release;
            }

            _layer.Move(moving, points);
            return ModifierResult.Handled;
        }

        var draft = _layer.Draft ?? [point, point];
        var tool = _layer.Tool ?? MedicalMeasurementKind.Distance;
        if (input.Kind == PointerEventKind.Moved)
        {
            _layer.Draft = tool == MedicalMeasurementKind.Freehand
                ? Grown(draft, point)
                : [draft[0], point];
            return ModifierResult.Handled;
        }

        if (input.Kind != PointerEventKind.Released)
        {
            return ModifierResult.Handled;
        }

        _isDrawing = false;
        var finished = tool == MedicalMeasurementKind.Freehand ? Grown(draft, point) : [draft[0], point];
        var isTooSmall = Distance(finished[0], finished[^1]) < 0.5 && tool != MedicalMeasurementKind.Freehand;
        if (isTooSmall || (tool == MedicalMeasurementKind.Freehand && finished.Length < 3))
        {
            _layer.Draft = null;
        }
        else if (tool == MedicalMeasurementKind.Angle)
        {
            // The first arm is drawn; the next press sets the end of the second arm.
            _layer.Draft = [finished[0], finished[1], finished[1]];
        }
        else
        {
            Finish(interaction, finished);
        }

        return ModifierResult.Release;
    }

    private static MedicalMeasurementKind? ToolOf(ChartKey key) => key switch
    {
        ChartKey.D => MedicalMeasurementKind.Distance,
        ChartKey.A => MedicalMeasurementKind.Angle,
        ChartKey.C => MedicalMeasurementKind.Circle,
        ChartKey.R => MedicalMeasurementKind.Rectangle,
        ChartKey.F => MedicalMeasurementKind.Freehand,
        _ => null,
    };

    private void Finish(ChartInteraction interaction, MedicalPoint[] points)
    {
        var tool = _layer.Tool ?? MedicalMeasurementKind.Distance;
        _layer.Draft = null;
        var measurement = _layer.Add(tool, points);
        _layer.Select(measurement);
        interaction.History.Record(new ListEdit(_layer, measurement, _layer.Measurements.Count - 1, isAdded: true));
    }

    // Moving the middle of a circle moves the whole circle; any other point moves alone.
    private static MedicalPoint[] Moved(MedicalMeasurement measurement, MedicalPoint[] before, int index, MedicalPoint grabbed, MedicalPoint now)
    {
        var points = (MedicalPoint[])before.Clone();
        var (dx, dy) = (now.X - grabbed.X, now.Y - grabbed.Y);
        if (measurement.Kind == MedicalMeasurementKind.Circle && index == 0)
        {
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = new MedicalPoint(points[i].X + dx, points[i].Y + dy);
            }

            return points;
        }

        points[index] = new MedicalPoint(before[index].X + dx, before[index].Y + dy);
        return points;
    }

    private static MedicalPoint[] Grown(MedicalPoint[] draft, MedicalPoint point) =>
        Distance(draft[^1], point) >= FreehandStep ? [.. draft, point] : draft;

    private static double Distance(MedicalPoint a, MedicalPoint b) => Vector2.Distance(new Vector2((float)a.X, (float)a.Y), new Vector2((float)b.X, (float)b.Y));

    private sealed class ListEdit(MedicalMeasurementLayer layer, MedicalMeasurement measurement, int index, bool isAdded) : IChartEdit
    {
        public void Undo() => Apply(!isAdded);

        public void Redo() => Apply(isAdded);

        private void Apply(bool add)
        {
            if (add)
            {
                layer.Insert(measurement, index);
            }
            else
            {
                layer.Remove(measurement);
            }
        }
    }

    private sealed class MoveEdit(MedicalMeasurementLayer layer, MedicalMeasurement measurement, MedicalPoint[] before, MedicalPoint[] after) : IChartEdit
    {
        public void Undo() => layer.Move(measurement, before);

        public void Redo() => layer.Move(measurement, after);
    }
}
