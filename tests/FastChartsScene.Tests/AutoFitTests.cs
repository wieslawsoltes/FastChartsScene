using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAutoFitTests()
    {
        Add("autofit includes positive and negative stack totals and bar widths",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[5,-4]),StackGroup="g",BarWidth=2 }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[6,-7]),StackGroup="g",BarWidth=2 });
            d.AutoFit(); True(d.YAxis.Range.Maximum>=11&&d.YAxis.Range.Minimum<=-11); True(d.XAxis.Range.Minimum<=-1&&d.XAxis.Range.Maximum>=2);
        });
        Add("autofit transposes horizontal stack bounds",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[5]),StackGroup="g",Horizontal=true }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[6]),StackGroup="g",Horizontal=true });
            d.AutoFit(); True(d.XAxis.Range.Maximum>=11&&d.XAxis.Range.Minimum<=0); True(d.YAxis.Range.Minimum<=-.4&&d.YAxis.Range.Maximum>=.4);
        });
        Add("3D autofit uses height values and independent Z coordinates",()=> {
            using var d=new ChartDocument { View=ChartView.ThreeD }; d.Series.Add(new SurfaceSeries3D { Grid=new(2,2,100,200,300,400,[-10,15,20,5]) }); d.AutoFit();
            True(d.XAxis.Range.Minimum<=100&&d.XAxis.Range.Maximum>=200); True(d.YAxis.Range.Minimum<=-10&&d.YAxis.Range.Maximum>=20); True(d.ZAxis.Range.Minimum<=300&&d.ZAxis.Range.Maximum>=400);
        });
        Add("autofit detects overflowing stacks before changing any axis",()=> {
            using var d=new ChartDocument(); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[1e308]),StackGroup="g" }); d.Series.Add(new BarSeries { Data=new UniformData(0,1,[1e308]),StackGroup="g" });
            var before=d.XAxis.Range; Throws<InvalidOperationException>(d.AutoFit); True(d.XAxis.Range==before);
        });
        Add("disposed documents reject validation and do not reattach on collection edits",()=> {
            var d=new ChartDocument(); d.Dispose(); var revision=d.Revision; var axis=new Axis("late"); d.YAxes.Add(axis); axis.Title="Changed";
            Equal(revision,d.Revision); Throws<ObjectDisposedException>(()=>d.Validate());
        });
        Add("invalid enum values and unknown segment axes are diagnosed",()=> {
            using var d=new ChartDocument(); Throws<ArgumentOutOfRangeException>(()=>d.View=(ChartView)123);
            d.AxisLayout=AxisLayout.Segmented; d.Segments.Add(new("a",1,["y","unknown"])); True(d.Validate().Any(e=>e.Contains("unknown")));
        });
    }
}
