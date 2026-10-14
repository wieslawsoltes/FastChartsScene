using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

/// <summary>One bounded measure/arrange pass. Tick lists are captured once from the
/// initial viewport; shrinking margins cannot oscillate tick generation or text.</summary>
internal sealed class MeasuredAxisLayout
{
    private sealed record Plan(Axis Axis, IReadOnlyList<AxisTick> Ticks, double Offset, double TitleDistance);
    private readonly List<Plan> _x = [], _y = [];
    private ITextMeasurer? _measure;
    private readonly List<AxisLabelPlacement> _placements = [];
    private readonly List<RectD> _occupied = [];
    public IReadOnlyList<AxisLabelPlacement> Placements { get; }
    public AxisLayoutReservation Reservation { get; }
    private static string Title(Axis a) => a.Units.Length == 0 ? a.Title : $"{a.Title} ({a.Units})";
    public MeasuredAxisLayout(ChartDocument d, ChartLayout initial, ITextMeasurer measure)
    {
        _measure = measure; Placements = _placements.AsReadOnly();
        double nearOffset = 0, farOffset = 0, left = 72, right = 24;
        double top = d.Subtitle.Length == 0 ? 55 : 74, bottom = 54 + Math.Max(0, d.XAxes.Count - 1) * 44;
        var layered = d.AxisLayout == AxisLayout.Layered;
        foreach (var a in d.YAxes)
        {
            var ticks = a.Ticks(Math.Clamp((int)(initial.ForAxis(a.Id).Height / 65), 2, 12));
            double labelWidth = 0;
            foreach (var t in ticks)
            {
                var m = measure.Measure(t.Label, 11);
                labelWidth = Math.Max(labelWidth, Math.Max(m.Advance, m.InkBounds.Right) - Math.Min(0, m.InkBounds.X));
                top = Math.Max(top, -m.InkBounds.Y + 6);
                bottom = Math.Max(bottom, m.InkBounds.Bottom + 10);
            }
            var side = a.Side == AxisSide.Near;
            var distance = Math.Max(side ? 52 : 46, labelWidth + 20);
            var titleMetrics = measure.Measure(Title(a), 12);
            var titleSide = Math.Max(Math.Abs(titleMetrics.InkBounds.Y), Math.Abs(titleMetrics.InkBounds.Bottom));
            var extent = Math.Max(labelWidth + 15, Title(a).Length == 0 ? 0 : distance + titleSide + 6);
            var offset = layered ? (side ? nearOffset : farOffset) : 0;
            _y.Add(new(a, ticks, offset, distance));
            if (side) { left = Math.Max(left, offset + extent); if (layered) nearOffset += Math.Max(52, extent + 8); }
            else { right = Math.Max(right, offset + extent); if (layered) farOffset += Math.Max(52, extent + 8); }
        }
        for (var i = 0; i < d.XAxes.Count; i++)
        {
            var a = d.XAxes[i]; var ticks = a.Ticks(Math.Clamp((int)(initial.Plot.Width / 100), 2, 15));
            foreach (var t in ticks)
            {
                var m = measure.Measure(t.Label, 11);
                // Use the largest overhang, not only endpoints: nonuniform custom
                // ticks can put long text immediately inside either plot edge.
                left = Math.Max(left, m.Advance / 2 - m.InkBounds.X + 6);
                right = Math.Max(right, m.InkBounds.Right - m.Advance / 2 + 6);
            }
            _x.Add(new(a, ticks, i * 44, 0));
        }
        Reservation = new(left, right, top, bottom);
    }
    public void Draw(ChartDocument d, ChartLayout l, FrameBuilder f)
    {
        if (_measure == null) throw new InvalidOperationException("Axis layout is already arranged.");
        var full = new RectD(0, 0, f.Width, f.Height); var start = f.Count;
        foreach (var label in f.Labels)
            if (label.Text.Length > 0) _occupied.Add(_measure.Measure(label.Text, label.Size, label.Bold).Bounds(label));
        foreach (var bar in l.ColorBars.Values) _occupied.Add(bar.Region);
        foreach (var plan in _y)
        {
            var a = plan.Axis; var p = l.ForAxis(a.Id); var near = a.Side == AxisSide.Near;
            var at = near ? p.X - plan.Offset : p.Right + plan.Offset;
            foreach (var tick in plan.Ticks)
            {
                var y = p.Bottom - tick.Unit * p.Height;
                if (d.ShowGrid && (d.AxisLayout != AxisLayout.Layered || ReferenceEquals(a, d.YAxis)))
                    f.Line(new(p.X, y), new(p.Right, y), d.Theme.Grid, 1, p);
                f.Line(new(at + (near ? -4 : 0), y), new(at + (near ? 0 : 4), y), d.Theme.Text, 1, full);
            }
            f.Line(new(at, p.Y), new(at, p.Bottom), d.Theme.Grid, 1, full);
            PlaceTicks(plan, false, t => new(t.Label, at + (near ? -9 : 9), p.Bottom - t.Unit * p.Height + 4,
                d.Theme.Text.ToHex(), 11, near ? "right" : "left"), f);
        }
        for (var i = 0; i < _x.Count; i++)
        {
            var plan = _x[i]; var at = l.Plot.Bottom + plan.Offset;
            foreach (var tick in plan.Ticks)
            {
                var x = l.Plot.X + tick.Unit * l.Plot.Width;
                if (i == 0 && d.ShowGrid) foreach (var p in l.AxisPlots.Values.Distinct())
                    f.Line(new(x, p.Y), new(x, p.Bottom), d.Theme.Grid, 1, p);
                f.Line(new(x, at), new(x, at + 4), d.Theme.Text, 1, full);
            }
            PlaceTicks(plan, true, t => new(t.Label, l.Plot.X + t.Unit * l.Plot.Width, at + 18,
                d.Theme.Text.ToHex(), 11, "center"), f);
        }
        // Titles cannot obscure accepted tick values. Their complete strings and
        // suppression reasons are available to hosts in the same immutable view.
        foreach (var plan in _y)
        {
            var p = l.ForAxis(plan.Axis.Id); var near = plan.Axis.Side == AxisSide.Near;
            var at = near ? p.X - plan.Offset : p.Right + plan.Offset;
            Place(plan.Axis.Id, false, true, null, new(Title(plan.Axis), at + (near ? -plan.TitleDistance : plan.TitleDistance),
                p.Y + p.Height / 2, d.Theme.Text.ToHex(), 12, "center", -90), f);
        }
        foreach (var plan in _x)
            Place(plan.Axis.Id, true, true, null, new(Title(plan.Axis), l.Plot.X + l.Plot.Width / 2,
                l.Plot.Bottom + plan.Offset + 39, d.Theme.Text.ToHex(), 12, "center"), f);
        f.Batch(start, full);
        // The published frame owns only value/string decisions, not live axes or a
        // disposable native font renderer. Unchanged frames need no arranger state.
        _x.Clear(); _y.Clear(); _occupied.Clear(); _measure = null;
    }
    private void PlaceTicks(Plan plan, bool horizontal, Func<AxisTick, TextLabel> label, FrameBuilder f)
    {
        // Spatial order rather than numeric order preserves endpoints on inverted
        // axes too. Keep the extremes first, then only non-overlapping interiors.
        var ticks = plan.Ticks.OrderBy(t => t.Unit).ToArray();
        if (ticks.Length == 0) return;
        void At(int i) => Place(plan.Axis.Id, horizontal, false, ticks[i].Value, label(ticks[i]), f);
        At(0); if (ticks.Length > 1) At(ticks.Length - 1);
        for (var i = 1; i + 1 < ticks.Length; i++) At(i);
    }
    private void Place(string id, bool horizontal, bool title, double? value, TextLabel label, FrameBuilder f)
    {
        var m = _measure!.Measure(label.Text, label.Size, label.Bold);
        label = label with { MeasuredAdvance = m.Advance };
        var b = m.Bounds(label); string? reason = null;
        if (label.Text.Length == 0) reason = "empty";
        else if (b.X < 2 || b.Y < 2 || b.Right > f.Width - 2 || b.Bottom > f.Height - 2) reason = "outside-canvas";
        else if (_occupied.Any(o => b.X < o.Right + 2 && b.Right + 2 > o.X && b.Y < o.Bottom + 2 && b.Bottom + 2 > o.Y)) reason = "overlap";
        var visible = reason == null;
        _placements.Add(new(id, horizontal, title, value, label, b, visible, reason));
        if (visible) { _occupied.Add(b); f.Labels.Add(label); }
    }
}
