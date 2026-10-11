using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FastChartsScene.Examples;
using FastChartsScene.Export;
using FastChartsScene.Serialization;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterProjectExportTests()
    {
        Add("project extraction includes exact edited chart and standalone host files", () => {
            using var d=ExampleCatalog.Find("surface").Create();d.Title="Edited <>& chart";d.Camera.Azimuth=137;
            var source=ChartJson.Write(d);using var bytes=new MemoryStream(ChartProjectExport.Write(d,"MyChart"));using var zip=new ZipArchive(bytes);
            Equal(12,zip.Entries.Count);True(zip.GetEntry("MyChart.csproj")!=null);
            using var reader=new StreamReader(zip.GetEntry("chart.json")!.Open());True(source==reader.ReadToEnd());
            foreach(var name in new[]{"MyChart.csproj","Directory.Build.props","NuGet.config","App.xaml"})
            { using var file=new StreamReader(zip.GetEntry(name)!.Open());XDocument.Parse(file.ReadToEnd()); }
            True(zip.Entries.All(e=>!e.FullName.StartsWith('/')&&!e.FullName.Split('/').Contains("..")));
            using var app=new StreamReader(zip.GetEntry("App.xaml.cs")!.Open());True(!app.ReadToEnd().Contains(d.Title));
        });
        Add("project archive is deterministic and independent of subsequent model changes", () => {
            using var d=LineDocument();var first=ChartProjectExport.Write(d);var second=ChartProjectExport.Write(d);True(first.SequenceEqual(second));
            d.Title="modified";True(!first.SequenceEqual(ChartProjectExport.Write(d)));
        });
        Add("project extraction rejects invalid names and disposed charts before output", () => {
            using var d=LineDocument();
            foreach(var name in new[]{"../bad","a/b","x.csproj","x\"y","9chart","a b","",new string('a',65)})
            { using var stream=new MemoryStream();Throws<ArgumentException>(()=>ChartProjectExport.Write(d,stream,name));Equal(0,stream.Length); }
            d.Dispose();using var output=new MemoryStream();Throws<ObjectDisposedException>(()=>ChartProjectExport.Write(d,output));Equal(0,output.Length);
        });
        Add("project extraction leaves caller stream open and stores data not executable strings", () => {
            using var d=LineDocument();d.Title="\"; throw new Exception(); //";using var output=new MemoryStream();ChartProjectExport.Write(d,output);True(output.CanWrite);
            output.Position=0;using(var zip=new ZipArchive(output,ZipArchiveMode.Read,true))
            {
                using var reader=new StreamReader(zip.GetEntry("chart.json")!.Open());using var loaded=ChartJson.Read(reader.ReadToEnd());True(loaded.Title==d.Title);
                using var readme=new StreamReader(zip.GetEntry("README.md")!.Open());True(readme.ReadToEnd().Contains("source-only extraction"));
            }
            output.WriteByte(0);
        });
        Add("extracted charts cover each current model family", () => {
            foreach(var example in ExampleCatalog.All)
            {
                using var d=example.Create();using var output=new MemoryStream(ChartProjectExport.Write(d));using var zip=new ZipArchive(output);
                using var reader=new StreamReader(zip.GetEntry("chart.json")!.Open());using var loaded=ChartJson.Read(reader.ReadToEnd());True(ChartJson.Write(d)==ChartJson.Write(loaded));
            }
        });
    }
}
