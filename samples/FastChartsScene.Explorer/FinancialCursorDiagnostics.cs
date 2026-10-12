using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Charts;
using FastChartsScene.Core;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public sealed record FinancialCursorSnapshot(string SeriesId, string Title, int Index, OhlcPoint Sample,
    string Field, PointD Data, PointD Screen, double Distance, RectD PaneBounds);
public sealed record FinancialQuerySnapshot(FinancialCursorSnapshot? Hit, string Readout, RectD? ReadoutBounds,
    long IndexBuilds, int VisitedNodes, int TestedSamples, long TextFormats);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FinancialQuerySnapshot))]
[JsonSerializable(typeof(PointD))]
internal partial class FinancialCursorJsonContext : JsonSerializerContext { }

public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string InspectFinancialCursor()
    {
        var chart = Page!.Chart; var input = chart.Interaction; var hit = chart.FinancialHit;
        return JsonSerializer.Serialize(new FinancialQuerySnapshot(hit == null ? null : new(hit.SeriesId, hit.Title, hit.Index,
            hit.Sample, hit.Field.ToString(), hit.Data, hit.Screen, hit.Distance, hit.PaneBounds),
            chart.FinancialCursorText, chart.FinancialCursorBounds, input?.FinancialIndexBuildCount ?? 0,
            input?.LastFinancialQueryVisits ?? 0, input?.LastFinancialQueryTests ?? 0, chart.FinancialTextFormatCount),
            FinancialCursorJsonContext.Default.FinancialQuerySnapshot);
    }
#if __WASM__
    [JSExport]
#endif
    public static string FinancialSourceScreen(int seriesIndex, int index, string field)
    {
        // Read-only geometry probe for secondary/stacked pane coordinate delivery.
        var chart = Page!.Chart; var document = Page.Document;
        var series = (FinancialSeries)document.Series[seriesIndex];
        var data = series.GetPriceData(Enum.Parse<FinancialValueField>(field));
        var point = chart.Frame!.Layout.ToScreen(document.FindX(series.XAxisId), document.FindY(series.YAxisId), new(data.X(index), data.Y(index)));
        return JsonSerializer.Serialize(point, FinancialCursorJsonContext.Default.PointD);
    }
#if __WASM__
    [JSExport]
#endif
    public static void CheckFinancialHostReplacement()
    {
        // Explicit public-host update probe, never a synthetic cursor assignment.
        var series = (FinancialSeries)Page!.Document.Series[0]; var points = series.Points.ToArray();
        points[0] = points[0] with { Volume = points[0].Volume + 1 }; series.SetData(points);
    }
}
