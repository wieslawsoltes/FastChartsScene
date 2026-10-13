using System.Runtime.CompilerServices;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;

namespace FastChartsScene.Interaction;

public sealed partial class ChartInteraction
{
    private readonly ConditionalWeakTable<IDataSource, CursorSegmentIndex> _cursorSegmentIndexes = new();
    public long CursorInterpolationIndexBuildCount { get; private set; }
    private readonly record struct CursorCrossing(int Index, PointD Data, SeriesCursorInterpolation? Interpolation);

    private CursorCrossing? QueryInterpolatedLine(IDataSource data, LineInterpolation method,
        Axis x, Axis y, RectD pane, PointD location)
    {
        var query = new SegmentCursorQuery(data, method, x, y, pane, location);
        if (data.Count == 1) query.TestPoint(0);
        else if (data.Count > 1 && data.IsOrdered)
        {
            // Search the same projected coordinates used by the intersection test.
            // Inverting a logarithmic projection can round across an original knot.
            int Bound(bool upper)
            {
                var lo = 0; var hi = data.Count;
                while (lo < hi)
                {
                    var mid = lo + (hi - lo) / 2; var raw = data.X(mid);
                    var screen = pane.X + x.ToUnit(raw) * pane.Width;
                    var precedes = x.Inverted ? screen > location.X : screen < location.X;
                    if (x.Scale == AxisScale.Logarithmic && raw <= 0 || precedes || upper && screen == location.X) lo = mid + 1;
                    else hi = mid;
                }
                return lo;
            }
            var lower = Bound(false); var upper = Bound(true);
            for (var i = Math.Max(0, lower - 1); i < Math.Min(data.Count - 1, Math.Max(lower, upper)); i++) query.TestSegment(i);
        }
        else if (data.Count > 1)
        {
            if (!_cursorSegmentIndexes.TryGetValue(data, out var index))
            {
                index = RetainSourceIndex(_cursorSegmentIndexes, data, new CursorSegmentIndex(data));
                CursorInterpolationIndexBuildCount++;
            }
            index.Visit(ref query);
        }
        LastCursorValuesTests += query.Tested;
        return query.Hit;
    }

    // Value-type traversal state; no per-query delegates, scratch lists, snapshots or
    // heap result objects. All coordinates/provenance are copied from immutable sources.
    private struct SegmentCursorQuery(IDataSource data, LineInterpolation method, Axis x, Axis y, RectD pane, PointD location)
    {
        public CursorCrossing? Hit;
        public int Tested;
        private double _distance = double.PositiveInfinity;
        private PointD Project(PointD p) => new(pane.X + x.ToUnit(p.X) * pane.Width, pane.Bottom - y.ToUnit(p.Y) * pane.Height);
        public bool MayIntersect(double minX, double maxX, double minY, double maxY)
        {
            minX = Math.Max(minX, x.Range.Minimum); maxX = Math.Min(maxX, x.Range.Maximum);
            minY = Math.Max(minY, y.Range.Minimum); maxY = Math.Min(maxY, y.Range.Maximum);
            if (minX > maxX || minY > maxY) return false;
            var a = Project(new(minX, minY)); var b = Project(new(maxX, maxY));
            if (location.X < Math.Min(a.X, b.X) || location.X > Math.Max(a.X, b.X)) return false;
            return Math.Abs(location.Y - Math.Clamp(location.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y))) <= _distance;
        }
        public void TestPoint(int index)
        {
            Tested++;
            var raw = new PointD(data.X(index), data.Y(index)); var screen = Project(raw);
            Original(index, raw, screen);
        }
        private void Original(int index, PointD raw, PointD screen)
        {
            if (screen.IsFinite && screen.X == location.X && screen.Y >= pane.Y && screen.Y <= pane.Bottom)
                Consider(new(index, raw, null), screen.Y);
        }
        public void TestSegment(int index)
        {
            Tested++;
            var a = new PointD(data.X(index), data.Y(index)); var b = new PointD(data.X(index + 1), data.Y(index + 1));
            var pa = Project(a); var pb = Project(b);
            // Isolated finite knots remain queryable, but a missing/invalid endpoint
            // never becomes a segment across a NaN break or logarithmic domain gap.
            Original(index, a, pa); Original(index + 1, b, pb);
            if (!pa.IsFinite || !pb.IsFinite) return;
            if (method == LineInterpolation.StepBefore)
            {
                var elbow = new PointD(pa.X, pb.Y);
                Edge(pa, elbow, index, a, b, pa, pb); Edge(elbow, pb, index, a, b, pa, pb);
            }
            else if (method == LineInterpolation.StepAfter)
            {
                var elbow = new PointD(pb.X, pa.Y);
                Edge(pa, elbow, index, a, b, pa, pb); Edge(elbow, pb, index, a, b, pa, pb);
            }
            else Edge(pa, pb, index, a, b, pa, pb);
        }
        private void Edge(PointD a, PointD b, int index, PointD sourceA, PointD sourceB, PointD projectedA, PointD projectedB)
        {
            if (location.X < Math.Min(a.X, b.X) || location.X > Math.Max(a.X, b.X)) return;
            double py;
            if (a.X == b.X) py = Math.Clamp(location.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
            else
            {
                var t = Ratio(location.X, a.X, b.X);
                py = Lerp(a.Y, b.Y, t);
            }
            if (!double.IsFinite(py) || py < pane.Y || py > pane.Bottom) return;
            if (location.X == projectedA.X && py == projectedA.Y) { Consider(new(index, sourceA, null), py); return; }
            if (location.X == projectedB.X && py == projectedB.Y) { Consider(new(index + 1, sourceB, null), py); return; }
            var resultX = location.X == projectedA.X ? sourceA.X : location.X == projectedB.X ? sourceB.X : x.FromUnit((location.X - pane.X) / pane.Width);
            var resultY = py == projectedA.Y ? sourceA.Y : py == projectedB.Y ? sourceB.Y : y.FromUnit((pane.Bottom - py) / pane.Height);
            var result = new PointD(resultX, resultY);
            if (result.IsFinite) Consider(new(index, sourceA, new(result, index + 1, sourceB, method)), py);
        }
        private void Consider(CursorCrossing candidate, double py)
        {
            var distance = Math.Abs(py - location.Y);
            // Exact original knots win equal-distance ties; otherwise preserve the
            // lowest original point/segment index, independently of tree traversal.
            if (Hit is { } old && (distance > _distance || distance == _distance &&
                (candidate.Interpolation.HasValue && !old.Interpolation.HasValue ||
                 candidate.Interpolation.HasValue == old.Interpolation.HasValue && candidate.Index >= old.Index))) return;
            Hit = candidate; _distance = distance;
        }
        private static double Ratio(double value, double a, double b)
        {
            var span = b - a;
            return Math.Clamp(double.IsFinite(span) ? (value - a) / span : (value / 2 - a / 2) / (b / 2 - a / 2), 0, 1);
        }
        private static double Lerp(double a, double b, double t) => t == 0 ? a : t == 1 ? b : a * (1 - t) + b * t;
    }

    /// <summary>Retained conservative bounds for freeform adjacent segments. The source
    /// is never sorted or changed; only original segment indices are sorted. Gaps keep
    /// finite endpoints but never acquire a connecting edge. Worst case remains O(N)
    /// when every segment crosses the query. Construction/cold arrays allocate.</summary>
    private sealed class CursorSegmentIndex
    {
        private readonly record struct Segment(int Index, double MinX, double MaxX, double MinY, double MaxY);
        private readonly record struct Node(double MinX, double MaxX, double MinY, double MaxY, int Start, int Count, int Left, int Right);
        private readonly Segment[] _segments;
        private readonly Node[] _nodes;
        public CursorSegmentIndex(IDataSource data)
        {
            var segments = new List<Segment>();
            for (var i = 0; i + 1 < data.Count; i++)
            {
                var ax = data.X(i); var bx = data.X(i + 1); var ay = data.Y(i); var by = data.Y(i + 1);
                if (double.IsNaN(ay) && double.IsNaN(by)) continue;
                var loY = double.IsNaN(ay) ? by : double.IsNaN(by) ? ay : Math.Min(ay, by);
                var hiY = double.IsNaN(ay) ? by : double.IsNaN(by) ? ay : Math.Max(ay, by);
                segments.Add(new(i, Math.Min(ax, bx), Math.Max(ax, bx), loY, hiY));
            }
            _segments = segments.ToArray();
            Array.Sort(_segments, static (a, b) => { var c = a.MinX.CompareTo(b.MinX); return c == 0 ? a.Index.CompareTo(b.Index) : c; });
            var nodes = new List<Node>(); if (_segments.Length > 0) Build(nodes, 0, _segments.Length); _nodes = nodes.ToArray();
        }
        private int Build(List<Node> nodes, int start, int count)
        {
            var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity; var minY = minX; var maxY = maxX;
            for (var i = start; i < start + count; i++)
            {
                var s = _segments[i]; minX = Math.Min(minX, s.MinX); maxX = Math.Max(maxX, s.MaxX);
                minY = Math.Min(minY, s.MinY); maxY = Math.Max(maxY, s.MaxY);
            }
            var at = nodes.Count; nodes.Add(default); var left = -1; var right = -1;
            if (count > 16) { var half = count / 2; left = Build(nodes, start, half); right = Build(nodes, start + half, count - half); }
            nodes[at] = new(minX, maxX, minY, maxY, start, count, left, right); return at;
        }
        public void Visit(ref SegmentCursorQuery query) { if (_nodes.Length > 0) Visit(0, ref query); }
        private void Visit(int index, ref SegmentCursorQuery query)
        {
            var node = _nodes[index];
            if (!query.MayIntersect(node.MinX, node.MaxX, node.MinY, node.MaxY)) return;
            if (node.Left >= 0) { Visit(node.Left, ref query); Visit(node.Right, ref query); return; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var s = _segments[i];
                if (query.MayIntersect(s.MinX, s.MaxX, s.MinY, s.MaxY)) query.TestSegment(s.Index);
            }
        }
    }
}
