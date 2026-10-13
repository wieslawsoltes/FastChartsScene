using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using FastChartsScene.Core;

namespace FastChartsScene.Charts;

public enum AnnotationKind { Text, HorizontalLine, VerticalLine, Rectangle }
public sealed class Annotation : ObservableModel
{
    private string _text="Annotation",_yAxisId="y";
    private AnnotationKind _kind;
    private double _x,_y,_x2=1,_y2=1;
    private ColorRgba _color=Palette.Series[2];
    public string Text { get=>_text; set=>Set(ref _text,value??""); }
    public string YAxisId { get=>_yAxisId; set { ArgumentException.ThrowIfNullOrWhiteSpace(value); Set(ref _yAxisId,value); } }
    public AnnotationKind Kind { get=>_kind; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _kind,value); } }
    public double X { get=>_x; set { Validate(value); Set(ref _x,value); } }
    public double Y { get=>_y; set { Validate(value); Set(ref _y,value); } }
    public double X2 { get=>_x2; set { Validate(value); Set(ref _x2,value); } }
    public double Y2 { get=>_y2; set { Validate(value); Set(ref _y2,value); } }
    public ColorRgba Color { get=>_color; set=>Set(ref _color,value); }
    private static void Validate(double v) { if(!double.IsFinite(v)) throw new ArgumentOutOfRangeException(nameof(v)); }
}

/// <summary>Immutable pane definition. Caller-owned arrays cannot silently invalidate a compiled layout.</summary>
public sealed record ChartSegment
{
    private readonly string[] _axisIds;
    public string Name { get; }
    public double Weight { get; }
    public string[] AxisIds=> (string[])_axisIds.Clone();
    public ChartSegment(string name,double weight,string[] axisIds)
    {
        ArgumentNullException.ThrowIfNull(axisIds);
        Name=name??""; Weight=weight; _axisIds=(string[])axisIds.Clone();
    }
}
public sealed class Camera3D : ObservableModel
{
    private double _azimuth=38,_elevation=28,_distance=4.5;
    private bool _orthographic;
    public double Azimuth { get=>_azimuth; set { if(!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _azimuth,value%360); } }
    public double Elevation { get=>_elevation; set { if(!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _elevation,Math.Clamp(value,-85,85)); } }
    public double Distance { get=>_distance; set { if(!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _distance,Math.Clamp(value,1.5,30)); } }
    public bool Orthographic { get=>_orthographic; set=>Set(ref _orthographic,value); }
    public void Reset() { Azimuth=38; Elevation=28; Distance=4.5; Orthographic=false; }
}

/// <summary>Renderer-independent, UI-thread-affine chart model. Nested updates coalesce notifications.</summary>
public sealed partial class ChartDocument : ObservableModel,IDisposable
{
    private string _title="FastChartsScene",_subtitle="";
    private ChartView _view;
    private ChartTheme _theme=ChartTheme.Dark;
    private AxisLayout _axisLayout;
    private bool _showGrid=true,_showLegend=true;
    private int _updateDepth;
    private bool _dirty,_disposed;
    private readonly HashSet<ObservableModel> _observed=[];
    public event EventHandler? Changed;
    public long Revision { get; private set; }
    public bool IsDisposed=>_disposed;
    public string Title { get=>_title; set=>Set(ref _title,value??""); }
    public string Subtitle { get=>_subtitle; set=>Set(ref _subtitle,value??""); }
    public ChartView View { get=>_view; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _view,value); } }
    public ChartTheme Theme { get=>_theme; set { ArgumentNullException.ThrowIfNull(value); Set(ref _theme,value); } }
    public AxisLayout AxisLayout { get=>_axisLayout; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _axisLayout,value); } }
    public bool ShowGrid { get=>_showGrid; set=>Set(ref _showGrid,value); }
    public bool ShowLegend { get=>_showLegend; set=>Set(ref _showLegend,value); }
    public ObservableCollection<Axis> XAxes { get; }=[new("x") { Title="X axis" }];
    public ObservableCollection<Axis> YAxes { get; }=[new("y") { Title="Y axis",Range=new(-1,1) }];
    public Axis ZAxis { get; }=new("z") { Title="Z axis",Range=new(-1,1) };
    public ObservableCollection<Series> Series { get; }=[];
    public ObservableCollection<Annotation> Annotations { get; }=[];
    public ObservableCollection<ChartSegment> Segments { get; }=[];
    public Camera3D Camera { get; }=new();
    public Axis XAxis=>XAxes[0];
    public Axis YAxis=>YAxes[0];
    public long TotalPoints=>Series.Sum(s=>(long)s.PointCount);
    public ChartDocument()
    {
        PropertyChanged+=ModelChanged;
        XAxes.CollectionChanged+=CollectionChanged; YAxes.CollectionChanged+=CollectionChanged;
        Series.CollectionChanged+=CollectionChanged; Annotations.CollectionChanged+=CollectionChanged; Segments.CollectionChanged+=CollectionChanged;
        Rewire();
    }
    private void CollectionChanged(object? sender,NotifyCollectionChangedEventArgs e)=>Rewire();
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)=>Invalidate();
    private void Rewire()
    {
        if(_disposed) return;
        var current=XAxes.Cast<ObservableModel>().Concat(YAxes).Concat(Series).Concat(Annotations).Append(Camera).Append(ZAxis).Where(m=>m!=null).ToHashSet();
        foreach(var item in _observed.Except(current)) item.PropertyChanged-=ModelChanged;
        foreach(var item in current.Except(_observed)) item.PropertyChanged+=ModelChanged;
        _observed.Clear(); _observed.UnionWith(current); Invalidate();
    }
    public IDisposable BeginUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed,this); _updateDepth++; return new UpdateScope(this);
    }
    private sealed class UpdateScope(ChartDocument owner):IDisposable
    {
        private ChartDocument? _owner=owner;
        public void Dispose()
        {
            var d=Interlocked.Exchange(ref _owner,null); if(d==null) return;
            if(--d._updateDepth==0&&d._dirty) d.Invalidate();
        }
    }
    public void Invalidate()
    {
        if(_disposed) return;
        if(_updateDepth>0) { _dirty=true; return; }
        _dirty=false; Revision++; Changed?.Invoke(this,EventArgs.Empty);
    }
    public IReadOnlyList<string> Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        List<string> errors=[];
        if(XAxes.Any(a=>a==null)||YAxes.Any(a=>a==null)||Series.Any(s=>s==null)||Annotations.Any(a=>a==null)||Segments.Any(s=>s==null))
            return ["Chart collections cannot contain null items."];
        if(XAxes.Count==0||YAxes.Count==0) errors.Add("At least one X and Y axis are required.");
        if(XAxes.GroupBy(a=>a.Id).Any(g=>g.Count()>1)||YAxes.GroupBy(a=>a.Id).Any(g=>g.Count()>1)) errors.Add("Axis IDs must be unique within each dimension.");
        if(Series.GroupBy(s=>s.Id).Any(g=>g.Count()>1)) errors.Add("Series IDs must be unique.");
        foreach(var s in Series)
        {
            if(!XAxes.Any(a=>a.Id==s.XAxisId)||!YAxes.Any(a=>a.Id==s.YAxisId)) errors.Add($"{s.Title}: missing axis binding.");
            if(s is BandSeries b&&(b.Data.Count!=b.Upper.Count||Enumerable.Range(0,b.Data.Count).Any(i=>b.Data.X(i)!=b.Upper.X(i)))) errors.Add($"{s.Title}: band X coordinates must match.");
            if(s is ErrorBarSeries e&&(e.Data.Count!=e.LowerErrors.Length||e.Data.Count!=e.UpperErrors.Length)) errors.Add($"{s.Title}: error lengths must match data.");
        }
        foreach(var a in Annotations) if(!YAxes.Any(y=>y.Id==a.YAxisId)) errors.Add($"Annotation '{a.Text}': missing Y axis binding.");
        if(AxisLayout==AxisLayout.Segmented)
        {
            if(Segments.Count==0||Segments.Any(s=>!double.IsFinite(s.Weight)||s.Weight<=0||s.AxisIds.Length==0)) errors.Add("Segments require finite positive weights and at least one axis.");
            foreach(var a in YAxes) if(Segments.Sum(s=>s.AxisIds.Count(id=>id==a.Id))!=1) errors.Add($"Axis {a.Id} must belong to exactly one segment.");
            foreach(var segment in Segments) foreach(var id in segment.AxisIds) if(!YAxes.Any(a=>a.Id==id)) errors.Add($"Segment '{segment.Name}' references unknown axis '{id}'.");
        }
        return errors;
    }
    public void AutoFit()=>ChartAutoFit.Apply(this);
    public void Dispose()
    {
        if(_disposed) return; _disposed=true;
        PropertyChanged-=ModelChanged;
        XAxes.CollectionChanged-=CollectionChanged; YAxes.CollectionChanged-=CollectionChanged;
        Series.CollectionChanged-=CollectionChanged; Annotations.CollectionChanged-=CollectionChanged; Segments.CollectionChanged-=CollectionChanged;
        foreach(var item in _observed) item.PropertyChanged-=ModelChanged;
        _observed.Clear(); Changed=null;
    }
}
