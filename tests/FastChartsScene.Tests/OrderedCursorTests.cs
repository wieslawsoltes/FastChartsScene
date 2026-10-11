using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterOrderedCursorTests()
    {
        Add("ordered cursor screen windows match the full index across duplicate and log boundaries", () => {
            var random = new Random(4557);
            var xs = Enumerable.Range(0, 15000).Select(i => .1 + (i / 30) * .019).ToArray();
            var ys = xs.Select((_, i) => i % 103 == 0 ? double.NaN : random.NextDouble() * 10).ToArray();
            var data = new XyData(xs, ys); var index = new PointSpatialIndex(data);
            for (var mode = 0; mode < 16; mode++)
            {
                using var document = new ChartDocument { ShowLegend = false };
                document.XAxis.Range = new(.1, 10); document.YAxis.Range = new(.1, 10);
                document.XAxis.Inverted = (mode & 1) != 0; document.YAxis.Inverted = (mode & 2) != 0;
                if ((mode & 4) != 0) { document.XAxis.Scale = AxisScale.Logarithmic; document.YAxis.Scale = AxisScale.Logarithmic; }
                var transpose = (mode & 8) != 0;
                document.Series.Add(transpose ? new BarSeries { Data = data, Horizontal = true, Baseline = .1, BarWidth = .0001 } : new LineSeries { Data = data });
                var frame = new SceneCompiler().Compile(document, 950, 610); var input = new ChartInteraction(document);
                for (var q = 0; q < 30; q++)
                {
                    var plot = frame.Layout.Plot; var location = new PointD(plot.X + random.NextDouble() * plot.Width, plot.Y + random.NextDouble() * plot.Height);
                    var expected = index.Nearest(document.XAxis, document.YAxis, plot, location, 12, transpose).Hit;
                    var actual = input.HitTestData(frame, location);
                    Equal(expected?.Index ?? -1, actual?.Index ?? -1);
                    if (actual != null) Equal(expected!.Value.Distance, actual.Distance);
                }
            }
        });
        Add("zero-radius cursor includes all equal projected positions", () => {
            using var d = new ChartDocument { ShowLegend = false };
            d.XAxis.Range = new(1e100, 1e100 * (1 + 1e-12)); d.XAxis.Scale = AxisScale.Logarithmic;
            d.YAxis.Range = new(0, 10);
            var x = d.XAxis.FromUnit(.5); var values = Enumerable.Repeat(9.0, 5000).ToArray(); values[11] = 5;
            var data = new UniformData(x, double.Epsilon, values); d.Series.Add(new LineSeries { Data = data });
            var f = new SceneCompiler().Compile(d, 800, 600); var location = f.Layout.ToScreen(d.XAxis, d.YAxis, new(x, 5));
            var hit = new ChartInteraction(d).HitTestData(f, location, 0); True(hit != null); Equal(11, hit!.Index);
        });
        Add("stale and entirely unpainted line cursors do not report data", () => {
            using var d = LineDocument(); var line = (LineSeries)d.Series[0]; var compiler = new SceneCompiler();
            var frame = compiler.Compile(d, 800, 600); var p = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2, -1)); var input = new ChartInteraction(d);
            line.Title = "Changed"; True(input.HitTestData(frame, p) == null);
            line.ShowLine = false; True(input.HitTestData(compiler.Compile(d, 800, 600), p) == null);
            line.Marker = MarkerShape.Circle; True(input.HitTestData(compiler.Compile(d, 800, 600), p) != null);
        });
    }
}
