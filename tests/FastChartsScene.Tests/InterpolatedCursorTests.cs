using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static ChartDocument InterpolatedDocument(IDataSource data, LineInterpolation method = LineInterpolation.Linear)
    {
        var d = new ChartDocument { ShowLegend = false }; d.XAxis.Range = new(0, 10); d.YAxis.Range = new(0, 20);
        d.Series.Add(new LineSeries { Data = data, Interpolation = method, Title = "Trace" }); return d;
    }
    private static List<SeriesCursorValue> InterpolatedValues(ChartDocument d, double x, double y)
    {
        var f = new SceneCompiler().Compile(d, 800, 600); List<SeriesCursorValue> values = [];
        new ChartInteraction(d).QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(x, y)), values, SeriesCursorTrackingMode.Interpolated);
        return values;
    }
    // Same immutable coordinates, but deliberately force the freeform bounds path.
    private sealed class FreeformCursorData(IDataSource source) : IDataSource
    {
        public int Count => source.Count;
        public bool IsOrdered => false;
        public double X(int i) => source.X(i);
        public double Y(int i) => source.Y(i);
    }
    private static void RegisterInterpolatedCursorTests()
    {
        Add("interpolated table retains both original endpoints separately from its calculated value", () => {
            using var d = InterpolatedDocument(new XyData([2, 8], [4, 16]));
            var v = InterpolatedValues(d, 5, 19).Single(); True(v.IsInterpolated); Equal(5, v.ResultData.X); Equal(10, v.ResultData.Y);
            Equal(0, v.Index); True(v.Data == new PointD(2, 4));
            var interpolation = v.Interpolation!.Value; Equal(1, interpolation.EndIndex); True(interpolation.EndData == new PointD(8, 16));
            True(interpolation.Method == LineInterpolation.Linear && v.Candle == null);
            var f = new SceneCompiler().Compile(d, 800, 600); List<SeriesCursorValue> original = [];
            new ChartInteraction(d).QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(5, 19)), original);
            True(!original[0].IsInterpolated && original[0].ResultData == original[0].Data); Equal(1, original[0].Index);
        });
        Add("interpolated exact knots remain original samples including the final boundary", () => {
            using var d = InterpolatedDocument(new XyData([2, 5, 8], [4, 10, 16]));
            for (var i = 0; i < 3; i++) { var v = InterpolatedValues(d, 2 + i * 3, 19).Single(); Equal(i, v.Index); True(!v.IsInterpolated); }
        });
        Add("step before and step after return the correct original plateau values", () => {
            foreach (var method in new[] { LineInterpolation.StepBefore, LineInterpolation.StepAfter })
            {
                using var d = InterpolatedDocument(new XyData([2, 8], [4, 16]), method); var v = InterpolatedValues(d, 5, 19).Single();
                Equal(method == LineInterpolation.StepBefore ? 16 : 4, v.ResultData.Y); True(v.Interpolation!.Value.Method == method);
                True(v.Data == new PointD(2, 4) && v.Interpolation.Value.EndData == new PointD(8, 16));
            }
        });
        Add("step vertical transitions choose the closest visible intersection without forging a sample", () => {
            using var before = InterpolatedDocument(new XyData([2, 8], [4, 16]), LineInterpolation.StepBefore);
            var a = InterpolatedValues(before, 2, 10).Single(); Equal(2, a.ResultData.X); Equal(10, a.ResultData.Y); True(a.IsInterpolated);
            using var after = InterpolatedDocument(new XyData([2, 8], [4, 16]), LineInterpolation.StepAfter);
            var b = InterpolatedValues(after, 8, 10).Single(); Equal(8, b.ResultData.X); Equal(10, b.ResultData.Y); True(b.IsInterpolated);
        });
        Add("duplicate X segments retain original order and exact endpoints win equal distance ties", () => {
            using var d = InterpolatedDocument(new XyData([2, 2, 8], [4, 16, 12]));
            var v = InterpolatedValues(d, 2, 10).Single(); Equal(0, v.Index); Equal(1, v.Interpolation!.Value.EndIndex); Equal(10, v.ResultData.Y);
            v = InterpolatedValues(d, 2, 16).Single(); Equal(1, v.Index); True(!v.IsInterpolated);
        });
        Add("interpolation neither bridges NaN gaps nor extrapolates beyond a finite run", () => {
            using var d = InterpolatedDocument(new XyData([2, 5, 8], [4, double.NaN, 16]));
            Equal(0, InterpolatedValues(d, 3, 19).Count); Equal(0, InterpolatedValues(d, 6, 19).Count);
            Equal(0, InterpolatedValues(d, 1, 19).Count); Equal(0, InterpolatedValues(d, 9, 19).Count);
            var v = InterpolatedValues(d, 8, 19).Single(); Equal(2, v.Index); True(!v.IsInterpolated);
        });
        Add("interpolation handles empty single and all-gap sources", () => {
            using var empty = InterpolatedDocument(new XyData([], [])); Equal(0, InterpolatedValues(empty, 5, 10).Count);
            using var one = InterpolatedDocument(new XyData([5], [10])); True(!InterpolatedValues(one, 5, 19).Single().IsInterpolated);
            Equal(0, InterpolatedValues(one, 6, 19).Count);
            using var gap = InterpolatedDocument(new XyData([2, 8], [double.NaN, double.NaN])); Equal(0, InterpolatedValues(gap, 5, 10).Count);
        });
        Add("freeform intersections select nearest screen Y and reuse retained segment bounds", () => {
            using var d = InterpolatedDocument(new XyData([1, 9, 1, 9], [2, 2, 18, 18]));
            var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            var point = f.Layout.ToScreen(d.XAxis, d.YAxis, new(5, 17.5));
            input.QuerySeriesCursorValues(f, point, rows, SeriesCursorTrackingMode.Interpolated);
            Equal(2, rows.Single().Index); Equal(18, rows[0].ResultData.Y); Equal(1, input.CursorInterpolationIndexBuildCount);
            input.QuerySeriesCursorValues(f, point, rows, SeriesCursorTrackingMode.Interpolated); Equal(1, input.CursorInterpolationIndexBuildCount);
            input.ClearPickingCache(); input.QuerySeriesCursorValues(f, point, rows, SeriesCursorTrackingMode.Interpolated); Equal(2, input.CursorInterpolationIndexBuildCount);
        });
        Add("ordered binary and retained freeform queries agree across gaps steps and duplicate coordinates", () => {
            var random = new Random(7919);
            var raw = new XyData(Enumerable.Range(0, 160).Select(i => i / 2 * .12).ToArray(),
                Enumerable.Range(0, 160).Select(i => i % 19 == 0 ? double.NaN : random.NextDouble() * 30 - 5).ToArray());
            foreach (var method in new[] { LineInterpolation.Linear, LineInterpolation.StepBefore, LineInterpolation.StepAfter })
            foreach (var invert in new[] { false, true })
            {
                using var d = InterpolatedDocument(raw, method); d.XAxis.Inverted = invert; d.YAxis.Inverted = !invert;
                var line = (LineSeries)d.Series[0]; var compiler = new SceneCompiler(); var input = new ChartInteraction(d);
                var points = Enumerable.Range(0, 100).Select(i => new PointD(i % 2 == 0 ? i / 2 * .12 : random.NextDouble() * 10, random.NextDouble() * 20)).ToArray();
                var f = compiler.Compile(d, 800, 600); List<SeriesCursorValue> rows = []; var expected = new SeriesCursorValue[points.Length][];
                for (var i = 0; i < points.Length; i++) { input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, points[i]), rows, SeriesCursorTrackingMode.Interpolated); expected[i] = rows.ToArray(); }
                line.Data = new FreeformCursorData(raw); f = compiler.Compile(d, 800, 600);
                for (var i = 0; i < points.Length; i++)
                {
                    input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, points[i]), rows, SeriesCursorTrackingMode.Interpolated);
                    True(expected[i].SequenceEqual(rows), $"Paths differ at {method}/{invert}/{i}.");
                }
                Equal(1, input.CursorInterpolationIndexBuildCount);
            }
        });
        Add("linear cursor intersections follow logarithmic axis projections and inversion", () => {
            using var d = InterpolatedDocument(new XyData([1, 100], [2, 20]));
            d.XAxis.Range = new(1, 100); d.XAxis.Scale = AxisScale.Logarithmic;
            foreach (var invert in new[] { false, true })
            {
                d.XAxis.Inverted = invert; d.YAxis.Inverted = invert;
                var v = InterpolatedValues(d, 10, 19).Single(); Equal(10, v.ResultData.X); Equal(11, v.ResultData.Y);
            }
            ((LineSeries)d.Series[0]).Data = new XyData([1, 100], [1, 100]); d.YAxis.Range = new(1, 100); d.YAxis.Scale = AxisScale.Logarithmic;
            var log = InterpolatedValues(d, 10, 80).Single(); Equal(10, log.ResultData.Y);
        });
        Add("clipped segment endpoints can produce a visible crossing but invalid log endpoints cannot", () => {
            using var d = InterpolatedDocument(new XyData([2, 8], [-10, 30]));
            Equal(10, InterpolatedValues(d, 5, 19).Single().ResultData.Y); Equal(0, InterpolatedValues(d, 2, 19).Count);
            ((LineSeries)d.Series[0]).Data = new XyData([-1, 8], [4, 16]); d.XAxis.Range = new(1, 10); d.XAxis.Scale = AxisScale.Logarithmic;
            Equal(0, InterpolatedValues(d, 5, 19).Count); True(!InterpolatedValues(d, 8, 19).Single().IsInterpolated);
        });
        Add("scatter-only cubic and financial table results remain explicitly original", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12, 77));
            d.Series.Add(new LineSeries { Data = new XyData([2, 8], [4, 16]), ShowLine = false, Marker = MarkerShape.Circle });
            d.Series.Add(new LineSeries { Data = new XyData([2, 8], [4, 16]), Interpolation = LineInterpolation.MonotoneCubic });
            var rows = InterpolatedValues(d, 5, 19); Equal(3, rows.Count); True(rows.All(v => !v.IsInterpolated)); Equal(77, rows[0].Candle!.Value.Volume);
        });
        Add("interpolated queries honor secondary axes hovered panes and tracking opt out", () => {
            using var d = InterpolatedDocument(new XyData([2, 8], [4, 16])); d.AxisLayout = AxisLayout.Stacked;
            var secondary = new Axis("secondary") { Range = new(1, 100), Scale = AxisScale.Logarithmic, Inverted = true }; d.YAxes.Add(secondary);
            var line = new LineSeries { YAxisId = secondary.Id, Data = new XyData([2, 8], [1, 100]) }; d.Series.Add(line);
            var c = new SceneCompiler(); var f = c.Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, secondary, new(5, 80)), rows, SeriesCursorTrackingMode.Interpolated);
            Equal(1, rows.Count); True(rows[0].SeriesId == line.Id); Equal(10, rows[0].ResultData.Y);
            line.CursorTrackEnabled = false; f = c.Compile(d, 800, 600);
            input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, secondary, new(5, 80)), rows, SeriesCursorTrackingMode.Interpolated); Equal(0, rows.Count);
        });
        Add("warmed ordered and freeform interpolated queries allocate no managed heap results", () => {
            foreach (var freeform in new[] { false, true })
            {
                IDataSource data = new UniformData(0, .001, Enumerable.Range(0, 10000).Select(i => 10 + Math.Sin(i * .01)).ToArray());
                if (freeform) data = new FreeformCursorData(data);
                using var d = InterpolatedDocument(data); var f = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
                var point = f.Layout.ToScreen(d.XAxis, d.YAxis, new(4.3215, 19));
                for (var i = 0; i < 10; i++) input.QuerySeriesCursorValues(f, point, rows, SeriesCursorTrackingMode.Interpolated);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 100; i++) input.QuerySeriesCursorValues(f, point, rows, SeriesCursorTrackingMode.Interpolated);
                Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); True(input.LastCursorValuesTests <= 16);
                Equal(freeform ? 1 : 0, input.CursorInterpolationIndexBuildCount);
            }
        });
        Add("invalid interpolation mode preserves output and copied endpoints survive source replacement", () => {
            using var d = InterpolatedDocument(new XyData([2, 8], [4, 16])); var f = new SceneCompiler().Compile(d, 800, 600);
            var input = new ChartInteraction(d); var rows = InterpolatedValues(d, 5, 19); var copy = rows[0];
            Throws<ArgumentOutOfRangeException>(() => input.QuerySeriesCursorValues(f, new(300, 300), rows, (SeriesCursorTrackingMode)99)); True(rows[0] == copy);
            ((LineSeries)d.Series[0]).Data = new XyData([1, 9], [1, 2]); True(copy.Data == new PointD(2, 4)); True(copy.Interpolation!.Value.EndData == new PointD(8, 16));
            input.QuerySeriesCursorValues(f, new(300, 300), rows, SeriesCursorTrackingMode.Interpolated); Equal(0, rows.Count);
        });
        Add("step plateaus preserve exact original double values through logarithmic round trips", () => {
            using var d = InterpolatedDocument(new XyData([2, 8], [Math.PI, Math.E]), LineInterpolation.StepBefore);
            d.YAxis.Range = new(1, 100); d.YAxis.Scale = AxisScale.Logarithmic;
            True(InterpolatedValues(d, 5, 19).Single().ResultData.Y == Math.E);
        });
        Add("freeform bounds remain valid after axis and viewport changes", () => {
            using var d = InterpolatedDocument(new XyData([9, 1, 9], [2, 18, 18])); var input = new ChartInteraction(d); var c = new SceneCompiler(); List<SeriesCursorValue> rows = [];
            foreach (var invert in new[] { false, true })
            {
                d.XAxis.Inverted = invert; d.YAxis.Inverted = !invert; var f = c.Compile(d, invert ? 420 : 800, invert ? 300 : 600);
                input.QuerySeriesCursorValues(f, f.Layout.ToScreen(d.XAxis, d.YAxis, new(5, 9)), rows, SeriesCursorTrackingMode.Interpolated);
                Equal(10, rows.Single().ResultData.Y); Equal(0, rows[0].Index);
            }
            Equal(1, input.CursorInterpolationIndexBuildCount);
        });
    }
}
