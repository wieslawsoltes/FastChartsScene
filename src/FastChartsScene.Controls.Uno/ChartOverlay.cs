using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Skia;
using SkiaSharp;
using Windows.Foundation;
using global::Uno.WinUI.Graphics2DSK;

namespace FastChartsScene.Controls.Uno;
internal sealed partial class ChartOverlay : SKCanvasElement,IDisposable
{
    private readonly SkiaFrameRenderer _renderer=new();
    private readonly SKPaint _paint=new() { IsAntialias=true };
    public RenderFrame? Frame { get; set; }
    public bool DrawGeometry { get; set; }
    public PointD? Cursor { get; set; }
    public RectD? Selection { get; set; }
    public DataHit? Hit { get; set; }
    public string Message { get; set; }="";
    public long DrawCount { get; private set; }
    public event Action? Rendered;
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        if(Frame is { } frame)
        {
            try { _renderer.Draw(canvas,frame,DrawGeometry); }
            catch(NotSupportedException e) { DrawMessage(canvas,e.Message,area); return; }
            if(Cursor is { } cursor&&frame.Layout.Plot.Contains(cursor))
            {
                var pane=frame.Layout.Plot; _paint.Color=new SKColor(200,205,215,120); _paint.StrokeWidth=1; _paint.Style=SKPaintStyle.Stroke;
                canvas.Save(); canvas.ClipRect(SkiaFrameRenderer.Rect(pane));
                canvas.DrawLine((float)cursor.X,(float)pane.Y,(float)cursor.X,(float)pane.Bottom,_paint);
                canvas.DrawLine((float)pane.X,(float)cursor.Y,(float)pane.Right,(float)cursor.Y,_paint); canvas.Restore();
                if(Hit is { } hit)
                {
                    _paint.Color=new SKColor(15,18,22,235); _paint.Style=SKPaintStyle.Fill;
                    var x=Math.Clamp(cursor.X+14,pane.X,Math.Max(pane.X,pane.Right-260)); var y=Math.Clamp(cursor.Y-40,pane.Y,Math.Max(pane.Y,pane.Bottom-44));
                    canvas.DrawRoundRect((float)x,(float)y,254,38,4,4,_paint);
                    _renderer.DrawLabel(canvas,new($"{hit.Title} · sample {hit.Index}",x+8,y+15,"#FFFFFFFF",11));
                    _renderer.DrawLabel(canvas,new(FormattableString.Invariant($"X {hit.Data.X:G7}   Y {hit.Data.Y:G7}"),x+8,y+30,"#C8DCEFFF",11));
                }
            }
            DrawScalarCursor(canvas,frame);
            if(Selection is { } selection)
            {
                _paint.Style=SKPaintStyle.Fill; _paint.Color=new SKColor(80,160,235,40); canvas.DrawRect(SkiaFrameRenderer.Rect(selection),_paint);
                _paint.Style=SKPaintStyle.Stroke; _paint.Color=new SKColor(110,190,255); _paint.StrokeWidth=1; canvas.DrawRect(SkiaFrameRenderer.Rect(selection),_paint);
            }
            DrawCount++; Rendered?.Invoke();
        }
        if(Message.Length>0) DrawMessage(canvas,Message,area);
    }
    private void DrawMessage(SKCanvas canvas,string message,Size area)
    {
        _paint.Color=new SKColor(27,29,32); _paint.Style=SKPaintStyle.Fill; canvas.DrawRect(0,0,(float)area.Width,(float)area.Height,_paint);
        var lines=message.Split('\n'); var y=area.Height/2;
        foreach(var line in lines) { _renderer.DrawLabel(canvas,new(line,area.Width/2,y,"#FFC87AFF",13,"center")); y+=20; }
    }
    public byte[] ExportPng(double scale=1)=>_renderer.ExportPng(Frame??throw new InvalidOperationException("No chart frame."),scale);
    public void Reset()=>_renderer.Reset();
    public void Dispose() { _renderer.Dispose(); _paint.Dispose(); }
}
