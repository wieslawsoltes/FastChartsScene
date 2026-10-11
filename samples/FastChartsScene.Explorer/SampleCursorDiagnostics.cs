using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Core;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public sealed partial record SampleCursorSnapshot(string SeriesId, string Title, int Index, PointD Data, PointD Screen, double Distance);
public sealed partial record SampleQuerySnapshot(SampleCursorSnapshot? Hit, long IndexBuilds, int VisitedNodes, int TestedSamples, bool CanUndo, bool CanRedo);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SampleQuerySnapshot))]
internal partial class SampleCursorJsonContext : JsonSerializerContext { }

public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string InspectSampleCursor()
    {
        var page = Page ?? throw new InvalidOperationException("Explorer not initialized.");
        var chart = page.Chart; var input = chart.Interaction; var hit = chart.DataHit;
        var snapshot = new SampleQuerySnapshot(hit == null ? null : new(hit.SeriesId, hit.Title, hit.Index, hit.Data, hit.Screen, hit.Distance),
            input?.SampleIndexBuildCount ?? 0, input?.LastSampleQueryVisits ?? 0, input?.LastSampleQueryTests ?? 0, input?.CanUndo ?? false, input?.CanRedo ?? false);
        return JsonSerializer.Serialize(snapshot, SampleCursorJsonContext.Default.SampleQuerySnapshot);
    }
}
