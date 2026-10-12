using System.Runtime.CompilerServices;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Rendering;

namespace FastChartsScene.Interaction;

/// <summary>One exact original candle, its selected price coordinate, and projection.
/// This is a nearest-price sample query, not interpolation, aggregation or candle-area picking.</summary>
public sealed record FinancialDataHit(string SeriesId, string Title, int Index, OhlcPoint Sample,
    FinancialValueField Field, PointD Data, PointD Screen, double Distance,
    string XAxisId, string YAxisId, RectD PaneBounds);

public sealed partial class ChartInteraction
{
    private readonly ConditionalWeakTable<IDataSource, PointSpatialIndex> _financialIndexes = new();
    public long FinancialIndexBuildCount { get; private set; }
    public int LastFinancialQueryVisits { get; private set; }
    public int LastFinancialQueryTests { get; private set; }

    /// <summary>Nearest visible original financial price, using each series's CursorField.
    /// Ordered data is searched within the projected tolerance window; larger unordered or
    /// duplicate groups use a retained source-space index. Equal distances prefer the last
    /// drawn series, then the lowest original candle index. Source order is never changed.</summary>
    public FinancialDataHit? HitTestFinancial(RenderFrame frame, PointD location, double tolerance = 12)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_document.IsDisposed, _document);
        if (!location.IsFinite) throw new ArgumentOutOfRangeException(nameof(location));
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        LastFinancialQueryVisits = LastFinancialQueryTests = 0;
        if (_document.View != ChartView.XY || frame.Metadata.Revision != _document.Revision || !frame.Layout.Plot.Contains(location) ||
            frame.Metadata.Hits.Any(h => h.Kind == "legend" && h.Bounds.Contains(location))) return null;
        FinancialDataHit? best = null;
        for (var i = _document.Series.Count - 1; i >= 0; i--)
        {
            if (_document.Series[i] is not FinancialSeries { Visible: true, CursorTrackEnabled: true } series ||
                series.Style == FinancialStyle.Ohlc && series.StrokeWidth <= 0) continue;
            var pane = frame.Layout.ForAxis(series.YAxisId);
            if (!pane.Contains(location)) continue;
            var x = _document.FindX(series.XAxisId); var y = _document.FindY(series.YAxisId);
            var source = series.GetPriceData(series.CursorField);
            var query = QuerySource(source, x, y, pane, location, best?.Distance ?? tolerance, false, _financialIndexes, out var built);
            if (built) FinancialIndexBuildCount++;
            LastFinancialQueryVisits += query.VisitedNodes; LastFinancialQueryTests += query.TestedSamples;
            if (query.Hit is { } hit && (best == null || hit.Distance < best.Distance))
                best = new(series.Id, series.Title, hit.Index, series.Points[hit.Index], series.CursorField,
                    hit.Data, hit.Screen, hit.Distance, x.Id, y.Id, pane);
        }
        return best;
    }

    private void ClearFinancialPickingCache()
    { _financialIndexes.Clear(); LastFinancialQueryVisits = LastFinancialQueryTests = 0; }
}
