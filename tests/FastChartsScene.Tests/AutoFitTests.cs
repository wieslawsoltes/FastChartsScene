using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAutoFitTests()
    {
        Add("autofit includes positive and negative totals and bar widths",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[5,-4]),StackGroup="g",BarWidth=2 }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[6,-7]),StackGroup="g",BarWidth=2 });
            d.AutoFit(); True(d.YAxis.Range.Maximum>=11&&d.YAxis.Range.Minimum<=-11); True(d.XAxis.Range.Minimum<=-1&&d.XAxis.Range.Maximum>=2);
        });
        Add("autofit transposes horizontal stack bounds",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[5]),StackGroup="g",Horizontal=true }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[6]),StackGroup="g",Horizontal=true });
            d.AutoFit(); True(d.XAxis.Range.Maximum>=11&&d.XAxis.Range.Minimum<=0); True(d.YAxis.Range.Minimum<=-.4&&d.YAxis.Range.Maximum>=.4);
        });
        Add("autofit isolates stack groups and excludes hidden series",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[4]),StackGroup="a" }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[6]),StackGroup="b" }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[100]),StackGroup="a",Visible=false });
            d.AutoFit(); True(d.YAxis.Range.Maximum>=6&&d.YAxis.Range.Maximum<7);
        });
        Add("3D autofit separates heights and spatial Z bounds",()=> {
            using var d=new ChartDocument { View=ChartView.ThreeD }; d.Series.Add(new SurfaceSeries3D { Grid=new(2,2,100,200,300,400,[-10,15,20,5]) }); d.AutoFit();
            True(d.XAxis.Range.Minimum<=100&&d.XAxis.Range.Maximum>=200); True(d.YAxis.Range.Minimum<=-10&&d.YAxis.Range.Maximum>=20); True(d.ZAxis.Range.Minimum<=300&&d.ZAxis.Range.Maximum>=400);
        });
        Add("autofit rejects overflowing stacks atomically",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[1e308]),StackGroup="g" }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[1e308]),StackGroup="g" });
            var x=d.XAxis.Range; var y=d.YAxis.Range; var revision=d.Revision;
            Throws<InvalidOperationException>(d.AutoFit); True(d.XAxis.Range==x&&d.YAxis.Range==y); Equal(revision,d.Revision);
        });
        Add("autofit includes error bounds and coalesces all axes",()=> {
            using var d=new ChartDocument(); var e=new ErrorBarSeries { Data=new UniformData(25,1,[2]) }; e.SetErrors([10],[20]); d.Series.Add(e);
            var events=0; d.Changed+=(_,_)=>events++; d.AutoFit(); True(d.YAxis.Range.Minimum<=-8&&d.YAxis.Range.Maximum>=22); Equal(1,events);
        });
        Add("disposed documents do not reattach collection observers",()=> {
            var d=new ChartDocument(); d.Dispose(); var revision=d.Revision; var axis=new Axis("late"); d.YAxes.Add(axis); axis.Title="Changed";
            Equal(revision,d.Revision); Throws<ObjectDisposedException>(()=>d.Validate()); Throws<ObjectDisposedException>(d.AutoFit);
        });
        Add("segments own their axis binding arrays",()=> {
            string[] ids=["y"]; var segment=new ChartSegment("A",1,ids); ids[0]="bad"; segment.AxisIds[0]="bad";
            True(segment.AxisIds[0]=="y");
            using var d=new ChartDocument { AxisLayout=AxisLayout.Segmented }; d.Segments.Add(segment); True(d.Validate().Count==0);
        });
        Add("duplicate series and invalid segment bindings are diagnosed",()=> {
            using var d=new ChartDocument(); var s=new LineSeries(); d.Series.Add(s); d.Series.Add(s); True(d.Validate().Any(e=>e.Contains("unique")));
            d.Series.Clear(); d.AxisLayout=AxisLayout.Segmented; d.Segments.Add(new("a",1,["y","unknown"])); True(d.Validate().Any(e=>e.Contains("unknown")));
            Throws<ArgumentOutOfRangeException>(()=>d.View=(ChartView)123);
        });
    }
}
