using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Rendering;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAxisImprovementTests()
    {
        Add("category replacement invalidates cached frames and owns both arrays",()=> {
            using var d=LineDocument(); d.XAxis.Scale=AxisScale.Category; string[] names=["A","B","C","D","E"]; d.XAxis.Categories=names;
            names[0]="wrong"; d.XAxis.Categories[1]="wrong"; True(d.XAxis.Format(0)=="A"&&d.XAxis.Format(1)=="B");
            var compiler=new SceneCompiler(); var first=compiler.Compile(d,800,600); d.XAxis.Categories=["P","Q","R","S","T"];
            var second=compiler.Compile(d,800,600); True(!ReferenceEquals(first,second)); True(second.Metadata.Labels.Any(l=>l.Text=="P"));
        });
        Add("date ticks align to calendar month boundaries across leap years",()=> {
            static double Time(int year,int month,int day)=>new DateTimeOffset(year,month,day,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds();
            var axis=new Axis("time") { Range=new(Time(2024,1,15),Time(2024,6,15)),Scale=AxisScale.DateTime };
            var ticks=axis.Ticks(6); True(ticks.Count>=4); True(ticks.All(t=>DateTimeOffset.UnixEpoch.AddSeconds(t.Value).Day==1));
            True(ticks.Any(t=>DateTimeOffset.UnixEpoch.AddSeconds(t.Value).Month==2));
        });
        Add("date time-of-day intervals align to whole minutes",()=> {
            var axis=new Axis("time") { Range=new(17,700),Scale=AxisScale.DateTime };
            var ticks=axis.Ticks(7); True(ticks.Count>=3); True(ticks.All(t=>t.Value%60==0));
        });
        Add("log transform remains defined for adjacent large floating-point values",()=> {
            const double first=1e100; var last=first; for(var i=0;i<8;i++) last=Math.BitIncrement(last);
            var axis=new Axis("log") { Range=new(first,last),Scale=AxisScale.Logarithmic };
            Equal(0,axis.ToUnit(first)); Equal(1,axis.ToUnit(last)); True(double.IsFinite(axis.ToUnit(Math.BitIncrement(first))));
            Equal(first,axis.FromUnit(0),0); Equal(last,axis.FromUnit(1),0);
        });
        Add("finite extreme constant fit and subnormal ticks do not overflow",()=> {
            var positive=AxisRange.FromData(double.MaxValue,double.MaxValue); True(positive.Maximum==double.MaxValue&&positive.Span>0);
            var negative=AxisRange.FromData(-double.MaxValue,-double.MaxValue); True(negative.Minimum==-double.MaxValue&&negative.Span>0);
            var axis=new Axis("tiny") { Range=new(0,double.Epsilon*4) }; True(axis.Ticks().All(t=>double.IsFinite(t.Value)&&double.IsFinite(t.Unit)));
        });
        Add("axes reject invalid enum values and null categories",()=> {
            var a=new Axis("x"); Throws<ArgumentOutOfRangeException>(()=>a.Scale=(AxisScale)100); Throws<ArgumentOutOfRangeException>(()=>a.Side=(AxisSide)10);
            Throws<ArgumentNullException>(()=>a.Categories=null!); Throws<ArgumentException>(()=>a.Categories=[null!]);
        });
    }
}
