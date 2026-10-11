using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

public sealed partial class SceneCompiler
{
    private static void BuildColorBars(ChartDocument document, ChartLayout layout, FrameBuilder frame)
    {
        var full = new RectD(0, 0, frame.Width, frame.Height); var start = frame.Count;
        foreach (var series in document.Series.OfType<HeatmapSeries>())
        {
            if (!layout.ColorBars.TryGetValue(series.Id, out var placement)) continue;
            var region = placement.Region; var strip = placement.Strip; var scale = series.ResolveColorScale();
            var font = Math.Min(11, region.Height * .12);
            string Fit(string text) { var count = Math.Max(1, (int)(region.Width / Math.Max(.01, font * .6))); return text.Length <= count ? text : text[..Math.Max(0, count - 1)] + "…"; }
            frame.Text(Fit(series.ValueTitle), region.X, region.Y + font, document.Theme.Text, font, bold: true);
            var units = series.ValueUnits + (series.ColorScale.Mode == ColorScaleMode.Logarithmic ? (series.ValueUnits.Length > 0 ? " · log" : "log") : "");
            if (units.Length > 0) frame.Text(Fit(units), region.X, region.Y + font * 2.2, document.Theme.Text.WithAlpha(.75f), font * .9);
            if (!scale.HasValues)
            {
                frame.Text("No data", region.X, strip.Y + strip.Height / 2, document.Theme.Text, Math.Min(font, region.Width / 5));
                continue;
            }
            // Share scalar mapping with the geometry. Include exact stop boundaries for stepped
            // palettes; gradient strips are bounded to 128 subdivisions plus explicit stops.
            SortedSet<double> divisions = [0, 1];
            foreach (var stop in series.ColorScale.Palette.Stops) divisions.Add(series.ColorScale.Reversed ? 1 - stop.Offset : stop.Offset);
            if (series.ColorScale.Palette.Interpolation == ColorInterpolation.Gradient)
                for (var i = 1; i < 128; i++) divisions.Add(i / 128.0);
            var offsets = divisions.ToArray();
            for (var i = 1; i < offsets.Length; i++)
            {
                var a = offsets[i - 1]; var b = offsets[i];
                var color = scale.Color(scale.FromUnit((a + b) / 2));
                frame.Rect(new(strip.X, strip.Bottom - b * strip.Height, strip.Width, (b - a) * strip.Height), color);
            }
            frame.Line(new(strip.X, strip.Y), new(strip.X, strip.Bottom), document.Theme.Text.WithAlpha(.6f), 1, full);
            frame.Line(new(strip.Right, strip.Y), new(strip.Right, strip.Bottom), document.Theme.Text.WithAlpha(.6f), 1, full);
            var labelWidth = Math.Max(.01, region.Right - strip.Right - 6);
            foreach (var tick in scale.Ticks(Math.Clamp((int)(strip.Height / 55) + 1, 2, 5)))
            {
                var y = strip.Bottom - tick.Unit * strip.Height;
                frame.Line(new(strip.Right, y), new(strip.Right + 4, y), document.Theme.Text, 1, full);
                var labelSize = Math.Min(font, labelWidth / Math.Max(1, tick.Label.Length * .62));
                var baseline = Math.Clamp(y + labelSize * .35, region.Y + labelSize, region.Bottom);
                frame.Text(tick.Label, strip.Right + 6, baseline, document.Theme.Text, labelSize);
            }
            frame.Hits.Add(new("colorbar", series.Id, strip));
        }
        frame.Batch(start, full);
    }
}
