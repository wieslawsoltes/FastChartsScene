using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string CheckCursorRowFormattingReuse()
    {
        var table = new CursorResultsTable();
        var value = new SeriesCursorValue("row", "Original", new(1, 0, 0, 1), 123,
            new(Math.PI, Math.E), "x", "y", null, null);
        table.SetValues([value]); var row = table.Rows[0];
        var x = row.XText; var y = row.YText; var index = row.IndexText; var field = row.FieldText;
        object? firstEvent = null; var events = 0;
        row.PropertyChanged += (_, args) =>
        {
            if (firstEvent == null) firstEvent = args;
            else if (!ReferenceEquals(firstEvent, args)) throw new InvalidOperationException("Row change notification was allocated again.");
            events++;
        };
        value = value with { Title = "Renamed" }; table.SetValues([value]);
        if (!ReferenceEquals(x, row.XText) || !ReferenceEquals(y, row.YText) || !ReferenceEquals(index, row.IndexText) ||
            !ReferenceEquals(field, row.FieldText) || !row.DetailText.StartsWith("Renamed "))
            throw new InvalidOperationException("Title update regenerated unrelated numeric cells.");
        var detail = row.DetailText; var swatch = row.Swatch;
        value = value with { Color = new ColorRgba(0, 1, 0, 1) }; table.SetValues([value]);
        if (!ReferenceEquals(detail, row.DetailText) || !ReferenceEquals(x, row.XText) || !ReferenceEquals(y, row.YText) ||
            ReferenceEquals(swatch, row.Swatch) || row.Swatch.Color.G != 255)
            throw new InvalidOperationException("Color update regenerated unrelated values or retained the wrong swatch.");
        value = value with { Data = new(Math.PI + .25, Math.E) }; table.SetValues([value]);
        if (!ReferenceEquals(y, row.YText) || ReferenceEquals(x, row.XText) || !ReferenceEquals(row, table.Rows[0]) ||
            !row.DetailText.Contains((Math.PI + .25).ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("X update did not preserve Y or full-precision details.");
        table.SetValues([value]);
        if (events != 3 || table.CreatedRows != 1 || table.UpdatedRows != 4 || row.Value != value)
            throw new InvalidOperationException("Row notification or update counts differ.");
        return "passed";
    }
}
