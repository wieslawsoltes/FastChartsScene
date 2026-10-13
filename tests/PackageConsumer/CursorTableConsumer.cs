using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.PackageConsumer;
public static class CursorTableConsumer
{
    public static CursorResultsTable Create(ChartDocument document)
    {
        var frame = new SceneCompiler().Compile(document, 800, 600);
        var input = new ChartInteraction(document); List<SeriesCursorValue> values = [];
        input.QuerySeriesCursorValues(frame, new(frame.Layout.Plot.X + 40, frame.Layout.Plot.Y + 30), values);
        var table = new CursorResultsTable { ResizeEnabled = true };
        table.ResizeRequested += (_, e) => table.Height = e.Reset ? 210 : Math.Clamp((double.IsNaN(table.Height) ? 210 : table.Height) + e.HeightChange, 140, 520);
        table.SetValues(values);
        _ = table.ExportCsv(); _ = table.Snapshot();
        return table;
    }
    public static void Attach(ChartControl chart, CursorResultsTable table)
    {
        chart.CursorValuesEnabled = true;
        chart.SeriesCursorValuesChanged += (_, _) => table.SetValues(chart.SeriesCursorValues, chart.CursorValuesAreActive);
    }
}
