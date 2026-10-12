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
    public const int MaximumNumericFormatLength = 64;
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
            throw new ArgumentException("Use a standard format (G/F/N/E/P, precision 0–17, or R), a custom format such as 0.00, or empty for automatic. Formats are limited to 64 single-line characters.", nameof(numericFormat));
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
        if (format.Length > MaximumNumericFormatLength || format.Any(IsLineBreakOrControl)) return false;
        // A single ASCII letter followed by digits is a STANDARD format even if its
        // precision is enormous. Never pass it to the custom formatter validation path.
        var standard = char.IsAsciiLetter(format[0]) && format.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
        if (standard)
        {
            if (format is "R" or "r") return true;
            if (format.Length > 3 || !"gGfFnNeEpP".Contains(format[0])) return false;
            return format.Length == 1 || int.TryParse(format.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var digits) && digits <= 17;
        }
        // Require numeric placeholders. This also rejects mistyped standard formats
        // such as F-1 instead of silently displaying them as a constant literal label.
        if (!format.Contains('0') && !format.Contains('#')) return false;
        // Validation happens only when constructing immutable settings, not per sample.
        // Custom precision/exponents/literals are bounded by format length; verify .NET
        // accepts the syntax for every sign/zero section and extreme finite magnitudes.
        ReadOnlySpan<double> probes = [0, 1.25, -1234.5, double.MaxValue, double.Epsilon];
        try
        {
            foreach (var value in probes)
            {
                var label = value.ToString(format, CultureInfo.InvariantCulture);
                if (label.Length > 2048 || label.Any(IsLineBreakOrControl)) return false;
            }
            return true;
        }
        catch (FormatException) { return false; }
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
        var remaining = text.AsSpan(); var lineNumber = 0;
        while (!remaining.IsEmpty)
        {
            var end = remaining.IndexOfAny('\r', '\n');
            var line = end < 0 ? remaining : remaining[..end];
            if (end < 0) remaining = default;
            else
            {
                var consumed = end + 1;
                if (remaining[end] == '\r' && consumed < remaining.Length && remaining[consumed] == '\n') consumed++;
                remaining = remaining[consumed..];
            }
            lineNumber++;
            // ReplaceLineEndings also treats Unicode separators/form-feed as new records.
            // That silently changed forbidden label text into otherwise-valid extra ticks.
            // This grammar permits only CR, LF and CRLF, exactly as the native draft comparer.
            if (line.IndexOfAny("\v\f\u0085\u2028\u2029".AsSpan()) >= 0)
                throw new FormatException($"Unsupported line separator on line {lineNumber}. Use CR, LF or CRLF.");
            if (line.IsWhiteSpace()) continue;
            if (ticks.Count == AxisTickOptions.MaximumTicks) throw new FormatException("At most 512 custom tick lines are supported.");
            var pipe = line.IndexOf('|'); var number = pipe < 0 ? line : line[..pipe];
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new FormatException($"Invalid finite tick value on line {lineNumber}.");
            string? label = null;
            if (pipe >= 0)
            {
                var labelText = line[(pipe + 1)..];
                if (labelText.Length > AxisTickOptions.MaximumLabelLength)
                    throw new FormatException($"Tick label on line {lineNumber} exceeds 256 characters.");
                foreach (var character in labelText)
                    if (char.IsControl(character))
                        throw new FormatException($"Tick label on line {lineNumber} must not contain control characters.");
                label = labelText.ToString();
            }
            ticks.Add(new(value, label));
        }
        if (ticks.Count == 0) return AxisTickOptions.Default.CustomTicks;
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
