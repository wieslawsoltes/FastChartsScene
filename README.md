# FastChartsScene

Independent scientific charting components for .NET and Uno Platform, with a desktop/browser explorer.

**Development preview, not a completed LightningChart .NET replacement.** There is no claim of API, feature, visual, or performance parity. The implementation is original and does not contain LightningChart source, binaries, branding, or artwork. See [the compatibility matrix](docs/PARITY.md) before choosing it for an application.

## Implemented application

The Uno explorer contains 29 deterministic examples: sampled and live signals, one-million-sample data, scatter, monotone cubic and step curves, areas, confidence bands, error bars, stacked/horizontal bars, candlestick/OHLC, heatmaps, contours, FFT spectra, spectrograms, polar and Smith plots, stacked/segmented axes, logarithmic/date axes, surfaces, waterfalls, 3D bars, spatial lines, meshes, pies, and doughnuts.

The explorer provides a searchable gallery, property inspector, data preview, mouse/keyboard/touch navigation, original-sample cursor queries, legend visibility controls, bounded synthetic acquisition, chart-document open/save, 2D PNG export, XY CSV export, and frame-metadata export. Unsupported view/series combinations report an error rather than substitute a different chart.

## Reusable projects

| Project | Responsibility |
| --- | --- |
| `FastChartsScene.Core` | Coordinates, ranges, axes, themes, observable primitives |
| `FastChartsScene.Data` | Immutable samples, rolling buffers, queries, envelope reduction, CSV |
| `FastChartsScene.SignalProcessing` | FFT, spectra, STFT, windows, filtering, financial indicators |
| `FastChartsScene.Charts` | Chart documents, typed series, annotations, cameras, geometry algorithms |
| `FastChartsScene.Rendering` | Layout and backend-neutral clipped geometry, labels, hit regions |
| `FastChartsScene.Interaction` | Zoom, pan/orbit, rectangle zoom, gesture history, sample picking |
| `FastChartsScene.Rendering.Skia` | Skia 2D presentation, text, and PNG export |
| `FastChartsScene.Controls.Uno` | Reusable Uno chart control, OpenGL surface, input and lifecycle adapters |
| `FastChartsScene.Examples` | Standalone deterministic chart fixtures |
| `FastChartsScene.Serialization` | Versioned, bounded, reflection-free JSON document persistence |

NuGet packages are built into CI artifacts, not published to nuget.org. CI requires every project to produce an implementation assembly and compiles a separate consumer using package references only. A successful project build alone is not a package-validation result.

## Build and run

The repository pins Uno.Sdk 6.7.30 and requires .NET 10. Browser GLCanvas support requires Uno 6.7 or newer.

```sh
dotnet workload install wasm-tools
dotnet run --project tests/FastChartsScene.Tests -c Release
dotnet build samples/FastChartsScene.Explorer -c Release -f net10.0-desktop
dotnet run --project samples/FastChartsScene.Explorer -c Release -f net10.0-desktop
dotnet publish samples/FastChartsScene.Explorer -c Release -f net10.0-browserwasm -o artifacts/publish
```

The browser application is configured for the `/FastChartsScene/` base path. The validation workflow copies its published site to `artifacts/site/FastChartsScene`; serve `artifacts/site` as the HTTP root. The browser requires HTTP/HTTPS rather than opening the generated index file directly.

```sh
python -m http.server 8000 --directory artifacts/site
# Open http://localhost:8000/FastChartsScene/
cd tests/browser
npm install --ignore-scripts --no-audit --no-fund
npx playwright install chromium
node browser-tests.mjs
```

The browser test script can start its own static server when `artifacts/site` is present. `SITE_ROOT`, `TEST_OUTPUT`, and `TEST_URL` override its defaults.

## Use a chart control

```csharp
using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Data;

var document = new ChartDocument { Title = "Sensor readings" };
document.XAxis.Range = new AxisRange(0, 3);
document.YAxis.Range = new AxisRange(-2, 2);
document.Series.Add(new LineSeries
{
    Title = "Channel A",
    Data = new UniformData(0, 1, [0, 1, -1, 0])
});
var chart = new ChartControl { Document = document };
// Assign chart to a Uno page or panel.
// The caller owns document. Detach it before disposing it.
```

Several related changes can be coalesced with `using (document.BeginUpdate()) { ... }`. Chart models and controllers are UI-thread-affine. Rolling buffers support synchronized producers; marshal immutable snapshots to the UI thread before replacing a series source.

```csharp
using FastChartsScene.Serialization;

string json = ChartJson.Write(document);
ChartDocument restored = ChartJson.Read(json);
```

Persistence restores data, styles, axes, bindings, annotations, and cameras. Session-only series IDs regenerate on import. NaN sample gaps use JSON null; no executable type activation or external references are supported. Input and numeric budgets are configurable through `ChartReadLimits`.

## Rendering and performance boundaries

The compiler reduces ordered line data against the visible pixel width while preserving extrema and breaks, and caches unchanged document frames. Geometry is shared by OpenGL and the 2D Skia backend. Text is drawn by Skia.

The Uno OpenGL host renders into an offscreen framebuffer and reads it back for composition. This is **not a zero-copy presentation path**. The explorer's compile-time counter measures CPU scene compilation, not end-to-end frame latency or GPU time. Software GL adapters can execute this code; their results do not establish hardware performance.

The Skia fallback supports 2D views only. Its acceleration depends on the host surface. 3D fallback and 3D PNG export are explicitly unsupported. See [architecture](docs/ARCHITECTURE.md), [compatibility](docs/PARITY.md), and CI artifacts for further boundaries and validation evidence.

## Development status

Work is tracked in draft PR #1 on `feat/uno-charting`. Main is not yet the implemented application, and no GitHub Pages deployment is claimed by this README. Test and artifact results must be associated with their exact source revision; an old successful build is not evidence that a newer revision passed.

## License

MIT. Third-party dependencies retain their respective licenses. No proprietary LightningChart assets are redistributed.
