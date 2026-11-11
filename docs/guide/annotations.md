# Annotations

In XAML they live under `Chart.Annotations`:

```xml
<t:HorizontalLineAnnotation Y="1.5" />
<t:VerticalLineAnnotation X="3" />
<t:BoxAnnotation />
<t:TextAnnotation X="3" Y="1.5" Text="Start" />
<t:LineAnnotation X1="1" Y1="0" X2="8" Y2="2" />
```

`LineAnnotation` is a free line between two points at any angle. In code it is `new LineAnnotation(xAxis, yAxis)` with `SetEnds(x1, y1, x2, y2)` and an optional `Label` at the second end.

## Editing by mouse

The positions are bindings like any other. With the `AnnotationEdit` block, annotations whose `IsEditable` is set can be edited by mouse, and the new values arrive in the binding:

| Where you drag | What changes |
|---|---|
| Inside, away from the handles | The whole annotation moves; a horizontal line only up and down, a vertical one only sideways |
| An edge of a band | That limit alone |
| An edge or a corner of a box | That edge, or the two edges of the corner |
| An end of a line or an arrow | That end alone, so place and direction change freely |

The handles show as small squares while the pointer is over the annotation. Each drag is one step of the history: Ctrl+Z takes it back, Ctrl+Y makes it again, in the same order as zoom and pan. Delete removes the annotation touched last. Annotations work in the units of the axes, also on instruments: on a spectrum analyzer a band edge moves in hertz and a line in dBm.

`Annotation.EditFinished` reports the end of a drag (and of an undo), for example to save the values; `Edited` reports every step while dragging.

Annotations also appear in every [export](export.md).
