using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Rendering;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
namespace FastChartsScene.Explorer;

public sealed record AxisLayoutReview(double Width, double Height, AxisLabelPlacement[] Labels);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AxisLayoutReview))]
internal partial class AxisLayoutReviewContext : JsonSerializerContext { }
public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string InspectMeasuredAxisLayout()
    {
        var frame = Page!.Chart.Frame ?? throw new InvalidOperationException("No compiled chart.");
        return JsonSerializer.Serialize(new AxisLayoutReview(frame.Metadata.Width, frame.Metadata.Height,
            frame.Layout.AxisLabels.ToArray()), AxisLayoutReviewContext.Default.AxisLayoutReview);
    }
}
