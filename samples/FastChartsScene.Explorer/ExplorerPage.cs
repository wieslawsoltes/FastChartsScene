using System.Text;
using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Examples;
using FastChartsScene.Export;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage : Page
{
    public ChartControl Chart { get; }=new();
    public ChartExample Current { get; private set; }=ExampleCatalog.All[0];
    public ChartDocument Document=>Chart.Document!;
    public bool IsStreaming=>_timer.IsEnabled;
    public string? LastCommandError { get; private set; }
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(32) };
    private readonly Grid _workspace=new();
    private readonly StackPanel _inspector=new() { Spacing=12,Padding=new Thickness(16) };
    private readonly TextBlock _status=new() { FontSize=11,Margin=new Thickness(16,6,16,4),TextTrimming=TextTrimming.CharacterEllipsis };
    private readonly TextBlock _description=new() { TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(16) };
    private readonly TextBox _data=new() { IsReadOnly=true,AcceptsReturn=true,FontFamily=new FontFamily("monospace"),FontSize=11,BorderThickness=new Thickness(0),Margin=new Thickness(12),TextWrapping=TextWrapping.NoWrap };
    private readonly TextBox _usage=new() { IsReadOnly=true,AcceptsReturn=true,FontFamily=new FontFamily("monospace"),FontSize=12,BorderThickness=new Thickness(0),Margin=new Thickness(12) };
    private readonly Button _play=new() { Content="Start acquisition",Padding=new Thickness(12,6,12,6) };
    private RollingBuffer[] _buffers=[];
    private readonly double[] _streamBatch=new double[64];
    private bool _inspectorVisible=true;
    private readonly Grid _root=new();
    private static SolidColorBrush Brush(string hex)
    {
        var c=ColorRgba.Parse(hex); return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(c.A*255),(byte)(c.R*255),(byte)(c.G*255),(byte)(c.B*255)));
    }
    private static TextBlock Label(string text,double size=12)=>new() { Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap };
    private static Button Button(string title,Action action)
    {
        var b=new Button { Content=title,Padding=new Thickness(12,6,12,6) }; AutomationProperties.SetName(b,title); b.Click+=(_,_)=>action(); return b;
    }
    private void Execute(Action action)
    {
        try { action(); LastCommandError=null; } catch(Exception e) { LastCommandError=e.Message; }
        UpdateStatus();
    }
    public ExplorerPage()
    {
        BrowserBridge.Page=this; BuildShell();
        Chart.FrameChanged+=(_,_)=>UpdateStatus(); _timer.Tick+=(_,_)=>Execute(AppendStream);
        SizeChanged+=(_,_)=>Responsive(); Unloaded+=(_,_)=> { SetStreaming(false); SaveGallerySettings(); };
        SelectExample("signals"); ShowGallery();
    }
    private void Responsive()
    {
        _workspace.ColumnDefinitions[0].Width=new GridLength(0);
        _workspace.ColumnDefinitions[2].Width=new GridLength(_inspectorVisible&&ActualWidth>=1100?274:0);
        _inspectorScroll.Visibility=_inspectorVisible&&ActualWidth>=1100?Visibility.Visible:Visibility.Collapsed;
        _center.RowDefinitions[1].Height=new GridLength(_detailsVisible?168:0);
        _center.Children[1].Visibility=_detailsVisible?Visibility.Visible:Visibility.Collapsed;
    }
    public void SelectExample(string id)
    {
        var example=ExampleCatalog.Find(id); var next=example.Create(); var old=Chart.Document;
        SetStreaming(false); _buffers=[]; Current=example; Chart.Document=next; old?.Dispose();
        UpdateExampleHeader(); ShowGallery(false);
        _description.Text=example.Description+"\n\nControls: drag to pan or orbit; wheel/pinch to zoom; Shift-drag for rectangle zoom; Home to fit; Z / Y to undo / redo. Click a legend entry to toggle its series.\n\nPNG includes 2D and depth-tested 3D. SVG preserves 2D vectors. CSV saves the first XY series; All data CSV saves every supported series.\n\nThis preview is not full LightningChart API, feature, visual, or performance parity.";
        _usage.Text=$"using FastChartsScene.Examples;\nusing FastChartsScene.Controls.Uno;\n\nvar document = ExampleCatalog.Find(\"{id}\").Create();\nvar chart = new ChartControl {{ Document = document }};\n// Document is caller-owned; dispose it after detaching.\n// Package: FastChartsScene.Controls.Uno";
        _play.IsEnabled=id is "signals" or "streaming";
        var first=next.Series.OfType<DataSeries>().FirstOrDefault();
        _data.Text=first==null?$"{next.Series.Count} series · {next.TotalPoints:N0} source values\nGrid, mesh and financial data are held in typed chart models.":SampleText(first.Data);
        BuildInspector(); UpdateStatus();
    }
    private static string SampleText(IDataSource data)
    {
        var b=new StringBuilder("Original samples (first 24)\nIndex        X                  Y\n");
        for(var i=0;i<Math.Min(24,data.Count);i++) b.AppendLine(FormattableString.Invariant($"{i,-10} {data.X(i),-18:G9} {data.Y(i),-18:G9}")); return b.ToString();
    }
    private void BuildInspector()
    {
        _inspector.Children.Clear(); var d=Document;
        _inspector.Children.Add(new TextBlock { Text="Properties",FontSize=13,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
        _inspector.Children.Add(_propertySearch);
        var title=new TextBox { Header="Title",Text=d.Title }; title.TextChanged+=(_,_)=>d.Title=title.Text; _inspector.Children.Add(title);
        var theme=new ComboBox { Header="Theme",ItemsSource=new[]{"Dark","Light","High contrast"},SelectedIndex=d.Theme==ChartTheme.Light?1:d.Theme==ChartTheme.HighContrast?2:0,HorizontalAlignment=HorizontalAlignment.Stretch };
        theme.SelectionChanged+=(_,_)=>SetTheme(theme.SelectedIndex==1?"light":theme.SelectedIndex==2?"contrast":"dark"); _inspector.Children.Add(theme);
        var renderer=new ComboBox { Header="Rendering backend",ItemsSource=Enum.GetValues<ChartRenderingMode>(),SelectedItem=Chart.RenderingMode,HorizontalAlignment=HorizontalAlignment.Stretch };
        renderer.SelectionChanged+=(_,_)=> { if(renderer.SelectedItem is ChartRenderingMode mode) Chart.RenderingMode=mode; }; _inspector.Children.Add(renderer);
        Check("Grid",d.ShowGrid,v=>d.ShowGrid=v); Check("Legend",d.ShowLegend,v=>d.ShowLegend=v);
        if(d.View is ChartView.ThreeD or ChartView.Pie3D) Check("Orthographic projection",d.Camera.Orthographic,v=>d.Camera.Orthographic=v);
        var axisSelector=new ComboBox { Header="Axis",ItemsSource=d.XAxes.Concat(d.YAxes).Append(d.ZAxis).ToArray(),DisplayMemberPath=nameof(Axis.Title),SelectedItem=d.XAxis,HorizontalAlignment=HorizontalAlignment.Stretch };
        var min=new NumberBox { Header="Minimum",Value=d.XAxis.Range.Minimum }; var max=new NumberBox { Header="Maximum",Value=d.XAxis.Range.Maximum };
        axisSelector.SelectionChanged+=(_,_)=> { if(axisSelector.SelectedItem is Axis a) { min.Value=a.Range.Minimum; max.Value=a.Range.Maximum; } };
        _inspector.Children.Add(axisSelector); _inspector.Children.Add(min); _inspector.Children.Add(max);
        _inspector.Children.Add(Button("Apply range",()=>Execute(()=> { if(axisSelector.SelectedItem is Axis a) a.Range=new AxisRange(min.Value,max.Value); })));
        _inspector.Children.Add(new Border { Height=1,Background=Brush("373D46"),Margin=new Thickness(0,4,0,4) });
        _inspector.Children.Add(Label("SERIES",11));
        foreach(var s in d.Series)
        {
            if(s is HeatmapSeries heat) _inspector.Children.Add(new IntensityScaleEditor { Series=heat });
            Check(s.Title,s.Visible,v=>s.Visible=v);
            var width=new Slider { Minimum=0,Maximum=6,StepFrequency=.25,Value=s.StrokeWidth,Header="Stroke width" }; width.ValueChanged+=(_,_)=>s.StrokeWidth=width.Value; _inspector.Children.Add(width);
            if(s is LineSeries line)
            {
                var marker=new ComboBox { Header="Marker",ItemsSource=Enum.GetValues<MarkerShape>(),SelectedItem=line.Marker,HorizontalAlignment=HorizontalAlignment.Stretch };
                marker.SelectionChanged+=(_,_)=> { if(marker.SelectedItem is MarkerShape m) line.Marker=m; }; _inspector.Children.Add(marker);
            }
        }
        foreach(var control in _inspector.Children.OfType<Control>())
        {
            control.FontSize=12; control.MinHeight=28;
            if(control is TextBox box) box.Padding=new Thickness(7,3,7,3);
        }
        FilterProperties();
        void Check(string text,bool initial,Action<bool> changed)
        {
            var c=new CheckBox { Content=text,IsChecked=initial }; c.Checked+=(_,_)=>changed(true); c.Unchecked+=(_,_)=>changed(false); _inspector.Children.Add(c);
        }
    }
    public void SetTheme(string name)=>Document.Theme=name switch { "light"=>ChartTheme.Light,"contrast"=>ChartTheme.HighContrast,"dark"=>ChartTheme.Dark,_=>throw new ArgumentException("Unknown theme.") };
    public void SetStreaming(bool enabled)
    {
        if(!enabled) { _timer.Stop(); _play.Content="Start acquisition"; return; }
        if(Current.Id is not ("signals" or "streaming")) throw new InvalidOperationException("Select a signal acquisition example first.");
        if(_buffers.Length==0)
        {
            _buffers=Enumerable.Range(0,3).Select(_=>new RollingBuffer(20000,0,.0005)).ToArray();
            for(var channel=0;channel<3;channel++) _buffers[channel].Append(Enumerable.Range(0,20000).Select(i=>Signal(i*.0005,channel)).ToArray());
        }
        _timer.Start(); _play.Content="Pause acquisition";
    }
    private static double Signal(double t,int channel)=>channel switch { 0=>Math.Sin(t*7)*(.7+.22*Math.Sin(t*.8)),1=>.55*Math.Cos(t*11)+.18*Math.Sin(t*29),_=>.35*Math.Sin(t*3)+.7 };
    private void AppendStream()
    {
        if(_buffers.Length!=3) return;
        using var update=Document.BeginUpdate();
        for(var channel=0;channel<3;channel++)
        {
            var buffer=_buffers[channel]; var first=buffer.SamplesWritten; var batch=_streamBatch;
            for(var i=0;i<batch.Length;i++) batch[i]=Signal((first+i)*.0005,channel);
            buffer.Append(batch); ((LineSeries)Document.Series[channel]).Data=buffer.Snapshot();
        }
        var end=(_buffers[0].SamplesWritten-1)*.0005; Document.XAxis.Range=new(end-10,end);
    }
    private void UpdateStatus()
    {
        var frame=Chart.Frame;
        _status.Text=LastCommandError??Chart.LastError??$"{Chart.Backend}    |    {Document.TotalPoints:N0} source values    |    {frame?.Metadata.VertexCount??0:N0} vertices    |    compile {Chart.CompileMilliseconds:F2} ms    |    {Current.Category}";
    }
    private async void Export(string type)
    {
        try
        {
            byte[] bytes; string mime; var extension=type=="allcsv"?"csv":type=="project"?"zip":type;
            if(type=="project") { bytes=ChartProjectExport.Write(Document); mime="application/zip"; }
            else if(type=="png") { bytes=Chart.ExportPng(2); mime="image/png"; }
            else if(type=="csv")
            {
                var data=Document.Series.OfType<DataSeries>().FirstOrDefault()?.Data??throw new NotSupportedException("Use All data CSV to export financial, grid or 3D series.");
                bytes=Encoding.UTF8.GetBytes(CsvData.Write(data)); mime="text/csv";
            }
            else if(type=="allcsv") { bytes=Encoding.UTF8.GetBytes(ChartDataExport.WriteCsv(Document)); mime="text/csv"; }
            else if(type=="svg") { bytes=Encoding.UTF8.GetBytes(SvgExport.Write(Chart.Frame??throw new InvalidOperationException("No rendered frame."))); mime="image/svg+xml"; }
            else { bytes=Encoding.UTF8.GetBytes(Chart.Frame?.MetadataJson()??throw new InvalidOperationException("No rendered frame.")); mime="application/json"; }
            await SaveFile(bytes,Current.Id+(type=="allcsv"?"-all":"")+"."+extension,mime,"."+extension); LastCommandError=null;
        }
        catch(Exception e) { LastCommandError=e.Message; }
        UpdateStatus();
    }
    private static async Task SaveFile(byte[] bytes,string name,string mime,string extension)
    {
#if __WASM__
        var base64=Convert.ToBase64String(bytes);
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS($"(()=>{{const s=atob('{base64}');const b=Uint8Array.from(s,c=>c.charCodeAt(0));const u=URL.createObjectURL(new Blob([b],{{type:'{mime}'}}));const a=document.createElement('a');a.href=u;a.download='{name}';a.click();setTimeout(()=>URL.revokeObjectURL(u),1000);}})();");
        await Task.CompletedTask;
#else
        var picker=new FileSavePicker { SuggestedFileName=name }; picker.FileTypeChoices.Add("Chart export",new List<string> { extension });
        var file=await picker.PickSaveFileAsync(); if(file!=null) await FileIO.WriteBytesAsync(file,bytes);
#endif
    }
}
