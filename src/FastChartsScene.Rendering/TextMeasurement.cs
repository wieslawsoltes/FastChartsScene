using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

/// <summary>Baseline-relative ink bounds plus typographic advance, in logical pixels.</summary>
public readonly record struct TextMeasurement(double Advance, RectD InkBounds)
{
    internal void Validate()
    {
        var b = InkBounds;
        if (!double.IsFinite(Advance) || Advance < 0 || Advance > 10_000_000 || !double.IsFinite(b.X) || !double.IsFinite(b.Y) ||
            !double.IsFinite(b.Right) || !double.IsFinite(b.Bottom) || b.Width < 0 || b.Height < 0 || Math.Abs(b.X) > 10_000_000 || Math.Abs(b.Y) > 10_000_000 || b.Width > 10_000_000 || b.Height > 10_000_000)
            throw new InvalidOperationException("Text measurer returned invalid metrics.");
    }
    public RectD Bounds(TextLabel label)
    {
        Validate();
        var offset = label.Align == "center" ? -Advance / 2 : label.Align == "right" ? -Advance : 0;
        var b = InkBounds;
        var angle = label.Rotation * Math.PI / 180; var c = Math.Cos(angle); var s = Math.Sin(angle);
        var x0 = double.PositiveInfinity; var y0 = x0; var x1 = double.NegativeInfinity; var y1 = x1;
        void Include(double x, double y)
        {
            x += offset; var px = label.X + c * x - s * y; var py = label.Y + s * x + c * y;
            x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
        }
        Include(b.X, b.Y); Include(b.Right, b.Y); Include(b.X, b.Bottom); Include(b.Right, b.Bottom);
        return new(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>UI-thread-affine, host-owned text measurement. Use the same font/shaping
/// policy as the renderer; call SceneCompiler.ClearCache after changing its fonts.</summary>
public interface ITextMeasurer
{
    TextMeasurement Measure(string text, double size, bool bold = false);
}

internal sealed class CachedTextMeasurer(ITextMeasurer inner) : ITextMeasurer
{
    private readonly Dictionary<(string Text, double Size, bool Bold), TextMeasurement> _cache = [];
    public ITextMeasurer Inner => inner;
    public long MeasurementCount { get; private set; }
    public void Clear() => _cache.Clear();
    public TextMeasurement Measure(string text, double size, bool bold = false)
    {
        var key = (text, size, bold);
        if (_cache.TryGetValue(key, out var metrics)) return metrics;
        metrics = inner.Measure(text, size, bold); metrics.Validate(); MeasurementCount++;
        // Bound retained text for streaming/custom tick labels. Frames retain only
        // their own decisions; an unchanged frame returns before any measurement.
        if (_cache.Count >= 2048) _cache.Clear();
        if (_cache.TryGetValue(key, out var nested)) return nested;
        _cache.Add(key, metrics); return metrics;
    }
}

public sealed record AxisLabelPlacement(string AxisId, bool Horizontal, bool IsTitle,
    double? Value, TextLabel Label, RectD Bounds, bool Visible, string? Reason);
internal sealed record AxisLayoutReservation(double Left, double Right, double Top, double Bottom);
