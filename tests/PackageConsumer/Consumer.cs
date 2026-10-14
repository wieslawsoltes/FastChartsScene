using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Examples;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Skia;
using FastChartsScene.Serialization;
using FastChartsScene.SignalProcessing;

namespace FastChartsScene.PackageConsumer;

/// <summary>Compiled using only produced NuGet packages: no project references or source inclusion.</summary>
public static class Consumer
{
    public static ChartControl CreateControl()
    {
        var document=ExampleCatalog.Find("signals").Create();
        return new ChartControl { Document=document,RenderingMode=ChartRenderingMode.Auto };
    }
    public static byte[] RenderPng()
    {
        using var document=new ChartDocument();
        document.XAxis.Range=new AxisRange(0,3);
        document.Series.Add(new LineSeries { Data=new UniformData(0,1,[0,1,-1,0]) });
        using var copy=ChartJson.Read(ChartJson.Write(document));
        using var renderer=new SkiaFrameRenderer();
        var compiler = new SceneCompiler { TextMeasurer = renderer };
        var frame=compiler.Compile(copy,640,480);
        _ = frame.Layout.AxisLabels; _ = compiler.TextMeasurementCount;
        var input=new ChartInteraction(copy); input.Zoom(frame,new PointD(300,200),.8); input.Undo();
        _=Fourier.Spectrum(new double[64],1024);
        return renderer.ExportPng(frame);
    }
}
