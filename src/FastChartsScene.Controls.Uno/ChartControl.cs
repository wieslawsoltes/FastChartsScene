using System.Diagnostics;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FastChartsScene.Controls.Uno;

public enum ChartRenderingMode { Auto, OpenGL, Skia }

/// <summary>Reusable Uno chart control. Owns renderer resources, but never disposes the caller's document.</summary>
public sealed class ChartControl : UserControl,IDisposable
{
    public static readonly DependencyProperty DocumentProperty=DependencyProperty.Register(nameof(Document),typeof(ChartDocument),typeof(ChartControl),new PropertyMetadata(null,DocumentChanged));
    public static readonly DependencyProperty RenderingModeProperty=DependencyProperty.Register(nameof(RenderingMode),typeof(ChartRenderingMode),typeof(ChartControl),new PropertyMetadata(ChartRenderingMode.Auto,(d,_)=>((ChartControl)d).RequestDraw()));
    public ChartDocument? Document { get=>(ChartDocument?)GetValue(DocumentProperty); set=>SetValue(DocumentProperty,value); }
    public ChartRenderingMode RenderingMode { get=>(ChartRenderingMode)GetValue(RenderingModeProperty); set=>SetValue(RenderingModeProperty,value); }
    private readonly SceneCompiler _compiler=new();
    private readonly ChartGlSurface _gpu=new();
    private readonly ChartOverlay _overlay=new() { IsHitTestVisible=false };
    private readonly Dictionary<uint,PointD> _pointers=[];
    private readonly long _glCallback;
    private ChartInteraction? _input;
    private XamlRoot? _observedRoot;
    private bool _attached,_queued,_disposed,_box;
    private PointD _first,_last;
    public RenderFrame? Frame { get; private set; }
    public ChartInteraction? Interaction=>_input;
    public string Backend { get; private set; }="Initializing";
    public string? LastError { get; private set; }
    public double CompileMilliseconds { get; private set; }
    public long GpuSubmissions=>_gpu.Submissions;
    public long OverlayDraws=>_overlay.DrawCount;
    public ScalarDataHit? ScalarHit => _overlay.ScalarHit;
    public event EventHandler? ScalarHitChanged;
    private void SetScalarHit(ScalarDataHit? hit)
    {
        if(Equals(_overlay.ScalarHit,hit)) return;
        _overlay.ScalarHit=hit; ScalarHitChanged?.Invoke(this,EventArgs.Empty);
    }
    public event EventHandler? FrameChanged;
    public event EventHandler? RenderCompleted;
    public ChartControl()
    {
        IsTabStop=true; HorizontalContentAlignment=HorizontalAlignment.Stretch; VerticalContentAlignment=VerticalAlignment.Stretch;
        var host=new Canvas { IsHitTestVisible=false }; host.Children.Add(_gpu);
        var root=new Grid { Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent) }; root.Children.Add(host); root.Children.Add(_overlay); Content=root;
        AutomationProperties.SetName(this,"Scientific chart");
        AutomationProperties.SetHelpText(this,"Drag to pan or orbit. Wheel or pinch to zoom. Shift-drag selects a range. Home fits data. Z undoes and Y redoes.");
        _glCallback=_gpu.RegisterPropertyChangedCallback(ChartGlSurface.IsGLInitializedProperty,(_,_)=>RequestDraw());
        _gpu.Rendered+=()=>RenderCompleted?.Invoke(this,EventArgs.Empty); _overlay.Rendered+=()=>RenderCompleted?.Invoke(this,EventArgs.Empty);
        Loaded+=OnLoaded; Unloaded+=OnUnloaded; SizeChanged+=(_,_)=>RequestDraw();
        PointerPressed+=Pressed; PointerMoved+=Moved; PointerReleased+=Released; PointerCanceled+=Canceled; PointerCaptureLost+=CaptureLost;
        PointerWheelChanged+=Wheel; PointerExited+=(_,_)=> { if(_pointers.Count==0) { _overlay.Cursor=null; _overlay.Hit=null; SetScalarHit(null); _overlay.Invalidate(); } };
        DoubleTapped+=(_,e)=> { Safe(()=>_input?.ResetView()); e.Handled=true; }; RightTapped+=(_,e)=>e.Handled=true; KeyDown+=Key;
        ManipulationMode=ManipulationModes.None;
    }
    private static void DocumentChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)
    {
        var c=(ChartControl)sender;
        if(e.OldValue is ChartDocument old) old.Changed-=c.OnModelChanged;
        c.Cancel(); c._compiler.ClearCache(); c._input=e.NewValue is ChartDocument current?new ChartInteraction(current):null;
        if(c._attached&&e.NewValue is ChartDocument d) d.Changed+=c.OnModelChanged;
        c.Frame=null; c._overlay.Cursor=null; c._overlay.Hit=null; c.SetScalarHit(null); c.RequestDraw();
    }
    private void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(_disposed||_attached) return; _attached=true;
        if(Document is { } d) { d.Changed-=OnModelChanged; d.Changed+=OnModelChanged; }
        _observedRoot=XamlRoot; if(_observedRoot!=null) _observedRoot.Changed+=RootChanged;
        RequestDraw();
    }
    private void OnUnloaded(object sender,RoutedEventArgs e)
    {
        _attached=false; if(Document is { } d) d.Changed-=OnModelChanged;
        if(_observedRoot!=null) _observedRoot.Changed-=RootChanged; _observedRoot=null;
        Cancel(); _overlay.Cursor=null; _overlay.Hit=null; SetScalarHit(null); _overlay.Reset(); _compiler.ClearCache();
    }
    private void RootChanged(XamlRoot sender,XamlRootChangedEventArgs args)=>RequestDraw();
    private void OnModelChanged(object? sender,EventArgs e)=>RequestDraw();
    public void RequestDraw()
    {
        if(_disposed||_queued) return; _queued=true;
        if(!DispatcherQueue.TryEnqueue(()=> { _queued=false; Draw(); })) _queued=false;
    }
    private void Draw()
    {
        if(_disposed||!_attached||ActualWidth<1||ActualHeight<1) return;
        var mode=RenderingMode; _gpu.Visibility=mode==ChartRenderingMode.Skia?Visibility.Collapsed:Visibility.Visible;
        var scale=XamlRoot?.RasterizationScale??1;
        _gpu.Width=Math.Ceiling(ActualWidth*scale); _gpu.Height=Math.Ceiling(ActualHeight*scale);
        _gpu.RenderTransform=new ScaleTransform { ScaleX=1/scale,ScaleY=1/scale };
        var gpu=mode!=ChartRenderingMode.Skia&&_gpu.IsGLInitialized==true;
        Backend=gpu?"OpenGL · "+_gpu.Driver:mode==ChartRenderingMode.Skia?"Skia (explicit)":"Skia fallback";
        _overlay.DrawGeometry=!gpu; _overlay.Message="";
        try
        {
            if(Document is not { } d) { Frame=null; _overlay.Message="Assign a ChartDocument to display a chart."; }
            else
            {
                var watch=Stopwatch.StartNew(); Frame=_compiler.Compile(d,ActualWidth,ActualHeight); CompileMilliseconds=watch.Elapsed.TotalMilliseconds;
                AutomationProperties.SetName(this,$"{d.Title}, {d.Series.Count} series, {d.TotalPoints:N0} samples");
                if(!gpu&&mode==ChartRenderingMode.OpenGL&&_gpu.IsGLInitialized==false) throw new InvalidOperationException(_gpu.Error??"OpenGL is unavailable on this device.");
            }
            LastError=null;
        }
        catch(Exception e) { LastError=e.Message; Frame=null; _overlay.Message=e.Message; }
        _gpu.Frame=Frame; _overlay.Frame=Frame;
        if(Frame is { } current && _overlay.Cursor is { } cursor && _input!=null)
        {
            Safe(()=> { _overlay.Hit=_input.HitTestData(current,cursor); SetScalarHit(_input.HitTestScalar(current,cursor)); });
        }
        else SetScalarHit(null);
        if(gpu) _gpu.Invalidate(); _overlay.Invalidate(); FrameChanged?.Invoke(this,EventArgs.Empty);
    }
    private static PointD Point(PointerRoutedEventArgs e,UIElement relative)
    { var p=e.GetCurrentPoint(relative).Position; return new(p.X,p.Y); }
    private void Safe(Action action) { try { action(); } catch(Exception e) { LastError=e.Message; FrameChanged?.Invoke(this,EventArgs.Empty); } }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(Frame is not { } frame||_input==null) return;
        var p=Point(e,this); var state=e.GetCurrentPoint(this);
        if(_pointers.Count==0&&_input.ToggleLegend(frame,p)) { e.Handled=true; return; }
        if(!frame.Layout.Plot.Contains(p)) return;
        Focus(FocusState.Pointer);
        if(_pointers.Count==0) { _input.BeginGesture(); _first=_last=p; _box=state.Properties.IsRightButtonPressed||e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift); }
        _pointers[e.Pointer.PointerId]=p; if(_pointers.Count>1) { _box=false; _overlay.Selection=null; }
        CapturePointer(e.Pointer); e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(Frame is not { } frame||_input==null) return;
        var p=Point(e,this); var id=e.Pointer.PointerId;
        if(_pointers.ContainsKey(id))
        {
            if(_pointers.Count==2)
            {
                var previous=_pointers.Values.ToArray(); _pointers[id]=p; var current=_pointers.Values.ToArray();
                static double Distance(PointD a,PointD b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
                var before=Distance(previous[0],previous[1]); var after=Distance(current[0],current[1]);
                var center=new PointD((current[0].X+current[1].X)/2,(current[0].Y+current[1].Y)/2);
                if(before>2&&after>2) Safe(()=>_input.Zoom(frame,center,before/after));
            }
            else if(_box) { _pointers[id]=p; _overlay.Selection=new(Math.Min(_first.X,p.X),Math.Min(_first.Y,p.Y),Math.Abs(p.X-_first.X),Math.Abs(p.Y-_first.Y)); }
            else { _pointers[id]=p; Safe(()=>_input.Pan(frame,_first,p.X-_last.X,p.Y-_last.Y)); }
            _last=p; e.Handled=true;
        }
        _overlay.Cursor=p; Safe(()=> { _overlay.Hit=_input.HitTestData(frame,p); SetScalarHit(_input.HitTestScalar(frame,p)); }); _overlay.Invalidate();
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(!_pointers.Remove(e.Pointer.PointerId)) return;
        if(_pointers.Count==0)
        {
            if(_box&&Frame is { } frame) Safe(()=>_input?.ZoomRectangle(frame,_first,Point(e,this)));
            _input?.EndGesture(); _overlay.Selection=null;
        }
        else _first=_last=_pointers.Values.First();
        ReleasePointerCapture(e.Pointer); _overlay.Invalidate(); e.Handled=true;
    }
    private void Canceled(object sender,PointerRoutedEventArgs e)=>Cancel();
    private void CaptureLost(object sender,PointerRoutedEventArgs e) { if(_pointers.ContainsKey(e.Pointer.PointerId)) Cancel(); }
    private void Cancel() { _input?.CancelGesture(); _pointers.Clear(); ReleasePointerCaptures(); _overlay.Selection=null; }
    private void Wheel(object sender,PointerRoutedEventArgs e)
    {
        if(Frame is not { } frame||_input==null) return;
        var delta=e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        Safe(()=>_input.Zoom(frame,Point(e,this),Math.Pow(.85,delta/120.0),!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift),!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)));
        e.Handled=true;
    }
    private void Key(object sender,KeyRoutedEventArgs e)
    {
        if(_input==null||Frame is not { } f) return;
        var center=new PointD(f.Layout.Plot.X+f.Layout.Plot.Width/2,f.Layout.Plot.Y+f.Layout.Plot.Height/2);
        switch(e.Key)
        {
            case VirtualKey.Home: Safe(_input.ResetView); break;
            case VirtualKey.Escape: Cancel(); break;
            case VirtualKey.Z: Safe(()=>_input.Undo()); break;
            case VirtualKey.Y: Safe(()=>_input.Redo()); break;
            case VirtualKey.Add: Safe(()=>_input.Zoom(f,center,.8)); break;
            case VirtualKey.Subtract: Safe(()=>_input.Zoom(f,center,1.25)); break;
            case VirtualKey.Left: Safe(()=>_input.Pan(f,center,20,0)); break;
            case VirtualKey.Right: Safe(()=>_input.Pan(f,center,-20,0)); break;
            case VirtualKey.Up: Safe(()=>_input.Pan(f,center,0,20)); break;
            case VirtualKey.Down: Safe(()=>_input.Pan(f,center,0,-20)); break;
            default:return;
        }
        e.Handled=true;
    }
    public byte[] ExportPng(double scale=1)=>_overlay.ExportPng(scale);
    public new void Dispose()
    {
        if(_disposed) return; OnUnloaded(this,new RoutedEventArgs()); _disposed=true;
        _gpu.UnregisterPropertyChangedCallback(ChartGlSurface.IsGLInitializedProperty,_glCallback); _overlay.Dispose();
        Content=null; Frame=null; _gpu.Frame=null; _overlay.Frame=null;
    }
}
