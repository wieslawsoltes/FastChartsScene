using System.Collections.ObjectModel;
using System.Globalization;

namespace FastChartsScene.Core;

public enum ColorInterpolation { Gradient, Stepped }
public enum ColorScaleMode { Linear, Logarithmic }
public enum ColorOutOfRange { Clamp, Transparent }
public readonly record struct ColorStop(double Offset, ColorRgba Color);

/// <summary>Immutable, normalized color stops. Stepped palettes are right-continuous:
/// a stop's color applies from its offset up to (but not including) the next stop.</summary>
public sealed class ColorMap
{
    public const int MaximumStops = 256;
    private readonly ColorStop[] _stops;
    public string Name { get; }
    public ColorInterpolation Interpolation { get; }
    public ReadOnlyCollection<ColorStop> Stops { get; }
    public ColorMap(string name, IEnumerable<ColorStop> stops, ColorInterpolation interpolation = ColorInterpolation.Gradient)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (!Enum.IsDefined(interpolation)) throw new ArgumentOutOfRangeException(nameof(interpolation));
        _stops = stops.Take(MaximumStops + 1).ToArray();
        if (_stops.Length < 2 || _stops.Length > MaximumStops || _stops[0].Offset != 0 || _stops[^1].Offset != 1)
            throw new ArgumentException("A palette needs 2–256 strictly ordered stops spanning [0,1].", nameof(stops));
        for (var i = 0; i < _stops.Length; i++)
        {
            var stop = _stops[i]; ValidateColor(stop.Color);
            if (!double.IsFinite(stop.Offset) || stop.Offset < 0 || stop.Offset > 1 || i > 0 && stop.Offset <= _stops[i - 1].Offset)
                throw new ArgumentException("Palette offsets must increase strictly within [0,1].", nameof(stops));
        }
        Name = name ?? ""; Interpolation = interpolation; Stops = Array.AsReadOnly(_stops);
    }
    public ColorMap WithInterpolation(ColorInterpolation interpolation) => new(Name, _stops, interpolation);
    public ColorRgba Evaluate(double unit)
    {
        if (!double.IsFinite(unit)) return new(0, 0, 0, 0);
        if (unit <= 0) return _stops[0].Color;
        if (unit >= 1) return _stops[^1].Color;
        var low = 0; var high = _stops.Length - 1;
        while (high - low > 1) { var middle = (low + high) / 2; if (_stops[middle].Offset <= unit) low = middle; else high = middle; }
        var a = _stops[low]; var b = _stops[high];
        return Interpolation == ColorInterpolation.Stepped ? a.Color : ColorRgba.Lerp(a.Color, b.Color, (unit - a.Offset) / (b.Offset - a.Offset));
    }
    internal static void ValidateColor(ColorRgba c)
    {
        if (!float.IsFinite(c.R) || !float.IsFinite(c.G) || !float.IsFinite(c.B) || !float.IsFinite(c.A) ||
            c.R < 0 || c.R > 1 || c.G < 0 || c.G > 1 || c.B < 0 || c.B > 1 || c.A < 0 || c.A > 1)
            throw new ArgumentOutOfRangeException(nameof(c), "Color channels must be finite and in [0,1].");
    }
    private static ColorMap Create(string name, params string[] colors) => new(name,
        colors.Select((color, i) => new ColorStop(i / (double)(colors.Length - 1), ColorRgba.Parse(color))));
    public static ColorMap Heat { get; } = Create("Heat", "09134D", "164BC4", "04BCD0", "55D574", "F6ED4C", "F68B30", "BA1740");
    public static ColorMap Grayscale { get; } = Create("Grayscale", "000000", "FFFFFF");
    public static ColorMap Diverging { get; } = Create("Blue–white–red", "1749A3", "FFFFFF", "B21734");
    public static ColorMap Thermal { get; } = Create("Thermal", "160D32", "6A1F73", "C13756", "F38432", "FFF1B6");
    public static IReadOnlyList<ColorMap> Presets { get; } = Array.AsReadOnly(new[] { Heat, Grayscale, Diverging, Thermal });
    public override string ToString() => Name;
}

/// <summary>Immutable scalar color settings, independent of charts and renderers. A null range
/// fits valid data. Reversed flips colors, not the numeric axis. Invalid/log-domain samples use
/// MissingColor; Transparent hides finite values outside an explicit range.</summary>
public sealed class ColorScale
{
    public ColorMap Palette { get; }
    public AxisRange? Range { get; }
    public ColorScaleMode Mode { get; }
    public bool Reversed { get; }
    public ColorRgba MissingColor { get; }
    public ColorOutOfRange OutOfRange { get; }
    public static ColorScale Default { get; } = new();
    public ColorScale(ColorMap? palette = null, AxisRange? range = null, ColorScaleMode mode = ColorScaleMode.Linear,
        bool reversed = false, ColorRgba? missingColor = null, ColorOutOfRange outOfRange = ColorOutOfRange.Clamp)
    {
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(outOfRange)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (range is { } r && (!(r.Span > 0) || !double.IsFinite(r.Span) || mode == ColorScaleMode.Logarithmic && r.Minimum <= 0))
            throw new ArgumentOutOfRangeException(nameof(range), "A manual logarithmic color range must be positive.");
        MissingColor = missingColor ?? new(0, 0, 0, 0); ColorMap.ValidateColor(MissingColor);
        Palette = palette ?? ColorMap.Heat; Range = range; Mode = mode; Reversed = reversed; OutOfRange = outOfRange;
    }
    /// <summary>Use NaN/NaN for an empty source. A constant source keeps its exact value and maps to the palette midpoint.</summary>
    public ResolvedColorScale Resolve(double minimum, double maximum)
    {
        if (Range is { } r) return new(this, r.Minimum, r.Maximum, true);
        if (double.IsNaN(minimum) && double.IsNaN(maximum)) return new(this, 0, 1, false);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum || Mode == ColorScaleMode.Logarithmic && minimum <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimum), "Auto-range extents must be finite, ordered and inside the scale domain.");
        return new(this, minimum, maximum, true);
    }
}

public sealed class ResolvedColorScale
{
    public ColorScale Settings { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public bool HasValues { get; }
    public bool IsConstant => HasValues && Minimum == Maximum;
    internal ResolvedColorScale(ColorScale settings, double minimum, double maximum, bool hasValues)
    { Settings = settings; Minimum = minimum; Maximum = maximum; HasValues = hasValues; }
    public bool IsDomainValue(double value) => double.IsFinite(value) && (Settings.Mode != ColorScaleMode.Logarithmic || value > 0);
    private static double LogRatio(double value, double basis)
    {
        var relative = (value - basis) / basis;
        if (Math.Abs(relative) < 1e-4) return relative * (1 + relative * (-.5 + relative * (1.0 / 3 + relative * (-.25 + relative * .2))));
        return double.IsFinite(relative) && relative > -.5 ? Math.Log(1 + relative) : Math.Log(value) - Math.Log(basis);
    }
    public double ToUnit(double value)
    {
        if (!HasValues || !IsDomainValue(value)) return double.NaN;
        if (IsConstant) return .5;
        if (value <= Minimum) return 0; if (value >= Maximum) return 1;
        if (Settings.Mode == ColorScaleMode.Logarithmic) return LogRatio(value, Minimum) / LogRatio(Maximum, Minimum);
        var span = Maximum - Minimum;
        return double.IsFinite(span) ? (value - Minimum) / span : (value / 2 - Minimum / 2) / (Maximum / 2 - Minimum / 2);
    }
    public double FromUnit(double unit)
    {
        if (!double.IsFinite(unit) || unit < 0 || unit > 1) throw new ArgumentOutOfRangeException(nameof(unit));
        if (!HasValues) return double.NaN;
        if (unit == 0 || IsConstant) return Minimum; if (unit == 1) return Maximum;
        if (Settings.Mode == ColorScaleMode.Logarithmic)
        {
            var step = unit * LogRatio(Maximum, Minimum);
            var delta = Math.Abs(step) < 1e-4 ? step * (1 + step * (.5 + step * (1.0 / 6 + step * (1.0 / 24 + step / 120)))) : Math.Exp(step) - 1;
            return Math.Abs(step) < .5 ? Minimum + Minimum * delta : Math.Exp(Math.Log(Minimum) + step);
        }
        var span = Maximum - Minimum;
        return double.IsFinite(span) ? Minimum + unit * span : Minimum * (1 - unit) + Maximum * unit;
    }
    public ColorRgba Color(double value)
    {
        var unit = ToUnit(value);
        if (double.IsNaN(unit)) return Settings.MissingColor;
        if (Settings.OutOfRange == ColorOutOfRange.Transparent && (value < Minimum || value > Maximum)) return new(0, 0, 0, 0);
        return Settings.Palette.Evaluate(Settings.Reversed ? 1 - unit : unit);
    }
    public IReadOnlyList<AxisTick> Ticks(int target = 5)
    {
        if (!HasValues) return Array.Empty<AxisTick>();
        if (IsConstant) return new[] { new AxisTick(Minimum, .5, Minimum.ToString("G6", CultureInfo.InvariantCulture)) };
        target = Math.Clamp(target, 2, 9);
        var values = Enumerable.Range(0, target).Select(i => FromUnit(i / (double)(target - 1))).Distinct().ToArray();
        var labels = values.Select(v => v.ToString("G5", CultureInfo.InvariantCulture)).ToArray();
        if (labels.Distinct().Count() != labels.Length) labels = values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)).ToArray();
        return values.Select((value, i) => new AxisTick(value, ToUnit(value), labels[i])).ToArray();
    }
}
