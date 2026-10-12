using System.Text.Json.Nodes;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Serialization;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static ChartDocument FinancialDocument(params OhlcPoint[] points)
    {
        var d = new ChartDocument { ShowLegend = false };
        d.XAxis.Range = new(0, 10); d.YAxis.Range = new(0, 20);
        var series = new FinancialSeries { Title = "Original OHLC" }; series.SetData(points); d.Series.Add(series);
        return d;
    }
    private static double Price(OhlcPoint point, FinancialValueField field) => field switch
    { FinancialValueField.Open => point.Open, FinancialValueField.High => point.High, FinancialValueField.Low => point.Low, _ => point.Close };
    static partial void RegisterFinancialCursorTests()
    {
        Add("financial field views preserve exact values source order and old snapshot ownership", () => {
            OhlcPoint[] source = [new(4, 10, 16, 8, 12, 12345), new(2, 5, 9, 3, 6, 42)];
            var series = new FinancialSeries(); series.SetData(source);
            foreach (var field in Enum.GetValues<FinancialValueField>())
            {
                var data = series.GetPriceData(field); True(ReferenceEquals(data, series.GetPriceData(field)));
                Equal(2, data.Count); True(!data.IsOrdered);
                for (var i = 0; i < source.Length; i++) { Equal(source[i].X, data.X(i)); Equal(Price(source[i], field), data.Y(i)); }
            }
            var old = series.GetPriceData(FinancialValueField.Close); source[0] = new(9, 1, 2, 0, 1);
            Equal(4, old.X(0)); Equal(12, old.Y(0));
            series.SetData([new(1, 2, 4, 1, 3)]); Equal(2, old.Count); Equal(12, old.Y(0));
            var current = series.GetPriceData(FinancialValueField.Close); True(!ReferenceEquals(current, old)); True(current.IsOrdered); Equal(1, current.Count);
        });
        Add("financial views allocate no per-call numeric copies and validate field and indices", () => {
            var s = new FinancialSeries(); s.SetData([new(1, 2, 4, 1, 3)]);
            var view = s.GetPriceData(FinancialValueField.High); var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { s.GetPriceData(FinancialValueField.High); view.X(0); view.Y(0); }
            Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
            Throws<ArgumentOutOfRangeException>(() => s.GetPriceData((FinancialValueField)99));
            Throws<ArgumentOutOfRangeException>(() => s.CursorField = (FinancialValueField)(-1));
            True(s.CursorField == FinancialValueField.Close);
            Throws<IndexOutOfRangeException>(() => view.X(-1)); Throws<IndexOutOfRangeException>(() => view.Y(1));
        });
        Add("financial data validation preserves existing cached view atomically", () => {
            var s = new FinancialSeries(); s.SetData([new(2, 2, 4, 1, 3)]);
            var source = s.GetPriceData(FinancialValueField.Low);
            Throws<ArgumentException>(() => s.SetData([new(1, 2, 1, 0, 3)]));
            True(ReferenceEquals(source, s.GetPriceData(FinancialValueField.Low))); Equal(2, source.X(0));
            var notifications = 0;
            s.PropertyChanged += (_, e) => { if (e.PropertyName == "Points") { Equal(7, s.GetPriceData(FinancialValueField.Low).Y(0)); notifications++; } };
            s.SetData([new(3, 8, 10, 7, 9)]); Equal(1, notifications);
        });
        Add("financial cursors return all original OHLC fields and volume for every selected price", () => {
            var raw = new OhlcPoint(4, 10, 16, 8, 12, 12345);
            using var d = FinancialDocument(raw); var s = (FinancialSeries)d.Series[0]; var input = new ChartInteraction(d); var compiler = new SceneCompiler();
            foreach (var field in Enum.GetValues<FinancialValueField>())
            {
                s.CursorField = field; var f = compiler.Compile(d, 800, 600); var data = new PointD(raw.X, Price(raw, field));
                var screen = f.Layout.ToScreen(d.XAxis, d.YAxis, data); var hit = input.HitTestFinancial(f, screen, 0);
                True(hit != null); True(hit!.Sample == raw); True(hit.Data == data && hit.Screen == screen && hit.Field == field);
                True(hit.Title == s.Title && hit.SeriesId == s.Id); Equal(0, hit.Index); Equal(0, hit.Distance);
                True(input.HitTestData(f, screen) == null); Equal(0, input.FinancialIndexBuildCount);
            }
        });
        Add("financial queries never invent interpolated prices between candles", () => {
            using var d = FinancialDocument(new OhlcPoint(2, 8, 12, 6, 10), new(8, 8, 12, 6, 10));
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
            True(input.HitTestFinancial(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(5, 10))) == null);
        });
        Add("financial exact ties prefer top series then lowest original index", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12));
            var top = new FinancialSeries { Title = "Top" }; top.SetData(Enumerable.Repeat(new OhlcPoint(4, 10, 16, 8, 12, 9), 5000).ToArray()); d.Series.Add(top);
            var c = new SceneCompiler(); var f = c.Compile(d, 800, 600); var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 12)); var input = new ChartInteraction(d);
            var hit = input.HitTestFinancial(f, p, 0)!; True(hit.SeriesId == top.Id); Equal(0, hit.Index); Equal(9, hit.Sample.Volume);
            Equal(1, input.FinancialIndexBuildCount); True(input.LastFinancialQueryTests < 64);
            top.CursorTrackEnabled = false; f = c.Compile(d, 800, 600); True(input.HitTestFinancial(f, p)!.SeriesId == d.Series[0].Id);
        });
        Add("financial picking respects visibility tracking and invisible OHLC strokes", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12)); var series = (FinancialSeries)d.Series[0];
            var c = new SceneCompiler(); var input = new ChartInteraction(d);
            FinancialDataHit? Query() { var f = c.Compile(d, 800, 600); return input.HitTestFinancial(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 12))); }
            True(Query() != null); series.Visible = false; True(Query() == null); series.Visible = true;
            series.CursorTrackEnabled = false; True(Query() == null); series.CursorTrackEnabled = true;
            series.Style = FinancialStyle.Ohlc; series.StrokeWidth = 0; True(Query() == null);
            series.Style = FinancialStyle.Candlestick; True(Query() != null); // Filled body remains visible.
        });
        Add("financial query rejects stale frames legends off-plot and invalid parameters", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12)); var c = new SceneCompiler(); var f = c.Compile(d, 800, 600);
            var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 12)); var input = new ChartInteraction(d);
            d.Title = "changed"; True(input.HitTestFinancial(f, p) == null);
            d.ShowLegend = true; f = c.Compile(d, 800, 600); var legend = f.Metadata.Hits.First(h => h.Kind == "legend").Bounds;
            True(input.HitTestFinancial(f, new(legend.X + 2, legend.Y + 2), 10000) == null);
            True(input.HitTestFinancial(f, new(0, 0), 10000) == null);
            foreach (var tolerance in new[] { -1.0, double.NaN, double.PositiveInfinity }) Throws<ArgumentOutOfRangeException>(() => input.HitTestFinancial(f, p, tolerance));
            Throws<ArgumentOutOfRangeException>(() => input.HitTestFinancial(f, new(double.NaN, 2)));
            Throws<ArgumentNullException>(() => input.HitTestFinancial(null!, p));
            d.Dispose(); Throws<ObjectDisposedException>(() => input.HitTestFinancial(f, p));
        });
        Add("financial queries use inverted logarithmic secondary axis bindings and exact clipping", () => {
            using var d = FinancialDocument(new OhlcPoint(2, 2, 8, .5, 4)); var s = (FinancialSeries)d.Series[0];
            var x = new Axis("time") { Range = new(.1, 10), Scale = AxisScale.Logarithmic, Inverted = true };
            var y = new Axis("price") { Range = new(1, 10), Scale = AxisScale.Logarithmic, Inverted = true };
            d.XAxes.Add(x); d.YAxes.Add(y); s.XAxisId = x.Id; s.YAxisId = y.Id;
            d.XAxis.Range = new(100, 200); d.YAxis.Range = new(100, 200);
            var c = new SceneCompiler(); var f = c.Compile(d, 900, 600); var input = new ChartInteraction(d);
            var hit = input.HitTestFinancial(f, f.Layout.ToScreen(x, y, new(2, 4)), 0)!;
            True(hit.XAxisId == "time" && hit.YAxisId == "price"); Equal(4, hit.Sample.Close); Equal(.5, hit.Sample.Low);
            s.CursorField = FinancialValueField.Low; f = c.Compile(d, 900, 600);
            True(input.HitTestFinancial(f, hit.Screen, 10000) == null);
        });
        Add("financial cursor is confined to its assigned stacked pane", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12));
            d.AxisLayout = AxisLayout.Stacked; d.YAxes.Add(new Axis("bottom") { Range = new(0, 20) });
            var c = new SceneCompiler(); var f = c.Compile(d, 800, 600); var input = new ChartInteraction(d);
            var bottom = f.Layout.ForAxis("bottom"); True(input.HitTestFinancial(f, new(bottom.X + 100, bottom.Y + bottom.Height / 2), 10000) == null);
            var hit = input.HitTestFinancial(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 12)))!;
            True(hit.PaneBounds == f.Layout.ForAxis("y"));
        });
        Add("ordered financial queries bound candidate scans without building full indexes", () => {
            using var d = FinancialDocument(Enumerable.Range(0, 20000).Select(i => new OhlcPoint(i, 10, 16, 8, 12)).ToArray());
            d.XAxis.Range = new(0, 20000); var f = new SceneCompiler().Compile(d, 1000, 600); var input = new ChartInteraction(d);
            var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(12345, 12)); var hit = input.HitTestFinancial(f, p)!;
            Equal(12345, hit.Index); Equal(0, input.FinancialIndexBuildCount); True(input.LastFinancialQueryTests < 600);
            True(((FinancialSeries)d.Series[0]).GetPriceData(FinancialValueField.Close).IsOrdered);
        });
        Add("unordered financial index survives view edits and invalidates for replacement field and explicit clear", () => {
            var raw = Enumerable.Range(0, 5000).Select(i => new OhlcPoint((i * 7919) % 5000 / 500.0, 10, 16, 8, 12)).ToArray();
            using var d = FinancialDocument(raw); var s = (FinancialSeries)d.Series[0]; var c = new SceneCompiler(); var input = new ChartInteraction(d);
            FinancialDataHit? Query() { var f = c.Compile(d, 800, 600); return input.HitTestFinancial(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, Price(raw[0], s.CursorField)))); }
            True(Query() != null); Equal(1, input.FinancialIndexBuildCount);
            d.Title = "changed"; d.Theme = ChartTheme.Light; d.XAxis.Inverted = true; True(Query() != null); Equal(1, input.FinancialIndexBuildCount);
            s.CursorField = FinancialValueField.High; True(Query() != null); Equal(2, input.FinancialIndexBuildCount);
            s.CursorField = FinancialValueField.Close; True(Query() != null); Equal(2, input.FinancialIndexBuildCount);
            input.ClearPickingCache(); Equal(0, input.LastFinancialQueryTests); True(Query() != null); Equal(3, input.FinancialIndexBuildCount);
            s.SetData(raw); True(Query() != null); Equal(4, input.FinancialIndexBuildCount);
        });
        Add("financial fields match exhaustive nearest-price oracle across transforms", () => {
            var random = new Random(63218);
            var raw = Enumerable.Range(0, 3000).Select(_ => { var o = .3 + random.NextDouble() * 9; var close = .3 + random.NextDouble() * 9;
                return new OhlcPoint(.3 + random.NextDouble() * 9, o, Math.Max(o, close) + .1, Math.Min(o, close) - .1, close, random.Next(500)); }).ToArray();
            using var d = FinancialDocument(raw); var s = (FinancialSeries)d.Series[0]; var compiler = new SceneCompiler(); var input = new ChartInteraction(d);
            d.XAxis.Range = new(.1, 10); d.YAxis.Range = new(.1, 10);
            for (var mode = 0; mode < 8; mode++)
            {
                d.XAxis.Inverted = (mode & 1) != 0; d.YAxis.Inverted = (mode & 2) != 0;
                d.XAxis.Scale = d.YAxis.Scale = (mode & 4) != 0 ? AxisScale.Logarithmic : AxisScale.Linear;
                foreach (var field in Enum.GetValues<FinancialValueField>())
                {
                    s.CursorField = field; var f = compiler.Compile(d, 800, 600); var pane = f.Layout.Plot;
                    for (var q = 0; q < 6; q++)
                    {
                        var p = new PointD(pane.X + random.NextDouble() * pane.Width, pane.Y + random.NextDouble() * pane.Height);
                        var expected = -1; var distance = 18.0;
                        for (var i = 0; i < raw.Length; i++)
                        {
                            var value = Price(raw[i], field); var projected = f.Layout.ToScreen(d.XAxis, d.YAxis, new(raw[i].X, value));
                            var dx = projected.X - p.X; var dy = projected.Y - p.Y; var candidate = Math.Sqrt(dx * dx + dy * dy);
                            if (candidate < distance || expected < 0 && candidate == distance) { expected = i; distance = candidate; }
                        }
                        var hit = input.HitTestFinancial(f, p, 18); Equal(expected, hit?.Index ?? -1);
                        if (hit != null) { True(hit.Sample == raw[expected]); Equal(distance, hit.Distance); }
                    }
                }
            }
        });
        Add("per-series cursor tracking disables XY and regular heatmap queries without hiding geometry", () => {
            using var d = new ChartDocument { ShowLegend = false }; d.YAxis.Range = new(0, 10);
            var line = new LineSeries { Data = new XyData([4], [6]), Marker = MarkerShape.Circle };
            var heat = new HeatmapSeries { Grid = new(1, 1, 1, 8, 1, 8, [5]) }; d.Series.Add(heat); d.Series.Add(line);
            var c = new SceneCompiler(); var f = c.Compile(d, 800, 600); var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 6)); var input = new ChartInteraction(d);
            True(input.HitTestData(f, p) != null); True(input.HitTestScalar(f, p) != null); var before = f.Vertices.ToArray();
            line.CursorTrackEnabled = heat.CursorTrackEnabled = false; f = c.Compile(d, 800, 600);
            True(input.HitTestData(f, p) == null && input.HitTestScalar(f, p) == null); True(before.SequenceEqual(f.Vertices.ToArray()));
            True(line.Visible && heat.Visible);
        });
        Add("financial cursor persistence roundtrips with legacy defaults and validates invalid settings", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12, 12345)); var series = (FinancialSeries)d.Series[0];
            series.CursorField = FinancialValueField.High; series.CursorTrackEnabled = false;
            var json = ChartJson.Write(d); using var copy = ChartJson.Read(json); True(ChartJson.Write(copy) == json);
            var restored = (FinancialSeries)copy.Series[0]; True(!restored.CursorTrackEnabled && restored.CursorField == FinancialValueField.High); True(restored.Points.SequenceEqual(series.Points));
            var legacy = JsonNode.Parse(json)!; var node = legacy["series"]![0]!.AsObject(); node.Remove("cursorTrackEnabled"); node.Remove("cursorField");
            using var old = ChartJson.Read(legacy.ToJsonString()); var oldSeries = (FinancialSeries)old.Series[0]; True(oldSeries.CursorTrackEnabled && oldSeries.CursorField == FinancialValueField.Close);
            node["cursorField"] = "Wrong"; Throws<FormatException>(() => ChartJson.Read(legacy.ToJsonString()));
            node["cursorField"] = "Close"; node["cursorTrackEnabled"] = "false"; Throws<FormatException>(() => ChartJson.Read(legacy.ToJsonString()));
        });
        Add("financial empty views and queries remain safe", () => {
            using var d = FinancialDocument(); var s = (FinancialSeries)d.Series[0];
            foreach (var field in Enum.GetValues<FinancialValueField>()) { Equal(0, s.GetPriceData(field).Count); True(s.GetPriceData(field).IsOrdered); }
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
            True(input.HitTestFinancial(f, new(300, 300)) == null); Equal(0, input.LastFinancialQueryTests);
        });
    }
}
