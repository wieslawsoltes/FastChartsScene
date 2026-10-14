using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Interaction;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
namespace FastChartsScene.Explorer;

public sealed record CursorTableSnapshot(bool Enabled, bool Active, bool Held, string Status,
    long Created, long Updated, long Queries, long Published, int Realized,
    SeriesCursorValue[] Values, string[] PaintedTexts);
public sealed record CursorTableScrollSnapshot(double Offset, double Extent, double Viewport, double Maximum, RectD Bounds);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CursorTableSnapshot))]
[JsonSerializable(typeof(CursorTableScrollSnapshot))]
internal partial class CursorTableJsonContext : JsonSerializerContext { }

public static partial class BrowserBridge
{
    private static IEnumerable<DependencyObject> CursorTableChildren(DependencyObject node)
    {
        yield return node;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in CursorTableChildren(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    // The native ComboBox template also owns a ListView (and may own an editable
    // TextBox). Resolve the table's named parts, not arbitrary template descendants.
    // Single keeps missing/duplicate named parts a diagnostic failure.
    private static T CursorTablePart<T>(string name) where T : FrameworkElement
        => CursorTableChildren(Page!.CursorTable).OfType<T>().Single(part => part.Name == name);
#if __WASM__
    [JSExport]
#endif
    public static string InspectCursorTable()
    {
        var table = Page!.CursorTable; var chart = Page.Chart;
        var visual = CursorTableChildren(table).ToArray();
        return JsonSerializer.Serialize(new CursorTableSnapshot(chart.CursorValuesEnabled, chart.CursorValuesAreActive,
            table.IsHeld, table.StatusText, table.CreatedRows, table.UpdatedRows, chart.CursorValuesQueryCount,
            chart.CursorValuesPublishCount, CursorTableChildren(CursorTablePart<ListView>("Results")).OfType<ListViewItem>().Count(), table.Snapshot().ToArray(),
            visual.OfType<TextBlock>().Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text).ToArray()), CursorTableJsonContext.Default.CursorTableSnapshot);
    }
#if __WASM__
    [JSExport]
#endif
    public static string CursorTableBounds(string name)
    {
        var table = Page!.CursorTable;
        var target = name == "$table" ? table : CursorTableChildren(table).OfType<FrameworkElement>().First(e => AutomationProperties.GetName(e) == name);
        return Bounds(target);
    }
#if __WASM__
    [JSExport]
#endif
    public static string CursorTableScroll()
    {
        var list = CursorTablePart<ListView>("Results");
        var scroll = CursorTableChildren(list).OfType<ScrollViewer>().First();
        var origin = scroll.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        return JsonSerializer.Serialize(new CursorTableScrollSnapshot(scroll.VerticalOffset, scroll.ExtentHeight,
            scroll.ViewportHeight, scroll.ScrollableHeight, new RectD(origin.X, origin.Y, scroll.ActualWidth, scroll.ActualHeight)),
            CursorTableJsonContext.Default.CursorTableScrollSnapshot);
    }
#if __WASM__
    [JSExport]
#endif
    public static string CursorTableRowBounds(int index)
    {
        var list = CursorTablePart<ListView>("Results");
        return list.ContainerFromIndex(index) is FrameworkElement item ? Bounds(item) : "";
    }
#if __WASM__
    [JSExport]
#endif
    public static string CursorTableDetails() => CursorTablePart<TextBox>("Details").Text;
#if __WASM__
    [JSExport]
#endif
    public static string CheckCursorTableReuse()
    {
        var table = new CursorResultsTable();
        var value = new SeriesCursorValue("id", "Source <&> \"quoted\"", new(1,0,0,1), 3, new(4,12), "x", "y", null, null);
        SeriesCursorValue[] input = [value]; table.SetValues(input); var row = table.Rows[0];
        input[0] = value with { Data = new(5, 20) };
        if (table.Rows[0].Value != value) throw new InvalidOperationException("Table retained the caller's mutable array.");
        table.SetValues([value]);
        if (!ReferenceEquals(row, table.Rows[0]) || table.CreatedRows != 1 || table.UpdatedRows != 1) throw new InvalidOperationException("Unchanged values reconstructed rows.");
        table.SetValues(input); if (!ReferenceEquals(row, table.Rows[0]) || table.UpdatedRows != 2) throw new InvalidOperationException("Changed values did not update in place.");
        var snapshot = table.Snapshot(); table.Clear(); if (snapshot[0] != input[0] || table.Rows.Count != 0) throw new InvalidOperationException("Clear invalidated an owned snapshot.");
        return "passed";
    }
    private static Action? _cancelTableReview;
    private static bool _tableReviewMutated, _tableReviewStale;
#if __WASM__
    [JSExport]
#endif
    public static void ArmCursorTableReview()
    {
        _cancelTableReview?.Invoke();
        var chart = Page!.Chart; var document = Page.Document;
        var line = document.Series.OfType<FastChartsScene.Charts.LineSeries>().First();
        _tableReviewMutated = _tableReviewStale = false;
        void FinancialChanged(object? sender, EventArgs e)
        {
            if (_tableReviewMutated || chart.FinancialHit == null) return;
            _tableReviewMutated = true; line.CursorTrackEnabled = false;
        }
        void ValuesChanged(object? sender, EventArgs e)
        {
            if (_tableReviewMutated && chart.SeriesCursorValues.Any(v => v.SeriesId == line.Id)) _tableReviewStale = true;
        }
        chart.FinancialHitChanged += FinancialChanged; chart.SeriesCursorValuesChanged += ValuesChanged;
        _cancelTableReview = () => { chart.FinancialHitChanged -= FinancialChanged; chart.SeriesCursorValuesChanged -= ValuesChanged; };
    }
#if __WASM__
    [JSExport]
#endif
    public static string FinishCursorTableReview()
    {
        CancelCursorTableReview();
        if (!_tableReviewMutated || _tableReviewStale) throw new InvalidOperationException("Stale all-series event batch or unexercised mutation.");
        return "passed";
    }
#if __WASM__
    [JSExport]
#endif
    public static void CancelCursorTableReview()
    { var cancel = _cancelTableReview; _cancelTableReview = null; cancel?.Invoke(); }
}
