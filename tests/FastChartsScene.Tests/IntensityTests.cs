using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterIntensityTests()
    {
        Add("color maps retain the original heat palette by default", () => {
            for (var i = 0; i <= 120; i++) { var a = ColorMap.Heat.Evaluate(i / 120.0); var b = Palette.Heat(i / 120.0);
                Equal(a.R, b.R, 2e-7); Equal(a.G, b.G, 2e-7); Equal(a.B, b.B, 2e-7); }
        });
        Add("color maps own ordered stops and expose a read-only view", () => {
            ColorStop[] stops = [new(0, new(0, 0, 0)), new(1, new(1, 1, 1))]; var map = new ColorMap("test", stops);
            stops[0] = new(0, new(1, 0, 0)); Equal(.5, map.Evaluate(.5).R);
            Throws<NotSupportedException>(() => ((IList<ColorStop>)map.Stops)[0] = stops[0]);
        });
        Add("color maps reject duplicate unordered missing and oversized stops", () => {
            Throws<ArgumentException>(() => new ColorMap("bad", [new(0, new()), new(0, new()), new(1, new())]));
            Throws<ArgumentException>(() => new ColorMap("bad", [new(.2, new()), new(1, new())]));
            Throws<ArgumentException>(() => new ColorMap("bad", Enumerable.Range(0, 257).Select(i => new ColorStop(i / 256.0, new()))));
            Throws<ArgumentOutOfRangeException>(() => new ColorMap("bad", [new(0, new(float.NaN, 0, 0)), new(1, new())]));
            Throws<ArgumentOutOfRangeException>(() => new ColorScale(missingColor: new(-1, 0, 0)));
        });
        Add("stepped palette boundaries are exact and right-continuous", () => {
            var map = new ColorMap("steps", [new(0, new(1,0,0)), new(.4, new(0,1,0)), new(1, new(0,0,1))], ColorInterpolation.Stepped);
            Equal(1, map.Evaluate(Math.BitDecrement(.4)).R); Equal(1, map.Evaluate(.4).G); Equal(1, map.Evaluate(1).B);
            Equal(0, map.Evaluate(double.NaN).A);
        });
        Add("color ranges clamp or hide finite outliers without changing endpoints", () => {
            var clamp = new ColorScale(ColorMap.Grayscale, new(10, 20)).Resolve(0, 100);
            Equal(0, clamp.Color(0).R); Equal(1, clamp.Color(100).R); Equal(.5, clamp.Color(15).R);
            var hide = new ColorScale(ColorMap.Grayscale, new(10, 20), outOfRange: ColorOutOfRange.Transparent).Resolve(0, 100);
            Equal(0, hide.Color(9).A); Equal(0, hide.Color(21).A); Equal(1, hide.Color(10).A); Equal(1, hide.Color(20).A);
        });
        Add("reversed palettes reverse colors not numeric tick positions", () => {
            var scale = new ColorScale(ColorMap.Grayscale, reversed: true).Resolve(-10, 30);
            Equal(1, scale.Color(-10).R); Equal(0, scale.Color(30).R); Equal(0, scale.Ticks()[0].Unit); Equal(-10, scale.FromUnit(0));
        });
        Add("constant and empty color ranges never invent extrema", () => {
            var constant = ColorScale.Default.Resolve(17, 17); True(constant.IsConstant); Equal(.5, constant.ToUnit(17)); Equal(17, constant.FromUnit(.7)); Equal(1, constant.Ticks().Count);
            var empty = ColorScale.Default.Resolve(double.NaN, double.NaN); True(!empty.HasValues); Equal(0, empty.Ticks().Count); Equal(0, empty.Color(double.NaN).A);
            var manual = new ColorScale(range: new(2, 4)).Resolve(double.NaN, double.NaN); True(manual.HasValues); Equal(2, manual.Minimum);
        });
        Add("log auto-range excludes zeros negatives and missing grid samples", () => {
            var heat = new HeatmapSeries { Grid = new(3, 2, 0, 1, 0, 1, [-3, 0, double.NaN, 1, 10, 100]), ColorScale = new(mode: ColorScaleMode.Logarithmic) };
            var scale = heat.ResolveColorScale(); Equal(1, scale.Minimum); Equal(100, scale.Maximum); Equal(.5, scale.ToUnit(10)); Equal(10, scale.FromUnit(.5), 1e-12); Equal(0, scale.Color(0).A);
            heat.Grid = new(1, 1, 0, 1, 0, 1, [-1]); True(!heat.ResolveColorScale().HasValues);
        });
        Add("color scale auto-range is cached by source and immutable settings", () => {
            using var d = new ChartDocument(); var heat = new HeatmapSeries { Grid = new(2, 1, 0, 1, 0, 1, [2, 8]) }; d.Series.Add(heat);
            var first = heat.ResolveColorScale(); True(ReferenceEquals(first, heat.ResolveColorScale())); var revision = d.Revision;
            heat.ColorScale = new(ColorMap.Grayscale); True(d.Revision > revision); True(!ReferenceEquals(first, heat.ResolveColorScale()));
            heat.Grid = new(1, 1, 0, 1, 0, 1, [100]); Equal(100, heat.ResolveColorScale().Minimum);
        });
        Add("color mapping handles extreme spans and adjacent positive values", () => {
            var wide = ColorScale.Default.Resolve(-double.MaxValue, double.MaxValue); Equal(.5, wide.ToUnit(0)); Equal(0, wide.FromUnit(.5));
            var min = 1e100; var max = min; for (var i = 0; i < 8; i++) max = Math.BitIncrement(max);
            var narrow = new ColorScale(mode: ColorScaleMode.Logarithmic).Resolve(min, max);
            Equal(0, narrow.ToUnit(min)); Equal(1, narrow.ToUnit(max)); True(double.IsFinite(narrow.ToUnit(Math.BitIncrement(min))));
            True(narrow.Ticks().Select(t => t.Label).Distinct().Count() == narrow.Ticks().Count);
        });
        Add("invalid color ranges and enums are rejected atomically", () => {
            Throws<ArgumentOutOfRangeException>(() => new ColorScale(range: default(AxisRange)));
            Throws<ArgumentOutOfRangeException>(() => new ColorScale(range: new(-1, 10), mode: ColorScaleMode.Logarithmic));
            Throws<ArgumentOutOfRangeException>(() => new ColorScale(mode: (ColorScaleMode)99));
            Throws<ArgumentOutOfRangeException>(() => ColorScale.Default.Resolve(10, 1));
            Throws<ArgumentOutOfRangeException>(() => ColorScale.Default.Resolve(0, 1).FromUnit(double.NaN));
        });
        Add("grid cell bounds and lookup use the same sample-center contract", () => {
            var g = new ScalarGrid(3, 3, 0, 4, 0, 4, new double[9]); var b = g.CellBounds(1, 1);
            Equal(1, b.X); Equal(1, b.Y); Equal(2, b.Width); Equal(2, b.Height);
            True(g.TryLocate(new(1, 1), out var col, out var row)); Equal(1, col); Equal(1, row);
            True(g.TryLocate(new(4, 4), out col, out row)); Equal(2, col); Equal(2, row);
            True(!g.TryLocate(new(-.01, 1), out _, out _)); True(!g.TryLocate(new(double.NaN, 1), out _, out _));
        });
        Add("single-cell grids cover extents and large coordinates remain finite", () => {
            var g = new ScalarGrid(1, 1, 1e308, 1.1e308, 10, 20, [3]); True(double.IsFinite(g.X(0))); Equal(15, g.Y(0));
            True(g.TryLocate(new(1.1e308, 20), out var col, out var row)); Equal(0, col); Equal(0, row);
            True(double.IsFinite(g.CellBounds(0, 0).Width)); Throws<ArgumentOutOfRangeException>(() => g.X(1));
        });
        RegisterIntensityRenderingTests();
    }
    static partial void RegisterIntensityRenderingTests();
}
