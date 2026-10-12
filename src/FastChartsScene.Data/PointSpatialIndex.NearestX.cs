using FastChartsScene.Core;

namespace FastChartsScene.Data;

public sealed partial class PointSpatialIndex
{
    /// <summary>Find the visible original sample nearest in screen X, then screen Y,
    /// then original index. Radius limits only horizontal distance. Distance in the
    /// returned hit is still the full Euclidean screen distance. Reuses this index's
    /// immutable nodes; transforms, inversion and transposition require no rebuild.
    /// Queries allocate no heap storage. Worst-case queries can visit every sample.</summary>
    public PointQueryResult NearestX(Axis x, Axis y, RectD plot, PointD location,
        double radius = double.MaxValue, bool transpose = false)
    {
        ArgumentNullException.ThrowIfNull(x); ArgumentNullException.ThrowIfNull(y);
        if (!location.IsFinite || !double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(plot.X) || !double.IsFinite(plot.Y) || !double.IsFinite(plot.Right) || !double.IsFinite(plot.Bottom) ||
            !double.IsFinite(plot.Width) || !double.IsFinite(plot.Height) || plot.Width <= 0 || plot.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(plot));
        if (_nodes.Length == 0 || !plot.Contains(location)) return default;
        var query = new XQuery(this, x, y, plot, location, radius, transpose); query.Visit(0);
        return new(query.Hit, query.Visited, query.Tested);
    }

    private struct XQuery(PointSpatialIndex owner, Axis x, Axis y, RectD plot, PointD location, double radius, bool transpose)
    {
        public IndexedPointHit? Hit;
        public int Visited, Tested;
        private double _dx = radius, _dy = double.PositiveInfinity;
        private (double X, double Y) Bound(Node node)
        {
            var minX = Math.Max(transpose ? node.MinY : node.MinX, x.Range.Minimum);
            var maxX = Math.Min(transpose ? node.MaxY : node.MaxX, x.Range.Maximum);
            var minY = Math.Max(transpose ? node.MinX : node.MinY, y.Range.Minimum);
            var maxY = Math.Min(transpose ? node.MaxX : node.MaxY, y.Range.Maximum);
            if (minX > maxX || minY > maxY) return (double.PositiveInfinity, double.PositiveInfinity);
            var a = plot.X + x.ToUnit(minX) * plot.Width; var b = plot.X + x.ToUnit(maxX) * plot.Width;
            var c = plot.Bottom - y.ToUnit(minY) * plot.Height; var d = plot.Bottom - y.ToUnit(maxY) * plot.Height;
            return (Math.Abs(location.X - Math.Clamp(location.X, Math.Min(a, b), Math.Max(a, b))),
                Math.Abs(location.Y - Math.Clamp(location.Y, Math.Min(c, d), Math.Max(c, d))));
        }
        public void Visit(int index)
        {
            var node = owner._nodes[index]; Visited++;
            var lower = Bound(node);
            if (lower.X > _dx || lower.X == _dx && (lower.Y > _dy || lower.Y == _dy &&
                Hit is { } previous && node.MinimumIndex >= previous.Index)) return;
            if (node.Left >= 0)
            {
                var a = node.Left; var b = node.Right;
                var first = Bound(owner._nodes[a]); var second = Bound(owner._nodes[b]);
                if (second.X < first.X || second.X == first.X && second.Y < first.Y) (a, b) = (b, a);
                Visit(a); Visit(b); return;
            }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var original = owner._order[i]; var raw = new PointD(owner._data.X(original), owner._data.Y(original));
                var px = transpose ? raw.Y : raw.X; var py = transpose ? raw.X : raw.Y; Tested++;
                if (px < x.Range.Minimum || px > x.Range.Maximum || py < y.Range.Minimum || py > y.Range.Maximum) continue;
                var screen = new PointD(plot.X + x.ToUnit(px) * plot.Width, plot.Bottom - y.ToUnit(py) * plot.Height);
                if (!screen.IsFinite) continue;
                var dx = Math.Abs(screen.X - location.X); var dy = Math.Abs(screen.Y - location.Y);
                if (dx > _dx || dx == _dx && (dy > _dy || dy == _dy && Hit is { } current && original >= current.Index)) continue;
                _dx = dx; _dy = dy;
                var maximum = Math.Max(dx, dy);
                var distance = maximum == 0 ? 0 : maximum * Math.Sqrt((dx / maximum) * (dx / maximum) + (dy / maximum) * (dy / maximum));
                Hit = new(original, raw, screen, distance);
            }
        }
    }
}
