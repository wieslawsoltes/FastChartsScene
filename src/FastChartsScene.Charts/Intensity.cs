using FastChartsScene.Core;

namespace FastChartsScene.Charts;

public sealed partial class ScalarGrid
{
    private static double Coordinate(double minimum, double maximum, int index, int count)
    {
        if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
        if (count == 1) return minimum + (maximum - minimum) / 2;
        if (index == count - 1) return maximum;
        return minimum + (maximum - minimum) * (index / (double)(count - 1));
    }
    /// <summary>Sample-centered cell boundaries, clipped to the declared grid extents.</summary>
    public RectD CellBounds(int column, int row)
    {
        var x = X(column); var y = Y(row);
        var left = column == 0 ? XMinimum : X(column - 1) + (x - X(column - 1)) / 2;
        var right = column == Columns - 1 ? XMaximum : x + (X(column + 1) - x) / 2;
        var bottom = row == 0 ? YMinimum : Y(row - 1) + (y - Y(row - 1)) / 2;
        var top = row == Rows - 1 ? YMaximum : y + (Y(row + 1) - y) / 2;
        return new(left, bottom, right - left, top - bottom);
    }
    /// <summary>Locate the rendered cell, not an invented interpolated value. Boundary ties use
    /// the higher column/row. Single-row/column grids occupy their full declared extent.</summary>
    public bool TryLocate(PointD point, out int column, out int row)
    {
        column = row = -1;
        if (!point.IsFinite || point.X < XMinimum || point.X > XMaximum || point.Y < YMinimum || point.Y > YMaximum) return false;
        static int Locate(double value, double minimum, double maximum, int count)
        {
            if (count == 1) return 0;
            var unit = (value - minimum) / (maximum - minimum);
            return Math.Clamp((int)Math.Floor(unit * (count - 1) + .5), 0, count - 1);
        }
        column = Locate(point.X, XMinimum, XMaximum, Columns); row = Locate(point.Y, YMinimum, YMaximum, Rows); return true;
    }
}

public partial class HeatmapSeries
{
    private ColorScale _colorScale = ColorScale.Default;
    private bool _showColorBar;
    private string _valueTitle = "Value", _valueUnits = "";
    private ScalarGrid? _resolvedGrid;
    private ColorScale? _resolvedSettings;
    private ResolvedColorScale? _resolved;
    public ColorScale ColorScale { get => _colorScale; set { ArgumentNullException.ThrowIfNull(value); Set(ref _colorScale, value); } }
    public bool ShowColorBar { get => _showColorBar; set => Set(ref _showColorBar, value); }
    public string ValueTitle { get => _valueTitle; set => Set(ref _valueTitle, value ?? ""); }
    public string ValueUnits { get => _valueUnits; set => Set(ref _valueUnits, value ?? ""); }
    /// <summary>Resolve once per immutable grid/settings identity. Log auto-range includes only positive samples.</summary>
    public ResolvedColorScale ResolveColorScale()
    {
        if (ReferenceEquals(_resolvedGrid, Grid) && ReferenceEquals(_resolvedSettings, ColorScale)) return _resolved!;
        var min = double.PositiveInfinity; var max = double.NegativeInfinity;
        if (ColorScale.Range == null)
            foreach (var value in Grid.Values.Span)
            {
                if (!double.IsFinite(value) || ColorScale.Mode == ColorScaleMode.Logarithmic && value <= 0) continue;
                min = Math.Min(min, value); max = Math.Max(max, value);
            }
        _resolved = ColorScale.Resolve(double.IsFinite(min) ? min : double.NaN, double.IsFinite(max) ? max : double.NaN);
        _resolvedGrid = Grid; _resolvedSettings = ColorScale; return _resolved;
    }
}
