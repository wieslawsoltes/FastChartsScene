using System.Numerics;
using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

internal static class ThreeDScene
{
    internal static Matrix4x4 CameraMatrix(Camera3D camera,RectD plot,double width,double height)
    {
        var a=(float)(camera.Azimuth*Math.PI/180); var e=(float)(camera.Elevation*Math.PI/180);
        var eye=new Vector3(MathF.Cos(e)*MathF.Sin(a),MathF.Sin(e),MathF.Cos(e)*MathF.Cos(a))*(float)camera.Distance;
        var view=Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY);
        var aspect=(float)(plot.Width/plot.Height);
        var projection=camera.Orthographic?Matrix4x4.CreateOrthographic((float)camera.Distance*aspect,(float)camera.Distance,.1f,100):
            Matrix4x4.CreatePerspectiveFieldOfView(.7f,aspect,.1f,100);
        // System.Numerics uses a [0,1] depth range; convert to OpenGL's [-1,1].
        var depth=Matrix4x4.Identity; depth.M33=2; depth.M43=-1;
        var viewport=Matrix4x4.CreateScale((float)(plot.Width/width),(float)(plot.Height/height),1)*
            Matrix4x4.CreateTranslation((float)(2*(plot.X+plot.Width/2)/width-1),(float)(1-2*(plot.Y+plot.Height/2)/height),0);
        return view*projection*depth*viewport;
    }
    public static void Build(ChartDocument d,ChartLayout layout,FrameBuilder f)
    {
        var p=layout.Plot; var matrix=CameraMatrix(d.Camera,p,f.Width,f.Height); var start=f.Count;
        f.Rect(p,d.Theme.Plot); f.Batch(start,new(0,0,f.Width,f.Height));
        Vector3 Map(Point3D v)
        {
            var result=new Vector3((float)(2*d.XAxis.ToUnit(v.X)-1),(float)(2*d.YAxis.ToUnit(v.Y)-1),(float)(2*d.ZAxis.ToUnit(v.Z)-1));
            if(!float.IsFinite(result.X)||!float.IsFinite(result.Y)||!float.IsFinite(result.Z)) throw new InvalidOperationException("3D coordinate is outside the axis domain.");
            return result;
        }
        if(d.View==ChartView.ThreeD)
        {
            start=f.Count;
            for(var i=0;i<=10;i++)
            {
                var t=-1+i*.2f;
                Tube(f,new(t,-1,-1),new(t,-1,1),.0025f,d.Theme.Grid);
                Tube(f,new(-1,-1,t),new(1,-1,t),.0025f,d.Theme.Grid);
            }
            Tube(f,new(-1,-1,-1),new(1,-1,-1),.006f,Palette.Series[2]);
            Tube(f,new(-1,-1,-1),new(-1,1,-1),.006f,Palette.Series[3]);
            Tube(f,new(-1,-1,-1),new(-1,-1,1),.006f,Palette.Series[1]);
            f.Batch(start,p,true,matrix);
            void Label(string text,Vector3 v)
            {
                var q=Vector4.Transform(new Vector4(v,1),matrix);
                if(q.W>0) f.Text(text,(q.X/q.W+1)*f.Width/2,(1-q.Y/q.W)*f.Height/2,d.Theme.Text,12,"center");
            }
            Label(d.XAxis.Title,new(1.12f,-1,-1)); Label(d.YAxis.Title,new(-1,1.12f,-1)); Label(d.ZAxis.Title,new(-1,-1,1.12f));
        }
        foreach(var s in d.Series.Where(s=>s.Visible))
        {
            start=f.Count;
            if(d.View==ChartView.Pie3D)
            {
                if(s is not PieSeries pie) throw new NotSupportedException("Pie3D requires PieSeries.");
                BuildPie(pie,f); f.Batch(start,p,true,matrix); continue;
            }
            switch(s)
            {
                case SurfaceSeries3D surface:
                    var g=surface.Grid;
                    for(var row=0;row<g.Rows-1;row++) for(var col=0;col<g.Columns-1;col++)
                    {
                        var v0=g[col,row]; var v1=g[col+1,row]; var v2=g[col+1,row+1]; var v3=g[col,row+1];
                        var a=double.IsFinite(v0)?Map(new(g.X(col),v0,g.Y(row))):default;
                        var b=double.IsFinite(v1)?Map(new(g.X(col+1),v1,g.Y(row))):default;
                        var c=double.IsFinite(v2)?Map(new(g.X(col+1),v2,g.Y(row+1))):default;
                        var e=double.IsFinite(v3)?Map(new(g.X(col),v3,g.Y(row+1))):default;
                        if(double.IsFinite(v0)&&double.IsFinite(v1)&&double.IsFinite(v2)) f.Triangle3D(a,c,b,Palette.Heat(g.Normalized(v0)),Palette.Heat(g.Normalized(v2)),Palette.Heat(g.Normalized(v1)));
                        if(double.IsFinite(v0)&&double.IsFinite(v2)&&double.IsFinite(v3)) f.Triangle3D(a,e,c,Palette.Heat(g.Normalized(v0)),Palette.Heat(g.Normalized(v3)),Palette.Heat(g.Normalized(v2)));
                    }
                    break;
                case WaterfallSeries3D waterfall:
                    var wg=waterfall.Grid;
                    for(var row=0;row<wg.Rows;row++) for(var col=1;col<wg.Columns;col++)
                    {
                        var v0=wg[col-1,row]; var v1=wg[col,row]; if(!double.IsFinite(v0)||!double.IsFinite(v1)) continue;
                        Tube(f,Map(new(wg.X(col-1),v0,wg.Y(row))),Map(new(wg.X(col),v1,wg.Y(row))),.004f,Palette.Heat((double)row/Math.Max(1,wg.Rows-1)));
                    }
                    break;
                case BarSeries3D bars:
                    var bg=bars.Grid;
                    for(var row=0;row<bg.Rows;row++) for(var col=0;col<bg.Columns;col++)
                    {
                        var v=bg[col,row]; if(!double.IsFinite(v)) continue;
                        var q=Map(new(bg.X(col),v,bg.Y(row))); var bottom=Map(new(bg.X(col),Math.Clamp(0,d.YAxis.Range.Minimum,d.YAxis.Range.Maximum),bg.Y(row)));
                        var dx=.7f/Math.Max(1,bg.Columns); var dz=.7f/Math.Max(1,bg.Rows);
                        Box(f,new(q.X-dx,Math.Min(bottom.Y,q.Y),q.Z-dz),new(q.X+dx,Math.Max(bottom.Y,q.Y),q.Z+dz),Palette.Heat(bg.Normalized(v)));
                    }
                    break;
                case MeshSeries3D mesh:
                    var vertices=mesh.Mesh.Vertices.Span; var indices=mesh.Mesh.Indices.Span;
                    for(var i=0;i<indices.Length;i+=3)
                    {
                        var a=vertices[indices[i]]; var b=vertices[indices[i+1]]; var c=vertices[indices[i+2]];
                        f.Triangle3D(Map(a.Position),Map(b.Position),Map(c.Position),a.Color,b.Color,c.Color);
                    }
                    break;
                case PointLineSeries3D line:
                    var points=line.Points.Span;
                    for(var i=0;i<points.Length;i++)
                    {
                        var q=Map(points[i]);
                        if(line.ShowLine&&i>0) Tube(f,Map(points[i-1]),q,(float)Math.Max(.001,line.StrokeWidth*.002),line.Color);
                        if(line.PointSize>0) { var r=(float)(line.PointSize*.002); Box(f,q-new Vector3(r),q+new Vector3(r),line.Color); }
                    }
                    break;
                default: throw new NotSupportedException($"{s.GetType().Name} cannot be rendered in the 3D view.");
            }
            f.Batch(start,p,true,matrix);
        }
        if(d.Annotations.Count>0) throw new NotSupportedException("3D annotations are not implemented.");
    }
    private static void Tube(FrameBuilder f,Vector3 a,Vector3 b,float radius,ColorRgba color)
    {
        var delta=b-a; if(delta.LengthSquared()<1e-16f) return;
        var axis=Vector3.Normalize(delta); var up=Math.Abs(axis.Y)<.9f?Vector3.UnitY:Vector3.UnitX;
        var u=Vector3.Normalize(Vector3.Cross(axis,up))*radius; var v=Vector3.Normalize(Vector3.Cross(axis,u))*radius;
        for(var i=0;i<6;i++)
        {
            var a0=i*MathF.PI/3; var a1=(i+1)*MathF.PI/3;
            var n0=MathF.Cos(a0)*u+MathF.Sin(a0)*v; var n1=MathF.Cos(a1)*u+MathF.Sin(a1)*v;
            f.Triangle3D(a+n0,b+n0,b+n1,color,color,color); f.Triangle3D(a+n0,b+n1,a+n1,color,color,color);
        }
    }
    private static void Box(FrameBuilder f,Vector3 min,Vector3 max,ColorRgba color)
    {
        Vector3[] v=[new(min.X,min.Y,min.Z),new(max.X,min.Y,min.Z),new(max.X,max.Y,min.Z),new(min.X,max.Y,min.Z),
            new(min.X,min.Y,max.Z),new(max.X,min.Y,max.Z),new(max.X,max.Y,max.Z),new(min.X,max.Y,max.Z)];
        int[] indices=[0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5];
        for(var i=0;i<indices.Length;i+=3) f.Triangle3D(v[indices[i]],v[indices[i+1]],v[indices[i+2]],color,color,color);
    }
    private static void BuildPie(PieSeries pie,FrameBuilder f)
    {
        var slices=pie.Slices.Span; var total=0.0; foreach(var s in slices) total+=s.Value;
        var angle=0.0;
        foreach(var slice in slices)
        {
            var sweep=slice.Value/total*2*Math.PI; var end=angle+sweep; if(sweep<=0) continue;
            var middle=(angle+end)/2; var offset=new Vector3((float)(Math.Cos(middle)*pie.Explosion),0,(float)(Math.Sin(middle)*pie.Explosion));
            Vector3 P(double a,double r,double y)=>offset+new Vector3((float)(Math.Cos(a)*r),(float)y,(float)(Math.Sin(a)*r));
            var steps=Math.Max(2,(int)Math.Ceiling(sweep*40)); var height=pie.Height; var inner=pie.InnerRadius;
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            { f.Triangle3D(a,b,c,slice.Color,slice.Color,slice.Color); f.Triangle3D(a,c,d,slice.Color,slice.Color,slice.Color); }
            for(var i=0;i<steps;i++)
            {
                var a=angle+sweep*i/steps; var b=angle+sweep*(i+1)/steps;
                Quad(P(a,inner,height),P(b,inner,height),P(b,1,height),P(a,1,height));
                Quad(P(a,1,0),P(a,1,height),P(b,1,height),P(b,1,0));
                if(inner>0) Quad(P(a,inner,0),P(b,inner,0),P(b,inner,height),P(a,inner,height));
                Quad(P(a,inner,0),P(a,1,0),P(b,1,0),P(b,inner,0));
            }
            Quad(P(angle,inner,0),P(angle,inner,height),P(angle,1,height),P(angle,1,0));
            Quad(P(end,inner,0),P(end,1,0),P(end,1,height),P(end,inner,height));
            angle=end;
        }
    }
}
