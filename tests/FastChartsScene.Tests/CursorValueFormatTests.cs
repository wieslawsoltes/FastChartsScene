using System.Globalization;
using FastChartsScene.Interaction;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static void RegisterCursorValueFormatTests()
    {
        Add("cursor display formats default to the existing G9 cells and normalize empty input", () => {
            True(new CursorValueFormat() == CursorValueFormat.Default);
            True(new CursorValueFormat("", "") == CursorValueFormat.Default);
            True(CursorValueFormat.Default.FormatX(Math.PI) == "3.14159265");
            True(CursorValueFormat.Default.FormatValue(123.5) == "123.5");
        });
        Add("cursor display formats independently control X and value precision", () => {
            var format = new CursorValueFormat("F2", "E3");
            True(format.FormatX(12.375) == "12.38"); True(format.FormatValue(1234.6) == "1.235E+003");
            True(new CursorValueFormat("0.000 's'", "+0.00;-0.00;0.00").FormatX(1.5) == "1.500 s");
        });
        Add("round-trip cursor format preserves all finite binary64 values including negative zero", () => {
            foreach (var value in new[] { Math.PI, Math.E, double.Epsilon, double.MaxValue, -double.MaxValue, -0.0, 1e12 + .125 })
            {
                var text = CursorValueFormat.RoundTrip.FormatValue(value);
                var parsed = double.Parse(text, CultureInfo.InvariantCulture);
                True(BitConverter.DoubleToInt64Bits(parsed) == BitConverter.DoubleToInt64Bits(value));
            }
        });
        Add("cursor display formatting remains invariant under host culture changes", () => {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                var format = new CursorValueFormat("N2", "F3");
                True(format.FormatX(1234.5) == "1,234.50"); True(format.FormatValue(1.5) == "1.500");
            }
            finally { CultureInfo.CurrentCulture = old; }
        });
        Add("cursor format validation rejects invalid and unbounded formats before state exists", () => {
            foreach (var text in new[] { "G99", "F999999999", "D2", "F-1", "literal", "0\n0", "0\u20280", new string('0', 65) })
            {
                Throws<ArgumentException>(() => new CursorValueFormat(text, "G9"));
                Throws<ArgumentException>(() => new CursorValueFormat("G9", text));
            }
            Throws<ArgumentNullException>(() => new CursorValueFormat(null!, "G9"));
            Throws<ArgumentNullException>(() => new CursorValueFormat("G9", null!));
        });
        Add("equal cursor formats compare by value without mutating shared presets", () => {
            True(CursorValueFormat.Scientific == new CursorValueFormat("E6", "E6"));
            True(new CursorValueFormat("F3", "G9") != CursorValueFormat.Default);
            True(CursorValueFormat.Default.XFormat == "G9" && CursorValueFormat.Default.ValueFormat == "G9");
        });
    }
}
