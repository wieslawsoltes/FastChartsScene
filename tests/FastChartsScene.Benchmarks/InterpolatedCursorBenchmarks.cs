using System.Diagnostics;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

internal static class InterpolatedCursorBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        foreach (var ordered in new[] { true, false })
        {
            using var document = new ChartDocument { ShowLegend = false };
            document.XAxis.Range = new(0, 49999); document.YAxis.Range = new(0, 20);
            for (var series = 0; series < 8; series++)
            {
                var xs = Enumerable.Range(0, 50000).Select(i => (double)(ordered ? i : i ^ 1)).ToArray();
                var ys = xs.Select(x => 2 + series + .5 * Math.Sin(x * .01)).ToArray();
                document.Series.Add(new LineSeries { Title = "Channel " + series, Data = new XyData(xs, ys) });
            }
            var frame = new SceneCompiler().Compile(document, 1000, 600);
            var input = new ChartInteraction(document);
            var points = Enumerable.Range(0, 16).Select(i => frame.Layout.ToScreen(document.XAxis, document.YAxis,
                new PointD(1234.2 + i * 1997, 19))).ToArray();
            List<SeriesCursorValue> actual = [], expected = [];
            var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.GetTimestamp();
            input.QuerySeriesCursorValues(frame, points[0], actual, SeriesCursorTrackingMode.Interpolated);
            var milliseconds = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

            // Independent flat traversal of every original adjacent segment. This
            // fixture has finite, non-vertical linear edges and interior non-knot X.
            // It deliberately does not share binary search or retained bounds code.
            void Exhaustive(PointD point)
            {
                expected.Clear();
                for (var s = 0; s < document.Series.Count; s++)
                {
                    var line = (LineSeries)document.Series[s]; var data = line.Data;
                    var nearest = double.PositiveInfinity; SeriesCursorValue? best = null;
                    for (var i = 0; i + 1 < data.Count; i++)
                    {
                        var a = new PointD(data.X(i), data.Y(i)); var b = new PointD(data.X(i + 1), data.Y(i + 1));
                        var pa = frame.Layout.ToScreen(document.XAxis, document.YAxis, a);
                        var pb = frame.Layout.ToScreen(document.XAxis, document.YAxis, b);
                        if (point.X < Math.Min(pa.X, pb.X) || point.X > Math.Max(pa.X, pb.X)) continue;
                        var t = (point.X - pa.X) / (pb.X - pa.X);
                        var py = pa.Y * (1 - t) + pb.Y * t; var distance = Math.Abs(py - point.Y);
                        if (distance >= nearest) continue;
                        nearest = distance;
                        var result = new PointD(document.XAxis.FromUnit((point.X - frame.Layout.Plot.X) / frame.Layout.Plot.Width),
                            document.YAxis.FromUnit((frame.Layout.Plot.Bottom - py) / frame.Layout.Plot.Height));
                        best = new SeriesCursorValue(line.Id, line.Title, line.Color, i, a, line.XAxisId, line.YAxisId, null, null)
                        { Interpolation = new(result, i + 1, b, LineInterpolation.Linear) };
                    }
                    if (best is not { } found) throw new InvalidOperationException("Benchmark has no visible line crossing.");
                    expected.Add(found);
                }
            }
            foreach (var point in points)
            {
                Exhaustive(point); input.QuerySeriesCursorValues(frame, point, actual, SeriesCursorTrackingMode.Interpolated);
                if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Interpolated cursor differs from exhaustive original-segment provenance/results.");
            }
            static double Checksum(List<SeriesCursorValue> values)
            {
                double sum = 0;
                for (var i = 0; i < values.Count; i++) sum += values[i].Index + values[i].ResultData.X + values[i].ResultData.Y;
                return sum;
            }
            var label = ordered ? "ordered" : "local-freeform";
            measure($"interpolated-{label}-exhaustive-8x50k-16", () => {
                double sum = 0; foreach (var point in points) { Exhaustive(point); sum += Checksum(expected); } return sum;
            });
            measure($"interpolated-{label}-reused-8x50k-16", () => {
                double sum = 0; foreach (var point in points) { input.QuerySeriesCursorValues(frame, point, actual, SeriesCursorTrackingMode.Interpolated); sum += Checksum(actual); } return sum;
            });
            measurements.Add(new { name = $"interpolated-{label}-first-query", series = 8, samplesPerSeries = 50000,
                milliseconds, allocatedBytes = bytes, indexBuilds = input.CursorInterpolationIndexBuildCount,
                lastTestedSegments = input.LastCursorValuesTests,
                note = "One cold query with output/index/JIT costs, then separate warm 16-query batches. Full original endpoint/result equality checked first. Freeform swaps adjacent X pairs; not a worst-case all-crossing workload. Excludes rendering/native rows; no GPU/FPS/reference-product claim." });
        }
    }
}
