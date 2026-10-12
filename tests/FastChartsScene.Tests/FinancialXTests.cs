using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static IndexedPointHit? XOracle(IDataSource data, Axis x, Axis y, RectD plot, PointD location,
        double radius = double.MaxValue, bool transpose = false)
    {
        IndexedPointHit? best = null; var horizontal = radius; var vertical = double.PositiveInfinity;
        for (var i = 0; i < data.Count; i++)
        {
            var raw = new PointD(data.X(i), data.Y(i)); var px = transpose ? raw.Y : raw.X; var py = transpose ? raw.X : raw.Y;
            if (!raw.IsFinite || px < x.Range.Minimum || px > x.Range.Maximum || py < y.Range.Minimum || py > y.Range.Maximum) continue;
            var screen = new PointD(plot.X + x.ToUnit(px) * plot.Width, plot.Bottom - y.ToUnit(py) * plot.Height);
            if (!screen.IsFinite) continue;
            var dx = Math.Abs(screen.X - location.X); var dy = Math.Abs(screen.Y - location.Y);
            if (dx > horizontal || dx == horizontal && dy >= vertical && best != null) continue;
            horizontal = dx; vertical = dy;
            best = new(i, raw, screen, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }
    private static void EqualXHit(IndexedPointHit? expected, IndexedPointHit? actual)
    {
        Equal(expected?.Index ?? -1, actual?.Index ?? -1);
        if (expected is { } e && actual is { } a)
        { True(e.Data == a.Data && e.Screen == a.Screen); Equal(e.Distance, a.Distance, 1e-8); }
    }
    static partial void RegisterFinancialXTests()
    {
        Add("nearest X index ranks horizontal before vertical while preserving Euclidean distance", () => {
            var data = new XyData([4.9, 5, 5, 5], [10, 1, 12, 12]);
            var x = new Axis("x"); var y = new Axis("y") { Range = new(0, 20) }; var plot = new RectD(10, 10, 1000, 400);
            var point = new PointD(510, 210); var tree = new PointSpatialIndex(data);
            var hit = tree.NearestX(x, y, plot, point).Hit!.Value;
            Equal(2, hit.Index); Equal(40, hit.Distance); Equal(5, hit.Data.X);
            Equal(0, tree.Nearest(x, y, plot, point, 12).Hit!.Value.Index);
            EqualXHit(XOracle(data, x, y, plot, point), hit);
        });
        Add("nearest X index filters both ranges gaps and bounds its horizontal radius", () => {
            var data = new XyData([5, 5, 6, -1], [double.NaN, 100, 8, 8]);
            var tree = new PointSpatialIndex(data); var x = new Axis("x"); var y = new Axis("y"); var plot = new RectD(0, 0, 100, 100);
            Equal(2, tree.NearestX(x, y, plot, new(50, 10), 10).Hit!.Value.Index);
            True(tree.NearestX(x, y, plot, new(50, 10), 9).Hit == null);
            True(tree.NearestX(x, y, plot, new(-1, 10)).Hit == null);
            True(new PointSpatialIndex(new XyData([], [])).NearestX(x, y, plot, new(50, 50)).Hit == null);
        });
        Add("nearest X index matches exhaustive queries for unordered log inverted and transposed data", () => {
            var random = new Random(718);
            var data = new XyData(Enumerable.Range(0, 5000).Select(_ => .1 + random.NextDouble() * 110).ToArray(),
                Enumerable.Range(0, 5000).Select(i => i % 97 == 0 ? double.NaN : .1 + random.NextDouble() * 110).ToArray());
            var tree = new PointSpatialIndex(data); var plot = new RectD(35, 24, 900, 517);
            foreach (var logarithmic in new[] { false, true }) foreach (var inverse in new[] { false, true }) foreach (var transpose in new[] { false, true })
            {
                var x = new Axis("x") { Range = new(1, 100), Scale = logarithmic ? AxisScale.Logarithmic : AxisScale.Linear, Inverted = inverse };
                var y = new Axis("y") { Range = new(2, 80), Scale = logarithmic ? AxisScale.Logarithmic : AxisScale.Linear, Inverted = !inverse };
                for (var i = 0; i < 32; i++)
                {
                    var point = new PointD(plot.X + random.NextDouble() * plot.Width, plot.Y + random.NextDouble() * plot.Height);
                    var radius = i % 2 == 0 ? 0.5 : double.MaxValue;
                    EqualXHit(XOracle(data, x, y, plot, point, radius, transpose), tree.NearestX(x, y, plot, point, radius, transpose).Hit);
                }
            }
        });
        Add("nearest X retains zero-allocation queries and validates options", () => {
            var tree = new PointSpatialIndex(new UniformData(0, 1, Enumerable.Range(0, 10000).Select(i => (double)(i % 10)).ToArray()));
            var x = new Axis("x") { Range = new(0, 9999) }; var y = new Axis("y"); var plot = new RectD(0, 0, 1000, 400); var p = new PointD(401, 25);
            tree.NearestX(x, y, plot, p); var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) tree.NearestX(x, y, plot, p);
            Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
            foreach (var radius in new[] { -1.0, double.NaN, double.PositiveInfinity }) Throws<ArgumentOutOfRangeException>(() => tree.NearestX(x, y, plot, p, radius));
            Throws<ArgumentNullException>(() => tree.NearestX(null!, y, plot, p));
            Throws<ArgumentOutOfRangeException>(() => tree.NearestX(x, y, new(0, 0, 0, 10), p));
            Throws<ArgumentOutOfRangeException>(() => tree.NearestX(x, y, plot, new(double.NaN, 0)));
        });
        Add("mixed financial snap modes never expand the nearest-point radius", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12));
            var distant = new FinancialSeries { CursorSnapMode = FinancialCursorSnapMode.NearestX };
            distant.SetData([new(4, 1, 3, 0, 2)]); d.Series.Add(distant);
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
            var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 14));
            True(input.HitTestFinancial(f, p, 12)!.SeriesId == distant.Id);
        });
        Add("financial nearest X follows a candle far from the pointer without inventing a price", () => {
            using var d = FinancialDocument(new OhlcPoint(2, 8, 12, 6, 10, 42), new(8, 8, 12, 6, 10, 17));
            var series = (FinancialSeries)d.Series[0]; var compiler = new SceneCompiler(); var input = new ChartInteraction(d);
            var f = compiler.Compile(d, 800, 600); var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(2.1, 19));
            True(input.HitTestFinancial(f, p) == null);
            series.CursorSnapMode = FinancialCursorSnapMode.NearestX; f = compiler.Compile(d, 800, 600);
            var hit = input.HitTestFinancial(f, p, 0)!;
            Equal(0, hit.Index); Equal(2, hit.Data.X); Equal(10, hit.Data.Y); Equal(42, hit.Sample.Volume);
            True(hit.Distance > 12 && hit.SnapMode == FinancialCursorSnapMode.NearestX); Equal(0, input.FinancialIndexBuildCount);
            True(series.Points[0] == new OhlcPoint(2, 8, 12, 6, 10, 42));
        });
        Add("ordered nearest X limits source scans and builds no index for isolated coordinates", () => {
            using var d = FinancialDocument(Enumerable.Range(0, 50000).Select(i => new OhlcPoint(i, 10, 16, 8, 12)).ToArray());
            d.XAxis.Range = new(0, 49999); var s = (FinancialSeries)d.Series[0]; s.CursorSnapMode = FinancialCursorSnapMode.NearestX;
            var compiler = new SceneCompiler(); var input = new ChartInteraction(d);
            foreach (var inverted in new[] { false, true })
            {
                d.XAxis.Inverted = inverted; var f = compiler.Compile(d, 1000, 600);
                for (var i = 0; i < 12; i++)
                {
                    var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(20000 + i + .1, 19));
                    Equal(20000 + i, input.HitTestFinancial(f, p)!.Index); True(input.LastFinancialQueryTests <= 2);
                }
            }
            Equal(0, input.FinancialIndexBuildCount);
        });
        Add("nearest X skips clipped closest prices and respects the selected OHLC field", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 25, 30, 24, 26), new(5, 10, 16, 8, 12));
            var s = (FinancialSeries)d.Series[0]; s.CursorSnapMode = FinancialCursorSnapMode.NearestX;
            var compiler = new SceneCompiler(); var input = new ChartInteraction(d);
            foreach (var field in Enum.GetValues<FinancialValueField>())
            {
                s.CursorField = field; var f = compiler.Compile(d, 800, 600); var hit = input.HitTestFinancial(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 1)))!;
                Equal(1, hit.Index); Equal(Price(s.Points[1], field), hit.Data.Y);
            }
            d.YAxis.Range = new(40, 50); var empty = compiler.Compile(d, 800, 600); True(input.HitTestFinancial(empty, new(300, 300)) == null);
        });
        Add("duplicate X price groups use vertical distance then original index and retain the tree", () => {
            using var d = FinancialDocument(Enumerable.Range(0, 5000).Select(i => new OhlcPoint(4, 10, 20, 0, i == 4900 || i == 4950 ? 19 : 1)).ToArray());
            var s = (FinancialSeries)d.Series[0]; s.CursorSnapMode = FinancialCursorSnapMode.NearestX;
            var f = new SceneCompiler().Compile(d, 1000, 600); var input = new ChartInteraction(d); var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 18));
            Equal(4900, input.HitTestFinancial(f, p)!.Index); Equal(1, input.FinancialIndexBuildCount);
            Equal(4900, input.HitTestFinancial(f, p)!.Index); True(input.LastFinancialQueryTests < 100); Equal(1, input.FinancialIndexBuildCount);
            input.ClearPickingCache(); input.HitTestFinancial(f, p); Equal(2, input.FinancialIndexBuildCount);
        });
        Add("ordered and unordered financial nearest X agree with the exact visible-price oracle", () => {
            var random = new Random(513);
            var raw = Enumerable.Range(0, 3000).Select(i => new OhlcPoint(.1 + i % 701 * .15, 10, 20, .01, .2 + random.NextDouble() * 19)).ToArray();
            foreach (var ordered in new[] { false, true }) foreach (var log in new[] { false, true }) foreach (var inverse in new[] { false, true })
            {
                using var d = FinancialDocument(ordered ? raw.OrderBy(p => p.X).ToArray() : raw);
                d.XAxis.Range = new(1, 100); d.YAxis.Range = new(1, 18); d.XAxis.Scale = d.YAxis.Scale = log ? AxisScale.Logarithmic : AxisScale.Linear;
                d.XAxis.Inverted = inverse; d.YAxis.Inverted = !inverse;
                var s = (FinancialSeries)d.Series[0]; s.CursorSnapMode = FinancialCursorSnapMode.NearestX;
                var f = new SceneCompiler().Compile(d, 1000, 600); var input = new ChartInteraction(d); var pane = f.Layout.ForAxis("y");
                for (var i = 0; i < 24; i++)
                {
                    var p = new PointD(pane.X + random.NextDouble() * pane.Width, pane.Y + random.NextDouble() * pane.Height);
                    var expected = XOracle(s.GetPriceData(s.CursorField), d.XAxis, d.YAxis, pane, p); var hit = input.HitTestFinancial(f, p);
                    Equal(expected?.Index ?? -1, hit?.Index ?? -1); if (hit != null) { True(hit.Sample == s.Points[hit.Index]); Equal(expected!.Value.Distance, hit.Distance, 1e-8); }
                }
            }
        });
        Add("financial nearest X keeps geometry unchanged validates enum and clears stale data", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12)); var s = (FinancialSeries)d.Series[0];
            var c = new SceneCompiler(); var old = c.Compile(d, 800, 600); var input = new ChartInteraction(d);
            s.CursorSnapMode = FinancialCursorSnapMode.NearestX; var f = c.Compile(d, 800, 600);
            True(old.Vertices.SequenceEqual(f.Vertices)); True(input.HitTestFinancial(old, new(300, 300)) == null);
            Throws<ArgumentOutOfRangeException>(() => s.CursorSnapMode = (FinancialCursorSnapMode)19); True(s.CursorSnapMode == FinancialCursorSnapMode.NearestX);
            s.CursorTrackEnabled = false; f = c.Compile(d, 800, 600); True(input.HitTestFinancial(f, new(300, 300)) == null);
        });
        Add("financial nearest X confines secondary log inverted tracking to the assigned pane", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12)); var s = (FinancialSeries)d.Series[0]; s.CursorSnapMode = FinancialCursorSnapMode.NearestX;
            var x = new Axis("time") { Range = new(1, 100), Scale = AxisScale.Logarithmic, Inverted = true };
            var y = new Axis("price") { Range = new(1, 20), Scale = AxisScale.Logarithmic, Inverted = true };
            d.XAxes.Add(x); d.YAxes.Add(y); d.AxisLayout = AxisLayout.Stacked; s.XAxisId = x.Id; s.YAxisId = y.Id;
            var f = new SceneCompiler().Compile(d, 1000, 600); var input = new ChartInteraction(d); var p = f.Layout.ToScreen(x, y, new(5, 2));
            var hit = input.HitTestFinancial(f, p)!; Equal(4, hit.Data.X); True(hit.XAxisId == "time" && hit.YAxisId == "price");
            var pane = f.Layout.ForAxis("y"); True(input.HitTestFinancial(f, new(pane.X + 30, pane.Y + 20)) == null);
        });
        Add("financial nearest X extreme adjacent log endpoints preserve exact source identity", () => {
            var min = 1e300; var max = Math.BitIncrement(min);
            using var d = FinancialDocument(new OhlcPoint(min, 10, 16, 8, 12), new(max, 10, 16, 8, 12));
            d.XAxis.Range = new(min, max); d.XAxis.Scale = AxisScale.Logarithmic; var s = (FinancialSeries)d.Series[0]; s.CursorSnapMode = FinancialCursorSnapMode.NearestX;
            foreach (var inverse in new[] { false, true })
            {
                d.XAxis.Inverted = inverse; var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
                for (var i = 0; i < 2; i++)
                { var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(s.Points[i].X, 1)); Equal(i, input.HitTestFinancial(f, p)!.Index); }
            }
        });
    }
}
