using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Examples;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Raster;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static readonly float[] Identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    private static float[] RasterVertices(params (float X,float Y,float Z,ColorRgba Color)[] source)
    {
        List<float> vertices=[];
        foreach(var v in source) vertices.AddRange([v.X,v.Y,v.Z,v.Color.R,v.Color.G,v.Color.B,v.Color.A,0,0,0]);
        return vertices.ToArray();
    }
    private static RenderFrame RasterFrame(float[] vertices,DrawBatch[]? batches=null,string background="#000000FF")
    {
        using var d=new ChartDocument(); var l=ChartLayout.Create(d,64,64); var count=vertices.Length/10;
        batches??=[new(0,count,PrimitiveKind.Triangles,true,new(0,0,64,64),Identity)];
        return new(vertices,new(64,64,background,batches,[],[],l.Plot,0,0,count,"Raster test"),l);
    }
    static partial void RegisterRasterTests()
    {
        var red=new ColorRgba(1,0,0); var green=new ColorRgba(0,1,0);
        Add("raster depth test resolves nearer triangles regardless of submission order",()=> {
            var near=RasterVertices((-.8f,-.8f,-.5f,green),(.8f,-.8f,-.5f,green),(0,.8f,-.5f,green));
            var far=RasterVertices((-.8f,-.8f,.5f,red),(.8f,-.8f,.5f,red),(0,.8f,.5f,red));
            var a=RasterFrameRenderer.Render(RasterFrame(near.Concat(far).ToArray()),64,64);
            var b=RasterFrameRenderer.Render(RasterFrame(far.Concat(near).ToArray()),64,64);
            True(a.Pixels.Span.SequenceEqual(b.Pixels.Span)); Equal(255,a.Pixels.Span[(32*64+32)*4+1]);
        });
        Add("raster clips near and far planes before perspective division",()=> {
            var outside=RasterVertices((-.8f,-.8f,2,red),(.8f,-.8f,2,red),(0,.8f,2,red));
            var image=RasterFrameRenderer.Render(RasterFrame(outside),64,64); True(Enumerable.Range(0,64*64).All(i=>image.Pixels.Span[i*4]==0));
            var crossing=RasterVertices((-.8f,-.8f,-2,red),(.8f,-.8f,0,red),(0,.8f,0,red));
            image=RasterFrameRenderer.Render(RasterFrame(crossing),64,64); True(Enumerable.Range(0,64*64).Count(i=>image.Pixels.Span[i*4]>0)>100);
        });
        Add("raster top-left coverage prevents double blending along shared edges",()=> {
            var color=new ColorRgba(1,0,0,.5f);
            var vertices=RasterVertices((-1,-1,0,color),(1,-1,0,color),(1,1,0,color),(-1,-1,0,color),(1,1,0,color),(-1,1,0,color));
            var image=RasterFrameRenderer.Render(RasterFrame(vertices,background:"#00000000"),64,64);
            for(var i=0;i<64*64;i++) { Equal(128,image.Pixels.Span[i*4]); Equal(128,image.Pixels.Span[i*4+3]); }
        });
        Add("raster scissor and winding match the two-sided GPU contract",()=> {
            var vertices=RasterVertices((-.8f,-.8f,0,red),(.8f,-.8f,0,red),(0,.8f,0,red));
            DrawBatch[] batch=[new(0,3,PrimitiveKind.Triangles,true,new(0,0,32,64),Identity)];
            var image=RasterFrameRenderer.Render(RasterFrame(vertices,batch),64,64);
            True(Enumerable.Range(0,64*64).Where(i=>i%64>=32).All(i=>image.Pixels.Span[i*4]==0));
            var reversed=RasterVertices((0,.8f,0,red),(.8f,-.8f,0,red),(-.8f,-.8f,0,red));
            var other=RasterFrameRenderer.Render(RasterFrame(reversed,batch),64,64); True(image.Pixels.Span.SequenceEqual(other.Pixels.Span));
        });
        Add("raster interpolates colors with reciprocal clip W",()=> {
            var vertices=RasterVertices((-.7f,-.7f,0,red),(.7f,-.7f,0,green),(0,.7f,1,new(0,0,1)));
            var matrix=(float[])Identity.Clone(); matrix[11]=1;
            DrawBatch[] batch=[new(0,3,PrimitiveKind.Triangles,true,new(0,0,64,64),matrix)];
            var image=RasterFrameRenderer.Render(RasterFrame(vertices,batch),64,64); var blue=image.Pixels.Span[(32*64+32)*4+2];
            True(Math.Abs(blue-123)<=2,$"Expected perspective-correct blue near 123; got {blue}.");
        });
        Add("raster validates output allocation limits and cancellation",()=> {
            var frame=RasterFrame([]); Throws<ArgumentOutOfRangeException>(()=>RasterFrameRenderer.Render(frame,100000,100000));
            using var cancel=new CancellationTokenSource(); cancel.Cancel(); Throws<OperationCanceledException>(()=>RasterFrameRenderer.Render(frame,64,64,cancel.Token));
        });
        foreach(var id in new[]{"surface","waterfall","bars3d","helix","mesh","pie","donut"})
            Add("depth-correct raster example: "+id,()=> {
                using var d=ExampleCatalog.Find(id).Create(); var frame=new SceneCompiler().Compile(d,800,600); var image=RasterFrameRenderer.Render(frame,256,192);
                var colored=0; var bytes=image.Pixels.Span;
                for(var i=0;i<bytes.Length;i+=4) if(Math.Max(bytes[i],Math.Max(bytes[i+1],bytes[i+2]))-Math.Min(bytes[i],Math.Min(bytes[i+1],bytes[i+2]))>40) colored++;
                True(colored>100,$"No visible geometry in {id}: {colored} colored pixels.");
            });
    }
}
