using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

public sealed record ColorBarPlacement(RectD Region, RectD Strip);

/// <summary>Shared renderer/cursor cell projection. Clip to axis domains before logarithmic
/// transformation so a cell crossing zero is not lost when its positive portion is visible.</summary>
public static class IntensityGeometry
{
    public static bool TryCellBounds(ChartLayout layout, Axis x, Axis y, ScalarGrid grid, int column, int row, out RectD screen)
    {
        ArgumentNullException.ThrowIfNull(layout); ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y); ArgumentNullException.ThrowIfNull(grid);
        screen = default; var cell = grid.CellBounds(column, row);
        var left = Math.Max(cell.X, x.Range.Minimum); var right = Math.Min(cell.Right, x.Range.Maximum);
        var lower = Math.Max(cell.Y, y.Range.Minimum); var upper = Math.Min(cell.Bottom, y.Range.Maximum);
        if (left >= right || lower >= upper) return false;
        var a = layout.ToScreen(x, y, new(left, lower)); var b = layout.ToScreen(x, y, new(right, upper));
        if (!a.IsFinite || !b.IsFinite) return false;
        var pane = layout.ForAxis(y.Id);
        var sx = Math.Max(pane.X, Math.Min(a.X, b.X)); var sy = Math.Max(pane.Y, Math.Min(a.Y, b.Y));
        var ex = Math.Min(pane.Right, Math.Max(a.X, b.X)); var ey = Math.Min(pane.Bottom, Math.Max(a.Y, b.Y));
        if (sx >= ex || sy >= ey) return false;
        screen = new(sx, sy, ex - sx, ey - sy); return true;
    }
}
