using FastChartsScene.Core;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAxisTickPersistenceTests();
    static partial void RegisterAxisTickTests()
    {
        RegisterAxisTickPersistenceTests();
        Add("custom numeric formats support fixed grouped scaled scientific and sign sections", () => {
            foreach (var format in new[] { "0.00", "#,##0.00", "0.0E+00", "0.##' mV'", "0.00%;(0.00%);'zero'", "0,,.00' M'" })
            {
                var axis = new Axis("custom") { TickOptions = new(numericFormat: format) };
                foreach (var value in new[] { 0d, 1.25, -1234.5, 12000000d, double.Epsilon, double.MaxValue })
                    True(axis.Format(value) == value.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
            }
            var fixedAxis = new Axis("fixed") { TickOptions = new(numericFormat: "0.00") };
            True(fixedAxis.Format(1.25) == "1.25" && fixedAxis.Format(-2) == "-2.00");
        });
        Add("custom format validation never treats excessive standard precision as custom", () => {
            var axis = new Axis("x"); var original = axis.TickOptions;
            foreach (var format in new[] { "F999999999", "G999999999", "P999999999", "R17", "X2", "0.00\n", "0.00\u2028", new string('0', 65) })
                Throws<ArgumentException>(() => axis.TickOptions = new(numericFormat: format));
            True(ReferenceEquals(original, axis.TickOptions));
        });
        Add("custom numeric formats flow through automatic labels and null custom labels", () => {
            var axis = new Axis("x") { Range = new(0, 10), TickOptions = new(numericFormat: "0.00") };
            True(axis.Ticks().All(t => t.Label == t.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            axis.TickOptions = new(AxisTickMode.Custom, [new(0), new(5, "explicit"), new(10, "")], "0.000");
            var ticks = axis.Ticks(); True(ticks[0].Label == "0.000" && ticks[1].Label == "explicit" && ticks[2].Label == "");
        });
        Add("tick labels and date formats reject Unicode line and paragraph separators", () => {
            foreach(var separator in new[]{'\u2028','\u2029'})
            {
                Throws<ArgumentException>(()=>new AxisTickOptions(customTicks:[new(0,"left"+separator+"right")]));
                Throws<ArgumentException>(()=>new AxisTickOptions(dateTimeFormat:"yyyy"+separator+"MM"));
            }
        });
        Add("custom ticks are owned sorted and exactly clipped before log projection", () => {
            CustomAxisTick[] ticks = [new(100,"high"), new(-1,"outside"), new(1,"low"), new(10)];
            var options = new AxisTickOptions(AxisTickMode.Custom,ticks,"F1"); ticks[0]=new(99,"mutated");
            var a=new Axis("x") { Range=new(1,100), Scale=AxisScale.Logarithmic, TickOptions=options };
            var result=a.Ticks(); Equal(3,result.Count); Equal(1,result[0].Value); Equal(100,result[2].Value);
            True(result[0].Label=="low" && result[1].Label=="10.0" && result[2].Label=="high"); Equal(.5,result[1].Unit);
            a.Inverted=true; var inverse=a.Ticks(); Equal(1,inverse[0].Unit); Equal(0,inverse[2].Unit);
            a.Range=new(2,50); Equal(1,a.Ticks().Count); Equal(4,options.CustomTicks.Count);
            Throws<NotSupportedException>(()=>((IList<CustomAxisTick>)options.CustomTicks)[0]=new(4));
        });
        Add("automatic tick cache returns immutable zero-allocation results and correct invalidation", () => {
            var a=new Axis("x");var ticks=a.Ticks();
            var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)a.Ticks();Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
            a.Title="new";a.Units="V";a.Side=AxisSide.Far;a.Weight=2;True(ReferenceEquals(ticks,a.Ticks()));Equal(1,a.TickGenerationCount);
            a.Range=new(1,20);True(!ReferenceEquals(ticks,a.Ticks()));Equal(2,a.TickGenerationCount);
            a.Scale=AxisScale.Logarithmic;a.Ticks();Equal(3,a.TickGenerationCount);
            a.Inverted=true;a.Ticks();Equal(4,a.TickGenerationCount);
            a.TickOptions=new(numericFormat:"E2");a.Ticks();Equal(5,a.TickGenerationCount);
            a.Ticks(9);Equal(6,a.TickGenerationCount);
            Throws<NotSupportedException>(()=>((IList<AxisTick>)ticks)[0]=default);
        });
        Add("custom cache ignores automatic density and empty labels are deliberate", () => {
            var a=new Axis("x") { TickOptions=new(AxisTickMode.Custom,[new(0,""),new(5),new(10,"end")]) };
            var ticks=a.Ticks(2);True(ReferenceEquals(ticks,a.Ticks(40)));True(ticks[0].Label=="");True(ticks[1].Label=="5");
            a.TickOptions=new(AxisTickMode.Custom);Equal(0,a.Ticks().Count);
        });
        Add("tick formatting is invariant and validates before mutation", () => {
            var a=new Axis("x");var original=a.TickOptions;
            foreach(var format in new[]{"F999999999","G18","Q","F-1","F 2","R4"}) Throws<ArgumentException>(()=>a.TickOptions=new(numericFormat:format));
            True(ReferenceEquals(original,a.TickOptions));
            a.TickOptions=new(numericFormat:"F3");True(a.Format(1.25)=="1.250");
            a.TickOptions=new(numericFormat:"R");True(a.Format(Math.PI)==Math.PI.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            a.Scale=AxisScale.DateTime;a.TickOptions=new(dateTimeFormat:"yyyy-MM-dd");True(a.Format(0)=="1970-01-01");
            Throws<ArgumentException>(()=>new AxisTickOptions(dateTimeFormat:"%"));
            Throws<ArgumentException>(()=>new AxisTickOptions(dateTimeFormat:new string('y',65)));
        });
        Add("tick settings reject invalid values duplicates control text and excess count atomically", () => {
            Throws<ArgumentException>(()=>new AxisTickOptions(customTicks:[new(double.NaN)]));
            Throws<ArgumentException>(()=>new AxisTickOptions(customTicks:[new(1),new(1)]));
            Throws<ArgumentException>(()=>new AxisTickOptions(customTicks:[new(0,"multi\nline")]));
            Throws<ArgumentException>(()=>new AxisTickOptions(customTicks:[new(0,new string('x',257))]));
            Throws<ArgumentException>(()=>new AxisTickOptions(customTicks:Enumerable.Range(0,513).Select(i=>new CustomAxisTick(i))));
            Throws<ArgumentOutOfRangeException>(()=>new AxisTickOptions((AxisTickMode)999));
            var o=AxisTickOptions.Default;True(ReferenceEquals(o,o.WithMode(o.Mode)));True(ReferenceEquals(o,o.WithFormats("","")));
        });
        Add("custom tick text roundtrips labels including null empty whitespace unicode and pipes", () => {
            CustomAxisTick[] ticks=[new(-1,"a|b"),new(0,""),new(1),new(2," Ω ")];
            var text=AxisTickText.Write(ticks);
            foreach(var newline in new[]{"\n","\r","\r\n"})True(ticks.SequenceEqual(AxisTickText.Parse(text.Replace("\n",newline))));
            Equal(0,AxisTickText.Parse(" \r\n ").Count);
            foreach(var invalid in new[]{"NaN|no","Infinity","1|one\n1|two","bogus",new string('x',AxisTickText.MaximumTextLength+1)})
                Throws<FormatException>(()=>AxisTickText.Parse(invalid));
        });
        Add("category labels invalidate ticks without leaking mutable cached arrays", () => {
            var a=new Axis("x") { Scale=AxisScale.Category,Range=new(0,2),Categories=["A","B","C"] };
            var old=a.Ticks();a.Categories=["X","Y","Z"];var current=a.Ticks();
            True(old[0].Label=="A" && current[0].Label=="X");Equal(2,a.TickGenerationCount);
        });
        Add("log normalization is refreshed before synchronous model observers run", () => {
            var a=new Axis("x") { Range=new(1,100),Scale=AxisScale.Logarithmic };var observed=0;
            a.PropertyChanged+=(_,_)=> { Equal(0,a.ToUnit(a.Range.Minimum));Equal(1,a.ToUnit(a.Range.Maximum));observed++; };
            a.Range=new(1,1000);Equal(1.0/3,a.ToUnit(10));a.Scale=AxisScale.Linear;Equal(0,a.ToUnit(1));
            a.Range=new(1,10);a.Scale=AxisScale.Logarithmic;Equal(4,observed);
        });
        Add("custom ticks in narrow extreme ranges preserve endpoint identity", () => {
            var min=1e300;var max=Math.BitIncrement(min);
            var a=new Axis("x") { Range=new(min,max),Scale=AxisScale.Logarithmic,TickOptions=new(AxisTickMode.Custom,[new(min,"min"),new(max,"max")]) };
            Equal(0,a.Ticks()[0].Unit);Equal(1,a.Ticks()[1].Unit);Equal(min,a.FromUnit(0));Equal(max,a.FromUnit(1));
        });
    }
}
