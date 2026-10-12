using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace FastChartsScene.Core;

public enum AxisTickMode { Automatic, Custom }
/// <summary>An original data-coordinate tick. Null labels use the axis formatter;
/// an empty string deliberately leaves the tick/grid line without text.</summary>
public readonly record struct CustomAxisTick(double Value, string? Label = null);

/// <summary>Owned immutable tick positions and invariant label formats. Custom mode uses
/// exactly the configured in-range ticks, without resampling. No callbacks or code are evaluated.</summary>
public sealed class AxisTickOptions
{
    public const int MaximumTicks = 512;
    public const int MaximumLabelLength = 256;
    public static AxisTickOptions Default { get; } = new();
    public AxisTickMode Mode { get; }
    public ReadOnlyCollection<CustomAxisTick> CustomTicks { get; }
    public string NumericFormat { get; }
    public string DateTimeFormat { get; }
    public AxisTickOptions(AxisTickMode mode = AxisTickMode.Automatic, IEnumerable<CustomAxisTick>? customTicks = null,
        string numericFormat = "", string dateTimeFormat = "")
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(numericFormat); ArgumentNullException.ThrowIfNull(dateTimeFormat);
        // Bound precision as well as text length: F999999999 must never reach a formatter.
        if (numericFormat.Length != 0 && !ValidNumericFormat(numericFormat))
            throw new ArgumentException("Use G, F, N, E or P with optional precision 0–17, R, or an empty automatic format.", nameof(numericFormat));
        if (dateTimeFormat.Length > 64 || dateTimeFormat.Any(IsLineBreakOrControl))
            throw new ArgumentException("Date format must be a single line of at most 64 characters.", nameof(dateTimeFormat));
        if (dateTimeFormat.Length != 0)
        {
            try { _ = DateTimeOffset.UnixEpoch.ToString(dateTimeFormat, CultureInfo.InvariantCulture); }
            catch (FormatException error) { throw new ArgumentException("Invalid invariant DateTime format.", nameof(dateTimeFormat), error); }
        }
        var owned = customTicks?.Take(MaximumTicks + 1).ToArray() ?? [];
        if (owned.Length > MaximumTicks) throw new ArgumentException($"At most {MaximumTicks} custom ticks are supported.", nameof(customTicks));
        foreach (var tick in owned)
            if (!double.IsFinite(tick.Value) || tick.Label is { } label && (label.Length > MaximumLabelLength || label.Any(IsLineBreakOrControl)))
                throw new ArgumentException("Tick values must be finite and labels single-line text of at most 256 characters.", nameof(customTicks));
        Array.Sort(owned, static (a, b) => a.Value.CompareTo(b.Value));
        for (var i = 1; i < owned.Length; i++) if (owned[i - 1].Value == owned[i].Value)
            throw new ArgumentException("Custom tick values must be unique.", nameof(customTicks));
        Mode = mode; CustomTicks = Array.AsReadOnly(owned); NumericFormat = numericFormat; DateTimeFormat = dateTimeFormat;
    }
    private static bool IsLineBreakOrControl(char value) => char.IsControl(value) || value is '\u2028' or '\u2029';
    private static bool ValidNumericFormat(string format)
    {
        if (format is "R" or "r") return true;
        if (format.Length > 3 || !"gGfFnNeEpP".Contains(format[0])) return false;
        return format.Length == 1 || format.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0 &&
            int.TryParse(format.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var digits) && digits <= 17;
    }
    public AxisTickOptions WithMode(AxisTickMode mode) => mode == Mode ? this : new(mode, CustomTicks, NumericFormat, DateTimeFormat);
    public AxisTickOptions WithFormats(string numericFormat, string dateTimeFormat) => numericFormat == NumericFormat && dateTimeFormat == DateTimeFormat ?
        this : new(Mode, CustomTicks, numericFormat, dateTimeFormat);
}

/// <summary>Bounded portable editor syntax: one value|label per line. A value with no pipe
/// uses automatic formatting; a trailing pipe means an explicitly empty label. Labels may
/// contain further pipes. CR, LF and CRLF input are equivalent; no label whitespace is removed.</summary>
public static class AxisTickText
{
    public const int MaximumTextLength = 160 * 1024;
    public static IReadOnlyList<CustomAxisTick> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextLength) throw new FormatException("Custom tick text exceeds 160 KiB.");
        List<CustomAxisTick> ticks = [];
        var lineNumber = 0;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNumber++; if (string.IsNullOrWhiteSpace(line)) continue;
            if (ticks.Count == AxisTickOptions.MaximumTicks) throw new FormatException("At most 512 custom tick lines are supported.");
            var pipe = line.IndexOf('|'); var number = pipe < 0 ? line : line[..pipe];
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new FormatException($"Invalid finite tick value on line {lineNumber}.");
            ticks.Add(new(value, pipe < 0 ? null : line[(pipe + 1)..]));
        }
        try { return new AxisTickOptions(AxisTickMode.Custom, ticks).CustomTicks; }
        catch (ArgumentException error) { throw new FormatException(error.Message, error); }
    }
    public static string Write(IEnumerable<CustomAxisTick> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        var owned = new AxisTickOptions(AxisTickMode.Custom, ticks).CustomTicks;
        var text = new StringBuilder();
        foreach (var tick in owned)
        {
            if (text.Length != 0) text.Append('\n');
            text.Append(tick.Value.ToString("R", CultureInfo.InvariantCulture));
            if (tick.Label != null) text.Append('|').Append(tick.Label);
        }
        return text.ToString();
    }
}
