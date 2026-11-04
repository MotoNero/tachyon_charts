using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A test plan over three stations, and a large plan of a test hall.</summary>
public static class GanttDemo
{
    /// <summary>The first day of the plans.</summary>
    public static readonly DateTime Start = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>How to operate a Gantt chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Click on a group: collapse or expand | Pointer: start, end and duration";

    /// <summary>Creates the test plan: three stations with their steps, dependencies and milestones.</summary>
    /// <returns>The plan.</returns>
    public static GanttData TestPlan()
    {
        var data = new GanttData();
        var plan = new (string Station, (string Step, double From, double Days)[] Steps)[]
        {
            ("Climate chamber", [("Prepare", 0, 1.5), ("Cold start", 1.5, 2), ("Heat soak", 3.5, 3), ("Report", 6.5, 1)]),
            ("Vibration", [("Fixture", 2, 1), ("Sine sweep", 3, 2.5), ("Random", 5.5, 3)]),
            ("EMC", [("Setup", 6, 1), ("Emission", 7, 2), ("Immunity", 9, 2.5), ("Report", 11.5, 1)]),
        };
        var previous = -1;
        foreach (var (station, steps) in plan)
        {
            foreach (var (step, from, days) in steps)
            {
                var row = data.AddRow(step, station);
                var task = data.Add(new GanttTask(row, Day(from), Day(from + days), step));
                if (previous >= 0)
                {
                    data.Link(previous, task);
                }

                previous = task;
            }
        }

        var gates = data.AddRow("Milestones");
        data.Add(new GanttTask(gates, Day(6.5), Day(6.5), "Climate done"));
        data.Add(new GanttTask(gates, Day(12.5), Day(12.5), "Release"));
        return data;
    }

    /// <summary>Creates a plan of 100 stations in ten halls with a thousand tests each.</summary>
    /// <returns>The plan.</returns>
    public static GanttData TestHall()
    {
        var data = new GanttData();
        var random = new Random(4); // NOSONAR: makes sample data, no security
        for (var station = 0; station < 100; station++)
        {
            var row = data.AddRow("Station " + (station + 1), "Hall " + ((station / 10) + 1));
            var time = random.NextDouble() * 2;
            for (var test = 0; test < 1000; test++)
            {
                var length = 0.05 + (random.NextDouble() * 0.6);
                data.Add(new GanttTask(row, Day(time), Day(time + length), "Test " + (test + 1)));
                time += length + (random.NextDouble() * 0.2);
            }
        }

        return data;
    }

    private static double Day(double days) => (Start + TimeSpan.FromDays(days)).Ticks;
}
