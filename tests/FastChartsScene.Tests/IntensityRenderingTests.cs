using System.Text.Json.Nodes;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Export;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Raster;
using FastChartsScene.Serialization;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static ChartDocument IntensityDocument()
    {
        var d = new ChartDocument { ShowLegend = false, ShowGrid = false };
        d.XAxis.Range = new(0, 4); d.YAxis.Range = new(0, 4);
        d.Series.Add(new HeatmapSeries { Grid = new(3, 3, 0, 4, 0, 4, [1,2,3,4,5,6,7,8,9]), ColorScale = new(ColorMap.Grayscale, new(0, 10)) });
        return d;
    }
    private static ColorRgba VertexColor(RenderFrame frame, int vertex)
    { var at = vertex * RenderFrame.VertexStride; return new(frame.Vertices[at + 3], frame.Vertices[at + 4], frame.Vertices[at + 5], frame.Vertices[at + 6]); }
    static partial void RegisterIntensityRenderingTests()
    {
        Add("heatmap triangles use the configured scalar mapping", () => {
            using var d = IntensityDocument(); var frame = new SceneCompiler().Compile(d, 800, 600); var batch = frame.Metadata.Batches[^1];
            Equal(54, batch.Count); Equal(.1, VertexColor(frame, batch.Start).R, 1e-7); Equal(.9, VertexColor(frame, batch.Start + 48).R, 1e-7);
        });
        Add("colorbars reserve a gutter and preserve exact numeric endpoints", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; var compiler = new SceneCompiler();
            var before = compiler.Compile(d, 800, 600); heat.ShowColorBar = true; heat.ValueTitle = "Power"; heat.ValueUnits = "mW";
            var after = compiler.Compile(d, 800, 600); var bar = after.Layout.ColorBars[heat.Id];
            True(after.Layout.Plot.Width < before.Layout.Plot.Width); True(bar.Strip.X > after.Layout.Plot.Right);
            True(bar.Region.Right <= 800 && bar.Region.Bottom <= 600); True(after.Metadata.Hits.Any(h => h.Kind == "colorbar" && h.Id == heat.Id));
            True(after.Metadata.Labels.Any(l => l.Text == "Power") && after.Metadata.Labels.Any(l => l.Text == "mW"));
            True(after.Metadata.Labels.Any(l => l.X > after.Layout.Plot.Right && l.Text == "10"));
        });
        Add("stacked far-axis labels stay outside the colorbar gutter", () => {
            using var d = IntensityDocument(); d.AxisLayout = AxisLayout.Stacked; d.YAxis.Side = AxisSide.Far;
            var heat = (HeatmapSeries)d.Series[0]; heat.ShowColorBar = true;
            var frame = new SceneCompiler().Compile(d, 800, 600); var bar = frame.Layout.ColorBars[heat.Id];
            True(bar.Region.X - frame.Layout.Plot.Right > 60);
        });
        Add("colorbar strips produce the same palette pixels as scalar cells", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.ShowColorBar = true;
            heat.ColorScale = new(ColorMap.Grayscale.WithInterpolation(ColorInterpolation.Stepped), new(0, 10));
            var frame = new SceneCompiler().Compile(d, 800, 600); var strip = frame.Layout.ColorBars[heat.Id].Strip;
            var image = RasterFrameRenderer.Render(frame, 800, 600);
            var at = ((int)(strip.Y + strip.Height * .3) * 800 + (int)(strip.X + strip.Width / 2)) * 4;
            Equal(0, image.Pixels.Span[at]); Equal(0, image.Pixels.Span[at + 1]); Equal(0, image.Pixels.Span[at + 2]);
            heat.ColorScale = heat.ColorScale.WithPalette(ColorMap.Grayscale);
            frame = new SceneCompiler().Compile(d, 800, 600); image = RasterFrameRenderer.Render(frame, 800, 600);
            True(image.Pixels.Span[at] > 170 && image.Pixels.Span[at] < 188);
        });
        Add("colorbar changes invalidate frames and hidden series release the gutter", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.ShowColorBar = true; var compiler = new SceneCompiler();
            var first = compiler.Compile(d, 800, 600); heat.ColorScale = new(ColorMap.Thermal);
            True(!ReferenceEquals(first, compiler.Compile(d, 800, 600))); heat.Visible = false;
            var hidden = compiler.Compile(d, 800, 600); True(hidden.Layout.ColorBars.Count == 0 && hidden.Layout.Plot.Width > first.Layout.Plot.Width);
        });
        Add("colorbars share a gutter without overlap and bind to stacked panes", () => {
            using var d = IntensityDocument(); d.AxisLayout = AxisLayout.Stacked; d.YAxes.Add(new Axis("other") { Range = new(0, 4) });
            var first = (HeatmapSeries)d.Series[0]; first.ShowColorBar = true;
            var second = new HeatmapSeries { Grid = first.Grid, ShowColorBar = true }; var third = new HeatmapSeries { Grid = first.Grid, ShowColorBar = true, YAxisId = "other" };
            d.Series.Add(second); d.Series.Add(third); var frame = new SceneCompiler().Compile(d, 480, 600);
            var a = frame.Layout.ColorBars[first.Id]; var b = frame.Layout.ColorBars[second.Id]; var c = frame.Layout.ColorBars[third.Id];
            True(a.Region.Bottom < b.Region.Y && b.Region.Bottom < c.Region.Y);
            True(c.Region.Y >= frame.Layout.ForAxis("other").Y && c.Region.Bottom <= frame.Layout.ForAxis("other").Bottom + 1e-9);
            True(frame.Metadata.Labels.All(l => double.IsFinite(l.X) && double.IsFinite(l.Y) && double.IsFinite(l.Size)));
        });
        Add("empty colorbars say no data while constant grids have one real tick", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.ShowColorBar = true; heat.ColorScale = ColorScale.Default;
            heat.Grid = new(1, 1, 0, 4, 0, 4, [double.NaN]); var compiler = new SceneCompiler();
            var empty = compiler.Compile(d, 800, 600); True(empty.Metadata.Labels.Any(l => l.Text == "No data"));
            heat.Grid = new(1, 1, 0, 4, 0, 4, [17]); var constant = compiler.Compile(d, 800, 600);
            Equal(1, constant.Metadata.Labels.Count(l => l.X > constant.Layout.Plot.Right && l.Text == "17"));
        });
        Add("scalar cursor reports the stored sample and rendered cell", () => {
            using var d = IntensityDocument(); var frame = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
            var location = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2.2, 1.6)); var hit = input.HitTestScalar(frame, location);
            True(hit != null && hit.CellBounds.Contains(location)); Equal(1, hit!.Column); Equal(1, hit.Row); Equal(5, hit.Value!.Value); Equal(2, hit.Data.X); Equal(.5, hit.Color.R);
        });
        Add("scalar cursor follows inverted axes and explicit bindings", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0];
            d.XAxes.Add(new Axis("x2") { Range = new(0, 4), Inverted = true }); d.YAxes.Add(new Axis("y2") { Range = new(0, 4), Inverted = true }); heat.XAxisId = "x2"; heat.YAxisId = "y2";
            var frame = new SceneCompiler().Compile(d, 800, 600); var point = frame.Layout.ToScreen(d.FindX("x2"), d.FindY("y2"), new(3.8, .2));
            var hit = new ChartInteraction(d).HitTestScalar(frame, point); True(hit != null); Equal(2, hit!.Column); Equal(0, hit.Row); Equal(3, hit.Value!.Value);
        });
        Add("log axis domain clipping preserves a partially visible boundary cell", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.Grid = new(2, 1, 0, 4, 0, 4, [2, 8]);
            d.XAxis.Range = new(1, 4); d.XAxis.Scale = AxisScale.Logarithmic; var frame = new SceneCompiler().Compile(d, 800, 600);
            True(IntensityGeometry.TryCellBounds(frame.Layout, d.XAxis, d.YAxis, heat.Grid, 0, 0, out var cell)); Equal(frame.Layout.Plot.X, cell.X);
            var point = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(1.5, 2)); var hit = new ChartInteraction(d).HitTestScalar(frame, point);
            True(hit != null && hit.Screen.IsFinite); Equal(0, hit!.Column); Equal(2, hit.Value!.Value);
        });
        Add("transparent cells allow underlying heatmap picking and opaque gaps preserve missing values", () => {
            using var d = IntensityDocument(); var bottom = (HeatmapSeries)d.Series[0];
            var top = new HeatmapSeries { Grid = new(1, 1, 0, 4, 0, 4, [double.NaN]) }; d.Series.Add(top); var compiler = new SceneCompiler();
            var frame = compiler.Compile(d, 800, 600); var point = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2, 2)); var input = new ChartInteraction(d);
            True(input.HitTestScalar(frame, point)!.SeriesId == bottom.Id);
            top.ColorScale = new(missingColor: new(1, 0, 1)); frame = compiler.Compile(d, 800, 600); var hit = input.HitTestScalar(frame, point);
            True(hit!.SeriesId == top.Id && hit.Value == null); Equal(1, hit.Color.R);
        });
        Add("scalar cursor ignores clipped hidden and legend-covered cells", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; d.ShowLegend = true; var compiler = new SceneCompiler(); var frame = compiler.Compile(d, 800, 600); var input = new ChartInteraction(d);
            var box = frame.Metadata.Hits.First(h => h.Kind == "legend").Bounds; True(input.HitTestScalar(frame, new(box.X + 2, box.Y + 2)) == null);
            True(input.HitTestScalar(frame, new(0, 0)) == null); heat.Visible = false; frame = compiler.Compile(d, 800, 600);
            True(input.HitTestScalar(frame, new(frame.Layout.Plot.X + 5, frame.Layout.Plot.Y + 5)) == null);
        });
        Add("manual transparent color ranges remove geometry and matching hits", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.ColorScale = new(range: new(4, 6), outOfRange: ColorOutOfRange.Transparent);
            var frame = new SceneCompiler().Compile(d, 800, 600); Equal(18, frame.Metadata.Batches[^1].Count);
            var input = new ChartInteraction(d); True(input.HitTestScalar(frame, frame.Layout.ToScreen(d.XAxis, d.YAxis, new(.1, .1))) == null);
            Equal(5, input.HitTestScalar(frame, frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2, 2)))!.Value!.Value);
        });
        Add("surface and waterfall geometry uses scalar rather than row colors", () => {
            using var d = new ChartDocument { View = ChartView.ThreeD, ShowLegend = false };
            var palette = new ColorMap("red", [new(0, new(1, 0, 0)), new(1, new(1, 0, 0))]);
            d.Series.Add(new SurfaceSeries3D { Grid = new(2, 2, 0, 1, 0, 1, [0, .5, 1, .2]), ColorScale = new(palette) });
            var frame = new SceneCompiler().Compile(d, 800, 600); var batch = frame.Metadata.Batches[^1];
            for (var i = batch.Start; i < batch.Start + batch.Count; i++) { Equal(1, VertexColor(frame, i).R); Equal(0, VertexColor(frame, i).G); }
            d.Series.Clear(); d.Series.Add(new WaterfallSeries3D { Grid = new(2, 1, 0, 1, 0, 1, [0, 1]), ColorScale = new(ColorMap.Grayscale) });
            frame = new SceneCompiler().Compile(d, 800, 600); batch = frame.Metadata.Batches[^1];
            True(Enumerable.Range(batch.Start, batch.Count).Any(i => VertexColor(frame, i).R == 0)); True(Enumerable.Range(batch.Start, batch.Count).Any(i => VertexColor(frame, i).R == 1));
        });
        Add("color scale persistence is exact and old version-one charts retain defaults", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.ShowColorBar = true; heat.ValueTitle = "Δ <value>"; heat.ValueUnits = "mW";
            heat.ColorScale = new(ColorMap.Thermal.WithInterpolation(ColorInterpolation.Stepped), new(1, 10), ColorScaleMode.Logarithmic, true, new(.123f, .456f, .789f, .5f), ColorOutOfRange.Transparent);
            var json = ChartJson.Write(d); using var copy = ChartJson.Read(json); True(json == ChartJson.Write(copy));
            var a = new SceneCompiler().Compile(d, 800, 600); var b = new SceneCompiler().Compile(copy, 800, 600); True(a.Vertices.SequenceEqual(b.Vertices));
            var old = JsonNode.Parse(json)!; var entry = old["series"]![0]!.AsObject(); entry.Remove("colorScale"); entry.Remove("colorBar");
            using var legacy = ChartJson.Read(old.ToJsonString()); var restored = (HeatmapSeries)legacy.Series[0];
            True(!restored.ShowColorBar && restored.ColorScale.Palette == ColorMap.Heat && restored.ColorScale.Range == null);
        });
        Add("persistence rejects invalid palette stops and numeric budget overflow", () => {
            using var d = IntensityDocument(); var json = JsonNode.Parse(ChartJson.Write(d))!;
            json["series"]![0]!["colorScale"]!["palette"]!["stops"]![1]!["offset"] = 0;
            Throws<FormatException>(() => ChartJson.Read(json.ToJsonString()));
            Throws<FormatException>(() => ChartJson.Read(ChartJson.Write(d), new(MaximumNumericValues: 40)));
        });
        Add("SVG and full-data CSV preserve configured color information", () => {
            using var d = IntensityDocument(); var heat = (HeatmapSeries)d.Series[0]; heat.ShowColorBar = true;
            var frame = new SceneCompiler().Compile(d, 800, 600); var svg = SvgExport.Write(frame); True(svg.Contains("#808080"));
            var csv = ChartDataExport.WriteCsv(d); True(csv.Contains("#808080FF"));
        });
        Add("fully transparent triangles never occlude visible raster geometry", () => {
            var hidden = new ColorRgba(1, 0, 0, 0); var visible = new ColorRgba(0, 1, 0);
            var vertices = RasterVertices((-.8f,-.8f,-.5f,hidden),(.8f,-.8f,-.5f,hidden),(0,.8f,-.5f,hidden),(-.8f,-.8f,.5f,visible),(.8f,-.8f,.5f,visible),(0,.8f,.5f,visible));
            var image = RasterFrameRenderer.Render(RasterFrame(vertices), 64, 64); Equal(255, image.Pixels.Span[(32 * 64 + 32) * 4 + 1]);
        });
    }
}
