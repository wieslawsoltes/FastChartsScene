using System.Text;
using FastChartsScene.Charts;
using FastChartsScene.Data;
using FastChartsScene.Examples;
using FastChartsScene.Rendering;
using FastChartsScene.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    /// <summary>Validates a complete imported scene before replacing the current document.</summary>
    public void ImportDocument(string json)
    {
        var next=ChartJson.Read(json);
        try { new SceneCompiler().Compile(next,Math.Max(400,Chart.ActualWidth),Math.Max(300,Chart.ActualHeight)); }
        catch { next.Dispose(); throw; }
        SetStreaming(false); _buffers=[];
        var old=Chart.Document;
        Current=new ChartExample("document","Documents",next.Title,"Imported chart document. Axis bindings, styles, data, annotations and camera settings were restored.",()=>ChartJson.Read(json));
        Chart.Document=next; old?.Dispose();
        _selecting=true; _examples.SelectedItem=null; _selecting=false; _play.IsEnabled=false;
        _description.Text=Current.Description+"\n\nThe file uses FastChartsScene format version 1. Session-only series IDs are regenerated on import.";
        _usage.Text="using FastChartsScene.Serialization;\n\nvar document = ChartJson.Read(json);\nchart.Document = document;\nvar saved = ChartJson.Write(document);";
        var first=next.Series.OfType<DataSeries>().FirstOrDefault();
        _data.Text=first==null?$"{next.Series.Count} series · {next.TotalPoints:N0} source values":SampleText(first.Data);
        BuildInspector(); LastCommandError=null; UpdateStatus();
    }
    private async void SaveDocument()
    {
        try
        {
            var json=ChartJson.Write(Document);
            await SaveFile(Encoding.UTF8.GetBytes(json),Current.Id+".fcscene.json","application/json",".json"); LastCommandError=null;
        }
        catch(Exception e) { LastCommandError=e.Message; }
        UpdateStatus();
    }
    private async void OpenDocument()
    {
        try
        {
            var picker=new FileOpenPicker { ViewMode=PickerViewMode.List }; picker.FileTypeFilter.Add(".json");
            var file=await picker.PickSingleFileAsync(); if(file==null) return;
            var properties=await file.GetBasicPropertiesAsync(); if(properties.Size>64UL*1024*1024) throw new FormatException("Chart file exceeds the 64 MiB input limit.");
            ImportDocument(await FileIO.ReadTextAsync(file));
        }
        catch(Exception e) { LastCommandError=e.Message; }
        UpdateStatus();
    }
}
public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string ExportDocument()=>ChartJson.Write((Page??throw new InvalidOperationException("Explorer not initialized.")).Document);
#if __WASM__
    [JSExport]
#endif
    public static void ImportDocument(string json)=>(Page??throw new InvalidOperationException("Explorer not initialized.")).ImportDocument(json);
}
