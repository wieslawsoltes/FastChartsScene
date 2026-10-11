using System.Xml.Linq;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Examples;
using FastChartsScene.Export;
using FastChartsScene.Rendering;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterExportTests()
    {
        Add("SVG exports actual triangles, clip paths and escaped text",()=> {
            using var d=LineDocument(); d.Title="<script>alert(1)</script> & chart"; var f=new SceneCompiler().Compile(d,800,600);
            var xml=XDocument.Parse(SvgExport.Write(f)); XNamespace ns="http://www.w3.org/2000/svg";
            True(xml.Root!.Name==ns+"svg"); True(xml.Descendants(ns+"title").Single().Value==d.Title);
            Equal(f.Metadata.VertexCount/3,xml.Descendants(ns+"polygon").Count()); Equal(f.Metadata.Batches.Length,xml.Descendants(ns+"clipPath").Count());
            True(!xml.Descendants(ns+"script").Any());
        });
        Add("SVG rejects depth-tested output rather than flattening incorrectly",()=> {
            using var d=ExampleCatalog.Find("surface").Create(); var f=new SceneCompiler().Compile(d,800,600);
            Throws<NotSupportedException>(()=>SvgExport.Write(f));
        });
        Add("CSV exports all series including gaps and hidden data by default",()=> {
            using var d=LineDocument([0,double.NaN,2,3,4]); d.Series.Add(new LineSeries { Title="second",Data=new UniformData(1,1,[5,6]),Visible=false });
            var csv=ChartDataExport.WriteCsv(d); Equal(9,csv.Split("\r\n").Length); True(csv.Contains("\"second\"")); True(!csv.Contains("NaN"));
            True(!ChartDataExport.WriteCsv(d,false).Contains("\"second\""));
        });
        Add("CSV neutralizes spreadsheet formula text and quotes delimiters",()=> {
            using var d=LineDocument(); d.Series[0].Title=" =SUM(1,2)\""; var csv=ChartDataExport.WriteCsv(d);
            True(csv.Contains("\"' =SUM(1,2)\"\"\""));
        });
        Add("CSV preserves financial fields, grid values, meshes and pie labels",()=> {
            foreach(var id in new[]{"financial","heatmap","mesh","pie"})
            {
                using var d=ExampleCatalog.Find(id).Create(); var csv=ChartDataExport.WriteCsv(d); True(csv.StartsWith(ChartDataExport.Header));
                var kind=id switch { "financial"=>"financial","heatmap"=>"grid","mesh"=>"mesh-triangle",_=>"pie" }; True(csv.Contains(","+kind+","));
            }
        });
        Add("SVG and CSV streaming writers remain caller-owned",()=> {
            using var d=LineDocument(); using var output=new StringWriter(); ChartDataExport.WriteCsv(d,output); output.Write("more");
            using var svg=new StringWriter(); SvgExport.Write(new SceneCompiler().Compile(d,800,600),svg); svg.Write("more"); True(svg.ToString().EndsWith("more"));
        });
    }
}
