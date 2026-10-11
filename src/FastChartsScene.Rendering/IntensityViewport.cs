using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

/// <summary>One visible source cell interval in logical screen pixels.</summary>
public readonly record struct ProjectedCellSpan(int Index, double Start, double Length);

/// <summary>Separable regular-grid projection. Searches exact cell boundaries before mapping
/// only visible columns/rows. Coordinates retain binary64 precision and logarithmic clipping;
/// no scalar samples are resampled or dropped inside the viewport.</summary>
public sealed class IntensityViewport
{
    public ReadOnlyMemory<ProjectedCellSpan> Columns { get; }
    public ReadOnlyMemory<ProjectedCellSpan> Rows { get; }
    public long CellCount => (long)Columns.Length * Rows.Length;
    private IntensityViewport(ProjectedCellSpan[] columns, ProjectedCellSpan[] rows)
    { Columns = columns; Rows = rows; }
    public static IntensityViewport Create(ChartLayout layout, Axis x, Axis y, ScalarGrid grid)
    {
        ArgumentNullException.ThrowIfNull(layout); ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y); ArgumentNullException.ThrowIfNull(grid);
        var pane = layout.ForAxis(y.Id);
        if (x.Range.Maximum <= grid.XMinimum || x.Range.Minimum >= grid.XMaximum ||
            y.Range.Maximum <= grid.YMinimum || y.Range.Minimum >= grid.YMaximum) return new([], []);
        ProjectedCellSpan[] Project(Axis axis, int count, bool vertical)
        {
            (double Start, double End) Cell(int index)
            {
                var cell = vertical ? grid.CellBounds(0, index) : grid.CellBounds(index, 0);
                return vertical ? (cell.Y, cell.Bottom) : (cell.X, cell.Right);
            }
            int LowerBound(bool endBoundary, double value)
            {
                var a = 0; var b = count;
                while (a < b)
                {
                    var m = a + (b - a) / 2; var cell = Cell(m);
                    var skip = endBoundary ? cell.End <= value : cell.Start < value;
                    if (skip) a = m + 1; else b = m;
                }
                return a;
            }
            var first = LowerBound(true, axis.Range.Minimum);
            var end = LowerBound(false, axis.Range.Maximum);
            var projected = new List<ProjectedCellSpan>(Math.Max(0, end - first));
            for (var index = first; index < end; index++)
            {
                var cell = Cell(index);
                var a = axis.ToUnit(Math.Max(cell.Start, axis.Range.Minimum));
                var b = axis.ToUnit(Math.Min(cell.End, axis.Range.Maximum));
                a = vertical ? pane.Bottom - a * pane.Height : pane.X + a * pane.Width;
                b = vertical ? pane.Bottom - b * pane.Height : pane.X + b * pane.Width;
                var lower = Math.Max(vertical ? pane.Y : pane.X, Math.Min(a, b));
                var upper = Math.Min(vertical ? pane.Bottom : pane.Right, Math.Max(a, b));
                if (double.IsFinite(lower) && double.IsFinite(upper) && upper > lower)
                    projected.Add(new(index, lower, upper - lower));
            }
            return projected.ToArray();
        }
        return new(Project(x, grid.Columns, false), Project(y, grid.Rows, true));
    }
}

public sealed partial class SceneCompiler
{
    private static void DrawHeatmap(HeatmapSeries series, ChartLayout layout, Axis x, Axis y, FrameBuilder frame)
    {
        var grid = series.Grid;
        var view = IntensityViewport.Create(layout, x, y, grid);
        if (view.CellCount == 0) return;
        var scale = series.ResolveColorScale();
        foreach (var row in view.Rows.Span)
        foreach (var column in view.Columns.Span)
        {
            var color = scale.Color(grid[column.Index, row.Index]);
            if (color.A > 0) frame.Rect(new(column.Start, row.Start, column.Length, row.Length), color);
        }
    }
}
