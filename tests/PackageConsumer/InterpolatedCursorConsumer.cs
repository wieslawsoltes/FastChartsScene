using FastChartsScene.Controls.Uno;
using FastChartsScene.Interaction;

namespace FastChartsScene.PackageConsumer;
public static class InterpolatedCursorConsumer
{
    public static CursorResultsTable Attach(ChartControl chart)
    {
        var table = new CursorResultsTable { TrackingModeSelectionEnabled = true, NumberFormatSelectionEnabled = true, NumberFormat = new CursorValueFormat("R", "E6"), TrackingMode = SeriesCursorTrackingMode.Interpolated };
        chart.CursorValuesTrackingMode = table.TrackingMode; chart.CursorValuesEnabled = true;
        table.TrackingModeChanged += (_, _) => chart.CursorValuesTrackingMode = table.TrackingMode;
        chart.SeriesCursorValuesChanged += (_, _) => table.SetValues(chart.SeriesCursorValues, chart.CursorValuesAreActive);
        foreach (var value in chart.SeriesCursorValues)
        {
            _ = value.ResultData; _ = value.IsInterpolated;
            if (value.Interpolation is { } calculation) { _ = calculation.Result; _ = calculation.EndIndex; _ = calculation.EndData; _ = calculation.Method; }
        }
        return table;
    }
}
