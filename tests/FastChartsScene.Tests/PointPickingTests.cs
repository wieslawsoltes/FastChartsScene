using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterPointPickingTests()
    {
        Add("spatial sample queries match exhaustive screen-distance search", () => {
            var random = new Random(637);
            var points = Enumerable.Range(0, 12000).Select(i => new PointD(random.NextDouble() * 20 - 2,
                i % 71 == 0 ? double.NaN : random.NextDouble() * 20 - 2)).ToArray();
            var data = XyData.FromPoints(points); var index = new PointSpatialIndex(data);
            for (var mode = 0; mode < 32; mode++)
            {
                var x = new Axis("x") { Range = new(.1, 14), Inverted = (mode & 1) != 0 };
                var y = new Axis("y") { Range = new(.2, 15), Inverted = (mode & 2) != 0 };
                if ((mode & 4) != 0) x.Scale = AxisScale.Logarithmic;
                if ((mode & 8) != 0) y.Scale = AxisScale.Logarithmic;
                var transpose = (mode & 16) != 0; var pane = new RectD(31, 42, 700, 470);
                for (var query = 0; query < 16; query++)
                {
                    var location = new PointD(pane.X + random.NextDouble() * pane.Width, pane.Y + random.NextDouble() * pane.Height);
                    var result = index.Nearest(x, y, pane, location, 18, transpose);
                    var expected = -1; var distance = 18.0;
                    for (var i = 0; i < data.Count; i++)
                    {
                        var px = transpose ? data.Y(i) : data.X(i); var py = transpose ? data.X(i) : data.Y(i);
                        if (!double.IsFinite(px) || !double.IsFinite(py) || px < x.Range.Minimum || px > x.Range.Maximum || py < y.Range.Minimum || py > y.Range.Maximum) continue;
                        var sx = pane.X + x.ToUnit(px) * pane.Width; var sy = pane.Bottom - y.ToUnit(py) * pane.Height;
                        var d = Math.Sqrt((sx - location.X) * (sx - location.X) + (sy - location.Y) * (sy - location.Y));
                        if (d <= distance) { distance = d; expected = i; }
                    }
                    Equal(expected, result.Hit?.Index ?? -1);
                    if (result.Hit is { } hit) { Equal(distance, hit.Distance); True(hit.Data == points[hit.Index]); }
                }
            }
        });
        Add("duplicate sample queries deterministically select lowest original index", () => {
            var data = new UniformData(1, double.Epsilon, Enumerable.Repeat(1.0, 10000).ToArray());
            var index = new PointSpatialIndex(data); var x = new Axis("x") { Range = new(0, 2) }; var y = new Axis("y") { Range = new(0, 2) };
            var result = index.Nearest(x, y, new(0, 0, 100, 100), new(50, 50), 0);
            Equal(0, result.Hit!.Value.Index); True(result.TestedSamples <= 32);
        });
        Add("spatial queries omit NaNs and offscreen centers without allocating", () => {
            var data = new UniformData(0, 1, [double.NaN, 1, 2, double.NaN, 1]); var index = new PointSpatialIndex(data);
            var x = new Axis("x") { Range = new(1, 3) }; var y = new Axis("y") { Range = new(0, 3) }; var pane = new RectD(0, 0, 200, 200);
            index.Nearest(x, y, pane, new(100, 100));
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) index.Nearest(x, y, pane, new(100, 100));
            Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
            True(index.Nearest(x, y, pane, new(201, 100), 100).Hit == null);
            Equal(3, index.FiniteCount);
        });
        Add("indexed random-cloud queries prune most samples and preserve source order", () => {
            var random = new Random(231); var points = Enumerable.Range(0, 100000).Select(_ => new PointD(random.NextDouble(), random.NextDouble())).ToArray();
            var data = XyData.FromPoints(points); var index = new PointSpatialIndex(data);
            var x = new Axis("x") { Range = new(0, 1) }; var y = new Axis("y") { Range = new(0, 1) };
            var result = index.Nearest(x, y, new(0, 0, 1000, 1000), new(501, 499));
            True(result.Hit != null && result.TestedSamples < 1024, $"Compared {result.TestedSamples} samples.");
            for (var i = 0; i < points.Length; i++) True(points[i] == new PointD(data.X(i), data.Y(i)));
        });
        Add("empty and all-gap point indexes return empty results and validate arguments", () => {
            var x = new Axis("x"); var y = new Axis("y"); var pane = new RectD(0, 0, 100, 100);
            foreach (var values in new[] { Array.Empty<double>(), new[] { double.NaN, double.NaN } })
            {
                var index = new PointSpatialIndex(new UniformData(0, 1, values));
                Equal(0, index.NodeCount); True(index.Nearest(x, y, pane, new(50, 50)).Hit == null);
                Throws<ArgumentOutOfRangeException>(() => index.Nearest(x, y, pane, new(50, 50), -1));
                Throws<ArgumentOutOfRangeException>(() => index.Nearest(x, y, default, new(50, 50)));
            }
        });
        Add("sample cursor respects topmost ties transparency legends and viewport", () => {
            using var d = LineDocument(); var original = (LineSeries)d.Series[0];
            var second = new LineSeries { Data = original.Data }; d.Series.Add(second);
            var compiler = new SceneCompiler(); var frame = compiler.Compile(d, 800, 600); var input = new ChartInteraction(d);
            var point = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2, -1));
            True(input.HitTestData(frame, point)!.SeriesId == second.Id);
            second.Color = second.Color.WithAlpha(0); frame = compiler.Compile(d, 800, 600);
            True(input.HitTestData(frame, point)!.SeriesId == original.Id);
            d.ShowLegend = true; frame = compiler.Compile(d, 800, 600); var legend = frame.Metadata.Hits[0].Bounds;
            True(input.HitTestData(frame, new(legend.X + 2, legend.Y + 2), 10000) == null);
            d.Dispose(); Throws<ObjectDisposedException>(() => input.HitTestData(frame, point));
        });
        Add("cursor reuses source index across axes and rebuilds for new source identity", () => {
            using var d = LineDocument(); var line = (LineSeries)d.Series[0];
            var random = new Random(878); line.Data = XyData.FromPoints(Enumerable.Range(0, 10000).Select(_ => new PointD(random.NextDouble() * 4, random.NextDouble() * 4 - 2)));
            var compiler = new SceneCompiler(); var f = compiler.Compile(d, 800, 600); var input = new ChartInteraction(d);
            input.HitTestData(f, new(300, 300)); Equal(1, input.SampleIndexBuildCount);
            d.XAxis.Range = new(.5, 3.5); input.HitTestData(compiler.Compile(d, 900, 650), new(300, 300)); Equal(1, input.SampleIndexBuildCount);
            input.ClearPickingCache(); input.HitTestData(compiler.Compile(d, 900, 650), new(300, 300)); Equal(2, input.SampleIndexBuildCount);
            line.Data = XyData.FromPoints(Enumerable.Range(0, 5000).Select(i => new PointD(i % 2, i * .0001)));
            input.HitTestData(compiler.Compile(d, 900, 650), new(300, 300)); Equal(3, input.SampleIndexBuildCount);
        });
        Add("ordered streaming and horizontal samples avoid unnecessary full-index builds", () => {
            using var d = LineDocument(); var bar = new BarSeries { Horizontal = true, Data = new UniformData(0, .0002, Enumerable.Repeat(2.0, 10000).ToArray()) };
            d.Series.Clear(); d.Series.Add(bar); d.YAxis.Range = new(0, 2);
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
            var screen = f.Layout.ToScreen(d.XAxis, d.YAxis, new(2, 1));
            var hit = input.HitTestData(f, screen); True(hit != null); Equal(1, hit!.Data.X); Equal(2, hit.Data.Y);
            True(input.LastSampleQueryTests < 1000); Equal(0, input.SampleIndexBuildCount);
        });
    }
}
