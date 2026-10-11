using FastChartsScene.Core;

namespace FastChartsScene.Data;

/// <summary>Min/max tree over 64-point blocks. Consecutive NaN samples are indexed as one
/// missing-data run, so selection cost does not depend on the length of an acquisition outage.</summary>
public sealed class EnvelopeIndex
{
    private const int BlockSize = 64;
    private readonly IDataSource _data;
    private readonly int[] _min, _max;
    private readonly int _leaves;
    private readonly record struct Gap(int Start, int End);
    private readonly Gap[] _gaps;
    public EnvelopeIndex(IDataSource data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count < 0) throw new ArgumentOutOfRangeException(nameof(data));
        _data = data;
        var blocks = data.Count / BlockSize + (data.Count % BlockSize == 0 ? 0 : 1);
        _leaves = 1;
        while (_leaves < blocks) _leaves *= 2;
        _min = new int[_leaves * 2]; Array.Fill(_min, -1); _max = (int[])_min.Clone();
        List<Gap> gaps = [];
        var gapStart = -1;
        for (var i = 0; i < data.Count; i++)
        {
            if (double.IsNaN(data.Y(i))) { if (gapStart < 0) gapStart = i; continue; }
            if (gapStart >= 0) { gaps.Add(new(gapStart, i)); gapStart = -1; }
            var n = _leaves + i / BlockSize;
            _min[n] = Min(_min[n], i); _max[n] = Max(_max[n], i);
        }
        if (gapStart >= 0) gaps.Add(new(gapStart, data.Count));
        for (var i = _leaves - 1; i > 0; i--)
        { _min[i] = Min(_min[i * 2], _min[i * 2 + 1]); _max[i] = Max(_max[i * 2], _max[i * 2 + 1]); }
        _gaps = gaps.ToArray();
    }
    private int Min(int a, int b) => a < 0 ? b : b < 0 ? a : _data.Y(a) <= _data.Y(b) ? a : b;
    private int Max(int a, int b) => a < 0 ? b : b < 0 ? a : _data.Y(a) >= _data.Y(b) ? a : b;
    public (int Minimum, int Maximum) Query(int start, int end)
    {
        if (start < 0 || end > _data.Count || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        var min = -1; var max = -1;
        void Include(int i) { if (!double.IsNaN(_data.Y(i))) { min = Min(min, i); max = Max(max, i); } }
        while (start < end && start % BlockSize != 0) Include(start++);
        while (end > start && end % BlockSize != 0) Include(--end);
        var a = start / BlockSize + _leaves; var b = end / BlockSize + _leaves;
        while (a < b)
        {
            if ((a & 1) != 0) { min = Min(min, _min[a]); max = Max(max, _max[a]); a++; }
            if ((b & 1) != 0) { --b; min = Min(min, _min[b]); max = Max(max, _max[b]); }
            a /= 2; b /= 2;
        }
        return (min, max);
    }
    /// <summary>Ordered screen-column envelope. Finite endpoints/extrema and both sides of
    /// every visible discontinuity are retained. Long missing runs need only one original
    /// NaN sample to break the line; no missing value is replaced by a finite value.
    /// Unordered sources remain untouched. Dense alternating gaps can still require O(N) output.</summary>
    public IReadOnlyList<int> Select(Axis axis, int pixelWidth)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (!_data.IsOrdered || _data.Count < 2) return Enumerable.Range(0, _data.Count).ToArray();
        pixelWidth = Math.Clamp(pixelWidth, 1, 32768);
        var first = Math.Max(0, DataSearch.LowerBound(_data, axis.Range.Minimum) - 1);
        var upper = DataSearch.UpperBound(_data, axis.Range.Maximum);
        var end = upper < _data.Count ? upper + 1 : _data.Count;
        if (end - first <= pixelWidth * 4) return Enumerable.Range(first, Math.Max(0, end - first)).ToArray();
        SortedSet<int> selected = [];
        void Bucket(int a, int b)
        {
            if (b <= a) return;
            selected.Add(a); selected.Add(b - 1);
            var mm = Query(a, b);
            if (mm.Minimum >= 0) selected.Add(mm.Minimum);
            if (mm.Maximum >= 0) selected.Add(mm.Maximum);
        }
        var start = first;
        for (var p = 1; p <= pixelWidth; p++)
        {
            var x = axis.FromUnit(axis.Inverted ? 1 - (double)p / pixelWidth : (double)p / pixelWidth);
            var stop = p == pixelWidth ? end : Math.Clamp(DataSearch.LowerBound(_data, x), start, end);
            Bucket(start, stop); start = stop;
        }
        // Skip all runs before the viewport in logarithmic time, including when a viewport
        // begins inside a very long run. Remove redundant NaN bucket endpoints for that run.
        var low = 0; var high = _gaps.Length;
        while (low < high)
        { var middle = low + (high - low) / 2; if (_gaps[middle].End <= first) low = middle + 1; else high = middle; }
        for (var i = low; i < _gaps.Length && _gaps[i].Start < end; i++)
        {
            var gap = _gaps[i]; var a = Math.Max(first, gap.Start); var b = Math.Min(end, gap.End);
            selected.GetViewBetween(a, b - 1).Clear(); selected.Add(a);
            if (gap.Start > first) selected.Add(gap.Start - 1);
            if (gap.End < end) selected.Add(gap.End);
        }
        return selected.ToArray();
    }
}
