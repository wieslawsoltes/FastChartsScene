using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Serialization;

namespace FastChartsScene.PackageConsumer;
public static class FinancialCursorConsumer
{
    public static ChartControl Create(out IDataSource immutablePriceSnapshot)
    {
        var series = new FinancialSeries { CursorField = FinancialValueField.High, CursorTrackEnabled = true };
        series.SetData([new(4, 10, 16, 8, 12, 12345)]);
        immutablePriceSnapshot = series.GetPriceData(FinancialValueField.Close);
        var document = new ChartDocument { ShowLegend = false }; document.Series.Add(series); document.AutoFit();
        var frame = new SceneCompiler().Compile(document, 800, 600);
        var input = new ChartInteraction(document);
        FinancialDataHit? hit = input.HitTestFinancial(frame, frame.Layout.ToScreen(document.XAxis, document.YAxis, new PointD(4, 16)));
        if (hit?.Sample.Volume != 12345) throw new InvalidOperationException("Original volume was not available.");
        var control = new ChartControl { Document = ChartJson.Read(ChartJson.Write(document)) };
        control.FinancialHitChanged += (_, _) => { _ = control.FinancialHit; _ = control.FinancialCursorText; _ = control.FinancialCursorBounds; };
        document.Dispose(); return control;
    }
}
