using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Rendering;

namespace FastChartsScene.Interaction;

/// <summary>Exact stored heatmap sample and its visible cell rectangle. Value is null only for a painted missing-data cell.</summary>
public sealed record ScalarDataHit(string SeriesId, string Title, int Column, int Row, PointD Data,
    PointD Screen, double? Value, ColorRgba Color, RectD CellBounds, string ValueTitle, string ValueUnits);

public sealed partial class ChartInteraction
{
    /// <summary>Constant-time cell lookup per visible regular heatmap, in reverse draw order.
    /// Transparent gaps/outliers allow picking an underlying heatmap; contours and 3D need separate picking tools.</summary>
    public ScalarDataHit? HitTestScalar(RenderFrame frame, PointD location)
    {
        ArgumentNullException.ThrowIfNull(frame); ObjectDisposedException.ThrowIf(_document.IsDisposed, _document);
        if (!location.IsFinite) throw new ArgumentOutOfRangeException(nameof(location));
        if (_document.View != ChartView.XY || frame.Metadata.Revision != _document.Revision || !frame.Layout.Plot.Contains(location)) return null;
        if (frame.Metadata.Hits.Any(h => h.Kind == "legend" && h.Bounds.Contains(location))) return null;
        foreach (var series in _document.Series.Reverse().OfType<HeatmapSeries>().Where(s => s.Visible && s.GetType() == typeof(HeatmapSeries)))
        {
            var pane = frame.Layout.ForAxis(series.YAxisId); if (!pane.Contains(location)) continue;
            var x = _document.FindX(series.XAxisId); var y = _document.FindY(series.YAxisId); var grid = series.Grid;
            var point = frame.Layout.FromScreen(x, y, location);
            if (!grid.TryLocate(point, out var column, out var row)) continue;
            var value = grid[column, row]; var color = series.ResolveColorScale().Color(value); if (color.A <= 0) continue;
            if (!IntensityGeometry.TryCellBounds(frame.Layout, x, y, grid, column, row, out var cell) || !cell.Contains(location)) continue;
            var data = new PointD(grid.X(column), grid.Y(row));
            return new(series.Id, series.Title, column, row, data, new(cell.X + cell.Width / 2, cell.Y + cell.Height / 2),
                double.IsNaN(value) ? null : value, color, cell, series.ValueTitle, series.ValueUnits);
        }
        return null;
    }
}
