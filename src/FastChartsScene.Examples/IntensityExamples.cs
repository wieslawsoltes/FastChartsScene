using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Examples;

public static partial class ExampleCatalog
{
    private static ChartDocument LogIntensity()
    {
        const int columns = 41, rows = 31; var values = new double[columns * rows];
        for (var row = 0; row < rows; row++) for (var col = 0; col < columns; col++)
            values[row * columns + col] = Math.Pow(10, -3 + 3.0 * col / (columns - 1) + 3.0 * row / (rows - 1));
        var d = Document("Logarithmic intensity", "X position", "Y position"); d.ShowLegend = false;
        d.XAxis.Range = new(-3, 3); d.YAxis.Range = new(-3, 3);
        d.Series.Add(new HeatmapSeries { Title = "Power field", Grid = new(columns, rows, -3, 3, -3, 3, values),
            ColorScale = new(ColorMap.Thermal, mode: ColorScaleMode.Logarithmic), ShowColorBar = true, ValueTitle = "Power", ValueUnits = "mW" });
        return d;
    }
    private static ChartDocument IntensityPanes()
    {
        var d = Document("Compared intensity fields", "X position", "Reference"); d.ShowLegend = false; d.AxisLayout = AxisLayout.Stacked;
        d.XAxis.Range = new(-3, 3); d.YAxis.Range = new(-3, 3); d.YAxes.Add(new("comparison") { Title = "Comparison", Range = new(-3, 3) });
        var grid = Field(41, 25); var other = new ScalarGrid(grid.Columns, grid.Rows, -3, 3, -3, 3, grid.Values.Span.ToArray().Select(v => v * .65 + .15).ToArray());
        var scale = new ColorScale(ColorMap.Diverging, new(-1.4, 1.4));
        d.Series.Add(new HeatmapSeries { Title = "Reference", Grid = grid, ColorScale = scale, ShowColorBar = true, ValueTitle = "Reference", ValueUnits = "V" });
        d.Series.Add(new HeatmapSeries { Title = "Comparison", Grid = other, ColorScale = scale, ShowColorBar = true, ValueTitle = "Comparison", ValueUnits = "V", YAxisId = "comparison" });
        return d;
    }
}
