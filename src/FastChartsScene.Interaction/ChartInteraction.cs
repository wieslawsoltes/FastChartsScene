using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Rendering;

namespace FastChartsScene.Interaction;

public sealed record DataHit(string SeriesId,string Title,int Index,PointD Data,PointD Screen,double Distance);

/// <summary>Platform-neutral chart input logic. Viewport edits are transactional; a drag becomes one undo entry.</summary>
public sealed partial class ChartInteraction
{
    private readonly record struct AxisState(AxisRange Range, AxisScale Scale, bool Inverted);
    private sealed record ViewState(Dictionary<Axis,AxisState> Ranges, Axis[] XAxes, Axis[] YAxes,
        ChartView View, AxisLayout Layout, double Azimuth,double Elevation,double Distance,bool Orthographic);
    private readonly ChartDocument _document;
    private readonly List<ViewState> _undo=[];
    private readonly List<ViewState> _redo=[];
    private ViewState? _gesture;
    public int HistoryCapacity { get; } = 64;
    public bool CanUndo=>_undo.Count>0 && Compatible(_undo[^1]);
    public bool CanRedo=>_redo.Count>0 && Compatible(_redo[^1]);
    public ChartInteraction(ChartDocument document) { ArgumentNullException.ThrowIfNull(document); _document=document; }
    private ViewState Capture()
    {
        ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        return new(_document.XAxes.Concat(_document.YAxes).Append(_document.ZAxis).Distinct()
            .ToDictionary(a=>a,a=>new AxisState(a.Range,a.Scale,a.Inverted)),_document.XAxes.ToArray(),_document.YAxes.ToArray(),
            _document.View,_document.AxisLayout,_document.Camera.Azimuth,_document.Camera.Elevation,_document.Camera.Distance,_document.Camera.Orthographic);
    }
    private bool Compatible(ViewState state)=>!_document.IsDisposed && state.View==_document.View && state.Layout==_document.AxisLayout &&
        state.XAxes.SequenceEqual(_document.XAxes) && state.YAxes.SequenceEqual(_document.YAxes) &&
        state.Ranges.All(p=>p.Key.Scale==p.Value.Scale && p.Key.Inverted==p.Value.Inverted);
    private bool Unchanged(ViewState state)=>Compatible(state) && state.Azimuth==_document.Camera.Azimuth && state.Elevation==_document.Camera.Elevation &&
        state.Distance==_document.Camera.Distance && state.Orthographic==_document.Camera.Orthographic && state.Ranges.All(p=>p.Key.Range==p.Value.Range);
    private bool Restore(ViewState state)
    {
        // Reject obsolete topology/scale histories before changing any axis or either stack.
        if(!Compatible(state)) return false;
        using var update=_document.BeginUpdate();
        foreach(var p in state.Ranges) p.Key.Range=p.Value.Range;
        _document.Camera.Azimuth=state.Azimuth; _document.Camera.Elevation=state.Elevation;
        _document.Camera.Distance=state.Distance; _document.Camera.Orthographic=state.Orthographic; return true;
    }
    private void Save(ViewState? before)
    {
        if(before==null || _gesture!=null || !Compatible(before) || Unchanged(before)) return;
        _undo.Add(before); if(_undo.Count>HistoryCapacity) _undo.RemoveAt(0); _redo.Clear();
    }
    /// <summary>Forget viewport history and any active gesture baseline without mutating the model.</summary>
    public void ClearHistory() { _undo.Clear(); _redo.Clear(); _gesture=null; }
    public void BeginGesture()
    {
        if(_gesture!=null) throw new InvalidOperationException("A gesture is already active.");
        _gesture=Capture();
    }
    public void EndGesture() { var before=_gesture; _gesture=null; Save(before); }
    public void CancelGesture() { var before=_gesture; _gesture=null; if(before!=null && !Restore(before)) ClearHistory(); }
    public bool Undo()
    {
        ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        if(_gesture!=null) CancelGesture(); if(_undo.Count==0) return false;
        var state=_undo[^1]; if(!Compatible(state)) { ClearHistory(); return false; }
        var current=Capture(); if(!Restore(state)) return false;
        _undo.RemoveAt(_undo.Count-1); _redo.Add(current); return true;
    }
    public bool Redo()
    {
        ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        if(_gesture!=null) CancelGesture(); if(_redo.Count==0) return false;
        var state=_redo[^1]; if(!Compatible(state)) { ClearHistory(); return false; }
        var current=Capture(); if(!Restore(state)) return false;
        _redo.RemoveAt(_redo.Count-1); _undo.Add(current); return true;
    }
    private static AxisRange Units(Axis axis,double a,double b)
    {
        var x=axis.FromUnit(a); var y=axis.FromUnit(b);
        var range=new AxisRange(Math.Min(x,y),Math.Max(x,y));
        if(axis.Scale==AxisScale.Logarithmic&&range.Minimum<=0) throw new ArgumentOutOfRangeException(nameof(a));
        return range;
    }
    private IEnumerable<Axis> ActiveY(ChartLayout layout,PointD anchor)=>
        _document.YAxes.Where(a=>layout.ForAxis(a.Id).Contains(anchor));
    public void Zoom(RenderFrame frame,PointD anchor,double factor,bool horizontal=true,bool vertical=true)
    {
        ArgumentNullException.ThrowIfNull(frame); ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        if(!anchor.IsFinite||!double.IsFinite(factor)||factor<=0) throw new ArgumentOutOfRangeException(nameof(factor));
        if(!frame.Layout.Plot.Contains(anchor) || !horizontal && !vertical) return;
        var before=_gesture==null?Capture():null;
        if(_document.View is ChartView.ThreeD or ChartView.Pie3D) _document.Camera.Distance*=factor;
        else
        {
            Dictionary<Axis,AxisRange> plan=[];
            if(horizontal&&_document.View==ChartView.XY)
                foreach(var a in _document.XAxes)
                {
                    var p=frame.Layout.Plot; var u=(anchor.X-p.X)/p.Width;
                    plan[a]=Units(a,u-u*factor,u+(1-u)*factor);
                }
            if(vertical)
                foreach(var a in ActiveY(frame.Layout,anchor))
                {
                    var p=frame.Layout.ForAxis(a.Id); var u=(p.Bottom-anchor.Y)/p.Height;
                    plan[a]=Units(a,u-u*factor,u+(1-u)*factor);
                }
            using var update=_document.BeginUpdate(); foreach(var p in plan) p.Key.Range=p.Value;
        }
        Save(before);
    }
    public void Pan(RenderFrame frame,PointD anchor,double dx,double dy)
    {
        ArgumentNullException.ThrowIfNull(frame); ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        if(!anchor.IsFinite||!double.IsFinite(dx)||!double.IsFinite(dy)) throw new ArgumentOutOfRangeException(nameof(dx));
        if(!frame.Layout.Plot.Contains(anchor) || dx==0 && dy==0) return;
        var before=_gesture==null?Capture():null;
        if(_document.View is ChartView.ThreeD or ChartView.Pie3D)
        {
            using var update=_document.BeginUpdate(); _document.Camera.Azimuth+=dx*.4; _document.Camera.Elevation+=dy*.3;
        }
        else if(_document.View==ChartView.XY)
        {
            Dictionary<Axis,AxisRange> plan=[];
            foreach(var a in _document.XAxes) { var delta=-dx/frame.Layout.Plot.Width; plan[a]=Units(a,delta,1+delta); }
            foreach(var a in ActiveY(frame.Layout,anchor)) { var delta=dy/frame.Layout.ForAxis(a.Id).Height; plan[a]=Units(a,delta,1+delta); }
            using var update=_document.BeginUpdate(); foreach(var p in plan) p.Key.Range=p.Value;
        }
        Save(before);
    }
    public void ZoomRectangle(RenderFrame frame,PointD first,PointD second)
    {
        ArgumentNullException.ThrowIfNull(frame); ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        if(!first.IsFinite||!second.IsFinite) throw new ArgumentOutOfRangeException(nameof(first));
        if(_document.View!=ChartView.XY) return;
        var p=frame.Layout.Plot;
        var left=Math.Clamp(Math.Min(first.X,second.X),p.X,p.Right); var right=Math.Clamp(Math.Max(first.X,second.X),p.X,p.Right);
        if(right-left<3||Math.Abs(first.Y-second.Y)<3) return;
        Dictionary<Axis,AxisRange> plan=[];
        foreach(var a in _document.XAxes) plan[a]=Units(a,(left-p.X)/p.Width,(right-p.X)/p.Width);
        foreach(var a in ActiveY(frame.Layout,first))
        {
            var pane=frame.Layout.ForAxis(a.Id); var top=Math.Clamp(Math.Min(first.Y,second.Y),pane.Y,pane.Bottom);
            var bottom=Math.Clamp(Math.Max(first.Y,second.Y),pane.Y,pane.Bottom);
            if(bottom-top<3) continue;
            plan[a]=Units(a,(pane.Bottom-bottom)/pane.Height,(pane.Bottom-top)/pane.Height);
        }
        var before=_gesture==null?Capture():null; using(_document.BeginUpdate()) foreach(var item in plan) item.Key.Range=item.Value;
        Save(before);
    }
    public void ResetView()
    {
        var before=_gesture==null?Capture():null; using(_document.BeginUpdate()) { _document.AutoFit(); _document.Camera.Reset(); } Save(before);
    }
    public bool ToggleLegend(RenderFrame frame,PointD location)
    {
        ArgumentNullException.ThrowIfNull(frame); ObjectDisposedException.ThrowIf(_document.IsDisposed,_document);
        if(!location.IsFinite) throw new ArgumentOutOfRangeException(nameof(location));
        var hit=frame.Metadata.Hits.LastOrDefault(h=>h.Kind=="legend"&&h.Bounds.Contains(location));
        var series=hit==null?null:_document.Series.FirstOrDefault(s=>s.Id==hit.Id);
        if(series==null) return false; series.Visible=!series.Visible; return true;
    }
}
