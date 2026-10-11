using FastChartsScene.Core;

internal static class CatalogBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        var entries = Enumerable.Range(0, 20000).Select(i => new CatalogEntry("entry" + i, "Signal instrument " + i,
            i % 2 == 0 ? "Scientific" : "Other", "Generated instrument description",
            i % 4 == 0 ? ["Realtime", "2D"] : ["2D"])).ToArray();
        var model = new CatalogBrowserModel(entries); string[] tags = ["Realtime", "2D"];
        var rows = entries.Select(e => (Entry: e, Search: e.Title + "\n" + e.Description)).ToArray();
        CatalogEntry[] Reference() => rows.Where(r => r.Entry.Category == "Scientific" &&
            r.Search.Contains("signal", StringComparison.OrdinalIgnoreCase) &&
            r.Search.Contains("instrument", StringComparison.OrdinalIgnoreCase) &&
            tags.All(t => r.Entry.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))).Select(r => r.Entry).ToArray();
        var expected = Reference(); var actual = model.Filter("instrument signal", "Scientific", tags);
        if (!actual.Select(e => e.Id).SequenceEqual(expected.Select(e => e.Id))) throw new InvalidOperationException("Catalog filter results differ.");
        measure("catalog-exhaustive-20k", () => Reference().Length);
        measure("catalog-retained-filter-20k", () => model.Filter("instrument signal", "Scientific", tags).Count);
        measurements.Add(new { name = "catalog-retained-result", entries = entries.Length, matches = actual.Count, filterPasses = model.FilterPasses,
            note = "Same-query cache only; changed searches still scan metadata. Dataset construction and initial filtering are outside warm timings." });
    }
}
