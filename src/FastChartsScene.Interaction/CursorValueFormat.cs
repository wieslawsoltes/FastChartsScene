using System.Globalization;
using FastChartsScene.Core;

namespace FastChartsScene.Interaction;

/// <summary>Immutable, bounded invariant number formats for cursor display only.
/// Source snapshots, full-precision details and CSV are not changed. The same numeric
/// format validation as axis ticks rejects unbounded precision and control characters.</summary>
public sealed record CursorValueFormat
{
    public static CursorValueFormat Default { get; } = new("G9", "G9");
    public static CursorValueFormat RoundTrip { get; } = new("R", "R");
    public static CursorValueFormat Scientific { get; } = new("E6", "E6");
    public string XFormat { get; }
    public string ValueFormat { get; }
    public CursorValueFormat(string xFormat = "G9", string valueFormat = "G9")
    {
        ArgumentNullException.ThrowIfNull(xFormat); ArgumentNullException.ThrowIfNull(valueFormat);
        // Empty input resets that column to the original compact format.
        XFormat = new AxisTickOptions(numericFormat: xFormat.Length == 0 ? "G9" : xFormat).NumericFormat;
        ValueFormat = new AxisTickOptions(numericFormat: valueFormat.Length == 0 ? "G9" : valueFormat).NumericFormat;
    }
    public string FormatX(double value) => value.ToString(XFormat, CultureInfo.InvariantCulture);
    public string FormatValue(double value) => value.ToString(ValueFormat, CultureInfo.InvariantCulture);
}
