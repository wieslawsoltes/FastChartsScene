using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Examples;
using FastChartsScene.Serialization;
using FastChartsScene.Rendering;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterSerializationTests()
    {
        foreach(var example in ExampleCatalog.All)
            Add("document JSON roundtrip: "+example.Id,()=> {
                using var original=example.Create(); var json=ChartJson.Write(original); using var loaded=ChartJson.Read(json);
                True(json==ChartJson.Write(loaded),"Canonical JSON changed after roundtrip.");
                var compiler=new SceneCompiler(); var a=compiler.Compile(original,900,600); var b=compiler.Compile(loaded,900,600);
                True(a.Vertices.SequenceEqual(b.Vertices),"Roundtrip changed chart geometry."); Equal(original.TotalPoints,loaded.TotalPoints);
            });
        Add("JSON preserves double samples, NaN gaps, annotations and unquantized colors",()=> {
            using var d=LineDocument([double.Epsilon,Math.PI,double.NaN,1.23456789012345,-1]);
            d.Series[0].Color=new(.1234567f,.7654321f,.2345678f,.456789f); d.Annotations.Add(new() { Text="α / \"quoted\" / <script>",X=1,Y=2,Color=new(.13f,.27f,.39f) });
            using var loaded=ChartJson.Read(ChartJson.Write(d)); True(d.Series[0].Color==loaded.Series[0].Color); True(loaded.Annotations[0].Text==d.Annotations[0].Text);
            True(double.IsNaN(((DataSeries)loaded.Series[0]).Data.Y(2))); Equal(double.Epsilon,((DataSeries)loaded.Series[0]).Data.Y(0),0);
        });
        Add("JSON rejects versions, duplicate keys and unknown series without activating types",()=> {
            using var d=LineDocument(); var json=ChartJson.Write(d);
            Throws<FormatException>(()=>ChartJson.Read(json.Replace("\"version\":1","\"version\":100")));
            Throws<FormatException>(()=>ChartJson.Read(json.Replace("\"kind\":\"line\"","\"kind\":\"System.Object\"")));
            Throws<FormatException>(()=>ChartJson.Read(json.Replace("\"version\":1","\"version\":1,\"version\":1")));
            Throws<FormatException>(()=>ChartJson.Read(json.Replace("\"xAxis\":\"x\"","\"xAxis\":\"missing\"")));
        });
        Add("JSON enforces input and numeric budgets before returning documents",()=> {
            using var d=LineDocument(); var json=ChartJson.Write(d);
            Throws<FormatException>(()=>ChartJson.Read(json,new(MaximumCharacters:20)));
            Throws<FormatException>(()=>ChartJson.Read(json,new(MaximumNumericValues:10)));
            Throws<ArgumentOutOfRangeException>(()=>ChartJson.Read(json,new(MaximumSeries:0)));
        });
        Add("JSON rejects nonfinite data, negative colors and numeric enum strings",()=> {
            using var d=LineDocument(); var json=ChartJson.Write(d);
            Throws<FormatException>(()=>ChartJson.Read(json.Replace("\"strokeWidth\":1.5","\"strokeWidth\":1e999")));
            Throws<FormatException>(()=>ChartJson.Read(json.Replace("\"view\":\"XY\"","\"view\":\"0\"")));
            d.Series[0].Color=new(-1,0,0); Throws<InvalidOperationException>(()=>ChartJson.Write(d));
        });
    }
}
