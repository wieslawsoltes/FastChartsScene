using System.Globalization;
using FastChartsScene.Core;
using FastChartsScene.Data;

namespace FastChartsScene.Charts;

public readonly record struct ContourSegment(PointD A,PointD B,double Level);
public static class GeometryAlgorithms
{
    /// <summary>Liang-Barsky clipping, computed in double precision before float GPU conversion.</summary>
    public static bool ClipLine(RectD rect,ref PointD a,ref PointD b)
    {
        if(!a.IsFinite||!b.IsFinite||rect.Width<=0||rect.Height<=0) return false;
        var dx=b.X-a.X; var dy=b.Y-a.Y; var t0=0.0; var t1=1.0;
        static bool Test(double p,double q,ref double low,ref double high)
        {
            if(p==0) return q>=0;
            var t=q/p;
            if(p<0) { if(t>high) return false; low=Math.Max(low,t); }
            else { if(t<low) return false; high=Math.Min(high,t); }
            return true;
        }
        if(!Test(-dx,a.X-rect.X,ref t0,ref t1)||!Test(dx,rect.Right-a.X,ref t0,ref t1)||!Test(-dy,a.Y-rect.Y,ref t0,ref t1)||!Test(dy,rect.Bottom-a.Y,ref t0,ref t1)) return false;
        b=new(a.X+t1*dx,a.Y+t1*dy); a=new(a.X+t0*dx,a.Y+t0*dy); return true;
    }
    public static PointD Polar(double angleDegrees,double radius)=>new(radius*Math.Cos(angleDegrees*Math.PI/180),radius*Math.Sin(angleDegrees*Math.PI/180));
    /// <summary>Normalized impedance z=r+jx to reflection coefficient (z-1)/(z+1).</summary>
    public static PointD Smith(double resistance,double reactance)
    {
        var d=(resistance+1)*(resistance+1)+reactance*reactance;
        if(!double.IsFinite(d)||d==0) return new(double.NaN,double.NaN);
        return new((resistance*resistance+reactance*reactance-1)/d,2*reactance/d);
    }
    public static PointD InverseSmith(double real,double imaginary)
    {
        var d=(1-real)*(1-real)+imaginary*imaginary;
        if(d==0) return new(double.NaN,double.NaN);
        return new((1-real*real-imaginary*imaginary)/d,2*imaginary/d);
    }
    /// <summary>Fritsch-Carlson monotone cubic interpolation; no overshoot between monotone samples.</summary>
    public static IReadOnlyList<PointD> Monotone(IDataSource data,int subdivisions=8)
    {
        subdivisions=Math.Clamp(subdivisions,1,64);
        List<PointD> output=[];
        var start=0;
        while(start<data.Count)
        {
            if(double.IsNaN(data.Y(start))) { output.Add(new(data.X(start),double.NaN)); start++; continue; }
            var end=start+1;
            while(end<data.Count&&double.IsFinite(data.Y(end))&&data.X(end)>data.X(end-1)) end++;
            var count=end-start;
            if(count<2) { output.Add(new(data.X(start),data.Y(start))); start=end; continue; }
            var d=new double[count-1]; var m=new double[count];
            for(var i=0;i<count-1;i++) d[i]=(data.Y(start+i+1)-data.Y(start+i))/(data.X(start+i+1)-data.X(start+i));
            m[0]=d[0]; m[^1]=d[^1];
            for(var i=1;i<count-1;i++) m[i]=d[i-1]*d[i]<=0?0:2/(1/d[i-1]+1/d[i]);
            for(var i=0;i<count-1;i++)
            {
                if(d[i]==0) { m[i]=m[i+1]=0; continue; }
                var a=m[i]/d[i]; var b=m[i+1]/d[i]; var sum=a*a+b*b;
                if(sum>9) { var t=3/Math.Sqrt(sum); m[i]=t*a*d[i]; m[i+1]=t*b*d[i]; }
            }
            for(var i=0;i<count-1;i++)
            {
                var x0=data.X(start+i); var x1=data.X(start+i+1); var y0=data.Y(start+i); var y1=data.Y(start+i+1); var h=x1-x0;
                for(var k=0;k<subdivisions;k++)
                {
                    var t=(double)k/subdivisions; var t2=t*t; var t3=t2*t;
                    output.Add(new(x0+t*h,(2*t3-3*t2+1)*y0+(t3-2*t2+t)*h*m[i]+(-2*t3+3*t2)*y1+(t3-t2)*h*m[i+1]));
                }
            }
            output.Add(new(data.X(end-1),data.Y(end-1))); start=end;
        }
        return output;
    }
    /// <summary>Marching squares with an asymptotic saddle decider. NaN cells are holes.</summary>
    public static IReadOnlyList<ContourSegment> Contours(ScalarGrid grid,ReadOnlySpan<double> levels)
    {
        List<ContourSegment> segments=[];
        foreach(var level in levels)
        for(var row=0;row<grid.Rows-1;row++)
        for(var col=0;col<grid.Columns-1;col++)
        {
            double[] v=[grid[col,row],grid[col+1,row],grid[col+1,row+1],grid[col,row+1]];
            if(v.Any(double.IsNaN)) continue;
            PointD[] p=[new(grid.X(col),grid.Y(row)),new(grid.X(col+1),grid.Y(row)),new(grid.X(col+1),grid.Y(row+1)),new(grid.X(col),grid.Y(row+1))];
            List<(int Edge,PointD Point)> hits=[];
            for(var e=0;e<4;e++)
            {
                var j=(e+1)%4;
                if((v[e]>=level)==(v[j]>=level)) continue;
                var t=(level-v[e])/(v[j]-v[e]); hits.Add((e,new(p[e].X+t*(p[j].X-p[e].X),p[e].Y+t*(p[j].Y-p[e].Y))));
            }
            if(hits.Count==2) segments.Add(new(hits[0].Point,hits[1].Point,level));
            else if(hits.Count==4)
            {
                var determinant=(v[0]-level)*(v[2]-level)-(v[1]-level)*(v[3]-level);
                if(determinant>=0) { segments.Add(new(hits[0].Point,hits[1].Point,level)); segments.Add(new(hits[2].Point,hits[3].Point,level)); }
                else { segments.Add(new(hits[0].Point,hits[3].Point,level)); segments.Add(new(hits[1].Point,hits[2].Point,level)); }
            }
        }
        return segments;
    }
    /// <summary>Reads OBJ vertices and polygon faces, including negative indices. Faces are fan-triangulated.</summary>
    public static MeshData ReadObj(string text,ColorRgba color)
    {
        List<MeshVertex> vertices=[]; List<int> indices=[]; var line=0;
        foreach(var raw in text.Split('\n'))
        {
            line++; var s=raw.Split('#')[0].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries); if(s.Length==0) continue;
            if(s[0]=="v")
            {
                if(s.Length<4) throw new FormatException($"OBJ vertex at line {line}.");
                vertices.Add(new(new(double.Parse(s[1],CultureInfo.InvariantCulture),double.Parse(s[2],CultureInfo.InvariantCulture),double.Parse(s[3],CultureInfo.InvariantCulture)),color));
            }
            else if(s[0]=="f")
            {
                if(s.Length<4) throw new FormatException($"OBJ face at line {line}.");
                int Index(string token) { var i=int.Parse(token.Split('/')[0],CultureInfo.InvariantCulture); i=i<0?vertices.Count+i:i-1; if(i<0||i>=vertices.Count) throw new FormatException($"OBJ index at line {line}."); return i; }
                var first=Index(s[1]); for(var j=2;j<s.Length-1;j++) indices.AddRange([first,Index(s[j]),Index(s[j+1])]);
            }
        }
        return new(vertices.ToArray(),indices.ToArray());
    }
}
