using System.Globalization;
using FastChartsScene.Core;
using FastChartsScene.Rendering;
using FastChartsScene.Serialization;
using FastChartsScene.Export;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAutomaticLabelTests()
    {
        Add("automatic numeric labels distinguish narrow large and small finite coordinates", () => {
            foreach (var min in new[] { 1e-300, 1e-20, 1.0, 1e12, 1e100, 1e300 })
            foreach (var scale in new[] { AxisScale.Linear, AxisScale.Logarithmic })
            {
                var max = min; for (var i = 0; i < 16; i++) max = Math.BitIncrement(max);
                var axis = new Axis("x") { Range = new(min,max), Scale = scale };
                var ticks = axis.Ticks(); CheckTicks(axis,ticks);
                foreach (var tick in ticks)
                {
                    var parsed = double.Parse(tick.Label,CultureInfo.InvariantCulture);
                    True(parsed >= min && parsed <= max, "An adaptive label rounded outside the narrow range.");
                }
                axis.Inverted = true; var inverted = axis.Ticks(); CheckTicks(axis,inverted);
                True(ticks.Select(t => t.Label).SequenceEqual(inverted.Select(t => t.Label)));
            }
        });
        Add("adjacent subnormal and extreme numeric ranges retain two distinct endpoints", () => {
            foreach (var (min,max) in new[] { (0.0,double.Epsilon), (double.Epsilon,2*double.Epsilon),
                (-double.Epsilon,0.0), (Math.BitDecrement(double.MaxValue),double.MaxValue),
                (-double.MaxValue,Math.BitIncrement(-double.MaxValue)) })
            {
                var a = new Axis("x") { Range = new(min,max) }; var ticks = a.Ticks(); CheckTicks(a,ticks);
                Equal(2,ticks.Count); True(ticks[0].Value == min && ticks[^1].Value == max);
                Equal(0,ticks[0].Unit); Equal(1,ticks[^1].Unit);
            }
        });
        Add("automatic tick clipping rejects just-outside rounding without losing range coverage", () => {
            var a = new Axis("x") { Range = new(Math.BitIncrement(1.0),Math.BitDecrement(2.0)) };
            CheckTicks(a,a.Ticks());
        });
        Add("automatic calendar labels include enough detail for distinct month divisions", () => {
            var start = new DateTimeOffset(2024,1,1,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds();
            var end = new DateTimeOffset(2027,1,1,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds();
            var a = new Axis("time") { Range = new(start,end), Scale = AxisScale.DateTime };
            var ticks = a.Ticks(15); CheckTicks(a,ticks);
            True(ticks.All(t => DateTimeOffset.UnixEpoch.AddSeconds(t.Value).Day == 1));
            True(ticks.Any(t => t.Label == "2024-04"));
        });
        Add("explicit repeated formats category names and custom text are not rewritten", () => {
            var a = new Axis("x") { Range = new(1,1.0001), TickOptions = new(numericFormat:"0") };
            True(a.Ticks().All(t => t.Label == "1"));
            a.TickOptions = new(AxisTickMode.Custom,[new(1,"same"),new(1.0001,"same")]);
            True(a.Ticks().All(t => t.Label == "same"));
            a.Scale = AxisScale.Category; a.Range = new(0,2); a.TickOptions = AxisTickOptions.Default; a.Categories = ["same","same","same"];
            True(a.Ticks().All(t => t.Label == "same"));
            a.Scale = AxisScale.DateTime; a.Range = new(0,86400*365*3); a.TickOptions = new(dateTimeFormat:"yyyy");
            True(a.Ticks(20).Select(t=>t.Label).Distinct().Count()<a.Ticks(20).Count);
        });
        Add("two-size tick caching avoids density thrashing and releases obsolete variants", () => {
            var a = new Axis("x"); var small = a.Ticks(4); var large = a.Ticks(12);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i=0;i<2000;i++) { a.Ticks(4); a.Ticks(12); }
            Equal(0,GC.GetAllocatedBytesForCurrentThread()-before); Equal(2,a.TickGenerationCount);
            True(ReferenceEquals(small,a.Ticks(4))); True(ReferenceEquals(large,a.Ticks(12)));
            a.Range = new(20,30); True(!ReferenceEquals(small,a.Ticks(4))); True(!ReferenceEquals(large,a.Ticks(12)));
            Equal(4,a.TickGenerationCount); True(small[0].Value<20);
        });
        Add("automatic correction preserves ordinary default axis labels", () => {
            var a = new Axis("x") { Range = new(0,10) };
            True(a.Ticks().Select(t=>t.Label).SequenceEqual(new[]{"0","2","4","6","8","10"}));
            True(a.Ticks().Select(t=>t.Value).SequenceEqual(new double[]{0,2,4,6,8,10}));
        });
        Add("automatic precision propagates through scene and saved SVG without changing data", () => {
            using var d=LineDocument(); d.ShowLegend=false; d.XAxis.Range=new(1e12,1e12+1);
            var original=ChartJson.Write(d); var f=new SceneCompiler().Compile(d,1200,600);
            var ticks=d.XAxis.Ticks(Math.Clamp((int)(f.Layout.Plot.Width/100),2,15)); CheckTicks(d.XAxis,ticks);
            var svg=SvgExport.Write(f);
            foreach(var t in ticks) { True(f.Metadata.Labels.Any(l=>l.Text==t.Label)); True(svg.Contains(">"+t.Label+"</text>")); }
            True(ChartJson.Write(d)==original);
        });
        Add("seeded automatic tick property checks cover exponent and ULP neighborhoods", () => {
            var random=new Random(18701);
            for(var i=0;i<500;i++)
            {
                var min=Math.ScaleB(1+random.NextDouble(),random.Next(-1020,1020));
                var max=min; for(var j=0;j<random.Next(1,65);j++)max=Math.BitIncrement(max);
                if(i%3==0)(min,max)=(-max,-min);
                var a=new Axis("x") { Range=new(min,max),Inverted=i%2==0 };
                CheckTicks(a,a.Ticks(random.Next(2,41)));
            }
        });
    }
    private static void CheckTicks(Axis a,IReadOnlyList<AxisTick> ticks)
    {
        True(ticks.Count>=2 && ticks.Count<=100,"Expected bounded nonempty automatic divisions.");
        True(ticks.All(t=>double.IsFinite(t.Value)&&double.IsFinite(t.Unit)&&t.Value>=a.Range.Minimum&&t.Value<=a.Range.Maximum&&t.Unit>=0&&t.Unit<=1));
        True(ticks.Zip(ticks.Skip(1)).All(p=>p.First.Value<p.Second.Value));
        Equal(ticks.Count,ticks.Select(t=>t.Label).Distinct(StringComparer.Ordinal).Count());
    }
}
