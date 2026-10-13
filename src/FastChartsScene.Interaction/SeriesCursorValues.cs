using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Rendering;

namespace FastChartsScene.Interaction;

/// <summary>One original source value. Contains no pointer-dependent distance, screen
/// coordinate or mutable source reference, so a copied value is a stable snapshot.</summary>
public readonly record struct SeriesCursorValue(string SeriesId, string Title, ColorRgba Color,
    int Index, PointD Data, string XAxisId, string YAxisId, FinancialValueField? Field, OhlcPoint? Candle);

public sealed partial class ChartInteraction
{
    public long CursorValuesIndexBuildCount { get; private set; }
    public int LastCursorValuesTests { get; private set; }

    /// <summary>Replace caller-owned output with one nearest-X original value per eligible
    /// line/scatter/financial series in the hovered pane, in document order. Horizontal
    /// distance, vertical distance and original index break ties within each series.
    /// No interpolation and no cross-series winner filtering. Hidden/untracked/clipped
    /// values are excluded. Financial CursorField is honored; this table query always
    /// uses nearest-X independently of each series's single-cursor snap setting.
    /// Reuse a List to avoid per-query result allocations after capacity/index warmup.
    /// Invalid arguments leave output unchanged; stale/off-plot frames clear it.
    /// Models and output are UI-thread-affine. Custom sources must honor IDataSource.</summary>
    public void QuerySeriesCursorValues(RenderFrame frame, PointD location, List<SeriesCursorValue> output)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(output);
        ObjectDisposedException.ThrowIf(_document.IsDisposed, _document);
        if (!location.IsFinite) throw new ArgumentOutOfRangeException(nameof(location));
        LastCursorValuesTests = 0; output.Clear();
        if (_document.View != ChartView.XY || frame.Metadata.Revision != _document.Revision || !frame.Layout.Plot.Contains(location)) return;
        foreach (var region in frame.Metadata.Hits)
            if (region.Kind == "legend" && region.Bounds.Contains(location)) return;
        for (var seriesIndex = 0; seriesIndex < _document.Series.Count; seriesIndex++)
        {
            var series = _document.Series[seriesIndex];
            if (!series.Visible || !series.CursorTrackEnabled) continue;
            IDataSource data;
            FinancialValueField? field = null;
            if (series is FinancialSeries financial)
            {
                if (financial.Style == FinancialStyle.Ohlc && financial.StrokeWidth <= 0) continue;
                field = financial.CursorField; data = financial.GetPriceData(field.Value);
            }
            else if (series is LineSeries line)
            {
                if (line.Color.A <= 0 || !line.FillToBaseline && line.Marker == MarkerShape.None && (!line.ShowLine || line.StrokeWidth <= 0)) continue;
                data = line.Data;
            }
            else continue;
            var pane = frame.Layout.ForAxis(series.YAxisId); if (!pane.Contains(location)) continue;
            var x = _document.FindX(series.XAxisId); var y = _document.FindY(series.YAxisId);
            var cache = field.HasValue ? _financialIndexes : _sampleIndexes;
            var query = QueryNearestX(data, x, y, pane, location, cache, out var built);
            if (built)
            {
                CursorValuesIndexBuildCount++;
                if (field.HasValue) FinancialIndexBuildCount++; else SampleIndexBuildCount++;
            }
            LastCursorValuesTests += query.TestedSamples;
            if (query.Hit is not { } hit) continue;
            output.Add(new(series.Id, series.Title, series.Color, hit.Index, hit.Data, x.Id, y.Id,
                field, series is FinancialSeries f ? f.Points[hit.Index] : null));
        }
    }
}
