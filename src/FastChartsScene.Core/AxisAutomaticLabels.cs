using System.Globalization;

namespace FastChartsScene.Core;

public sealed partial class Axis
{
    /// <summary>Keep normal automatic divisions, but never return an invalid projected tick.
    /// Very narrow numeric ranges may have no representable aligned division. In that case
    /// show the exact two range endpoints instead of inventing sub-ULP intermediate values.</summary>
    private IReadOnlyList<AxisTick> BuildReadableAutomaticTicks(int target)
    {
        var generated = BuildAutomaticTicks(target);
        var ticks = new List<AxisTick>(generated.Count);
        foreach (var tick in generated)
        {
            if (!double.IsFinite(tick.Value) || !double.IsFinite(tick.Unit) || tick.Value < Range.Minimum ||
                tick.Value > Range.Maximum || tick.Unit < 0 || tick.Unit > 1 ||
                ticks.Count > 0 && tick.Value <= ticks[^1].Value) continue;
            ticks.Add(tick);
        }
        var numeric = Scale is AxisScale.Linear or AxisScale.Logarithmic;
        if (numeric && ticks.Count < 2)
        {
            ticks.Clear();
            ticks.Add(new(Range.Minimum, ToUnit(Range.Minimum), Format(Range.Minimum)));
            ticks.Add(new(Range.Maximum, ToUnit(Range.Maximum), Format(Range.Maximum)));
        }
        // Explicit numeric/date formats and category text are user choices. Never rewrite
        // an intentional repeated label (or any custom tick label) to force uniqueness.
        if (ticks.Count < 2 || numeric && TickOptions.NumericFormat.Length != 0 ||
            Scale == AxisScale.Category || Scale == AxisScale.DateTime && TickOptions.DateTimeFormat.Length != 0 ||
            UniqueLabels(ticks) && (!numeric || NumericLabelsFitRange(ticks))) return ticks;
        if (numeric)
        {
            // G17 distinguishes finite binary64 coordinates; stop as soon as a shorter
            // invariant representation suffices for this whole set, not per-label precision.
            for (var digits = 4; digits <= 17; digits++)
            {
                var format = "G" + digits.ToString(CultureInfo.InvariantCulture);
                for (var i = 0; i < ticks.Count; i++) ticks[i] = ticks[i] with
                    { Label = ticks[i].Value.ToString(format, CultureInfo.InvariantCulture) };
                if (UniqueLabels(ticks) && NumericLabelsFitRange(ticks)) break;
            }
        }
        else if (Scale == AxisScale.DateTime)
        {
            // Calendar ticks within a multi-year range can share the adaptive "yyyy"
            // label. Escalate detail while retaining every original calendar position.
            string[] formats = ["yyyy-MM", "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fffffff"];
            foreach (var format in formats)
            {
                for (var i = 0; i < ticks.Count; i++)
                {
                    string label;
                    try { label = DateTimeOffset.UnixEpoch.AddSeconds(ticks[i].Value).ToString(format, CultureInfo.InvariantCulture); }
                    catch (ArgumentOutOfRangeException) { label = "out of date range"; }
                    ticks[i] = ticks[i] with { Label = label };
                }
                if (UniqueLabels(ticks)) break;
            }
        }
        return ticks;
    }
    private bool NumericLabelsFitRange(IReadOnlyList<AxisTick> ticks)
    {
        foreach (var tick in ticks)
            if (!double.TryParse(tick.Label, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < Range.Minimum || value > Range.Maximum) return false;
        return true;
    }
    private static bool UniqueLabels(IReadOnlyList<AxisTick> ticks)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tick in ticks) if (!labels.Add(tick.Label)) return false;
        return true;
    }
}
