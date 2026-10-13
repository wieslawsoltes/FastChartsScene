using System.Diagnostics;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

internal static class SeriesCursorBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        using var d = new ChartDocument { ShowLegend = false };
        d.XAxis.Range = new(0, 49999); d.YAxis.Range = new(0, 20);
        for (var i = 0; i < 8; i++) d.Series.Add(new LineSeries { Title = "Channel " + i, Data = new UniformData(0, 1, Enumerable.Repeat(2.0 + i, 50000).ToArray()) });
        var frame = new SceneCompiler().Compile(d, 1000, 600); var input = new ChartInteraction(d);
        var locations = Enumerable.Range(0, 16).Select(i => frame.Layout.ToScreen(d.XAxis, d.YAxis, new PointD(1234.2 + i * 1997, 19))).ToArray();
        List<SeriesCursorValue> values = [];
        var startBytes = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        input.QuerySeriesCursorValues(frame, locations[0], values);
        var coldMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; var coldBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        int Find(LineSeries series, PointD point)
        {
            var best = -1; var dx = double.PositiveInfinity; var dy = double.PositiveInfinity;
            for (var i = 0; i < series.Data.Count; i++)
            {
                var s = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(series.Data.X(i), series.Data.Y(i)));
                var x = Math.Abs(s.X - point.X); var y = Math.Abs(s.Y - point.Y);
                if (x < dx || x == dx && y < dy) { best = i; dx = x; dy = y; }
            }
            return best;
        }
        foreach (var p in locations)
        {
            input.QuerySeriesCursorValues(frame, p, values);
            if (values.Count != 8) throw new InvalidOperationException("Not all series were queried.");
            for (var i = 0; i < 8; i++)
            {
                var s = (LineSeries)d.Series[i]; var original = Find(s, p);
                if (values[i].Index != original || values[i].SeriesId != s.Id || values[i].Data != new PointD(s.Data.X(original), s.Data.Y(original)))
                    throw new InvalidOperationException("All-series result differs from exhaustive source lookup.");
            }
        }
        measure("series-table-exhaustive-8x50k-16-queries", () => {
            double sum = 0; foreach (var p in locations) for (var i = 0; i < 8; i++) sum += Find((LineSeries)d.Series[i], p); return sum;
        });
        measure("series-table-reused-8x50k-16-queries", () => {
            double sum = 0; foreach (var p in locations) { input.QuerySeriesCursorValues(frame, p, values); for (var i = 0; i < values.Count; i++) sum += values[i].Index; } return sum;
        });
        measurements.Add(new { name = "series-table-first-query", series = 8, samplesPerSeries = 50000,
            milliseconds = coldMs, allocatedBytes = coldBytes, indexes = input.CursorValuesIndexBuildCount,
            note = "One first query including output capacity/JIT costs; separate warm medians compare equivalent source values. Excludes rendering, native table and pointer-to-display latency." });
    }
}
