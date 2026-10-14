using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterRendering()
    {
        Add("layout rejects invalid sizes and axis bindings", () => {
            using var d=new ChartDocument();
            Throws<ArgumentOutOfRangeException>(()=>ChartLayout.Create(d,double.NaN,400));
            Throws<ArgumentOutOfRangeException>(()=>ChartLayout.Create(d,0,400));
            d.YAxes.Clear(); Throws<InvalidOperationException>(()=>ChartLayout.Create(d,800,400));
        });
        Add("layout screen-data transforms round trip with log and inversion", () => {
            using var d=new ChartDocument(); d.XAxis.Range=new(1,1000); d.XAxis.Scale=AxisScale.Logarithmic; d.XAxis.Inverted=true;
            d.YAxis.Range=new(-80,200); var l=ChartLayout.Create(d,1024,768);
            var p=new PointD(28,135); var q=l.FromScreen(d.XAxis,d.YAxis,l.ToScreen(d.XAxis,d.YAxis,p));
            Equal(p.X,q.X); Equal(p.Y,q.Y);
        });
        Add("stacked panes remain within tiny viewports", () => {
            using var d=new ChartDocument { AxisLayout=AxisLayout.Stacked };
            for(var i=1;i<8;i++) d.YAxes.Add(new Axis("y"+i));
            var l=ChartLayout.Create(d,20,20);
            True(l.AxisPlots.Values.All(r=>r.Height>0&&r.Y>=l.Plot.Y&&r.Bottom<=l.Plot.Bottom+1e-9));
        });
        Add("large finite pane weights do not overflow layout", () => {
            using var d=new ChartDocument { AxisLayout=AxisLayout.Stacked }; d.YAxis.Weight=double.MaxValue;
            d.YAxes.Add(new Axis("y2") { Weight=double.MaxValue });
            var l=ChartLayout.Create(d,800,600); Equal(l.ForAxis("y").Height,l.ForAxis("y2").Height);
            True(double.IsFinite(l.ForAxis("y").Height));
        });
        Add("segmented axes share only their assigned pane", () => {
            using var d=new ChartDocument { AxisLayout=AxisLayout.Segmented }; d.YAxes.Add(new("b")); d.YAxes.Add(new("c"));
            d.Segments.Add(new("first",2,["y","b"])); d.Segments.Add(new("second",1,["c"]));
            var l=ChartLayout.Create(d,800,600); True(l.ForAxis("y")==l.ForAxis("b"));
            Equal(l.ForAxis("y").Height,2*l.ForAxis("c").Height);
        });
        Add("invalid packed frames fail validation", () => {
            using var d=new ChartDocument(); var l=ChartLayout.Create(d,800,600);
            var m=new FrameMetadata(800,600,"#000000",[],[],[],l.Plot,1,0,1,"test");
            Throws<InvalidOperationException>(()=>new RenderFrame([],m,l).Validate());
            new RenderFrame([],m with { VertexCount=0 },l).Validate();
        });
        RegisterIssueRegressionTests();
        RegisterSceneTests();
        RegisterCursorIndexReviewTests();
        RegisterCursorValueFormatTests();
    }
    static partial void RegisterSceneTests();
}
