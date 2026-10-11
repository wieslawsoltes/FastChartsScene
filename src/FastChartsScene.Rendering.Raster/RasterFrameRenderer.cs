using System.Numerics;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering.Raster;

/// <summary>Owned, top-to-bottom, premultiplied RGBA8 image. Text is composed by the host's text renderer.</summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
    internal RasterImage(int width,int height,byte[] pixels) { Width=width; Height=height; Pixels=pixels; }
}

/// <summary>Deterministic software backend for export and device fallback. Implements six-plane
/// homogeneous clipping, perspective-correct color, LEQUAL depth, scissoring, top-left coverage
/// and source-over blending. It executes the same geometry and lighting contract as the GL backend.</summary>
public static class RasterFrameRenderer
{
    public const int MaximumPixels=16_000_000;
    public static RasterImage Render(RenderFrame frame,int width,int height,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(frame); frame.Validate();
        if(width<1||height<1||(long)width*height>MaximumPixels) throw new ArgumentOutOfRangeException(nameof(width),"Raster output must fit within 16 megapixels.");
        if(!(frame.Metadata.Width>0)||!(frame.Metadata.Height>0)) throw new ArgumentException("Invalid logical dimensions.",nameof(frame));
        var renderer=new Renderer(frame,width,height,cancellationToken); renderer.Draw(); return new(width,height,renderer.Pixels);
    }
    private readonly record struct ClipVertex(Vector4 Position,Vector4 Color);
    private readonly record struct ScreenVertex(double X,double Y,double Z,double InverseW,Vector4 Color);
    private sealed class Renderer
    {
        private static readonly Vector3 Light=Vector3.Normalize(new(.4f,.8f,.6f));
        private readonly RenderFrame _frame;
        private readonly int _width,_height;
        private readonly float[] _depth;
        private readonly CancellationToken _cancellation;
        private int _left,_top,_right,_bottom;
        public byte[] Pixels { get; }
        public Renderer(RenderFrame frame,int width,int height,CancellationToken cancellation)
        {
            _frame=frame; _width=width; _height=height; _cancellation=cancellation;
            Pixels=new byte[checked(width*height*4)]; _depth=new float[width*height]; Array.Fill(_depth,1);
            var background=ColorRgba.Parse(frame.Metadata.Background); var alpha=Math.Clamp(background.A,0,1);
            for(var i=0;i<Pixels.Length;i+=4)
            { Pixels[i]=Byte(background.R*alpha); Pixels[i+1]=Byte(background.G*alpha); Pixels[i+2]=Byte(background.B*alpha); Pixels[i+3]=Byte(alpha); }
        }
        private static byte Byte(double value)=>(byte)Math.Clamp((int)Math.Round(value*255),0,255);
        private ClipVertex Vertex(int index,Matrix4x4 matrix)
        {
            var a=_frame.Vertices; var i=index*RenderFrame.VertexStride;
            var p=Vector4.Transform(new Vector4(a[i],a[i+1],a[i+2],1),matrix);
            if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||!float.IsFinite(p.W)) throw new InvalidOperationException("Vertex transformation overflowed.");
            var n=new Vector3(a[i+7],a[i+8],a[i+9]);
            var light=n.LengthSquared()>.001f?.35f+.65f*Math.Abs(Vector3.Dot(Vector3.Normalize(n),Light)):1;
            return new(p,new(a[i+3]*light,a[i+4]*light,a[i+5]*light,a[i+6]));
        }
        private static double Distance(ClipVertex vertex,int plane)
        {
            var p=vertex.Position;
            return plane switch { 0=>(double)p.W+p.X,1=>(double)p.W-p.X,2=>(double)p.W+p.Y,3=>(double)p.W-p.Y,4=>(double)p.W+p.Z,_=>(double)p.W-p.Z };
        }
        private ScreenVertex Project(ClipVertex v)
        {
            var inverse=1.0/v.Position.W;
            return new((v.Position.X*inverse+1)*_width/2,(1-v.Position.Y*inverse)*_height/2,v.Position.Z*inverse,inverse,v.Color);
        }
        public void Draw()
        {
            Span<ClipVertex> first=stackalloc ClipVertex[16]; Span<ClipVertex> second=stackalloc ClipVertex[16];
            foreach(var batch in _frame.Metadata.Batches)
            {
                _cancellation.ThrowIfCancellationRequested();
                _left=(int)Math.Clamp(Math.Floor(batch.Clip.X*_width/_frame.Metadata.Width),0,_width);
                _right=(int)Math.Clamp(Math.Ceiling(batch.Clip.Right*_width/_frame.Metadata.Width),0,_width);
                _top=(int)Math.Clamp(Math.Floor(batch.Clip.Y*_height/_frame.Metadata.Height),0,_height);
                _bottom=(int)Math.Clamp(Math.Ceiling(batch.Clip.Bottom*_height/_frame.Metadata.Height),0,_height);
                if(_left>=_right||_top>=_bottom) continue;
                var m=batch.Transform;
                var matrix=new Matrix4x4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
                if(batch.Kind==PrimitiveKind.Points)
                {
                    for(var i=batch.Start;i<batch.Start+batch.Count;i++) Point(Vertex(i,matrix),batch);
                    continue;
                }
                for(var index=batch.Start;index<batch.Start+batch.Count;index+=3)
                {
                    if((index&511)==0) _cancellation.ThrowIfCancellationRequested();
                    var input=first; var output=second; var count=3;
                    for(var i=0;i<3;i++) input[i]=Vertex(index+i,matrix);
                    for(var plane=0;plane<6&&count>0;plane++)
                    {
                        var previous=input[count-1]; var previousDistance=Distance(previous,plane); var n=0;
                        for(var i=0;i<count;i++)
                        {
                            var current=input[i]; var distance=Distance(current,plane);
                            if((distance>=0)!=(previousDistance>=0))
                            {
                                var t=(float)(previousDistance/(previousDistance-distance));
                                output[n++]=new(Vector4.Lerp(previous.Position,current.Position,t),Vector4.Lerp(previous.Color,current.Color,t));
                            }
                            if(distance>=0) output[n++]=current;
                            previous=current; previousDistance=distance;
                        }
                        count=n; var temporary=input; input=output; output=temporary;
                    }
                    if(count<3) continue;
                    for(var i=1;i<count-1;i++)
                    {
                        if(input[0].Position.W<=0||input[i].Position.W<=0||input[i+1].Position.W<=0) continue;
                        Triangle(Project(input[0]),Project(input[i]),Project(input[i+1]),batch.DepthTest);
                    }
                }
            }
        }
        private static double Edge(ScreenVertex a,ScreenVertex b,double x,double y)=>(b.X-a.X)*(y-a.Y)-(b.Y-a.Y)*(x-a.X);
        private static bool TopLeft(ScreenVertex a,ScreenVertex b)=>b.Y<a.Y||b.Y==a.Y&&b.X>a.X;
        private static bool Inside(double edge,bool topLeft)=>edge>0||edge==0&&topLeft;
        private void Triangle(ScreenVertex a,ScreenVertex b,ScreenVertex c,bool depthTest)
        {
            var area=Edge(a,b,c.X,c.Y); if(Math.Abs(area)<1e-14) return;
            if(area<0) { (b,c)=(c,b); area=-area; }
            var left=Math.Max(_left,(int)Math.Ceiling(Math.Min(a.X,Math.Min(b.X,c.X))-.5));
            var right=Math.Min(_right-1,(int)Math.Floor(Math.Max(a.X,Math.Max(b.X,c.X))-.5));
            var top=Math.Max(_top,(int)Math.Ceiling(Math.Min(a.Y,Math.Min(b.Y,c.Y))-.5));
            var bottom=Math.Min(_bottom-1,(int)Math.Floor(Math.Max(a.Y,Math.Max(b.Y,c.Y))-.5));
            var top0=TopLeft(b,c); var top1=TopLeft(c,a); var top2=TopLeft(a,b);
            for(var y=top;y<=bottom;y++)
            {
                if((y&63)==0) _cancellation.ThrowIfCancellationRequested();
                for(var x=left;x<=right;x++)
                {
                    var e0=Edge(b,c,x+.5,y+.5); var e1=Edge(c,a,x+.5,y+.5); var e2=Edge(a,b,x+.5,y+.5);
                    if(!Inside(e0,top0)||!Inside(e1,top1)||!Inside(e2,top2)) continue;
                    var w0=e0/area; var w1=e1/area; var w2=e2/area;
                    var z=(float)Math.Clamp((a.Z*w0+b.Z*w1+c.Z*w2)*.5+.5,0,1);
                    var pixel=y*_width+x; if(depthTest&&z>_depth[pixel]) continue;
                    var denominator=a.InverseW*w0+b.InverseW*w1+c.InverseW*w2;
                    if(!(denominator>0)) continue;
                    var color=(a.Color*(float)(a.InverseW*w0)+b.Color*(float)(b.InverseW*w1)+c.Color*(float)(c.InverseW*w2))/(float)denominator;
                    if(color.W<=0) continue;
                    Blend(pixel,color); if(depthTest) _depth[pixel]=z;
                }
            }
        }
        private void Point(ClipVertex vertex,DrawBatch batch)
        {
            if(vertex.Position.W<=0||vertex.Color.W<=0) return;
            for(var plane=0;plane<6;plane++) if(Distance(vertex,plane)<0) return;
            var v=Project(vertex); var radius=batch.PointSize*_width/_frame.Metadata.Width/2;
            var left=Math.Max(_left,(int)Math.Ceiling(v.X-radius-.5)); var right=Math.Min(_right-1,(int)Math.Ceiling(v.X+radius-.5)-1);
            var top=Math.Max(_top,(int)Math.Ceiling(v.Y-radius-.5)); var bottom=Math.Min(_bottom-1,(int)Math.Ceiling(v.Y+radius-.5)-1);
            var z=(float)Math.Clamp(v.Z*.5+.5,0,1);
            for(var y=top;y<=bottom;y++) for(var x=left;x<=right;x++)
            {
                var pixel=y*_width+x; if(batch.DepthTest&&z>_depth[pixel]) continue;
                Blend(pixel,v.Color); if(batch.DepthTest) _depth[pixel]=z;
            }
        }
        private void Blend(int pixel,Vector4 color)
        {
            var alpha=Math.Clamp(color.W,0,1); var inverse=1-alpha; var i=pixel*4;
            Pixels[i]=Byte(Math.Clamp(color.X,0,1)*alpha+Pixels[i]/255.0*inverse);
            Pixels[i+1]=Byte(Math.Clamp(color.Y,0,1)*alpha+Pixels[i+1]/255.0*inverse);
            Pixels[i+2]=Byte(Math.Clamp(color.Z,0,1)*alpha+Pixels[i+2]/255.0*inverse);
            Pixels[i+3]=Byte(alpha+Pixels[i+3]/255.0*inverse);
        }
    }
}
