using System.Runtime.CompilerServices;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Rendering;

namespace FastChartsScene.Interaction;

public sealed partial class ChartInteraction
{
    private readonly ConditionalWeakTable<IDataSource, PointSpatialIndex> _sampleIndexes = new();
    public long SampleIndexBuildCount { get; private set; }
    public int LastSampleQueryVisits { get; private set; }
    public int LastSampleQueryTests { get; private set; }
    /// <summary>Nearest original sample center, not an interpolated or decimated value.
    /// Equal distances prefer the last drawn series, then its lowest original sample index.
    /// For bars this is the source category/value, not a cumulative-stack geometry query.</summary>
    public DataHit? HitTestData(RenderFrame frame, PointD location, double tolerance = 12)
    {
        ArgumentNullException.ThrowIfNull(frame); ObjectDisposedException.ThrowIf(_document.IsDisposed, _document);
        if (!location.IsFinite || !double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        LastSampleQueryVisits = LastSampleQueryTests = 0;
        if (_document.View != ChartView.XY || frame.Metadata.Revision != _document.Revision || !frame.Layout.Plot.Contains(location) ||
            frame.Metadata.Hits.Any(h => h.Kind == "legend" && h.Bounds.Contains(location))) return null;
        DataHit? best = null;
        for (var s = _document.Series.Count - 1; s >= 0; s--)
        {
            if (_document.Series[s] is not DataSeries { Visible: true, CursorTrackEnabled: true } series || series.Color.A <= 0) continue;
            if (series is LineSeries { FillToBaseline: false, Marker: MarkerShape.None } line && (!line.ShowLine || line.StrokeWidth <= 0)) continue;
            var pane = frame.Layout.ForAxis(series.YAxisId); if (!pane.Contains(location)) continue;
            var x = _document.FindX(series.XAxisId); var y = _document.FindY(series.YAxisId);
            var radius = best?.Distance ?? tolerance; var transpose = series is BarSeries { Horizontal: true };
            var query = QuerySource(series.Data, x, y, pane, location, radius, transpose, _sampleIndexes, out var built);
            if (built) SampleIndexBuildCount++;
            LastSampleQueryVisits += query.VisitedNodes; LastSampleQueryTests += query.TestedSamples;
            if (query.Hit is { } hit && (best == null || hit.Distance < best.Distance))
                best = new(series.Id, series.Title, hit.Index, hit.Data, hit.Screen, hit.Distance);
        }
        return best;
    }
    private static PointQueryResult QuerySource(IDataSource data, Axis x, Axis y, RectD pane,
        PointD location, double radius, bool transpose, ConditionalWeakTable<IDataSource, PointSpatialIndex> indexes, out bool built)
    {
        built = false;
        var first = 0; var end = data.Count;
        if (data.IsOrdered)
            (first, end) = VisibleSourceWindow(data, transpose ? y : x, pane, location, radius, transpose);
        if (end - first <= 2048)
            return Scan(data, x, y, pane, location, radius, transpose, first, end);
        if (!indexes.TryGetValue(data, out var index))
        { index = new(data); indexes.Add(data, index); built = true; }
        return index.Nearest(x, y, pane, location, radius, transpose);
    }
    // Search projected positions directly instead of inverse-transforming the cursor window.
    // This preserves duplicate-X groups and boundary samples even when axis inversion would
    // round an inverse data coordinate by several representable values.
    private static (int First, int End) VisibleSourceWindow(IDataSource data, Axis axis, RectD pane,
        PointD location, double radius, bool vertical)
    {
        var first = DataSearch.LowerBound(data, axis.Range.Minimum);
        var end = DataSearch.UpperBound(data, axis.Range.Maximum);
        var coordinate = vertical ? location.Y : location.X;
        var minimum = Math.BitDecrement(coordinate - radius);
        var maximum = Math.BitIncrement(coordinate + radius);
        var ascending = vertical ? axis.Inverted : !axis.Inverted;
        int Boundary(bool upper)
        {
            var a = first; var b = end;
            while (a < b)
            {
                var middle = a + (b - a) / 2;
                var unit = axis.ToUnit(data.X(middle));
                var screen = vertical ? pane.Bottom - unit * pane.Height : pane.X + unit * pane.Width;
                var skip = ascending ? (upper ? screen <= maximum : screen < minimum) :
                    (upper ? screen >= minimum : screen > maximum);
                if (skip) a = middle + 1; else b = middle;
            }
            return a;
        }
        var lower = Boundary(false); var upper = Boundary(true); return (lower, upper);
    }
    private static PointQueryResult Scan(IDataSource data, Axis x, Axis y, RectD pane, PointD location,
        double radius, bool transpose, int first, int end)
    {
        IndexedPointHit? best = null;
        for (var i = first; i < end; i++)
        {
            var raw = new PointD(data.X(i), data.Y(i)); if (!raw.IsFinite) continue;
            var px = transpose ? raw.Y : raw.X; var py = transpose ? raw.X : raw.Y;
            if (px < x.Range.Minimum || px > x.Range.Maximum || py < y.Range.Minimum || py > y.Range.Maximum) continue;
            var screen = new PointD(pane.X + x.ToUnit(px) * pane.Width, pane.Bottom - y.ToUnit(py) * pane.Height);
            if (!screen.IsFinite) continue;
            var dx = Math.Abs(screen.X - location.X); var dy = Math.Abs(screen.Y - location.Y);
            var maximum = Math.Max(dx, dy);
            var distance = maximum == 0 ? 0 : maximum * Math.Sqrt((dx / maximum) * (dx / maximum) + (dy / maximum) * (dy / maximum));
            if (distance <= radius && (best == null || distance < best.Value.Distance)) best = new(i, raw, screen, distance);
        }
        return new(best, 0, end - first);
    }
    /// <summary>Release source indexes explicitly on host unload or when no more queries are needed.</summary>
    public void ClearPickingCache() { _sampleIndexes.Clear(); _cursorSegmentIndexes.Clear(); ClearFinancialPickingCache(); LastSampleQueryVisits = LastSampleQueryTests = 0; }
}
