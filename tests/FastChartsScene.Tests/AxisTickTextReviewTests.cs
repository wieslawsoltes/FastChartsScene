using System.Globalization;
using FastChartsScene.Core;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAxisTickTextReviewTests()
    {
        Add("tick parser cannot turn hidden line separators into additional records", () => {
            foreach (var separator in new[] { '\v', '\f', '\u0085', '\u2028', '\u2029' })
            {
                var input = "0|First" + separator + "1|Second";
                Throws<FormatException>(() => AxisTickText.Parse(input));
                Throws<FormatException>(() => AxisTickText.Parse(separator.ToString()));
                var axis = new Axis("x") { TickOptions = new(AxisTickMode.Custom, [new(3, "Original")]) };
                var original = axis.TickOptions;
                Throws<FormatException>(() => axis.TickOptions = new(AxisTickMode.Custom, AxisTickText.Parse(input)));
                True(ReferenceEquals(original, axis.TickOptions));
            }
        });
        Add("span tick parser preserves mixed CR LF CRLF labels and original double bits", () => {
            var input = "  \r\n10\r5| \u03A9 | \u03B2  \n-0|\r\n-1.7976931348623157E+308|min\r4.9406564584124654E-324|tiny\n";
            var ticks = AxisTickText.Parse(input);
            Equal(5, ticks.Count); True(ticks[0].Value == -double.MaxValue);
            True(BitConverter.DoubleToInt64Bits(ticks[1].Value) == BitConverter.DoubleToInt64Bits(-0.0));
            True(ticks[1].Label == ""); True(ticks[2].Value == double.Epsilon);
            True(ticks[3].Label == " \u03A9 | \u03B2  "); True(ticks[4].Label == null);
            var roundtrip = AxisTickText.Parse(AxisTickText.Write(ticks));
            True(ticks.SequenceEqual(roundtrip));
        });
        Add("tick text errors identify logical lines after mixed newline sequences", () => {
            var input = "\r\n \r0|valid\n\t\nnot-a-number|bad";
            try { AxisTickText.Parse(input); throw new Exception("Expected invalid tick value."); }
            catch (FormatException error) { True(error.Message.Contains("line 5.")); }
        });
        Add("maximum-size blank tick text uses bounded constant temporary storage", () => {
            var text = string.Concat(Enumerable.Repeat(" \r\n", AxisTickText.MaximumTextLength / 3));
            AxisTickText.Parse(text);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var ticks = AxisTickText.Parse(text);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Equal(0, ticks.Count); True(ReferenceEquals(ticks, AxisTickOptions.Default.CustomTicks));
            True(bytes <= 1024, $"Blank-line parsing allocated {bytes} bytes.");
            Throws<FormatException>(() => AxisTickText.Parse(text + new string(' ', 4)));
        });
        Add("blank lines do not count against the custom tick limit", () => {
            var text = string.Join("\r\n\n", Enumerable.Range(0, 512).Select(i => $"{i}|T{i}")) + "\n\r\n ";
            var ticks = AxisTickText.Parse(text); Equal(512, ticks.Count); Equal(511, ticks[^1].Value);
            Throws<FormatException>(() => AxisTickText.Parse(text + "\n512|one too many"));
        });
        Add("tick parser validates label size and controls before allocating label strings", () => {
            var valid = "1|" + new string('x', 256); Equal(256, AxisTickText.Parse(valid)[0].Label!.Length);
            Throws<FormatException>(() => AxisTickText.Parse("1|" + new string('x', 257)));
            foreach (var c in new[] { '\0', '\t', '\b', '\u007F' })
                Throws<FormatException>(() => AxisTickText.Parse("1|left" + c + "right"));
            // Spaces and tabs around a numeric token remain valid invariant whitespace.
            Equal(1.25, AxisTickText.Parse(" \t1.25\t |label")[0].Value);
        });
        Add("tick parser remains invariant and owns label text after span scanning", () => {
            var before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                var ticks = AxisTickText.Parse("1.25|A|B\n2.5| ");
                Equal(1.25, ticks[0].Value); True(ticks[0].Label == "A|B"); True(ticks[1].Label == " ");
                Throws<FormatException>(() => AxisTickText.Parse("1,25|not invariant"));
                Throws<NotSupportedException>(() => ((IList<CustomAxisTick>)ticks)[0] = new(99));
            }
            finally { CultureInfo.CurrentCulture = before; }
        });
    }
}
