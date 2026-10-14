using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
namespace FastChartsScene.Explorer;

public static partial class BrowserBridge
{
    private static FrameworkElement CursorFormatPart(string name)
    {
        var button = CursorTablePart<Button>("NumberFormatButton");
        if (name == "$button") return button;
        var content = ((Flyout)button.Flyout).Content;
        return CursorTableChildren(content).OfType<FrameworkElement>().Single(p => AutomationProperties.GetName(p) == name);
    }
#if __WASM__
    [JSExport]
#endif
    public static string CursorFormatBounds(string name) => Bounds(CursorFormatPart(name));
#if __WASM__
    [JSExport]
#endif
    public static string CursorNumberFormats() => Page!.CursorTable.NumberFormat.XFormat + "|" + Page.CursorTable.NumberFormat.ValueFormat;
#if __WASM__
    [JSExport]
#endif
    public static string CursorFormatError() => ((TextBlock)CursorFormatPart("Cursor format validation")).Text;
#if __WASM__
    [JSExport]
#endif
    public static string CheckCursorFormatContracts()
    {
        var value = new SeriesCursorValue("source", "Exact original", new(1, 0, 0, 1), 0, new(Math.PI, Math.E), "x", "y", null, null);
        var table = new CursorResultsTable(); table.SetValues([value]); var row = table.Rows[0];
        var exact = row.DetailText; var xText = row.XText; var swatch = row.Swatch;
        var csv = table.ExportCsv(); var created = table.CreatedRows; var updates = table.UpdatedRows;
        var notifications = 0; row.PropertyChanged += (_, _) => notifications++;
        table.NumberFormat = new("G9", "F2");
        if (row.YText != "2.72" || !ReferenceEquals(xText, row.XText) || !ReferenceEquals(exact, row.DetailText) ||
            !ReferenceEquals(swatch, row.Swatch) || row.Value != value || csv != table.ExportCsv() || table.CreatedRows != created)
            throw new InvalidOperationException("Format-only edit changed original data or unrelated retained presentation.");
        if (table.UpdatedRows != updates + 1 || notifications != 1) throw new InvalidOperationException("Format edit did not update existing rows exactly once.");
        table.NumberFormat = new("G9", "F2");
        if (table.UpdatedRows != updates + 1 || notifications != 1) throw new InvalidOperationException("Equal formats performed redundant updates.");
        table.SetValues([value]);
        if (table.UpdatedRows != updates + 1 || notifications != 1) throw new InvalidOperationException("Unchanged sample regenerated formatted rows.");
        table.NumberFormat = CursorValueFormat.RoundTrip;
        if (row.XText != "3.141592653589793" || row.YText != "2.718281828459045" || row.DetailText != exact)
            throw new InvalidOperationException("Round-trip presentation lost precision.");
        table.SetValue(CursorResultsTable.NumberFormatProperty, null);
        if (table.NumberFormat != CursorValueFormat.Default || row.XText != "3.14159265") throw new InvalidOperationException("Null did not restore compact display.");
        table.Clear(); table.NumberFormat = new("F3", "E2"); table.SetValues([value]);
        if (table.Rows[0].XText != "3.142" || table.Rows[0].YText != "2.72E+000") throw new InvalidOperationException("New rows ignored the retained format.");
        return "passed";
    }
}
