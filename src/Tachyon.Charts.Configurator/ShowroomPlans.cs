using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The plans and mixtures of the showroom pages as sample data: the same tasks, links and random values, so that a page
/// opens with the data it shows.
/// </summary>
internal static class ShowroomPlans
{
    private const string Mixture = "(double[] A, double[] B, double[] C)";
    private static readonly DateTime _start = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        new("testPlan", "Test plan of three stations", DataShape.Gantt, 13, _ => TestPlan(), "GanttData", method => SampleCatalog.Header("test plan of three stations", method, "GanttData", "Not used: the plan is fixed.") + """
                {
                    var start = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
                    double Day(double days) => (start + TimeSpan.FromDays(days)).Ticks;
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
                            var task = data.Add(new GanttTask(data.AddRow(step, station), Day(from), Day(from + days), step));
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

            """) { HasCount = false },
        new("testHall", "A test hall of 100 stations", DataShape.Gantt, 100_000, TestHall, "GanttData", method => SampleCatalog.Header("a test hall of 100 stations", method, "GanttData", "The number of tests, spread over the stations.") + """
                {
                    var start = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
                    double Day(double days) => (start + TimeSpan.FromDays(days)).Ticks;
                    var data = new GanttData();
                    var random = new Random(4);
                    for (var station = 0; station < 100; station++)
                    {
                        var row = data.AddRow("Station " + (station + 1), "Hall " + ((station / 10) + 1));
                        var time = random.NextDouble() * 2;
                        for (var test = 0; test < count / 100; test++)
                        {
                            var length = 0.05 + (random.NextDouble() * 0.6);
                            data.Add(new GanttTask(row, Day(time), Day(time + length), "Test " + (test + 1)));
                            time += length + (random.NextDouble() * 0.2);
                        }
                    }

                    return data;
                }

            """) { MinimumCount = 100 },
        new("randomMixture", "Random mixtures of three parts", DataShape.Ternary, 300, RandomMixture, Mixture, method => SampleCatalog.Header("random mixtures of three parts", method, Mixture) + """
                {
                    var random = new Random(2);
                    var (a, b, c) = (new double[count], new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        a[i] = random.NextDouble();
                        b[i] = random.NextDouble();
                        c[i] = random.NextDouble();
                    }

                    return (a, b, c);
                }

            """),
    ];

    private static double Day(double days) => (_start + TimeSpan.FromDays(days)).Ticks;

    private static GanttData TestPlan()
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
                var task = data.Add(new GanttTask(data.AddRow(step, station), Day(from), Day(from + days), step));
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

    private static GanttData TestHall(int count)
    {
        var data = new GanttData();
        var random = new Random(4); // NOSONAR: makes sample data, no security
        for (var station = 0; station < 100; station++)
        {
            var row = data.AddRow("Station " + (station + 1), "Hall " + ((station / 10) + 1));
            var time = random.NextDouble() * 2;
            for (var test = 0; test < count / 100; test++)
            {
                var length = 0.05 + (random.NextDouble() * 0.6);
                data.Add(new GanttTask(row, Day(time), Day(time + length), "Test " + (test + 1)));
                time += length + (random.NextDouble() * 0.2);
            }
        }

        return data;
    }

    private static object RandomMixture(int count)
    {
        var random = new Random(2); // NOSONAR: makes sample data, no security
        var (a, b, c) = (new double[count], new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            a[i] = random.NextDouble();
            b[i] = random.NextDouble();
            c[i] = random.NextDouble();
        }

        return (a, b, c);
    }
}
