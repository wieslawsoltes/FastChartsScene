using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Serialization;

namespace FastChartsScene.PackageConsumer;

public static class IntensityConsumer
{
    public static IntensityScaleEditor CreateEditor(HeatmapSeries series) => new() { Series = series };
    public static ScalarDataHit? QueryCell()
    {
        using var document = new ChartDocument { ShowLegend = false };
        var scale = new ColorScale(ColorMap.Thermal, new AxisRange(1, 100), ColorScaleMode.Logarithmic);
        var heat = new HeatmapSeries { Grid = new(2, 2, 0, 10, -1, 1, [1, 10, 30, 100]), ColorScale = scale.WithReversed(true), ShowColorBar = true, ValueUnits = "mW" };
        document.Series.Add(heat);
        using var restored = ChartJson.Read(ChartJson.Write(document));
        var frame = new SceneCompiler().Compile(restored, 800, 600);
        _ = frame.Layout.ColorBars.Count;
        return new ChartInteraction(restored).HitTestScalar(frame, frame.Layout.ToScreen(restored.XAxis, restored.YAxis, new PointD(1, 0)));
    }
}
