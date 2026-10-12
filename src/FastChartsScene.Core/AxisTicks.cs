namespace FastChartsScene.Core;

public sealed partial class Axis
{
    private AxisTickOptions _tickOptions = AxisTickOptions.Default;
    private TickCache? _tickCache;
    private sealed record TickCache(AxisRange Range, AxisScale Scale, bool Inverted, string[] Categories,
        AxisTickOptions Options, int Target, IReadOnlyList<AxisTick> Ticks);
    public AxisTickOptions TickOptions
    {
        get => _tickOptions;
        set { ArgumentNullException.ThrowIfNull(value); Set(ref _tickOptions, value); }
    }
    /// <summary>Actual tick/label generation passes, excluding retained-cache hits.</summary>
    public long TickGenerationCount { get; private set; }
    /// <summary>Read-only generated ticks. An unchanged transform/categories/options/target
    /// reuses the same list without allocating. Title, units, side and weight do not affect it.</summary>
    public IReadOnlyList<AxisTick> Ticks(int target = 7)
    {
        target = TickOptions.Mode == AxisTickMode.Custom ? 0 : Math.Clamp(target, 2, 40);
        if (_tickCache is { } cached && cached.Range == Range && cached.Scale == Scale && cached.Inverted == Inverted &&
            ReferenceEquals(cached.Categories, _categories) && ReferenceEquals(cached.Options, TickOptions) && cached.Target == target)
            return cached.Ticks;
        var source = TickOptions.Mode == AxisTickMode.Custom ? CustomTicksInRange() : BuildAutomaticTicks(target);
        var ticks = Array.AsReadOnly(source.ToArray());
        _tickCache = new(Range, Scale, Inverted, _categories, TickOptions, target, ticks); TickGenerationCount++;
        return ticks;
    }
    private IReadOnlyList<AxisTick> CustomTicksInRange()
    {
        var source = TickOptions.CustomTicks;
        var low = 0; var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].Value < Range.Minimum) low = middle + 1; else high = middle;
        }
        List<AxisTick> ticks = [];
        for (var i = low; i < source.Count && source[i].Value <= Range.Maximum; i++)
        {
            var tick = source[i]; var unit = ToUnit(tick.Value);
            if (double.IsFinite(unit)) ticks.Add(new(tick.Value, unit, tick.Label ?? Format(tick.Value)));
        }
        return ticks;
    }
}
