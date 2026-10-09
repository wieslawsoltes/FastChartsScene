using FastChartsScene.Core;
using System.Globalization;
using System.Text;

namespace FastChartsScene.Data;

/// <summary>Immutable data source. A NaN Y value is an explicit break, never an interpolated point.</summary>
public interface IDataSource
{
    int Count { get; }
    bool IsOrdered { get; }
    double X(int index);
    double Y(int index);
}

public sealed class XyData : IDataSource
{
    private readonly double[] _x,_y;
    public int Count=>_x.Length;
    public bool IsOrdered { get; }
    public XyData(ReadOnlySpan<double> x,ReadOnlySpan<double> y)
    {
        if(x.Length!=y.Length) throw new ArgumentException("X and Y lengths differ.");
        _x=x.ToArray(); _y=y.ToArray(); var ordered=true;
        for(var i=0;i<x.Length;i++)
        {
            if(!double.IsFinite(x[i])||double.IsInfinity(y[i])) throw new ArgumentException("X must be finite; Y may be finite or NaN.");
            if(i>0&&x[i]<x[i-1]) ordered=false;
        }
        IsOrdered=ordered;
    }
    public double X(int i)=>_x[i];
    public double Y(int i)=>_y[i];
    public static XyData FromPoints(IEnumerable<PointD> points)
    {
        var p=points.ToArray(); return new(p.Select(v=>v.X).ToArray(),p.Select(v=>v.Y).ToArray());
    }
}

public sealed class UniformData : IDataSource
{
    private readonly double[] _samples;
    public int Count=>_samples.Length;
    public bool IsOrdered=>true;
    public double Start { get; }
    public double Step { get; }
    public UniformData(double start,double step,ReadOnlySpan<double> samples)
    {
        if(!double.IsFinite(start)||!double.IsFinite(step)||step<=0||!double.IsFinite(start+step*Math.Max(0,samples.Length-1))) throw new ArgumentOutOfRangeException(nameof(step));
        if(samples.Contains(double.PositiveInfinity)||samples.Contains(double.NegativeInfinity)) throw new ArgumentException("Infinite samples are invalid.");
        Start=start; Step=step; _samples=samples.ToArray();
    }
    public double X(int i) { if((uint)i>=(uint)Count) throw new ArgumentOutOfRangeException(nameof(i)); return Start+Step*i; }
    public double Y(int i)=>_samples[i];
}

/// <summary>Bounded, synchronized FIFO. Snapshots own their data and remain valid during future writes.</summary>
public sealed class RollingBuffer
{
    private readonly object _gate=new();
    private readonly double[] _data;
    private int _head,_count;
    private long _written;
    public int Capacity=>_data.Length;
    public double Start { get; }
    public double Step { get; }
    public long SamplesWritten { get { lock(_gate) return _written; } }
    public RollingBuffer(int capacity,double start=0,double step=1)
    {
        if(capacity<1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if(!double.IsFinite(start)||!double.IsFinite(step)||step<=0) throw new ArgumentOutOfRangeException(nameof(step));
        _data=new double[capacity]; Start=start; Step=step;
    }
    public void Append(ReadOnlySpan<double> values)
    {
        foreach(var v in values) if(double.IsInfinity(v)) throw new ArgumentException("Infinite sample.");
        lock(_gate)
        {
            if(_written>long.MaxValue-values.Length || !double.IsFinite(Start+Step*(_written+values.Length))) throw new OverflowException("Sample time overflow.");
            foreach(var v in values) { _data[_head]=v; _head=(_head+1)%Capacity; _count=Math.Min(_count+1,Capacity); _written++; }
        }
    }
    public UniformData Snapshot()
    {
        lock(_gate)
        {
            var result=new double[_count]; var first=(_head-_count+Capacity)%Capacity;
            for(var i=0;i<_count;i++) result[i]=_data[(first+i)%Capacity];
            return new(Start+(_written-_count)*Step,Step,result);
        }
    }
    public void Clear() { lock(_gate) { _head=_count=0; _written=0; } }
}

public static class DataSearch
{
    public static int LowerBound(IDataSource data,double x)
    {
        if(!data.IsOrdered) throw new ArgumentException("Binary search requires ordered X values.");
        var l=0; var r=data.Count;
        while(l<r) { var m=l+(r-l)/2; if(data.X(m)<x) l=m+1; else r=m; }
        return l;
    }
    public static int UpperBound(IDataSource data,double x)
    {
        if(!data.IsOrdered) throw new ArgumentException("Binary search requires ordered X values.");
        var l=0; var r=data.Count;
        while(l<r) { var m=l+(r-l)/2; if(data.X(m)<=x) l=m+1; else r=m; }
        return l;
    }
    public static PointD? Nearest(IDataSource data,double x)
    {
        if(data.Count==0) return null;
        if(!data.IsOrdered)
        {
            var best=-1; var d=double.PositiveInfinity;
            for(var i=0;i<data.Count;i++) if(double.IsFinite(data.Y(i))&&Math.Abs(data.X(i)-x)<d) { d=Math.Abs(data.X(i)-x); best=i; }
            return best<0?null:new(data.X(best),data.Y(best));
        }
        var right=LowerBound(data,x); var left=right-1;
        while(left>=0||right<data.Count)
        {
            var takeLeft=left>=0&&(right>=data.Count||Math.Abs(data.X(left)-x)<=Math.Abs(data.X(right)-x));
            var i=takeLeft?left--:right++;
            if(double.IsFinite(data.Y(i))) return new(data.X(i),data.Y(i));
        }
        return null;
    }
    public static double Interpolate(IDataSource data,double x)
    {
        if(data.Count==0||!data.IsOrdered) return double.NaN;
        var j=LowerBound(data,x);
        if(j<data.Count&&data.X(j)==x) return data.Y(j);
        if(j==0||j==data.Count) return double.NaN;
        var a=data.Y(j-1); var b=data.Y(j);
        return a+(b-a)*(x-data.X(j-1))/(data.X(j)-data.X(j-1));
    }
}

public static class CsvData
{
    public static string Write(IDataSource data)
    {
        var b=new StringBuilder("X,Y\n");
        for(var i=0;i<data.Count;i++) b.Append(data.X(i).ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(double.IsNaN(data.Y(i))?"":data.Y(i).ToString("R",CultureInfo.InvariantCulture)).Append('\n');
        return b.ToString();
    }
    public static XyData Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<double> x=[],y=[]; var lineNumber=0;
        foreach(var raw in text.Split('\n'))
        {
            lineNumber++; var s=raw.Trim(); if(s.Length==0) continue;
            if(lineNumber==1&&s.Equals("X,Y",StringComparison.OrdinalIgnoreCase)) continue;
            var fields=s.Split(',');
            if(fields.Length!=2||!double.TryParse(fields[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var a)||!double.IsFinite(a)) throw new FormatException($"Invalid X at line {lineNumber}.");
            var v=double.NaN;
            if(fields[1].Length>0&&(!double.TryParse(fields[1],NumberStyles.Float,CultureInfo.InvariantCulture,out v)||double.IsInfinity(v))) throw new FormatException($"Invalid Y at line {lineNumber}.");
            x.Add(a); y.Add(v);
        }
        return new(x.ToArray(),y.ToArray());
    }
}
