using System.Runtime.CompilerServices;
using FastChartsScene.Core;
using FastChartsScene.Data;

namespace FastChartsScene.Interaction;

public sealed partial class ChartInteraction
{
    private PointQueryResult QueryFinancialX(IDataSource data, Axis x, Axis y, RectD pane, PointD location)
    {
        var result = QueryNearestX(data, x, y, pane, location, _financialIndexes, out var built);
        if (built) FinancialIndexBuildCount++;
        return result;
    }
    private static PointQueryResult QueryNearestX(IDataSource data, Axis x, Axis y, RectD pane, PointD location,
        ConditionalWeakTable<IDataSource, PointSpatialIndex> indexes, out bool built)
    {
        built = false;
        // Reuse an already-built exact source index across modes, field view revisits,
        // panning and resizing. A new immutable source gets a new weak cache entry.
        if (indexes.TryGetValue(data, out var retained)) return retained.NearestX(x, y, pane, location);
        var scanned = 0;
        if (data.IsOrdered)
        {
            var first = DataSearch.LowerBound(data, x.Range.Minimum);
            var end = DataSearch.UpperBound(data, x.Range.Maximum);
            // Binary-search projected X rather than inverse-transforming the cursor.
            // This preserves adjacent-value/duplicate boundaries on log/inverted axes.
            double Project(int index) => pane.X + x.ToUnit(data.X(index)) * pane.Width;
            var a = first; var b = end;
            while (a < b)
            {
                var middle = a + (b - a) / 2;
                if (x.Inverted ? Project(middle) > location.X : Project(middle) < location.X) a = middle + 1;
                else b = middle;
            }
            var left = a - 1; var right = a;
            IndexedPointHit? best = null;
            var bestX = double.PositiveInfinity; var bestY = double.PositiveInfinity;
            while (left >= first || right < end)
            {
                var dxLeft = left >= first ? Math.Abs(Project(left) - location.X) : double.PositiveInfinity;
                var dxRight = right < end ? Math.Abs(Project(right) - location.X) : double.PositiveInfinity;
                var dx = Math.Min(dxLeft, dxRight);
                if (best != null && dx > bestX) return new(best, 0, scanned);
                // Bound pathological duplicate or clipped groups. The retained tree also
                // bounds Y and resolves vertical ties without linear duplicate scans.
                if (scanned == 2048) break;
                var index = dxLeft <= dxRight ? left-- : right++;
                scanned++;
                var raw = new PointD(data.X(index), data.Y(index));
                if (!raw.IsFinite || raw.Y < y.Range.Minimum || raw.Y > y.Range.Maximum) continue;
                var screen = new PointD(Project(index), pane.Bottom - y.ToUnit(raw.Y) * pane.Height);
                if (!screen.IsFinite) continue;
                var dy = Math.Abs(screen.Y - location.Y);
                if (best != null && (dx > bestX || dx == bestX && (dy > bestY || dy == bestY && index >= best.Value.Index))) continue;
                bestX = dx; bestY = dy;
                var maximum = Math.Max(dx, dy);
                var distance = maximum == 0 ? 0 : maximum * Math.Sqrt((dx / maximum) * (dx / maximum) + (dy / maximum) * (dy / maximum));
                best = new(index, raw, screen, distance);
            }
            if (left < first && right >= end) return new(best, 0, scanned);
        }
        var indexTree = new PointSpatialIndex(data); indexes.Add(data, indexTree); built = true;
        var query = indexTree.NearestX(x, y, pane, location);
        return query with { TestedSamples = scanned + query.TestedSamples };
    }
}
