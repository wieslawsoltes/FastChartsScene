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
    public static bool FinancialFrameIsCurrent(double width, double height)
    {
        var page = Page; var chart = page?.Chart; var frame = chart?.Frame;
        return page != null && chart?.Document is { IsDisposed: false } document && frame != null &&
            Math.Abs(page.ActualWidth - width) < .01 && Math.Abs(page.ActualHeight - height) < .01 &&
            Math.Abs(frame.Metadata.Width - chart.ActualWidth) < .01 &&
            Math.Abs(frame.Metadata.Height - chart.ActualHeight) < .01 && frame.Metadata.Revision == document.Revision;
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

public static partial class BrowserBridge
{
    private static Action? _cancelFinancialReview;
    private static bool _financialReviewMutated, _financialReviewStale;
#if __WASM__
    [JSExport]
#endif
    public static void ArmFinancialNotificationReview()
    {
        CancelFinancialNotificationReview();
        var chart = Page!.Chart; var document = Page.Document;
        var series = document.Series.OfType<FinancialSeries>().Single();
        var oldVolume = series.Points[0].Volume;
        _financialReviewMutated = _financialReviewStale = false;
        void ScalarChanged(object? sender, EventArgs args)
        {
            if (_financialReviewMutated || chart.ScalarHit == null || !ReferenceEquals(document, chart.Document)) return;
            _financialReviewMutated = true;
            var points = series.Points.ToArray();
            points[0] = points[0] with { Volume = oldVolume + 1 };
            series.SetData(points); // Real host mutation during synchronous cursor notification.
        }
        void FinancialChanged(object? sender, EventArgs args)
        {
            if (_financialReviewMutated && chart.FinancialHit is { } hit && hit.Sample.Volume == oldVolume)
                _financialReviewStale = true;
        }
        chart.ScalarHitChanged += ScalarChanged; chart.FinancialHitChanged += FinancialChanged;
        _cancelFinancialReview = () => { chart.ScalarHitChanged -= ScalarChanged; chart.FinancialHitChanged -= FinancialChanged; };
    }
#if __WASM__
    [JSExport]
#endif
    public static string FinishFinancialNotificationReview()
    {
        CancelFinancialNotificationReview();
        if (!_financialReviewMutated || _financialReviewStale)
            throw new InvalidOperationException("Cursor event batch published a stale financial result or did not exercise the host mutation.");
        return "passed";
    }
#if __WASM__
    [JSExport]
#endif
    public static void CancelFinancialNotificationReview()
    {
        var cancel = _cancelFinancialReview; _cancelFinancialReview = null; cancel?.Invoke();
    }
}
