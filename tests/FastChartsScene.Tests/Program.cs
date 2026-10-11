using System.Numerics;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Charts;
using FastChartsScene.SignalProcessing;

namespace FastChartsScene.Tests;

internal static partial class Program
{
    private static readonly List<(string Name,Action Test)> Tests=[];
    public static int Main()
    {
        RegisterCore(); RegisterRendering();
        var failed=0;
        foreach(var test in Tests)
        {
            try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
            catch(Exception e) { failed++; Console.Error.WriteLine($"FAIL {test.Name}\n{e}"); }
        }
        Console.WriteLine($"RESULT {Tests.Count-failed}/{Tests.Count} passed; {failed} failed");
        return failed==0?0:1;
    }
    private static void Add(string name,Action test)=>Tests.Add((name,test));
    private static void True(bool condition,string message="Assertion failed") { if(!condition) throw new Exception(message); }
    private static void Equal(double expected,double actual,double tolerance=1e-9) { if(!double.IsFinite(actual)||Math.Abs(actual-expected)>tolerance) throw new Exception($"Expected {expected:R}, got {actual:R}"); }
    private static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    static partial void RegisterRendering();
    private static void RegisterCore()
    {
        Add("axis rejects zero, infinite and overflowing spans",()=> { Throws<ArgumentOutOfRangeException>(()=>new AxisRange(1,1)); Throws<ArgumentOutOfRangeException>(()=>new AxisRange(0,double.PositiveInfinity)); Throws<ArgumentOutOfRangeException>(()=>new AxisRange(-1e308,1e308)); });
        Add("linear axis round trip at timestamp magnitudes",()=> { var a=new Axis("x") { Range=new(1_728_000_000,1_728_000_010) }; foreach(var u in new[]{0.0,.25,.5,1}) Equal(u,a.ToUnit(a.FromUnit(u))); });
        Add("inverted axis and anchor-preserving zoom",()=> { var a=new Axis("x") { Range=new(0,100),Inverted=true }; var anchor=a.FromUnit(.3); a.Zoom(.3,.5); Equal(anchor,a.FromUnit(.3)); Equal(50,a.Range.Span); });
        Add("log axis round trip and anchored zoom",()=> { var a=new Axis("x") { Range=new(1,1000),Scale=AxisScale.Logarithmic }; Equal(10,a.FromUnit(1.0/3),1e-8); var v=a.FromUnit(.7); a.Zoom(.7,.8); Equal(v,a.FromUnit(.7),1e-8); });
        Add("log invalid values are explicit",()=> { var a=new Axis("x") { Range=new(-1,1) }; Throws<ArgumentOutOfRangeException>(()=>a.Scale=AxisScale.Logarithmic); a.Range=new(1,10); a.Scale=AxisScale.Logarithmic; True(double.IsNaN(a.ToUnit(0))); });
        Add("axis ticks remain ordered and in range",()=> { var a=new Axis("x") { Range=new(-.0007,.0032) }; var t=a.Ticks(); True(t.Count>=2); True(t.All(t=>t.Value>=a.Range.Minimum-1e-12&&t.Value<=a.Range.Maximum+1e-12)); True(t.Zip(t.Skip(1)).All(p=>p.First.Value<p.Second.Value)); });
        Add("date and category formatting",()=> { var a=new Axis("x") { Range=new(0,120),Scale=AxisScale.DateTime }; True(a.Format(60).StartsWith("00:01:00")); a.Scale=AxisScale.Category; a.Categories=["A","B"]; True(a.Format(1)=="B"); });
        Add("color hex roundtrip and heat endpoints",()=> { True(ColorRgba.Parse("12345678").ToHex()=="#12345678"); True(Palette.Heat(0)!=Palette.Heat(1)); Equal(0,Palette.Heat(double.NaN).A); });
        Add("XY source owns buffers",()=> { double[] x=[0,1],y=[2,3]; var d=new XyData(x,y); x[0]=100; y[0]=100; Equal(0,d.X(0)); Equal(2,d.Y(0)); });
        Add("invalid data rejected; NaN is a break",()=> { Throws<ArgumentException>(()=>new XyData([0],[double.PositiveInfinity])); Throws<ArgumentException>(()=>new XyData([double.NaN],[1])); var d=new XyData([0],[double.NaN]); True(double.IsNaN(d.Y(0))); });
        Add("uniform source uses implicit X",()=> { var d=new UniformData(5,.25,[1,2,3]); Equal(5.5,d.X(2)); Throws<ArgumentOutOfRangeException>(()=>d.X(3)); });
        Add("FIFO wraps with absolute sample times",()=> { var r=new RollingBuffer(3,10,.5); r.Append([1,2,3,4,5]); var d=r.Snapshot(); Equal(3,d.Count); Equal(3,d.Y(0)); Equal(5,d.Y(2)); Equal(11,d.X(0)); });
        Add("FIFO snapshot stays immutable",()=> { var r=new RollingBuffer(2); r.Append([1,2]); var d=r.Snapshot(); r.Append([3,4]); Equal(1,d.Y(0)); Equal(4,r.Snapshot().Y(1)); });
        Add("FIFO rejects entire invalid batch atomically",()=> { var r=new RollingBuffer(2); r.Append([1]); Throws<ArgumentException>(()=>r.Append([2,double.PositiveInfinity])); Equal(1,r.SamplesWritten); });
        Add("FIFO supports concurrent producers",()=> { var r=new RollingBuffer(128); Parallel.For(0,1000,i=>r.Append(new double[]{i})); Equal(1000,r.SamplesWritten); Equal(128,r.Snapshot().Count); });
        Add("binary search duplicates and empty boundaries",()=> { var d=new XyData([1,2,2,4],[0,1,2,3]); Equal(1,DataSearch.LowerBound(d,2)); Equal(3,DataSearch.UpperBound(d,2)); Equal(4,DataSearch.LowerBound(d,8)); Equal(0,DataSearch.LowerBound(new XyData([],[]),0)); });
        Add("nearest point skips gaps without inventing values",()=> { var d=new XyData([0,1,2],[3,double.NaN,5]); Equal(3,DataSearch.Nearest(d,.9)!.Value.Y); True(double.IsNaN(DataSearch.Interpolate(d,.5))); });
        Add("interpolation does not extrapolate",()=> { var d=new XyData([0,1],[2,4]); Equal(3,DataSearch.Interpolate(d,.5)); True(double.IsNaN(DataSearch.Interpolate(d,2))); });
        Add("envelope block queries equal brute force",()=> { var random=new Random(123); var y=Enumerable.Range(0,2000).Select(_=>random.NextDouble()).ToArray(); var d=new UniformData(0,1,y); var index=new EnvelopeIndex(d); for(var k=0;k<200;k++) { var a=random.Next(1999); var b=random.Next(a+1,2001); var mm=index.Query(a,b); Equal(y[a..b].Min(),d.Y(mm.Minimum)); Equal(y[a..b].Max(),d.Y(mm.Maximum)); } });
        Add("screen envelope retains isolated spikes and gaps",()=> { var y=new double[100_000]; y[12345]=100; y[52345]=-100; y[30000]=double.NaN; var d=new UniformData(0,1,y); var a=new Axis("x") { Range=new(0,99999) }; var indices=new EnvelopeIndex(d).Select(a,200); True(indices.Contains(12345)&&indices.Contains(52345)&&indices.Contains(30000)); True(indices.Count<1000); True(indices.Zip(indices.Skip(1)).All(v=>v.First<v.Second)); });
        Add("freeform sources are not reordered by decimation",()=> { var d=new XyData([2,0,1],[2,0,1]); True(!d.IsOrdered); True(new EnvelopeIndex(d).Select(new Axis("x"),1).SequenceEqual(new[]{0,1,2})); });
        Add("CSV invariant culture roundtrip including breaks",()=> { var d=new XyData([1.5,2.5],[2.25,double.NaN]); var copy=CsvData.Read(CsvData.Write(d)); Equal(1.5,copy.X(0)); Equal(2.25,copy.Y(0)); True(double.IsNaN(copy.Y(1))); Throws<FormatException>(()=>CsvData.Read("X,Y\na,b")); });
        Add("FFT inverse roundtrip",()=> { var a=Enumerable.Range(0,128).Select(i=>new Complex(Math.Sin(i*.3),Math.Cos(i*.2))).ToArray(); var b=(Complex[])a.Clone(); Fourier.Transform(b); Fourier.Transform(b,true); for(var i=0;i<a.Length;i++) Equal(0,(a[i]-b[i]).Magnitude,1e-10); });
        Add("FFT detects sinusoid frequency and physical amplitude",()=> { var y=Enumerable.Range(0,1024).Select(i=>2*Math.Sin(2*Math.PI*64*i/1024)).ToArray(); var s=Fourier.Spectrum(y,1024,WindowFunction.Rectangular); var peak=Array.IndexOf(s.Amplitudes,s.Amplitudes.Max()); Equal(64,s.Frequencies[peak]); Equal(2,s.Amplitudes[peak],1e-10); });
        Add("FFT DC and Nyquist are not doubled",()=> { var dc=Fourier.Spectrum(Enumerable.Repeat(3.0,32).ToArray(),32,WindowFunction.Rectangular); Equal(3,dc.Amplitudes[0]); var ny=Fourier.Spectrum(Enumerable.Range(0,32).Select(i=>i%2==0?2.0:-2).ToArray(),32,WindowFunction.Rectangular); Equal(2,ny.Amplitudes[^1]); });
        Add("FFT validates sample shape and finite values",()=> { Throws<ArgumentException>(()=>Fourier.Transform(new Complex[3])); Throws<ArgumentException>(()=>Fourier.Spectrum([0,double.NaN,0,1],1)); });
        Add("STFT dimensions and frequency spacing",()=> { var s=Fourier.Spectrogram(new double[1024],1024,256,64); Equal(13,s.TimeBins); Equal(129,s.FrequencyBins); Equal(4,s.FrequencyStep); True(s.Values.All(double.IsFinite)); });
        Add("low pass filter converges to unit DC gain",()=> { var f=new BiquadLowPass(1000,30); var y=0.0; for(var i=0;i<1000;i++) y=f.Process(1); Equal(1,y,1e-8); });
        Add("SMA and EMA warmup semantics",()=> { var s=Indicators.Sma([1,2,3,4],3); True(double.IsNaN(s[0])); Equal(2,s[2]); Equal(3,s[3]); var e=Indicators.Ema([1,2,3,4],3); Equal(2,e[2]); Equal(3,e[3]); });
        Add("RSI monotone and flat cases",()=> { Equal(100,Indicators.Rsi(Enumerable.Range(1,20).Select(i=>(double)i).ToArray())[^1]); Equal(50,Indicators.Rsi(Enumerable.Repeat(2.0,20).ToArray())[^1]); });
        Add("Bollinger population deviation",()=> { var b=Indicators.Bollinger([1,2,3],3,2); Equal(2,b.Middle[2]); Equal(2+2*Math.Sqrt(2.0/3),b.Upper[2]); });
        Add("document update transactions coalesce events",()=> { using var d=new ChartDocument(); var events=0; d.Changed+=(_,_)=>events++; using(d.BeginUpdate()) { d.Title="A"; using(d.BeginUpdate()) { d.XAxis.Range=new(0,2); d.YAxis.Range=new(0,4); } Equal(0,events); } Equal(1,events); });
        Add("removed series no longer invalidates document",()=> { using var d=new ChartDocument(); var s=new LineSeries(); d.Series.Add(s); d.Series.Remove(s); var revision=d.Revision; s.Title="orphan"; Equal(revision,d.Revision); });
        Add("document axis bindings and paired arrays validated",()=> { using var d=new ChartDocument(); d.Series.Add(new BandSeries { Data=new XyData([0],[1]),Upper=new XyData([1],[2]) }); True(d.Validate().Count==1); });
        Add("autofit includes finite extrema but ignores gaps",()=> { using var d=new ChartDocument(); d.Series.Add(new LineSeries { Data=new XyData([1,2,3],[10,double.NaN,20]) }); d.AutoFit(); True(d.YAxis.Range.Minimum<10&&d.YAxis.Range.Maximum>20); });
        Add("invalid OHLC is rejected",()=> { var s=new FinancialSeries(); Throws<ArgumentException>(()=>s.SetData([new(1,10,9,8,11)])); s.SetData([new(1,10,12,8,11)]); Equal(1,s.PointCount); });
        Add("pie validates totals and geometry",()=> { var p=new PieSeries(); Throws<ArgumentException>(()=>p.SetData([new("zero",0,Palette.Series[0])])); Throws<ArgumentOutOfRangeException>(()=>p.InnerRadius=1); });
        Add("grid dimensions and constant range normalization",()=> { Throws<ArgumentException>(()=>new ScalarGrid(2,2,0,1,0,1,[1])); var g=new ScalarGrid(2,2,0,1,0,1,[2,2,2,2]); Equal(.5,g.Normalized(2)); });
        Add("line clipping handles crossing, outside and degenerate",()=> { var a=new PointD(-10,5); var b=new PointD(20,5); True(GeometryAlgorithms.ClipLine(new(0,0,10,10),ref a,ref b)); Equal(0,a.X); Equal(10,b.X); a=new(-2,-1); b=new(-2,20); True(!GeometryAlgorithms.ClipLine(new(0,0,10,10),ref a,ref b)); });
        Add("polar cardinal positions",()=> { var p=GeometryAlgorithms.Polar(90,2); Equal(0,p.X); Equal(2,p.Y); });
        Add("Smith impedance transform and inverse",()=> { Equal(0,GeometryAlgorithms.Smith(1,0).X); Equal(-1,GeometryAlgorithms.Smith(0,0).X); var p=GeometryAlgorithms.Smith(.7,1.2); var r=GeometryAlgorithms.InverseSmith(p.X,p.Y); Equal(.7,r.X); Equal(1.2,r.Y); });
        Add("monotone cubic has no overshoot",()=> { var p=GeometryAlgorithms.Monotone(new XyData([0,1,2,3],[0,1,1.2,2]),32); True(p.All(p=>p.Y>=0&&p.Y<=2)); True(p.Zip(p.Skip(1)).All(v=>v.First.Y<=v.Second.Y+1e-12)); });
        Add("marching squares produces expected line",()=> { var g=new ScalarGrid(2,2,0,1,0,1,[0,1,0,1]); var c=GeometryAlgorithms.Contours(g,[.5]); Equal(1,c.Count); Equal(.5,c[0].A.X); Equal(.5,c[0].B.X); });
        Add("marching squares saddle and NaN holes",()=> { var g=new ScalarGrid(2,2,0,1,0,1,[2,-1,-1,2]); Equal(2,GeometryAlgorithms.Contours(g,[0]).Count); var h=new ScalarGrid(2,2,0,1,0,1,[0,1,double.NaN,1]); Equal(0,GeometryAlgorithms.Contours(h,[.5]).Count); });
        Add("OBJ negative-index faces and validation",()=> { var m=GeometryAlgorithms.ReadObj("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf -4 -3 -2 -1",Palette.Series[0]); Equal(6,m.Indices.Length); Throws<FormatException>(()=>GeometryAlgorithms.ReadObj("v 0 0 0\nf 1 2 3",Palette.Series[0])); });
    }
}
