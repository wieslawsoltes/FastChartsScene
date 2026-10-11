#if __WASM__
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using FastChartsScene.Rendering;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Windows.Foundation;
using global::Uno.WinUI.Graphics2DSK;

namespace FastChartsScene.Controls.Uno;

/// <summary>Browser-owned WebGL2 context. The backend is a static WasmScripts asset loaded by
/// Uno Bootstrapper. Standard RGBA readback avoids platform-specific BGRA presentation errors.</summary>
internal sealed partial class ChartGlSurface : SKCanvasElement
{
    public static readonly DependencyProperty IsGLInitializedProperty=DependencyProperty.Register(nameof(IsGLInitialized),typeof(bool?),typeof(ChartGlSurface),new PropertyMetadata(null));
    public bool? IsGLInitialized { get=>(bool?)GetValue(IsGLInitializedProperty); private set=>SetValue(IsGLInitializedProperty,value); }
    public RenderFrame? Frame { get; set; }
    public string Driver { get; private set; }="Not initialized";
    public string? Error { get; private set; }
    public long Submissions { get; private set; }
    public event Action? Rendered;
    private static int _nextId;
    private readonly int _id=Interlocked.Increment(ref _nextId);
    private SKBitmap? _bitmap;
    private byte[] _pixels=[];
    private RenderFrame? _uploaded;
    private RenderFrame? _drawn;
    private int _width,_height;
    private bool _contextCreated;
    public ChartGlSurface()
    {
        IsHitTestVisible=false;
        Loaded+=(_,_)=>Initialize();
        Unloaded+=(_,_)=>Release();
    }
    private void Initialize()
    {
        if(_contextCreated) return;
        try
        {
            Driver=Create(_id); _contextCreated=true; _uploaded=_drawn=null; Error=null; IsGLInitialized=true; Invalidate();
        }
        catch(Exception error) { Error=error.Message; IsGLInitialized=false; Console.Error.WriteLine("Chart WebGL initialization: "+error); }
    }
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        if(!_contextCreated||Frame is not { } frame||area.Width<1||area.Height<1) return;
        try
        {
            var width=checked((int)Math.Ceiling(area.Width)); var height=checked((int)Math.Ceiling(area.Height));
            if((long)width*height>16_000_000) throw new InvalidOperationException("Chart framebuffer exceeds the 16 megapixel limit.");
            var resized=width!=_width||height!=_height;
            if(resized)
            {
                _bitmap?.Dispose(); _bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Premul));
                _pixels=new byte[checked(width*height*4)]; _width=width; _height=height;
            }
            if(resized||!ReferenceEquals(frame,_drawn))
            {
                var upload=!ReferenceEquals(frame,_uploaded);
                Draw(_id,upload?MemoryMarshal.AsBytes(frame.Vertices.AsSpan()):Span<byte>.Empty,frame.MetadataJson(),width,height,_pixels.AsSpan());
                Marshal.Copy(_pixels,0,_bitmap!.GetPixels(),_pixels.Length); _bitmap.NotifyPixelsChanged();
                _uploaded=_drawn=frame; Submissions++; Error=null; Rendered?.Invoke();
            }
            canvas.DrawBitmap(_bitmap!,0,0);
        }
        catch(Exception error)
        {
            Error=error.Message; IsGLInitialized=false; Console.Error.WriteLine("Chart WebGL presentation: "+error);
        }
    }
    private void Release()
    {
        if(_contextCreated) { Destroy(_id); _contextCreated=false; }
        _bitmap?.Dispose(); _bitmap=null; _pixels=[]; _uploaded=_drawn=null; _width=_height=0; IsGLInitialized=null;
    }
    // Global functions use a globalThis-qualified name, with no ES-module argument.
    [JSImport("globalThis.FastChartsSceneWebGL.create")]
    private static partial string Create(int id);
    [JSImport("globalThis.FastChartsSceneWebGL.draw")]
    private static partial void Draw(int id,[JSMarshalAs<JSType.MemoryView>] Span<byte> vertices,string metadata,int width,int height,[JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
    [JSImport("globalThis.FastChartsSceneWebGL.destroy")]
    private static partial void Destroy(int id);
}
#endif
