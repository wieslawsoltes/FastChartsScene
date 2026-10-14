using System.Xml.Linq;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Export;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Raster;
using FastChartsScene.Serialization;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private sealed class ReviewTextMeasurer : ITextMeasurer
    {
        public int Calls;
        public TextMeasurement Measure(string text, double size, bool bold = false)
        {
            Calls++;
            // Deterministic proportional fake; native tests independently use real Skia metrics.
            var advance = text.Sum(c => c == 'W' ? .98 : c == 'i' ? .25 : .65) * size;
            return new(advance, new(-.5, -size * .8, advance + 1, size));
        }
    }
    private sealed class InvalidReviewTextMeasurer : ITextMeasurer
    {
        public TextMeasurement Measure(string text, double size, bool bold = false) => new(double.NaN, default);
    }
    private sealed class MutatingReviewTextMeasurer(ChartDocument document) : ITextMeasurer
    {
        private bool _changed;
        public TextMeasurement Measure(string text, double size, bool bold = false)
        {
            if (!_changed) { _changed = true; document.XAxis.Range = new(0, 8); }
            return new(text.Length * size / 2, new(0, -size, text.Length * size / 2, size));
        }
    }
    private static ChartDocument FillReviewDocument(bool band, float alpha)
    {
        var d = new ChartDocument { ShowLegend = false, ShowGrid = false, Theme = ChartTheme.Light };
        d.XAxis.Range = new(0, 10); d.YAxis.Range = new(0, 10);
        DataSeries series = band ? new BandSeries { Data = new XyData([1, 9], [2, 2]), Upper = new XyData([1, 9], [8, 8]) } :
            new LineSeries { Data = new XyData([1, 9], [8, 8]), Baseline = 2, FillToBaseline = true, ShowLine = false };
        series.Color = new(1, 0, 0, alpha); series.StrokeWidth = 0; d.Series.Add(series); return d;
    }
    private static float[] RedAlphas(RenderFrame frame) => Enumerable.Range(0, frame.Metadata.VertexCount)
        .Where(i => VertexColor(frame, i) is { R: 1, G: 0, B: 0 }).Select(i => VertexColor(frame, i).A).ToArray();
    private static void AssertInsideAndSeparated(RenderFrame frame)
    {
        var visible = frame.Layout.AxisLabels.Where(l => l.Visible).ToArray();
        True(visible.Length > 0);
        foreach (var label in visible)
        {
            var b = label.Bounds;
            True(b.X >= 2 && b.Y >= 2 && b.Right <= frame.Metadata.Width - 2 && b.Bottom <= frame.Metadata.Height - 2);
            True(frame.Metadata.Labels.Contains(label.Label));
        }
        for (var i = 0; i < visible.Length; i++) for (var j = i + 1; j < visible.Length; j++)
        {
            var a = visible[i].Bounds; var b = visible[j].Bounds;
            True(a.Right <= b.X || b.Right <= a.X || a.Bottom <= b.Y || b.Bottom <= a.Y, "Measured axis labels overlap.");
        }
    }
    private static void RegisterIssueRegressionTests()
    {
        Add("issue 5 area fill multiplies base opacity in every interpolation mode", () => {
            foreach (var mode in Enum.GetValues<LineInterpolation>()) foreach (var alpha in new[] { 0f, .25f, .5f, 1f })
            {
                using var d = FillReviewDocument(false, alpha); ((LineSeries)d.Series[0]).Interpolation = mode;
                var values = RedAlphas(new SceneCompiler().Compile(d, 800, 600));
                if (alpha == 0) Equal(0, values.Length);
                else { True(values.Length > 0); True(values.All(a => a == alpha * .25f)); }
            }
        });
        Add("issue 5 band fill multiplies base opacity and zero alpha emits no fill", () => {
            foreach (var alpha in new[] { 0f, .25f, .5f, 1f })
            {
                using var d = FillReviewDocument(true, alpha); var values = RedAlphas(new SceneCompiler().Compile(d, 800, 600));
                if (alpha == 0) Equal(0, values.Length);
                else { True(values.Length > 0); True(values.All(a => a == alpha * .32f)); }
            }
        });
        Add("issue 5 raster pixels and SVG alphas agree with composed area and band opacity", () => {
            foreach (var band in new[] { false, true }) foreach (var alpha in new[] { 0f, .5f, 1f })
            {
                using var d = FillReviewDocument(band, alpha); var frame = new SceneCompiler().Compile(d, 800, 600);
                var image = RasterFrameRenderer.Render(frame, 800, 600); var p = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(4, 5));
                var at = ((int)p.Y * 800 + (int)p.X) * 4; var composed = alpha * (band ? .32f : .25f);
                Equal(255, image.Pixels.Span[at]); Equal(255 * (1 - composed), image.Pixels.Span[at + 1], 1.5);
                var svg = XDocument.Parse(SvgExport.Write(frame));
                var polygons = svg.Descendants().Where(e => e.Name.LocalName == "polygon" && (string?)e.Attribute("fill") == "#FF0000").ToArray();
                if (alpha == 0) Equal(0, polygons.Length);
                else { True(polygons.Length > 0); True(polygons.All(p => Math.Abs((double)p.Attribute("fill-opacity")! - composed) < 1e-7)); }
            }
        });
        Add("rectangle annotation fill also composes base opacity without changing its outline", () => {
            using var d = FillReviewDocument(false, 0); d.Series.Clear();
            d.Annotations.Add(new Annotation { Kind = AnnotationKind.Rectangle, X = 1, Y = 2, X2 = 9, Y2 = 8, Color = new(1, 0, 0, .5f) });
            var values = RedAlphas(new SceneCompiler().Compile(d, 800, 600));
            True(values.Contains(.075f) && values.Contains(.5f)); True(values.All(v => v == .075f || v == .5f));
        });
        Add("issue 13 measured narrow trillion range preserves endpoint strings inside both canvas edges", () => {
            foreach (var width in new[] { 320d, 480, 1440 }) foreach (var inverted in new[] { false, true })
            {
                using var d = LineDocument(); d.XAxis.Range = new(1e12, 1e12 + 1); d.XAxis.Inverted = inverted;
                var frame = new SceneCompiler { TextMeasurer = new ReviewTextMeasurer() }.Compile(d, width, 600);
                AssertInsideAndSeparated(frame);
                var labels = frame.Layout.AxisLabels.Where(p => p.Horizontal && !p.IsTitle && p.Visible).ToArray();
                True(labels.Any(p => p.Value == 1e12 && p.Label.Text == "1000000000000"));
                True(labels.Any(p => p.Value == 1e12 + 1 && p.Label.Text == "1000000000001"));
                True(labels.Select(p => p.Label.Text).Distinct().Count() == labels.Length);
            }
        });
        Add("issue 13 custom crowded labels retain exact suppressed text and all tick coordinates", () => {
            using var d = LineDocument(); d.XAxis.TickOptions = new(AxisTickMode.Custom, customTicks:
                Enumerable.Range(0, 41).Select(i => new CustomAxisTick(i * .1, "Long coordinate " + i)).ToArray());
            var source = ChartJson.Write(d); var frame = new SceneCompiler { TextMeasurer = new ReviewTextMeasurer() }.Compile(d, 480, 400);
            var ticks = frame.Layout.AxisLabels.Where(p => p.Horizontal && !p.IsTitle).ToArray();
            Equal(41, ticks.Length); True(ticks.Any(p => !p.Visible && p.Reason == "overlap"));
            True(ticks.Any(p => p.Visible && p.Value == 0)); True(ticks.Any(p => p.Visible && p.Value == 4));
            True(ticks.All(p => p.Label.Text == "Long coordinate " + (int)Math.Round(p.Value!.Value * 10)));
            True(ChartJson.Write(d) == source); AssertInsideAndSeparated(frame);
        });
        Add("issue 13 measured layered far and near secondary axes reserve their own title lanes", () => {
            using var d = LineDocument(); d.YAxis.Title = "Primary";
            d.YAxes.Add(new Axis("near") { Range = new(1e12, 1e12 + 1), Title = "Secondary" });
            d.YAxes.Add(new Axis("far") { Range = new(1, 100), Scale = AxisScale.Logarithmic, Side = AxisSide.Far, Inverted = true, Title = "Far" });
            d.XAxes.Add(new Axis("log") { Range = new(1, 100), Scale = AxisScale.Logarithmic, Inverted = true });
            var frame = new SceneCompiler { TextMeasurer = new ReviewTextMeasurer() }.Compile(d, 1100, 700);
            AssertInsideAndSeparated(frame);
            foreach (var id in new[] { "y", "near", "far", "x", "log" }) True(frame.Layout.AxisLabels.Any(p => p.AxisId == id && p.Visible));
        });
        Add("issue 13 measured stacked and segmented panes remain positive on tiny surfaces", () => {
            foreach (var layout in new[] { AxisLayout.Stacked, AxisLayout.Segmented })
            {
                using var d = LineDocument(); d.YAxes.Add(new Axis("other")); d.AxisLayout = layout;
                if (layout == AxisLayout.Segmented) { d.Segments.Add(new("first", 1, ["y"])); d.Segments.Add(new("second", 1, ["other"])); }
                var frame = new SceneCompiler { TextMeasurer = new ReviewTextMeasurer() }.Compile(d, 20, 20);
                True(frame.Layout.AxisPlots.Values.All(p => p.Width > 0 && p.Height > 0));
                True(frame.Layout.AxisLabels.Any(p => !p.Visible && p.Reason == "outside-canvas"));
                True(frame.Layout.AxisLabels.Where(p => p.Visible).All(p => p.Bounds.X >= 0 && p.Bounds.Right <= 20));
            }
        });
        Add("issue 13 label-only changes reuse font measurements and original data indexes", () => {
            using var d = LineDocument(); var measurer = new ReviewTextMeasurer(); var c = new SceneCompiler { TextMeasurer = measurer };
            var first = c.Compile(d, 800, 600); var measured = c.TextMeasurementCount; Equal(1, c.SourceIndexBuildCount);
            True(ReferenceEquals(first, c.Compile(d, 800, 600))); Equal(measured, c.TextMeasurementCount);
            d.Series[0].Color = Palette.Series[1]; c.Compile(d, 800, 600); Equal(1, c.SourceIndexBuildCount); Equal(measured, c.TextMeasurementCount);
            d.XAxis.Title = "A new axis title"; c.Compile(d, 800, 600); Equal(1, c.SourceIndexBuildCount); True(c.TextMeasurementCount > measured);
            c.TextMeasurer = new ReviewTextMeasurer(); c.Compile(d, 800, 600); Equal(1, c.SourceIndexBuildCount);
        });
        Add("issue 13 SVG retains exact label text and measured advances", () => {
            using var d = LineDocument(); d.XAxis.TickOptions = new(AxisTickMode.Custom, customTicks: [new(0, "A<&>"), new(4, "WiiiW")]);
            var frame = new SceneCompiler { TextMeasurer = new ReviewTextMeasurer() }.Compile(d, 800, 600);
            var svg = XDocument.Parse(SvgExport.Write(frame));
            var texts = svg.Descendants().Where(e => e.Name.LocalName == "text" && e.Value == "A<&>").ToArray();
            Equal(1, texts.Length); True((double)texts[0].Attribute("textLength")! > 0);
            True((string?)texts[0].Attribute("lengthAdjust") == "spacingAndGlyphs");
        });
        Add("invalid font metrics are rejected before caching a corrupt frame", () => {
            using var d = LineDocument(); var c = new SceneCompiler { TextMeasurer = new InvalidReviewTextMeasurer() };
            Throws<InvalidOperationException>(() => c.Compile(d, 800, 600)); Equal(0, c.CompilationCount);
            c.TextMeasurer = new ReviewTextMeasurer(); c.Compile(d, 800, 600).Validate();
        });
        Add("model mutation inside a text callback cannot certify an obsolete scene", () => {
            using var d = LineDocument(); var c = new SceneCompiler { TextMeasurer = new MutatingReviewTextMeasurer(d) };
            Throws<InvalidOperationException>(() => c.Compile(d, 800, 600)); Equal(0, c.CompilationCount);
            var frame = c.Compile(d, 800, 600); Equal(d.Revision, frame.Metadata.Revision); frame.Validate();
        });
        Add("measured frame label snapshots survive later axis changes", () => {
            using var d = LineDocument(); var c = new SceneCompiler { TextMeasurer = new ReviewTextMeasurer() };
            var frame = c.Compile(d, 800, 600); var snapshot = frame.Layout.AxisLabels.ToArray();
            d.XAxis.Range = new(0, 100); d.YAxis.Title = "changed"; c.Compile(d, 800, 600); c.ClearCache();
            True(frame.Layout.AxisLabels.SequenceEqual(snapshot));
        });
        Add("text ink bounds include alignment and rotation rather than advance alone", () => {
            var m = new TextMeasurement(100, new(-2, -9, 105, 12));
            var b = m.Bounds(new("sample", 200, 100, "#000000FF", 11, "right"));
            Equal(98, b.X); Equal(91, b.Y); Equal(105, b.Width); Equal(12, b.Height);
            b = m.Bounds(new("sample", 200, 100, "#000000FF", 11, "center", -90));
            Equal(191, b.X); Equal(47, b.Y); Equal(12, b.Width); Equal(105, b.Height);
        });
    }
}
