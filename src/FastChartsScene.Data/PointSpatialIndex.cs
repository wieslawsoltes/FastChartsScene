using FastChartsScene.Core;

namespace FastChartsScene.Data;

public readonly record struct IndexedPointHit(int Index, PointD Data, PointD Screen, double Distance);
public readonly record struct PointQueryResult(IndexedPointHit? Hit, int VisitedNodes, int TestedSamples);

/// <summary>Immutable, exact nearest-original-sample index. A median-partitioned bounding tree
/// is built once per immutable source; queries reproject bounds for the supplied axes, so pan,
/// resize, inversion, logarithmic scales and transposition do not require rebuilding the tree.
/// Construction is O(N log N) on typical input. Worst-case queries may visit every finite sample.
/// Source data must remain immutable. Queries allocate no heap storage and share no mutable state.</summary>
public sealed class PointSpatialIndex
{
    private const int LeafSize = 32;
    private readonly IDataSource _data;
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private readonly record struct Node(double MinX, double MaxX, double MinY, double MaxY,
        int Start, int Count, int Left, int Right, int MinimumIndex);
    public int SourceCount { get; }
    public int FiniteCount => _order.Length;
    public int NodeCount => _nodes.Length;

    public PointSpatialIndex(IDataSource data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count < 0) throw new ArgumentOutOfRangeException(nameof(data));
        _data = data; SourceCount = data.Count;
        var count = 0;
        for (var i = 0; i < SourceCount; i++)
        {
            if (!double.IsFinite(data.X(i)) || double.IsInfinity(data.Y(i)))
                throw new ArgumentException("X must be finite; Y must be finite or NaN.", nameof(data));
            if (!double.IsNaN(data.Y(i))) count++;
        }
        _order = new int[count];
        for (int i = 0, j = 0; i < SourceCount; i++) if (!double.IsNaN(data.Y(i))) _order[j++] = i;
        var nodes = new List<Node>();
        if (count > 0) Build(nodes, 0, count);
        _nodes = nodes.ToArray();
    }
    private int Build(List<Node> nodes, int start, int count)
    {
        var minX = double.PositiveInfinity; var minY = minX;
        var maxX = double.NegativeInfinity; var maxY = maxX; var minimumIndex = int.MaxValue;
        for (var i = start; i < start + count; i++)
        {
            var index = _order[i]; var x = _data.X(index); var y = _data.Y(index);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minimumIndex = Math.Min(minimumIndex, index);
        }
        var at = nodes.Count; nodes.Add(default); var left = -1; var right = -1;
        if (count > LeafSize)
        {
            var splitX = maxX / 2 - minX / 2 >= maxY / 2 - minY / 2;
            var middle = start + count / 2; Partition(start, start + count - 1, middle, splitX);
            left = Build(nodes, start, middle - start); right = Build(nodes, middle, start + count - middle);
        }
        nodes[at] = new(minX, maxX, minY, maxY, start, count, left, right, minimumIndex); return at;
    }
    private int Compare(int a, int b, bool x)
    {
        var comparison = (x ? _data.X(a) : _data.Y(a)).CompareTo(x ? _data.X(b) : _data.Y(b));
        return comparison != 0 ? comparison : a.CompareTo(b);
    }
    private void Partition(int low, int high, int target, bool x)
    {
        // Bound adversarial quickselect work; deterministic sort is a safe fallback.
        var budget = 2 * (int)Math.Log2(high - low + 1) + 2;
        while (low < high)
        {
            if (--budget < 0)
            {
                Array.Sort(_order, low, high - low + 1, Comparer<int>.Create((a, b) => Compare(a, b, x))); return;
            }
            var pivot = _order[low + (high - low) / 2]; var a = low; var b = high;
            while (a <= b)
            {
                while (Compare(_order[a], pivot, x) < 0) a++;
                while (Compare(_order[b], pivot, x) > 0) b--;
                if (a <= b) { (_order[a], _order[b]) = (_order[b], _order[a]); a++; b--; }
            }
            if (target <= b) high = b;
            else if (target >= a) low = a;
            else return;
        }
    }
    /// <summary>Find the nearest visible sample center within a logical-pixel radius.
    /// Equal distances select the lowest original index. NaN samples and samples outside either
    /// axis range are excluded, including nonpositive coordinates on logarithmic axes.</summary>
    public PointQueryResult Nearest(Axis x, Axis y, RectD plot, PointD location, double radius = 12, bool transpose = false)
    {
        ArgumentNullException.ThrowIfNull(x); ArgumentNullException.ThrowIfNull(y);
        if (!location.IsFinite || !double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(plot.X) || !double.IsFinite(plot.Y) || !double.IsFinite(plot.Right) || !double.IsFinite(plot.Bottom) ||
            !double.IsFinite(plot.Width) || !double.IsFinite(plot.Height) || plot.Width <= 0 || plot.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(plot));
        if (_nodes.Length == 0 || !plot.Contains(location)) return default;
        var query = new Query(this, x, y, plot, location, radius, transpose); query.Visit(0);
        return new(query.Hit, query.Visited, query.Tested);
    }
    private struct Query(PointSpatialIndex owner, Axis x, Axis y, RectD plot, PointD location, double radius, bool transpose)
    {
        public IndexedPointHit? Hit;
        public int Visited, Tested;
        private double _distance = radius;
        private static double Distance(double dx, double dy)
        {
            var maximum = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return maximum == 0 ? 0 : maximum * Math.Sqrt((dx / maximum) * (dx / maximum) + (dy / maximum) * (dy / maximum));
        }
        private double Bound(Node node)
        {
            var minX = Math.Max(transpose ? node.MinY : node.MinX, x.Range.Minimum);
            var maxX = Math.Min(transpose ? node.MaxY : node.MaxX, x.Range.Maximum);
            var minY = Math.Max(transpose ? node.MinX : node.MinY, y.Range.Minimum);
            var maxY = Math.Min(transpose ? node.MaxX : node.MaxY, y.Range.Maximum);
            if (minX > maxX || minY > maxY) return double.PositiveInfinity;
            var a = plot.X + x.ToUnit(minX) * plot.Width; var b = plot.X + x.ToUnit(maxX) * plot.Width;
            var c = plot.Bottom - y.ToUnit(minY) * plot.Height; var d = plot.Bottom - y.ToUnit(maxY) * plot.Height;
            return Distance(location.X - Math.Clamp(location.X, Math.Min(a, b), Math.Max(a, b)),
                location.Y - Math.Clamp(location.Y, Math.Min(c, d), Math.Max(c, d)));
        }
        public void Visit(int index)
        {
            var node = owner._nodes[index]; Visited++;
            var lower = Bound(node);
            if (lower > _distance || lower == _distance && Hit is { } previous && node.MinimumIndex >= previous.Index) return;
            if (node.Left >= 0)
            {
                var a = node.Left; var b = node.Right;
                if (Bound(owner._nodes[b]) < Bound(owner._nodes[a])) (a, b) = (b, a);
                Visit(a); Visit(b); return;
            }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var original = owner._order[i]; var raw = new PointD(owner._data.X(original), owner._data.Y(original));
                var px = transpose ? raw.Y : raw.X; var py = transpose ? raw.X : raw.Y; Tested++;
                if (px < x.Range.Minimum || px > x.Range.Maximum || py < y.Range.Minimum || py > y.Range.Maximum) continue;
                var screen = new PointD(plot.X + x.ToUnit(px) * plot.Width, plot.Bottom - y.ToUnit(py) * plot.Height);
                if (!screen.IsFinite) continue;
                var distance = Distance(screen.X - location.X, screen.Y - location.Y);
                if (distance < _distance || distance == _distance && (Hit == null || original < Hit.Value.Index))
                { _distance = distance; Hit = new(original, raw, screen, distance); }
            }
        }
    }
}
