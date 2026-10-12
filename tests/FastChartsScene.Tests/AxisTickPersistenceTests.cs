using System.Text.Json.Nodes;
using FastChartsScene.Core;
using FastChartsScene.Rendering;
using FastChartsScene.Serialization;
using FastChartsScene.Export;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterAxisTickPersistenceTests()
    {
        Add("custom tick JSON preserves labels formats and compiled scene across all axis scales", () => {
            foreach(var scale in Enum.GetValues<AxisScale>())
            {
                using var d=LineDocument();d.XAxis.Range=new(1,100);d.XAxis.Scale=scale;
                d.XAxis.TickOptions=new(AxisTickMode.Custom,[new(1,"<Start & end>"),new(10),new(100,"")],"F2","yyyy-MM-dd");
                var json=ChartJson.Write(d);using var loaded=ChartJson.Read(json);True(ChartJson.Write(loaded)==json);
                True(d.XAxis.TickOptions.CustomTicks.SequenceEqual(loaded.XAxis.TickOptions.CustomTicks));
                var compiler=new SceneCompiler();var a=compiler.Compile(d,960,600);var b=compiler.Compile(loaded,960,600);
                True(a.Metadata.Labels.SequenceEqual(b.Metadata.Labels));True(a.Vertices.SequenceEqual(b.Vertices));
                True(a.Metadata.Labels.Any(l=>l.Text=="<Start & end>"));
                True(SvgExport.Write(a).Contains("&lt;Start &amp; end&gt;"));
            }
        });
        Add("legacy documents without tick settings retain original automatic ticks", () => {
            using var d=LineDocument();var node=JsonNode.Parse(ChartJson.Write(d))!;
            foreach(var name in new[]{"xAxes","yAxes"})foreach(var axis in node[name]!.AsArray())axis!.AsObject().Remove("tickOptions");
            node["zAxis"]!.AsObject().Remove("tickOptions");using var loaded=ChartJson.Read(node.ToJsonString());
            True(ReferenceEquals(AxisTickOptions.Default,loaded.XAxis.TickOptions));True(d.XAxis.Ticks().SequenceEqual(loaded.XAxis.Ticks()));
        });
        Add("malformed tick options reject invalid labels duplicate coordinates enums and precision", () => {
            using var d=LineDocument();d.XAxis.TickOptions=new(AxisTickMode.Custom,[new(1,"one")]);
            var json=ChartJson.Write(d);
            foreach(var replacement in new[]{"{\"mode\":\"Custom\",\"numericFormat\":\"\",\"dateTimeFormat\":\"\",\"ticks\":[{\"value\":1,\"label\":\"a\"},{\"value\":1,\"label\":\"b\"}]}",
                "{\"mode\":\"Unknown\",\"numericFormat\":\"\",\"dateTimeFormat\":\"\",\"ticks\":[]}",
                "{\"mode\":\"Custom\",\"numericFormat\":\"F999999999\",\"dateTimeFormat\":\"\",\"ticks\":[]}",
                "{\"mode\":\"Custom\",\"numericFormat\":\"\",\"dateTimeFormat\":\"\",\"ticks\":[{\"value\":1,\"label\":false}]}"})
            {
                var node=JsonNode.Parse(json)!;node["xAxes"]![0]!["tickOptions"]=JsonNode.Parse(replacement);
                Throws<FormatException>(()=>ChartJson.Read(node.ToJsonString()));True(ChartJson.Write(d)==json);
            }
            var large=JsonNode.Parse(json)!;large["xAxes"]![0]!["tickOptions"]!["ticks"]![0]!["label"]=new string('x',257);
            Throws<FormatException>(()=>ChartJson.Read(large.ToJsonString()));
        });
        Add("custom tick numbers consume the shared import budget", () => {
            using var d=LineDocument();var original=ChartJson.Write(d);ChartJson.Read(original,new(MaximumNumericValues:100)).Dispose();
            d.XAxis.TickOptions=new(AxisTickMode.Custom,Enumerable.Range(0,100).Select(i=>new CustomAxisTick(i)));
            Throws<FormatException>(()=>ChartJson.Read(ChartJson.Write(d),new(MaximumNumericValues:100)));
        });
        Add("scene recompilation for data changes retains unchanged axis tick lists", () => {
            using var d=LineDocument();var compiler=new SceneCompiler();compiler.Compile(d,900,600);
            var x=d.XAxis.TickGenerationCount;var y=d.YAxis.TickGenerationCount;
            for(var i=0;i<24;i++){d.Title="Update "+i;compiler.Compile(d,900,600);}
            Equal(x,d.XAxis.TickGenerationCount);Equal(y,d.YAxis.TickGenerationCount);
        });
    }
}
