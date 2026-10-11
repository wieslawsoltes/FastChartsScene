using System.Runtime.InteropServices;
using FastChartsScene.Core;
using FastChartsScene.Rendering.Raster;
using SkiaSharp;

namespace FastChartsScene.Rendering.Skia;

/// <summary>Skia vector/text backend with a depth-correct CPU raster path for 3D fallback and PNG.
/// GPU presentation remains the chart control's default. The host determines Skia's acceleration.</summary>
public sealed class SkiaFrameRenderer : IDisposable
{
    private readonly SKPaint _paint=new() { IsAntialias=true,Color=SKColors.White };
    private readonly SKTypeface _regular=SKTypeface.FromFamilyName("sans-serif",SKFontStyle.Normal);
    private readonly SKTypeface _bold=SKTypeface.FromFamilyName("sans-serif",SKFontStyle.Bold);
    private readonly Dictionary<(float Size,bool Bold),SKFont> _fonts=[];
    private readonly List<(SKVertices Vertices,SKRect Clip)> _meshes=[];
    private RenderFrame? _cached;
    private SKBitmap? _raster;
    private int _rasterWidth,_rasterHeight;
    private bool _disposed;
    public static SKColor Color(ColorRgba c)=>new((byte)Math.Clamp(Math.Round(c.R*255),0,255),(byte)Math.Clamp(Math.Round(c.G*255),0,255),(byte)Math.Clamp(Math.Round(c.B*255),0,255),(byte)Math.Clamp(Math.Round(c.A*255),0,255));
    public static SKRect Rect(RectD r)=>new((float)r.X,(float)r.Y,(float)r.Right,(float)r.Bottom);
    public void Reset()
    {
        foreach(var mesh in _meshes) mesh.Vertices.Dispose(); _meshes.Clear();
        _raster?.Dispose(); _raster=null; _rasterWidth=_rasterHeight=0; _cached=null;
    }
    private void Prepare(RenderFrame frame,float scale)
    {
        var spatial=frame.Metadata.Batches.Any(b=>b.DepthTest);
        var width=spatial?checked((int)Math.Ceiling(frame.Metadata.Width*scale)):0;
        var height=spatial?checked((int)Math.Ceiling(frame.Metadata.Height*scale)):0;
        if(ReferenceEquals(frame,_cached)&&width==_rasterWidth&&height==_rasterHeight) return;
        Reset();
        if(spatial)
        {
            var image=RasterFrameRenderer.Render(frame,width,height);
            _raster=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul));
            var pixels=image.Pixels.ToArray(); Marshal.Copy(pixels,0,_raster.GetPixels(),pixels.Length);
            _rasterWidth=width; _rasterHeight=height; _cached=frame; return;
        }
        foreach(var batch in frame.Metadata.Batches)
        {
            if(batch.Kind!=PrimitiveKind.Triangles) throw new NotSupportedException("Skia vector fallback requires triangle batches.");
            var positions=new SKPoint[batch.Count]; var colors=new SKColor[batch.Count];
            for(var i=0;i<batch.Count;i++)
            {
                var at=(batch.Start+i)*RenderFrame.VertexStride; var v=frame.Vertices;
                positions[i]=new((float)((v[at]+1)*frame.Metadata.Width/2),(float)((1-v[at+1])*frame.Metadata.Height/2));
                colors[i]=Color(new(v[at+3],v[at+4],v[at+5],v[at+6]));
            }
            _meshes.Add((SKVertices.CreateCopy(SKVertexMode.Triangles,positions,colors),Rect(batch.Clip)));
        }
        _cached=frame;
    }
    public void Draw(SKCanvas canvas,RenderFrame frame,bool geometry=true,bool labels=true)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(geometry)
        {
            var transform=canvas.TotalMatrix; var scale=Math.Max(1,Math.Max(Math.Abs(transform.ScaleX),Math.Abs(transform.ScaleY)));
            Prepare(frame,scale);
            if(_raster!=null)
            {
                _paint.Color=SKColors.White; _paint.Style=SKPaintStyle.Fill;
                canvas.DrawBitmap(_raster,new SKRect(0,0,(float)frame.Metadata.Width,(float)frame.Metadata.Height),_paint);
            }
            else
            {
                _paint.Color=Color(ColorRgba.Parse(frame.Metadata.Background)); _paint.Style=SKPaintStyle.Fill;
                canvas.DrawRect(0,0,(float)frame.Metadata.Width,(float)frame.Metadata.Height,_paint); _paint.Color=SKColors.White;
                foreach(var mesh in _meshes)
                {
                    canvas.Save(); canvas.ClipRect(mesh.Clip,SKClipOperation.Intersect,false);
                    canvas.DrawVertices(mesh.Vertices,SKBlendMode.Modulate,_paint); canvas.Restore();
                }
            }
        }
        if(labels) foreach(var label in frame.Metadata.Labels) DrawLabel(canvas,label);
    }
    public void DrawLabel(SKCanvas canvas,TextLabel label)
    {
        var key=((float)label.Size,label.Bold);
        if(!_fonts.TryGetValue(key,out var font)) _fonts[key]=font=new SKFont(label.Bold?_bold:_regular,(float)label.Size);
        _paint.Color=Color(ColorRgba.Parse(label.Color)); _paint.Style=SKPaintStyle.Fill;
        var align=label.Align=="center"?SKTextAlign.Center:label.Align=="right"?SKTextAlign.Right:SKTextAlign.Left;
        canvas.Save(); canvas.Translate((float)label.X,(float)label.Y); canvas.RotateDegrees((float)label.Rotation);
        canvas.DrawText(label.Text,0,0,align,font,_paint); canvas.Restore();
    }
    public byte[] ExportPng(RenderFrame frame,double scale=1)
    {
        if(!double.IsFinite(scale)||scale<=0||scale>8) throw new ArgumentOutOfRangeException(nameof(scale));
        var width=checked((int)Math.Ceiling(frame.Metadata.Width*scale)); var height=checked((int)Math.Ceiling(frame.Metadata.Height*scale));
        if((long)width*height>64_000_000) throw new ArgumentOutOfRangeException(nameof(scale),"Image exceeds the 64 megapixel export limit.");
        using var surface=SKSurface.Create(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul))??throw new InvalidOperationException("Could not allocate export surface.");
        surface.Canvas.Scale((float)scale); Draw(surface.Canvas,frame);
        using var image=surface.Snapshot(); using var encoded=image.Encode(SKEncodedImageFormat.Png,100); return encoded.ToArray();
    }
    public void Dispose()
    {
        if(_disposed) return; Reset(); foreach(var font in _fonts.Values) font.Dispose(); _fonts.Clear();
        _regular.Dispose(); _bold.Dispose(); _paint.Dispose(); _disposed=true;
    }
}
