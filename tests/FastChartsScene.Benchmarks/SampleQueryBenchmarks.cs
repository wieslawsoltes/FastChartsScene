using System.Diagnostics;
using FastChartsScene.Core;
using FastChartsScene.Data;

internal static class SampleQueryBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        const int count = 100000, queries = 64;
        var random = new Random(7765);
        var data = XyData.FromPoints(Enumerable.Range(0, count).Select(_ => new PointD(random.NextDouble(), random.NextDouble())));
        var targets = Enumerable.Range(0, queries).Select(_ => new PointD(random.NextDouble() * 1000, random.NextDouble() * 700)).ToArray();
        var x = new Axis("x") { Range = new(0, 1) }; var y = new Axis("y") { Range = new(0, 1) }; var plot = new RectD(0, 0, 1000, 700);
        var started = Stopwatch.GetTimestamp(); var allocated = GC.GetAllocatedBytesForCurrentThread();
        var index = new PointSpatialIndex(data);
        var buildBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var buildMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        int Reference(PointD target)
        {
            var best = -1; var distance = 12.0;
            for (var i = 0; i < data.Count; i++)
            {
                var dx = x.ToUnit(data.X(i)) * plot.Width - target.X;
                var dy = plot.Bottom - y.ToUnit(data.Y(i)) * plot.Height - target.Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }
        var tested = 0;
        foreach (var target in targets)
        {
            var result = index.Nearest(x, y, plot, target); tested += result.TestedSamples;
            if (Reference(target) != (result.Hit?.Index ?? -1)) throw new InvalidOperationException("Indexed sample query differs from exhaustive reference.");
        }
        double Exhaustive() { double checksum = 0; foreach (var target in targets) checksum += Reference(target); return checksum; }
        double Indexed() { double checksum = 0; foreach (var target in targets) checksum += index.Nearest(x, y, plot, target).Hit?.Index ?? -1; return checksum; }
        measure("sample-cursors-exhaustive-64x100k", Exhaustive);
        measure("sample-cursors-indexed-64x100k", Indexed);
        measurements.Add(new { name = "sample-index-construction", sourceSamples = count, queries,
            buildMilliseconds, buildAllocatedBytes = buildBytes, nodeCount = index.NodeCount, testedSamples = tested });
        Console.WriteLine(FormattableString.Invariant($"Sample index: build {buildMilliseconds:F3} ms / {buildBytes} B; {tested} sample comparisons for {queries} queries; {index.NodeCount} nodes."));
    }
}
