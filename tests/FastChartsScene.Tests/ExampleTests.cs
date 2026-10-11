using FastChartsScene.Examples;
using FastChartsScene.Rendering;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterExampleTests()
    {
        foreach(var example in ExampleCatalog.All)
            Add("example compiles: "+example.Id,()=> { using var d=example.Create(); var f=new SceneCompiler().Compile(d,1280,720); f.Validate(); True(f.Metadata.VertexCount>0); True(d.Validate().Count==0); });
        RegisterAutoFitTests(); RegisterExportTests(); RegisterAxisImprovementTests(); RegisterRasterTests(); RegisterIntensityTests(); RegisterPerformanceReviewTests(); RegisterCompletionReviewTests(); RegisterSpectrogramReviewTests();
    }
    static partial void RegisterSpectrogramReviewTests();
    static partial void RegisterCompletionReviewTests();
    static partial void RegisterAutoFitTests();
    static partial void RegisterExportTests();
    static partial void RegisterAxisImprovementTests();
    static partial void RegisterRasterTests();
    static partial void RegisterIntensityTests();
    static partial void RegisterPerformanceReviewTests();
}
