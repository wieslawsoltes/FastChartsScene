using System.IO.Compression;
using System.Text.Json.Nodes;
using FastChartsScene.Charts;
using FastChartsScene.Serialization;
using FastChartsScene.Export;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterFinancialXPersistenceTests()
    {
        Add("financial X mode persists exactly in saved and extracted charts", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12, 12345));
            var series = (FinancialSeries)d.Series[0]; series.CursorSnapMode = FinancialCursorSnapMode.NearestX;
            var text = ChartJson.Write(d); using var copy = ChartJson.Read(text); True(ChartJson.Write(copy) == text);
            True(((FinancialSeries)copy.Series[0]).CursorSnapMode == FinancialCursorSnapMode.NearestX);
            using var bytes = new MemoryStream(ChartProjectExport.Write(d)); using var zip = new ZipArchive(bytes);
            using var reader = new StreamReader(zip.GetEntry("chart.json")!.Open()); True(reader.ReadToEnd() == text);
        });
        Add("financial X saved mode keeps legacy default and rejects malformed options atomically", () => {
            using var d = FinancialDocument(new OhlcPoint(4, 10, 16, 8, 12)); var text = ChartJson.Write(d);
            var json = JsonNode.Parse(text)!; var s = json["series"]![0]!.AsObject(); s.Remove("cursorSnapMode");
            using var old = ChartJson.Read(json.ToJsonString()); True(((FinancialSeries)old.Series[0]).CursorSnapMode == FinancialCursorSnapMode.NearestPoint);
            foreach (var value in new JsonNode?[] { JsonValue.Create("unknown"), JsonValue.Create("nearestx"), JsonValue.Create(1), JsonValue.Create(true), null })
            { s["cursorSnapMode"] = value; Throws<FormatException>(() => ChartJson.Read(json.ToJsonString())); True(ChartJson.Write(d) == text); }
        });
    }
}
