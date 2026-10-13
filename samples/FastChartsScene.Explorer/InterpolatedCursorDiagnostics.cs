using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
namespace FastChartsScene.Explorer;

public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string CursorTrackingMode() => $"{Page!.CursorTable.TrackingMode}/{Page.Chart.CursorValuesTrackingMode}";
#if __WASM__
    [JSExport]
#endif
    public static int CursorTrackingTests() => Page!.Chart.Interaction?.LastCursorValuesTests ?? 0;
#if __WASM__
    [JSExport]
#endif
    public static string CursorTrackingOptionBounds(int index)
    {
        var combo = CursorTableChildren(Page!.CursorTable).OfType<ComboBox>().Single();
        return combo.IsDropDownOpen && index >= 0 && index < combo.Items.Count && combo.ContainerFromIndex(index) is FrameworkElement item ? Bounds(item) : "";
    }
#if __WASM__
    [JSExport]
#endif
    public static string CheckInterpolatedCursorContracts()
    {
        var table = new CursorResultsTable { TrackingModeSelectionEnabled = true, TrackingMode = SeriesCursorTrackingMode.Interpolated };
        var value = new SeriesCursorValue("trace", "Source, \"quoted\"", new(1, 0, 0, 1), 4, new(Math.PI, Math.E), "x", "y", null, null)
        { Interpolation = new(new(7, 8), 5, new(10, 12), LineInterpolation.Linear) };
        table.SetValues([value]); var row = table.Rows[0]; var snapshot = table.Snapshot();
        if (row.XText != "7" || row.YText != "8" || row.IndexText != "4–5" || row.FieldText != "Calc Y" ||
            !row.DetailText.Contains("From 4: (3.141592653589793, 2.718281828459045) to 5: (10, 12)"))
            throw new InvalidOperationException("Calculated row does not distinguish result and original endpoints.");
        var x = row.XText; var y = row.YText; var index = row.IndexText; var detail = row.DetailText;
        table.SetValues([value with { Color = new(0, 1, 0, 1) }]);
        if (!ReferenceEquals(x, row.XText) || !ReferenceEquals(y, row.YText) || !ReferenceEquals(index, row.IndexText) || !ReferenceEquals(detail, row.DetailText))
            throw new InvalidOperationException("Color-only changes regenerated calculated numeric cells.");
        var csv = table.ExportCsv();
        if (!csv.Contains("\"Source, \"\"quoted\"\"\",4,3.141592653589793,2.718281828459045,\"Y\"") ||
            !csv.Contains(",Interpolated,7,8,5,10,12,\"Linear\"\r\n"))
            throw new InvalidOperationException("CSV lost original precision, escaping or separate calculated provenance.");
        table.SetValues([value with { Title = "  =1+1", SeriesId = "@source", XAxisId = "-axis", YAxisId = "\t+axis" }]);
        var guardedCsv = table.ExportCsv();
        if (!guardedCsv.Contains("\"'  =1+1\"") || !guardedCsv.Contains("\"'@source\"") ||
            !guardedCsv.Contains("\"'-axis\"") || !guardedCsv.Contains("\"'\t+axis\""))
            throw new InvalidOperationException("Cursor CSV did not neutralize spreadsheet formula metadata.");
        table.SetValues([value]);
        var changed = 0; table.TrackingModeChanged += (_, _) => changed++;
        try { table.SetValue(CursorResultsTable.TrackingModeProperty, (SeriesCursorTrackingMode)99); throw new InvalidOperationException("Invalid table mode accepted."); }
        catch (ArgumentOutOfRangeException) { }
        if (table.TrackingMode != SeriesCursorTrackingMode.Interpolated || table.Rows.Count != 1 || changed != 0)
            throw new InvalidOperationException("Invalid table mode mutated valid state.");
        table.TrackingMode = SeriesCursorTrackingMode.NearestX;
        if (changed != 1 || table.Rows.Count != 0 || table.IsHeld || snapshot[0] != value)
            throw new InvalidOperationException("Mode change did not clear live state or corrupted an owned snapshot.");
        table.SetValues([value with { Interpolation = null }]);
        if (table.ExportCsv().Split('\n')[0].TrimEnd('\r') != "SeriesId,SeriesTitle,Index,X,Value,Field,XAxis,YAxis,Open,High,Low,Close,Volume")
            throw new InvalidOperationException("Default original CSV contract changed.");
        using var chart = new ChartControl { CursorValuesTrackingMode = SeriesCursorTrackingMode.Interpolated };
        try { chart.SetValue(ChartControl.CursorValuesTrackingModeProperty, (SeriesCursorTrackingMode)99); throw new InvalidOperationException("Invalid chart mode accepted."); }
        catch (ArgumentOutOfRangeException) { }
        if (chart.CursorValuesTrackingMode != SeriesCursorTrackingMode.Interpolated) throw new InvalidOperationException("Invalid chart mode was not restored.");
        return "passed";
    }
    private sealed class CursorCallbackData(IDataSource source) : IDataSource
    {
        public Action? Once;
        public int Count => source.Count;
        public bool IsOrdered => source.IsOrdered;
        public double X(int index) { var callback = Once; Once = null; callback?.Invoke(); return source.X(index); }
        public double Y(int index) => source.Y(index);
    }
    private static Action? _cancelCursorModeReview;
    private static bool _cursorModeMutated, _cursorModeReentered, _cursorModeStale;
#if __WASM__
    [JSExport]
#endif
    public static void ArmCursorModeReentry()
    {
        CancelCursorModeReentry(); var chart = Page!.Chart;
        if (chart.CursorValuesTrackingMode != SeriesCursorTrackingMode.NearestX) throw new InvalidOperationException("The native fixture must start in Nearest X mode.");
        var line = Page.Document.Series.OfType<LineSeries>().First(); var source = new CursorCallbackData(line.Data);
        _cursorModeMutated = _cursorModeReentered = _cursorModeStale = false;
        void Changed(object? sender, EventArgs e)
        {
            if (_cursorModeReentered && chart.SeriesCursorValues.Any(v => v.IsInterpolated)) _cursorModeStale = true;
            if (_cursorModeMutated || !chart.CursorValuesAreActive || chart.SeriesCursorValues.Count < 2) return;
            _cursorModeMutated = true;
            // Arm only inside a real published batch, then refresh by the public mode
            // property. The getter callback therefore occurs inside that new query,
            // not during compilation or a separate nearest-point hit test.
            source.Once = () => { _cursorModeReentered = true; chart.CursorValuesTrackingMode = SeriesCursorTrackingMode.NearestX; };
            chart.CursorValuesTrackingMode = SeriesCursorTrackingMode.Interpolated;
        }
        chart.SeriesCursorValuesChanged += Changed; line.Data = source;
        _cancelCursorModeReview = () => { chart.SeriesCursorValuesChanged -= Changed; source.Once = null; };
    }
#if __WASM__
    [JSExport]
#endif
    public static string FinishCursorModeReentry()
    {
        CancelCursorModeReentry(); var chart = Page!.Chart;
        if (!_cursorModeMutated || !_cursorModeReentered || _cursorModeStale || !chart.CursorValuesEnabled || !chart.CursorValuesAreActive ||
            chart.CursorValuesTrackingMode != SeriesCursorTrackingMode.NearestX || chart.SeriesCursorValues.Count != 2 || chart.SeriesCursorValues.Any(v => v.IsInterpolated))
            throw new InvalidOperationException("Reentrant cursor mode change published obsolete values or erased the newer batch.");
        return "passed";
    }
#if __WASM__
    [JSExport]
#endif
    public static void CancelCursorModeReentry()
    { var cancel = _cancelCursorModeReview; _cancelCursorModeReview = null; cancel?.Invoke(); }
}
