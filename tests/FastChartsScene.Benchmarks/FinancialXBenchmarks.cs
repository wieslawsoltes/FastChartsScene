using System.Diagnostics;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

internal static class FinancialXBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        foreach (var ordered in new[] { true, false })
        {
            using var document = new ChartDocument { ShowLegend = false };
            document.XAxis.Range = new(0, 49999); document.YAxis.Range = new(0, 20);
            var points = Enumerable.Range(0, 50000).Select(i => new OhlcPoint(ordered ? i : i * 7919L % 50000, 10, 16, 8, 12, i)).ToArray();
            var series = new FinancialSeries { CursorSnapMode = FinancialCursorSnapMode.NearestX }; series.SetData(points); document.Series.Add(series);
            var frame = new SceneCompiler().Compile(document, 1000, 600); var input = new ChartInteraction(document);
            var locations = Enumerable.Range(0, 16).Select(i => frame.Layout.ToScreen(document.XAxis, document.YAxis, new PointD(1234.2 + i * 1997, 19))).ToArray();
            int Exhaustive(PointD p)
            {
                var best = -1; var dx = double.PositiveInfinity; var dy = double.PositiveInfinity;
                for (var i = 0; i < points.Length; i++)
                {
                    var screen = frame.Layout.ToScreen(document.XAxis, document.YAxis, new(points[i].X, points[i].Close));
                    var cx = Math.Abs(screen.X - p.X); var cy = Math.Abs(screen.Y - p.Y);
                    if (cx < dx || cx == dx && cy < dy) { best = i; dx = cx; dy = cy; }
                }
                return best;
            }
            var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
            var first = input.HitTestFinancial(frame, locations[0]);
            var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds; var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            foreach (var p in locations)
            {
                var expected = Exhaustive(p); var actual = input.HitTestFinancial(frame, p);
                if (actual == null || actual.Index != expected || actual.Sample != points[expected]) throw new InvalidOperationException("Nearest-X financial query differs from exhaustive lookup.");
            }
            var label = ordered ? "ordered" : "unordered";
            measure($"financial-X-{label}-exhaustive-50k-16", () => locations.Sum(p => (double)Exhaustive(p)));
            measure($"financial-X-{label}-optimized-50k-16", () => locations.Sum(p => (double)input.HitTestFinancial(frame, p)!.Index));
            measurements.Add(new { name = $"financial-X-{label}-cold", candles = points.Length, queries = locations.Length,
                milliseconds, allocatedBytes = bytes, indexBuilds = input.FinancialIndexBuildCount, lastTestedSamples = input.LastFinancialQueryTests,
                note = "One cold query, plus separately measured warm 16-query batches. Cold includes price view/index/result creation; fixture and rendering excluded. Not a cold median, FPS, or hardware/reference-product benchmark." });
        }
    }
}
