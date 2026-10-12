namespace FastChartsScene.Core;

public sealed partial class Axis
{
    private AxisTickOptions _tickOptions = AxisTickOptions.Default;
    private TickCache? _tickCache, _alternateTickCache;
    private sealed record TickCache(AxisRange Range, AxisScale Scale, bool Inverted, string[] Categories,
        AxisTickOptions Options, int Target, IReadOnlyList<AxisTick> Ticks);
    private bool SameTransform(TickCache cached) => cached.Range == Range && cached.Scale == Scale && cached.Inverted == Inverted &&
        ReferenceEquals(cached.Categories, _categories) && ReferenceEquals(cached.Options, TickOptions);
    public AxisTickOptions TickOptions
    {
        get => _tickOptions;
        set { ArgumentNullException.ThrowIfNull(value); Set(ref _tickOptions, value); }
    }
    /// <summary>Actual tick/label generation passes, excluding retained-cache hits.</summary>
    public long TickGenerationCount { get; private set; }
    /// <summary>Read-only generated ticks. An unchanged transform/categories/options/target
    /// reuses the same list without allocating. Two density variants are retained. Title, units, side and weight do not affect it.</summary>
    public IReadOnlyList<AxisTick> Ticks(int target = 7)
    {
        target = TickOptions.Mode == AxisTickMode.Custom ? 0 : Math.Clamp(target, 2, 40);
        if (_tickCache is { } cached && cached.Target == target && SameTransform(cached)) return cached.Ticks;
        if (_alternateTickCache is { } alternate && alternate.Target == target && SameTransform(alternate))
        {
            _alternateTickCache = _tickCache; _tickCache = alternate; return alternate.Ticks;
        }
        var source = TickOptions.Mode == AxisTickMode.Custom ? CustomTicksInRange() : BuildReadableAutomaticTicks(target);
        var ticks = Array.AsReadOnly(source.ToArray());
        // Two active sizes (screen/export or two panes) must not invalidate each other.
        // Keep at most two variants and do not retain old transforms/options in the spare.
        _alternateTickCache = _tickCache is { } previous && SameTransform(previous) ? previous : null;
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
