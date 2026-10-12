using System.Globalization;
using FastChartsScene.Core;

internal static class AxisTickTextBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        var text = string.Concat(Enumerable.Repeat(" \r\n", 40000)) + "0|Start\r\n5|Middle\n10|End";
        static IReadOnlyList<CustomAxisTick> Previous(string text)
        {
            List<CustomAxisTick> ticks = [];
            foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var pipe = line.IndexOf('|'); var number = pipe < 0 ? line : line[..pipe];
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                    throw new FormatException("Invalid benchmark input.");
                ticks.Add(new(value, pipe < 0 ? null : line[(pipe + 1)..]));
            }
            return new AxisTickOptions(AxisTickMode.Custom, ticks).CustomTicks;
        }
        var reference = Previous(text); var actual = AxisTickText.Parse(text);
        if (!reference.SequenceEqual(actual)) throw new InvalidOperationException("Tick parser results differ.");
        measure("tick-text-normalize-split-120k", () => Previous(text).Sum(t => t.Value));
        measure("tick-text-span-scan-120k", () => AxisTickText.Parse(text).Sum(t => t.Value));
        measurements.Add(new { name = "tick-text-input", characters = text.Length, ticks = actual.Count,
            note = "Equivalent valid-input parsing with 40000 blank lines, not chart rendering, normal typing latency or GPU throughput." });
    }
}
