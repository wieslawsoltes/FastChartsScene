using FastChartsScene.Examples;
using FastChartsScene.Export;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Raster;

namespace PackageConsumer;

public static class ExportConsumer
{
    public static string Svg()
    {
        using var document=ExampleCatalog.Find("signals").Create();
        return SvgExport.Write(new SceneCompiler().Compile(document,800,600));
    }
    public static string Csv()
    {
        using var document=ExampleCatalog.Find("financial").Create();
        return ChartDataExport.WriteCsv(document);
    }
    public static RasterImage Raster()
    {
        using var document=ExampleCatalog.Find("surface").Create();
        return RasterFrameRenderer.Render(new SceneCompiler().Compile(document,800,600),320,240);
    }
}
