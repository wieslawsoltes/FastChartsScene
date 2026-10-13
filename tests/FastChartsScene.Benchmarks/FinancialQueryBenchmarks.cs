using System.Diagnostics;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

internal static class FinancialQueryBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        SeriesCursorBenchmarks.Run(measure, measurements);
        FinancialXBenchmarks.Run(measure, measurements);
        using var document = new ChartDocument { ShowLegend = false };
        document.XAxis.Range = new(0, 10); document.YAxis.Range = new(0, 20);
        var source = Enumerable.Range(0, 50000).Select(i => new OhlcPoint((i * 7919L) % 50000 / 5000.0,
            10, 16, 8, 10 + (i * 379L % 49999) / 25000.0, i)).ToArray();
        var series = new FinancialSeries(); series.SetData(source); document.Series.Add(series);
        var frame = new SceneCompiler().Compile(document, 1000, 600);
        var input = new ChartInteraction(document);
        var locations = Enumerable.Range(0, 16).Select(i => frame.Layout.ToScreen(document.XAxis, document.YAxis,
            new PointD(source[i * 977].X, source[i * 977].Close))).ToArray();
        // Report cold construction independently: do not hide source-view/index work
        // behind the exhaustive-equivalence warmup below.
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        var cold = input.HitTestFinancial(frame, locations[0]);
        var coldMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var coldBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (cold == null) throw new InvalidOperationException("Initial financial query returned no sample.");
        (int Index, double Distance) Previous(PointD p)
        {
            var best = -1; var distance = 12.0;
            for (var i = 0; i < source.Length; i++)
            {
                var screen = frame.Layout.ToScreen(document.XAxis, document.YAxis, new(source[i].X, source[i].Close));
                var dx = screen.X - p.X; var dy = screen.Y - p.Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < distance || best < 0 && d == distance) { best = i; distance = d; }
            }
            return (best, distance);
        }
        foreach (var p in locations)
        {
            var expected = Previous(p); var actual = input.HitTestFinancial(frame, p);
            if (actual == null || expected.Index != actual.Index || expected.Distance != actual.Distance || actual.Sample != source[actual.Index])
                throw new InvalidOperationException("Financial query results do not match exhaustive original-price lookup.");
        }
        measure("financial-cursor-exhaustive-50k-16-queries", () => locations.Sum(p => (double)Previous(p).Index));
        measure("financial-cursor-indexed-50k-16-queries", () => locations.Sum(p => (double)(input.HitTestFinancial(frame, p)?.Index ?? -1)));
        measurements.Add(new { name = "financial-cursor-first-query", candles = source.Length,
            milliseconds = coldMilliseconds, allocatedBytes = coldBytes,
            note = "One cold query including immutable price-view creation, index construction and returned record. Scene compilation and fixture creation excluded; not a median or hardware-GPU result." });
        measurements.Add(new { name = "financial-cursor-workload", candles = source.Length, queries = locations.Length,
            input.FinancialIndexBuildCount, input.LastFinancialQueryTests,
            note = "Warm original-price cursor lookup against an equivalent exhaustive oracle. Index construction and scene rendering are excluded; not chart FPS or hardware/reference-product throughput." });
    }
}
