using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterSeriesCursorTests()
    {
        Add("series table returns all original line and financial values in document order", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12, 12345));
            var line = new LineSeries { Title = "Average", Data = new XyData([3, 5], [5, 7]) }; d.Series.Add(line);
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4.2, 19)), rows);
            Equal(2, rows.Count); True(rows[0].SeriesId == d.Series[0].Id && rows[1].SeriesId == line.Id);
            Equal(12345, rows[0].Candle!.Value.Volume); True(rows[0].Field == FinancialValueField.Close);
            Equal(5, rows[1].Data.X); Equal(7, rows[1].Data.Y); Equal(1, rows[1].Index); True(rows[1].Candle == null);
            // Single-cursor proximity stays independent from the all-series X table.
            True(input.HitTestFinancial(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4.2, 19))) == null);
        });
        Add("series table respects per-series tracking visibility geometry and selected field", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12));
            var financial = (FinancialSeries)d.Series[0]; financial.CursorField = FinancialValueField.Low;
            var line = new LineSeries { Data = new XyData([4], [5]) }; d.Series.Add(line);
            var c = new SceneCompiler(); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            void Query() { var f = c.Compile(d, 800, 600); input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 19)), rows); }
            Query(); Equal(8, rows[0].Data.Y); line.CursorTrackEnabled = false; Query(); Equal(1, rows.Count);
            line.CursorTrackEnabled = true; line.ShowLine = false; Query(); Equal(1, rows.Count);
            line.Marker = MarkerShape.Circle; Query(); Equal(2, rows.Count);
            financial.Visible = false; Query(); Equal(1, rows.Count); True(rows[0].SeriesId == line.Id);
            line.Color = new(1, 0, 0, 0); Query(); Equal(0, rows.Count);
        });
        Add("series table uses each secondary logarithmic inverted axis and the hovered pane", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12));
            d.AxisLayout = AxisLayout.Stacked;
            var y = new Axis("secondary") { Range = new(1, 100), Scale = AxisScale.Logarithmic, Inverted = true }; d.YAxes.Add(y);
            var x = new Axis("time") { Range = new(1, 100), Scale = AxisScale.Logarithmic, Inverted = true }; d.XAxes.Add(x);
            var line = new LineSeries { XAxisId = x.Id, YAxisId = y.Id, Data = new XyData([2, 10], [5, 50]) }; d.Series.Add(line);
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(x, y, new(10, 80)), rows);
            Equal(1, rows.Count); True(rows[0].SeriesId == line.Id && rows[0].XAxisId == "time"); Equal(50, rows[0].Data.Y);
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 19)), rows);
            Equal(1, rows.Count); True(rows[0].Candle.HasValue);
        });
        Add("series table reuses warmed output and source indexes without per-query allocations", () => {
            using var d = FinancialDocument(Enumerable.Range(0, 4096).Select(i => new OhlcPoint(i * 7919 % 4096 / 410.0, 10, 16, 8, 12)).ToArray());
            d.Series.Add(new LineSeries { Data = new UniformData(0, .001, Enumerable.Repeat(5.0, 10000).ToArray()) });
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            var p = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 19)); input.QuerySeriesCursorValues(f, p, rows);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) input.QuerySeriesCursorValues(f, p, rows);
            Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Equal(1, input.CursorValuesIndexBuildCount);
            input.ClearPickingCache(); input.QuerySeriesCursorValues(f, p, rows); Equal(2, input.CursorValuesIndexBuildCount);
        });
        Add("copied table values survive source replacement and invalid arguments preserve output", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12, 12)); var input = new ChartInteraction(d);
            var f = new SceneCompiler().Compile(d, 800, 600); List<SeriesCursorValue> rows = [];
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 19)), rows); var original = rows[0];
            Throws<ArgumentOutOfRangeException>(() => input.QuerySeriesCursorValues(f, new(double.NaN, 1), rows)); True(rows[0] == original);
            Throws<ArgumentNullException>(() => input.QuerySeriesCursorValues(null!, new(1, 1), rows)); True(rows[0] == original);
            ((FinancialSeries)d.Series[0]).SetData([new(5, 2, 4, 1, 3, 88)]); Equal(12, original.Candle!.Value.Volume);
            input.QuerySeriesCursorValues(f, new(300, 300), rows); Equal(0, rows.Count);
            d.Dispose(); Throws<ObjectDisposedException>(() => input.QuerySeriesCursorValues(f, new(300, 300), rows));
        });
        Add("series table skips NaN clipped prices and unsupported families without interpolation", () => {
            using var d = FinancialDocument();
            d.Series.Add(new LineSeries { Data = new XyData([4, 4, 5, 5], [double.NaN, 100, 12, 12]) });
            d.Series.Add(new BarSeries { Data = new XyData([4], [3]) });
            var f = new SceneCompiler().Compile(d, 800, 600); List<SeriesCursorValue> rows = [];
            new ChartInteraction(d).QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 19)), rows);
            Equal(1, rows.Count); Equal(2, rows[0].Index); Equal(5, rows[0].Data.X);
        });
        Add("series table clears results over legend and outside the plot", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12)); d.ShowLegend = true;
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 19)), rows); Equal(1, rows.Count);
            var legend = f.Metadata.Hits.First(h => h.Kind == "legend").Bounds;
            input.QuerySeriesCursorValues(f, new(legend.X + 1, legend.Y + 1), rows); Equal(0, rows.Count);
            input.QuerySeriesCursorValues(f, new(0, 0), rows); Equal(0, rows.Count);
        });
    }
}
