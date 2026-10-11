using System.IO.Compression;
using System.Text;
using FastChartsScene.Charts;
using FastChartsScene.Serialization;
using FastChartsScene.Rendering;

namespace FastChartsScene.Export;

/// <summary>Standalone Uno project extraction of the current chart document. Chart content is
/// stored as JSON, never interpolated into executable source. Fixed archive paths and bounded,
/// validated options prevent path traversal or build-file injection. Packages are separate.</summary>
public static class ChartProjectExport
{
    public const string PackageVersion = "0.1.0";
    public const int MaximumDocumentBytes = 64 * 1024 * 1024;
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static byte[] Write(ChartDocument document, string projectName = "ExtractedChart")
    {
        using var output = new MemoryStream(); Write(document, output, projectName); return output.ToArray();
    }
    /// <summary>Writes to a caller-owned stream. Validation/serialization occurs before output
    /// starts. IO failures can leave a partial stream; the caller decides file replacement policy.</summary>
    public static void Write(ChartDocument document, Stream output, string projectName = "ExtractedChart")
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Output stream is not writable.", nameof(output));
        if (string.IsNullOrEmpty(projectName) || projectName.Length > 64 || !IsStart(projectName[0]) ||
            projectName.Any(c => !IsStart(c) && !(c >= '0' && c <= '9')))
            throw new ArgumentException("Project name must be an ASCII letter/underscore followed by up to 63 letters, digits or underscores.", nameof(projectName));
        ObjectDisposedException.ThrowIf(document.IsDisposed, document);
        new SceneCompiler().Compile(document, 800, 600).Validate();
        var json = ChartJson.Write(document);
        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes) throw new ArgumentException("Chart document exceeds the 64 MiB extraction limit.", nameof(document));
        // All construction happens before opening the archive, leaving output untouched on model/option errors.
        var files = new (string Path, string Content)[]
        {
            (projectName + ".csproj", Project), ("global.json", Global), ("Directory.Build.props", Properties),
            ("NuGet.config", NuGet), ("App.xaml", Xaml), ("App.xaml.cs", Application),
            ("Platforms/Desktop/Program.cs", Desktop), ("Platforms/WebAssembly/Program.cs", Browser),
            ("chart.json", json), ("README.md", Instructions.Replace("{PROJECT}", projectName, StringComparison.Ordinal)),
            (".gitignore", "bin/\nobj/\npackages/*.nupkg\n"), ("packages/README.md", "Copy the twelve FastChartsScene 0.1.0 NuGet packages from the matching GitHub Actions packages artifact into this directory. Dependencies restore from nuget.org.\n")
        };
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files)
        {
            var entry = zip.CreateEntry(file.Path, CompressionLevel.Fastest); entry.LastWriteTime = Timestamp;
            using var stream = entry.Open(); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(file.Content);
        }
    }
    private static bool IsStart(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c == '_';
    private const string Project = """
        <Project Sdk="Uno.Sdk">
          <PropertyGroup>
            <TargetFrameworks>net10.0-browserwasm;net10.0-desktop</TargetFrameworks>
            <OutputType>Exe</OutputType><UnoSingleProject>true</UnoSingleProject>
            <RootNamespace>ExtractedChart</RootNamespace><AssemblyName>ExtractedChart</AssemblyName>
            <ApplicationTitle>Extracted chart</ApplicationTitle><ApplicationId>org.fastchartsscene.extracted</ApplicationId>
            <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion><ApplicationVersion>1</ApplicationVersion>
            <UnoFeatures>SkiaRenderer;GLCanvas;</UnoFeatures>
            <WasmShellWebAppBasePath>/</WasmShellWebAppBasePath><WasmShellEnablePWA>false</WasmShellEnablePWA>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="FastChartsScene.Controls.Uno" Version="0.1.0" />
            <PackageReference Include="FastChartsScene.Serialization" Version="0.1.0" />
            <EmbeddedResource Include="chart.json" LogicalName="ExtractedChart.chart.json" />
          </ItemGroup>
        </Project>
        """;
    private const string Global = """
        { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }, "msbuild-sdks": { "Uno.Sdk": "6.7.30" } }
        """;
    private const string Properties = """
        <Project><PropertyGroup><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><LangVersion>latest</LangVersion><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Deterministic>true</Deterministic></PropertyGroup></Project>
        """;
    private const string NuGet = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources><clear/><add key="FastChartsScene-local" value="packages"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
          <packageSourceMapping>
            <packageSource key="FastChartsScene-local"><package pattern="FastChartsScene.*"/></packageSource>
            <packageSource key="nuget.org"><package pattern="*"/></packageSource>
          </packageSourceMapping>
        </configuration>
        """;
    private const string Xaml = """
        <Application x:Class="ExtractedChart.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources>
        </Application>
        """;
    private const string Application = """
        using FastChartsScene.Charts;
        using FastChartsScene.Controls.Uno;
        using FastChartsScene.Serialization;
        using Microsoft.UI.Xaml;
        namespace ExtractedChart;
        public sealed partial class App : Application
        {
            private Window? _window;
            private ChartDocument? _document;
            private ChartControl? _chart;
            public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Dark; }
            protected override void OnLaunched(LaunchActivatedEventArgs args)
            {
                using var stream = typeof(App).Assembly.GetManifestResourceStream("ExtractedChart.chart.json") ?? throw new InvalidOperationException("Missing chart document.");
                using var reader = new StreamReader(stream);
                _document = ChartJson.Read(reader.ReadToEnd());
                _chart = new ChartControl { Document = _document };
                _window = new Window { Title = _document.Title, Content = _chart };
                _window.Closed += (_, _) => { if (_chart != null) { _chart.Document = null; _chart.Dispose(); } _document?.Dispose(); _chart = null; _document = null; };
                _window.Activate();
            }
        }
        """;
    private const string Desktop = """
        using Uno.UI.Hosting;
        namespace ExtractedChart;
        internal static class Program
        {
            [STAThread]
            public static void Main(string[] args)
            {
                var host = UnoPlatformHostBuilder.Create().App(() => new App()).UseX11().UseLinuxFrameBuffer().UseMacOS().UseWin32().Build();
                host.Run();
            }
        }
        """;
    private const string Browser = """
        using Uno.UI.Hosting;
        namespace ExtractedChart;
        public static class Program
        {
            public static async Task Main(string[] args)
            {
                var host = UnoPlatformHostBuilder.Create().App(() => new App()).UseWebAssembly().Build();
                await host.RunAsync();
            }
        }
        """;
    private const string Instructions = """
        # Extracted chart project

        This project contains the exact current FastChartsScene chart document, including edits,
        axes, data, styles, annotations and camera settings. `chart.json` is embedded as data;
        chart strings are never evaluated as C# or JavaScript. Session-only series IDs regenerate
        on import. Acquisition is a snapshot, not a connection to a device or a running producer.

        ## Dependencies

        Install .NET 10 and the WebAssembly workload:

        ```sh
        dotnet workload install wasm-tools
        ```

        Copy the **twelve FastChartsScene 0.1.0 NuGet packages from the matching repository
        GitHub Actions packages artifact** into `packages/`. These preview packages are not
        assumed to be published on nuget.org. NuGet.config deliberately resolves FastChartsScene
        packages from this local folder; other dependencies use nuget.org. Uno.Sdk is pinned to
        6.7.30. The packages are not embedded in this source-only extraction.

        ```sh
        dotnet build {PROJECT}.csproj -c Release -f net10.0-desktop
        dotnet run --project {PROJECT}.csproj -c Release -f net10.0-desktop
        dotnet publish {PROJECT}.csproj -c Release -f net10.0-browserwasm -o publish
        ```

        Serve the directory containing the generated `index.html` over HTTP. This export uses
        `/` as its browser base path; change WasmShellWebAppBasePath before publishing under a
        subdirectory. Do not open the HTML using a file:// URL.

        The reusable ChartControl defaults to native OpenGL / browser WebGL2 with Skia fallback.
        GPU presentation still includes readback. Hardware acceleration, native runtime support
        and production acceptance depend on the host. This is an independent preview API, not
        full LightningChart compatibility. Keep the document alive while attached to the control;
        this sample detaches/disposes both on window closure.
        """;
}
