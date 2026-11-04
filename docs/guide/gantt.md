# Gantt and time lines

```csharp
var plan = new GanttData();
var prepare = plan.AddRow("Prepare", group: "Climate chamber");
var soak = plan.AddRow("Heat soak", group: "Climate chamber");
var gates = plan.AddRow("Milestones");

var first = plan.Add(new GanttTask(prepare, start.Ticks, start.AddDays(1.5).Ticks, "Prepare"));
var second = plan.Add(new GanttTask(soak, start.AddDays(1.5).Ticks, start.AddDays(4.5).Ticks, "Heat soak"));
plan.Link(first, second);                                                   // an arrow from the end of one to the start of the other
plan.Add(new GanttTask(gates, start.AddDays(5).Ticks, start.AddDays(5).Ticks, "Done"));   // end = start: a milestone

chart.Configure(c => c.Title("Test plan")
    .XAxis(a => a.DateTime("time").AutoRange())
    .Gantt(plan)
    .Modifiers(m => m.ZoomPan()));
```

`Gantt` adds the Y axis with the names of the lines from the top. A group gets a line of its own with a bar over the span of all its tasks; a click on that line collapses or expands the group, or call `plan.SetCollapsed("Climate chamber", true)`. Tasks without a color take the palette of the theme by row; set `GanttTask.Color` for your own.

Under the pointer a task lights up and shows its name, start, end and duration. Labels sit inside bars that are wide enough, milestones have theirs beside the diamond.

## Many tasks

Only visible lines and tasks are drawn. Tasks narrower than a pixel that touch merge into one bar, so 100 000 tasks on 100 rows zoom at 60 frames per second in a full-screen window. Writing to `GanttData` is safe from any thread.

In the showroom: *Gantt*, *Gantt, 100 000 tasks*.
