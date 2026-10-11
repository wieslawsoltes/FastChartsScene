using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Examples;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterCompletionReviewTests()
    {
        Add("long acquisition outages retain one original gap sentinel", () => {
            var values = Enumerable.Repeat(double.NaN, 1_000_000).ToArray();
            values[0] = 1; values[^1] = 2;
            var data = new UniformData(1, 1, values);
            var index = new EnvelopeIndex(data);
            var axis = new Axis("x") { Range = new(1, 1_000_000) };
            var selected = index.Select(axis, 256);
            True(selected.SequenceEqual(new[] { 0, 1, 999999 }), "An outage must not emit every missing sample.");
            axis.Range = new(200000, 800000);
            selected = index.Select(axis, 256);
            Equal(1, selected.Count); True(double.IsNaN(data.Y(selected[0])));
            axis.Inverted = true; axis.Scale = AxisScale.Logarithmic;
            Equal(1, index.Select(axis, 256).Count);
        });
        Add("gap index allocation scales with runs rather than missing samples", () => {
            var data = new UniformData(0, 1, Enumerable.Repeat(double.NaN, 1_000_000).ToArray());
            _ = new EnvelopeIndex(new UniformData(0, 1, [double.NaN]));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var index = new EnvelopeIndex(data);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            True(allocated < 400000, $"Index allocated {allocated} bytes for a single outage.");
            Equal(-1, index.Query(0, data.Count).Minimum);
        });
        Add("gap reduction preserves finite islands endpoints extrema and discontinuities", () => {
            var random = new Random(1881);
            for (var trial = 0; trial < 24; trial++)
            {
                var values = new double[8192];
                for (var i = 0; i < values.Length; i++) values[i] = random.NextDouble();
                for (var i = 0; i < 40; i++)
                {
                    var start = random.Next(values.Length); var end = Math.Min(values.Length, start + random.Next(1, 160));
                    Array.Fill(values, double.NaN, start, end - start);
                }
                var data = new UniformData(1, 1, values);
                var axis = new Axis("x") { Range = new(1, values.Length), Inverted = (trial & 1) != 0 };
                if ((trial & 2) != 0) axis.Scale = AxisScale.Logarithmic;
                var selected = new EnvelopeIndex(data).Select(axis, 32).ToArray();
                True(selected.SequenceEqual(selected.Distinct().Order()));
                var set = selected.ToHashSet();
                for (var i = 0; i < values.Length; i++)
                    if (double.IsFinite(values[i]) && (i == 0 || i == values.Length - 1 || double.IsNaN(values[i - 1]) || double.IsNaN(values[i + 1])))
                        True(set.Contains(i), "Lost finite run endpoint.");
                for (var i = 1; i < selected.Length; i++)
                    if (double.IsFinite(values[selected[i - 1]]) && double.IsFinite(values[selected[i]]))
                        for (var at = selected[i - 1]; at <= selected[i]; at++) True(!double.IsNaN(values[at]), "Bridged an outage.");
                var finite = values.Where(double.IsFinite).ToArray();
                Equal(finite.Min(), selected.Where(i => double.IsFinite(values[i])).Min(i => values[i]));
                Equal(finite.Max(), selected.Where(i => double.IsFinite(values[i])).Max(i => values[i]));
            }
        });
        Add("gap selection preserves adjacent single-sample islands and small raw sources", () => {
            var values = Enumerable.Range(0, 20000).Select(i => i % 2 == 0 ? i : double.NaN).ToArray();
            var index = new EnvelopeIndex(new UniformData(0, 1, values));
            var axis = new Axis("x") { Range = new(18000, 19999) };
            var selected = index.Select(axis, 10);
            True(selected.Contains(18000) && selected.Contains(19998));
            True(selected.Where(i => i >= 18000 && i % 2 == 0).Count() == 1000);
            var small = new EnvelopeIndex(new UniformData(0, 1, [1, double.NaN, double.NaN, 2]));
            True(small.Select(axis, 10).SequenceEqual(new[] { 3 }));
            axis.Range = new(0, 3); True(small.Select(axis, 10).SequenceEqual(new[] { 0, 1, 2, 3 }));
        });
        Add("line compilation remains bounded for a million-sample outage", () => {
            using var d = new ChartDocument { ShowLegend = false };
            var samples = Enumerable.Repeat(double.NaN, 1_000_000).ToArray(); samples[0] = 1; samples[^1] = 1;
            d.XAxis.Range = new(0, samples.Length); d.Series.Add(new LineSeries { Data = new UniformData(0, 1, samples) });
            var frame = new SceneCompiler().Compile(d, 900, 600);
            frame.Validate(); True(frame.Metadata.VertexCount < 1000); Equal(samples.Length, frame.Metadata.SourcePoints);
        });
        foreach (var kind in new[] { "surface", "waterfall", "bars3d", "line", "mesh" })
        foreach (var logarithmic in new[] { false, true })
            Add($"3D {kind} respects secondary {(logarithmic ? "logarithmic" : "linear")} axes", () => {
                using var bound = BoundSpatialDocument(kind, true, logarithmic);
                using var primary = BoundSpatialDocument(kind, false, logarithmic);
                var a = new SceneCompiler().Compile(bound, 900, 600);
                var b = new SceneCompiler().Compile(primary, 900, 600);
                True(a.Vertices.SequenceEqual(b.Vertices), "Secondary X/Y bindings were not applied consistently.");
                a.Validate();
            });
        Add("3D grid toggle removes floor geometry without changing series geometry", () => {
            using var d = ExampleCatalog.Find("surface").Create(); var compiler = new SceneCompiler();
            var before = compiler.Compile(d, 900, 600); d.ShowGrid = false; var after = compiler.Compile(d, 900, 600);
            Equal(22 * 36, before.Metadata.VertexCount - after.Metadata.VertexCount);
            var a = before.Metadata.Batches.Last(b => b.DepthTest); var b = after.Metadata.Batches.Last(b => b.DepthTest);
            True(before.Vertices.AsSpan(a.Start * 10, a.Count * 10).SequenceEqual(after.Vertices.AsSpan(b.Start * 10, b.Count * 10)));
        });
        Add("3D stroke controls suppress zero-width lines and scale waterfall thickness", () => {
            using var d = BoundSpatialDocument("waterfall", false, false); var compiler = new SceneCompiler();
            var series = d.Series[0]; var normal = compiler.Compile(d, 900, 600);
            series.StrokeWidth = 3; var thick = compiler.Compile(d, 900, 600);
            Equal(normal.Metadata.VertexCount, thick.Metadata.VertexCount); True(!normal.Vertices.SequenceEqual(thick.Vertices));
            series.StrokeWidth = 0; var hidden = compiler.Compile(d, 900, 600); True(hidden.Metadata.VertexCount < thick.Metadata.VertexCount);
            using var lineDocument = BoundSpatialDocument("line", false, false);
            var line = (PointLineSeries3D)lineDocument.Series[0]; line.PointSize = 0;
            var drawn = compiler.Compile(lineDocument, 900, 600); line.StrokeWidth = 0;
            var blank = compiler.Compile(lineDocument, 900, 600); True(blank.Metadata.VertexCount < drawn.Metadata.VertexCount);
        });
        Add("envelope rejects null inputs explicitly", () => {
            Throws<ArgumentNullException>(() => new EnvelopeIndex(null!));
            var index = new EnvelopeIndex(new UniformData(0, 1, []));
            Throws<ArgumentNullException>(() => index.Select(null!, 1));
        });
    }
    private static ChartDocument BoundSpatialDocument(string kind, bool secondary, bool log)
    {
        var d = new ChartDocument { View = ChartView.ThreeD, ShowLegend = false };
        d.XAxis.Range = d.YAxis.Range = secondary ? new(100, 200) : new(1, 10);
        d.ZAxis.Range = new(1, 10);
        var x = new Axis("bound-x") { Range = new(1, 10), Inverted = true };
        var y = new Axis("bound-y") { Range = new(1, 10), Inverted = true };
        d.XAxes.Add(x); d.YAxes.Add(y);
        d.XAxis.Inverted = d.YAxis.Inverted = true;
        if (log) { x.Scale = y.Scale = d.XAxis.Scale = d.YAxis.Scale = AxisScale.Logarithmic; }
        var grid = new ScalarGrid(2, 2, 2, 8, 2, 8, [2, 4, 6, 8]);
        Series series;
        switch (kind)
        {
            case "surface": series = new SurfaceSeries3D { Grid = grid }; break;
            case "waterfall": series = new WaterfallSeries3D { Grid = grid }; break;
            case "bars3d": series = new BarSeries3D { Grid = grid }; break;
            case "line":
                var line = new PointLineSeries3D(); line.SetData([new(2, 2, 2), new(8, 8, 8)]); series = line; break;
            default:
                series = new MeshSeries3D { Mesh = new([new(new(2, 2, 2), Palette.Series[0]), new(new(8, 2, 2), Palette.Series[1]), new(new(2, 8, 8), Palette.Series[2])], [0, 1, 2]) }; break;
        }
        if (secondary) { series.XAxisId = x.Id; series.YAxisId = y.Id; }
        d.Series.Add(series); return d;
    }
}
