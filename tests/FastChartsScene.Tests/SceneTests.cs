using System.Text.Json;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static ChartDocument LineDocument(double[]? samples=null)
    {
        var d=new ChartDocument { ShowGrid=false,ShowLegend=false }; d.XAxis.Range=new(0,4); d.YAxis.Range=new(-2,2);
        d.Series.Add(new LineSeries { Data=new UniformData(0,1,samples??[0,1,-1,1,0]) }); return d;
    }
    static partial void RegisterSceneTests()
    {
        Add("scene cache reuses frame and invalidates on nested model changes",()=> {
            using var d=LineDocument(); var c=new SceneCompiler(); var a=c.Compile(d,800,600); var b=c.Compile(d,800,600);
            True(ReferenceEquals(a,b)); Equal(1,c.CompilationCount);
            d.Series[0].Color=Palette.Series[1]; True(!ReferenceEquals(a,c.Compile(d,800,600))); Equal(2,c.CompilationCount);
            True(!ReferenceEquals(b,c.Compile(d,900,600)));
        });
        Add("line NaN breaks are not bridged by GPU triangles",()=> {
            using var a=LineDocument([0,1,double.NaN,-1,0]); using var b=LineDocument([0,1,0,-1,0]);
            var c=new SceneCompiler(); var fa=c.Compile(a,800,600); var fb=c.Compile(b,800,600);
            Equal(12,fb.Metadata.VertexCount-fa.Metadata.VertexCount);
        });
        Add("screen envelope bounds million sample line geometry",()=> {
            using var d=LineDocument(); var values=new double[1_000_000]; values[512345]=1;
            ((LineSeries)d.Series[0]).Data=new UniformData(0,.000004,values);
            var f=new SceneCompiler().Compile(d,800,600); f.Validate(); True(f.Metadata.VertexCount<25_000); Equal(1_000_000,f.Metadata.SourcePoints);
        });
        Add("filled geometry is clipped before float packing",()=> {
            using var d=LineDocument([-1e20,1e20,-1e20,1e20,0]); var s=(LineSeries)d.Series[0]; s.FillToBaseline=true;
            var f=new SceneCompiler().Compile(d,800,600); f.Validate(); True(f.Vertices.All(v=>Math.Abs(v)<1e6));
        });
        Add("all line interpolation and marker modes compile",()=> {
            using var d=LineDocument(); var s=(LineSeries)d.Series[0]; var c=new SceneCompiler();
            foreach(var mode in Enum.GetValues<LineInterpolation>()) foreach(var marker in Enum.GetValues<MarkerShape>())
            { s.Interpolation=mode; s.Marker=marker; c.Compile(d,800,600).Validate(); }
        });
        Add("heatmaps honor NaN holes",()=> {
            using var d=LineDocument(); d.Series.Clear(); var s=new HeatmapSeries { Grid=new(2,2,0,4,-2,2,[0,1,2,3]) }; d.Series.Add(s);
            var c=new SceneCompiler(); var count=c.Compile(d,800,600).Metadata.VertexCount;
            s.Grid=new(2,2,0,4,-2,2,[0,1,double.NaN,3]); Equal(6,count-c.Compile(d,800,600).Metadata.VertexCount);
        });
        Add("bands, bars, errors and financial styles compile",()=> {
            using var d=LineDocument(); var data=((LineSeries)d.Series[0]).Data; d.Series.Clear();
            d.Series.Add(new BandSeries { Data=data,Upper=new UniformData(0,1,[1,2,0,2,1]) });
            d.Series.Add(new BarSeries { Data=data }); var e=new ErrorBarSeries { Data=data }; e.SetErrors([.1,.2,.3,.2,.1],[.2,.3,.4,.3,.2]); d.Series.Add(e);
            var financial=new FinancialSeries(); financial.SetData([new(1,0,1,-1,.5),new(2,.5,1,-1,-.5)]); d.Series.Add(financial);
            var c=new SceneCompiler(); c.Compile(d,800,600).Validate(); financial.Style=FinancialStyle.Ohlc; c.Compile(d,800,600).Validate();
        });
        Add("polar and Smith generate finite circular scenes",()=> {
            using var d=LineDocument([0,1,.5,1,0]); var c=new SceneCompiler(); d.View=ChartView.Polar;
            c.Compile(d,800,600).Validate(); d.View=ChartView.Smith; c.Compile(d,800,600).Validate();
        });
        Add("wrong view-series combinations fail explicitly",()=> {
            using var d=LineDocument(); d.View=ChartView.ThreeD; Throws<NotSupportedException>(()=>new SceneCompiler().Compile(d,800,600));
        });
        Add("3D surface normals and projection matrices stay finite",()=> {
            using var d=LineDocument(); d.Series.Clear(); d.View=ChartView.ThreeD; d.Series.Add(new SurfaceSeries3D { Grid=new(3,3,0,4,-1,1,[0,1,0,1,0,-1,0,-1,0]) });
            var c=new SceneCompiler(); foreach(var ortho in new[]{false,true}) { d.Camera.Orthographic=ortho; var f=c.Compile(d,800,600); f.Validate(); True(f.Metadata.Batches.Any(b=>b.DepthTest)); }
        });
        Add("pie and doughnut meshes compile with exploded slices",()=> {
            using var d=LineDocument(); d.View=ChartView.Pie3D; d.Series.Clear(); var p=new PieSeries { Explosion=.12,InnerRadius=.4 };
            p.SetData([new("A",2,Palette.Series[0]),new("B",3,Palette.Series[1])]); d.Series.Add(p); new SceneCompiler().Compile(d,800,600).Validate();
        });
        Add("frame JSON uses stable camelCase metadata",()=> {
            using var d=LineDocument(); var f=new SceneCompiler().Compile(d,800,600); using var json=JsonDocument.Parse(f.MetadataJson());
            Equal(f.Metadata.VertexCount,json.RootElement.GetProperty("vertexCount").GetInt32());
        });
        Add("wheel zoom preserves pointer data position and supports undo redo",()=> {
            using var d=LineDocument(); var c=new SceneCompiler(); var f=c.Compile(d,800,600); var input=new ChartInteraction(d);
            var p=new PointD(f.Layout.Plot.X+123,f.Layout.Plot.Y+145); var before=f.Layout.FromScreen(d.XAxis,d.YAxis,p); var initial=d.XAxis.Range;
            input.Zoom(f,p,.5); var after=f.Layout.FromScreen(d.XAxis,d.YAxis,p); Equal(before.X,after.X); Equal(before.Y,after.Y);
            True(input.Undo()); True(d.XAxis.Range==initial); True(input.Redo()); Equal(initial.Span*.5,d.XAxis.Range.Span);
        });
        Add("drag is one history entry and cancellation restores viewport",()=> {
            using var d=LineDocument(); var c=new SceneCompiler(); var f=c.Compile(d,800,600); var input=new ChartInteraction(d); var p=new PointD(300,200); var initial=d.XAxis.Range;
            input.BeginGesture(); for(var i=0;i<20;i++) input.Pan(f,p,1,1); input.EndGesture(); True(input.Undo()); True(d.XAxis.Range==initial); True(!input.CanUndo);
            input.BeginGesture(); input.Pan(f,p,50,50); input.CancelGesture(); True(d.XAxis.Range==initial);
        });
        Add("rectangle zoom clamps to viewport and selects active pane",()=> {
            using var d=LineDocument(); var f=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d);
            input.ZoomRectangle(f,new(f.Layout.Plot.X,f.Layout.Plot.Y),new(f.Layout.Plot.X+f.Layout.Plot.Width/2,f.Layout.Plot.Bottom));
            Equal(2,d.XAxis.Range.Maximum); Equal(-2,d.YAxis.Range.Minimum); Equal(2,d.YAxis.Range.Maximum);
        });
        Add("cursor returns original samples and legend toggles model visibility",()=> {
            using var d=LineDocument(); d.ShowLegend=true; var f=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d);
            var screen=f.Layout.ToScreen(d.XAxis,d.YAxis,new(2,-1)); var hit=input.HitTestData(f,screen); True(hit!=null); Equal(2,hit!.Index); Equal(-1,hit.Data.Y);
            var legend=f.Metadata.Hits[0].Bounds; True(input.ToggleLegend(f,new(legend.X+2,legend.Y+2))); True(!d.Series[0].Visible);
        });
        Add("invalid zoom leaves both axes and history untouched",()=> {
            using var d=LineDocument(); var f=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d); var x=d.XAxis.Range; var y=d.YAxis.Range;
            Throws<ArgumentOutOfRangeException>(()=>input.Zoom(f,new(300,200),double.NaN)); True(d.XAxis.Range==x&&d.YAxis.Range==y&&!input.CanUndo);
        });
        RegisterSerializationTests();
        RegisterExampleTests();
    }
    static partial void RegisterSerializationTests();
    static partial void RegisterExampleTests();
}
