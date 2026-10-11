using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterInteractionReviewTests()
    {
        Add("obsolete linear-to-log undo cannot partially restore axes or move history stacks", () => {
            using var d=LineDocument(); var frame=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d);
            input.Zoom(frame,new(300,200),.5); True(input.CanUndo);
            d.YAxis.Range=new(1,10); d.YAxis.Scale=AxisScale.Logarithmic;
            var x=d.XAxis.Range; var y=d.YAxis.Range; var revision=d.Revision;
            True(!input.CanUndo); True(!input.Undo()); True(!input.CanRedo); True(d.XAxis.Range==x && d.YAxis.Range==y); Equal(revision,d.Revision);
        });
        Add("removed axes and changed view safely invalidate viewport history", () => {
            using var d=LineDocument(); var input=new ChartInteraction(d); var frame=new SceneCompiler().Compile(d,800,600);
            input.Pan(frame,new(300,200),40,20); d.YAxes[0]=new Axis("y") { Range=new(-5,5) }; var before=d.XAxis.Range;
            True(!input.Undo()); True(d.XAxis.Range==before); True(!input.CanRedo);
            input.Pan(frame,new(300,200),40,20); d.View=ChartView.Polar; True(!input.Undo());
        });
        Add("gesture cancellation respects external topology changes and disposal", () => {
            var d=LineDocument(); var frame=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d);
            input.BeginGesture(); input.Pan(frame,new(300,200),20,20); d.XAxis.Inverted=true; var range=d.XAxis.Range;
            input.CancelGesture(); True(d.XAxis.Range==range); True(!input.CanUndo);
            input.BeginGesture(); d.Dispose(); input.CancelGesture();
            Throws<ObjectDisposedException>(()=>input.Zoom(frame,new(300,200),.5));
        });
        Add("viewport history supports shared axis instances and keeps one entry per gesture", () => {
            using var d=LineDocument(); var shared=d.XAxis; d.YAxes.Clear(); d.YAxes.Add(shared); d.Series[0].YAxisId=shared.Id;
            var f=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d); var initial=shared.Range;
            input.BeginGesture(); for(var i=0;i<20;i++) input.Pan(f,new(300,200),2,1); input.EndGesture();
            True(input.Undo()); True(shared.Range==initial); True(!input.CanUndo); True(input.Redo());
            input.ClearHistory(); True(!input.CanRedo && !input.CanUndo);
        });
        Add("no-axis zoom and out-of-plot pan leave model and history unchanged", () => {
            using var d=LineDocument(); var f=new SceneCompiler().Compile(d,800,600); var input=new ChartInteraction(d); var revision=d.Revision;
            input.Zoom(f,new(300,200),.5,false,false); input.Pan(f,new(0,0),30,40);
            Equal(revision,d.Revision); True(!input.CanUndo);
        });
        Add("financial point views are allocation-free stable snapshots", () => {
            var series=new FinancialSeries(); series.SetData([new(1,2,3,1,2)]); var old=series.Points;
            var before=GC.GetAllocatedBytesForCurrentThread(); double checksum=0;
            for(var i=0;i<10000;i++) checksum+=series.Points[0].Close;
            Equal(0,GC.GetAllocatedBytesForCurrentThread()-before); Equal(20000,checksum);
            True(ReferenceEquals(old,series.Points)); series.SetData([new(2,3,4,2,3)]); Equal(1,old[0].X); Equal(2,series.Points[0].X);
        });
        Add("invalid series enums and empty axis bindings fail before model mutation", () => {
            var line=new LineSeries(); var finance=new FinancialSeries();
            Throws<ArgumentOutOfRangeException>(()=>line.Marker=(MarkerShape)999);
            Throws<ArgumentOutOfRangeException>(()=>line.Interpolation=(LineInterpolation)999);
            Throws<ArgumentOutOfRangeException>(()=>finance.Style=(FinancialStyle)999);
            Throws<ArgumentException>(()=>line.XAxisId=" "); Throws<ArgumentNullException>(()=>line.YAxisId=null!);
        });
    }
}
