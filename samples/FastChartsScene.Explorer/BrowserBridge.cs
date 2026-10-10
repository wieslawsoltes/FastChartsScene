using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Examples;
using Windows.Foundation;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public sealed record AxisState(string Id,double Minimum,double Maximum);
public sealed record BrowserState(string Example,string View,string Backend,string? Error,bool Streaming,long Revision,long SourcePoints,int Vertices,long GpuSubmissions,long OverlayDraws,double CompileMilliseconds,RectD Bounds,RectD Plot,AxisState[] Axes,string[] Examples,int SeriesCount,bool Grid,bool Legend,bool Orthographic);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserState))]
internal partial class BrowserJsonContext : JsonSerializerContext { }

/// <summary>Same-origin diagnostic exports for automated tests and host integrations. No network or filesystem access.</summary>
public static partial class BrowserBridge
{
    internal static ExplorerPage? Page { get; set; }
#if __WASM__
    [JSExport]
#endif
    public static string Inspect()
    {
        var page=Page??throw new InvalidOperationException("Explorer not initialized."); var chart=page.Chart; var d=page.Document;
        var origin=chart.TransformToVisual(null).TransformPoint(new Point(0,0));
        var state=new BrowserState(page.Current.Id,d.View.ToString(),chart.Backend,page.LastCommandError??chart.LastError,page.IsStreaming,d.Revision,d.TotalPoints,chart.Frame?.Metadata.VertexCount??0,chart.GpuSubmissions,chart.OverlayDraws,chart.CompileMilliseconds,
            new(origin.X,origin.Y,chart.ActualWidth,chart.ActualHeight),chart.Frame?.Layout.Plot??default,
            d.XAxes.Concat(d.YAxes).Append(d.ZAxis).Select(a=>new AxisState(a.Id,a.Range.Minimum,a.Range.Maximum)).ToArray(),ExampleCatalog.All.Select(e=>e.Id).ToArray(),d.Series.Count,d.ShowGrid,d.ShowLegend,d.Camera.Orthographic);
        return JsonSerializer.Serialize(state,BrowserJsonContext.Default.BrowserState);
    }
#if __WASM__
    [JSExport]
#endif
    public static void Command(string command,string value)
    {
        var p=Page??throw new InvalidOperationException("Explorer not initialized.");
        switch(command)
        {
            case "example":p.SelectExample(value);break;
            case "backend":p.Chart.RenderingMode=Enum.Parse<ChartRenderingMode>(value);break;
            case "theme":p.SetTheme(value);break;
            case "grid":p.Document.ShowGrid=bool.Parse(value);break;
            case "legend":p.Document.ShowLegend=bool.Parse(value);break;
            case "projection":p.Document.Camera.Orthographic=bool.Parse(value);break;
            case "stream":p.SetStreaming(bool.Parse(value));break;
            case "fit":p.Chart.Interaction?.ResetView();break;
            case "undo":p.Chart.Interaction?.Undo();break;
            case "redo":p.Chart.Interaction?.Redo();break;
            default:throw new ArgumentException($"Unknown command '{command}'.");
        }
    }
}
