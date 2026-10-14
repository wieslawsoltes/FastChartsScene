# FastChartsScene

Independent scientific charting components for .NET 10 and Uno Platform, with a desktop/browser explorer and reusable controls.

**Development preview, not full LightningChart .NET parity.** The public API is FastChartsScene's own API. No API, visual, performance, or complete feature equivalence is claimed. The implementation is original; no LightningChart source, binaries, branding, or artwork is redistributed. See [compatibility and remaining scope](docs/PARITY.md).

## Application

The explorer opens in a reference-style charcoal/yellow **tile or list gallery** with
real rendered previews, keyword/phrase search, category/tag filters, favorites, and
portable favorite lists. Selected charts use a compact header, a larger chart-first
work area, and a searchable light Properties panel. Details opens the data/usage tabs.
**Extract project** saves the edited chart as a standalone desktop/browser Uno source
project, built against the matching downloadable NuGet packages. See the
[gallery, extraction and performance contract](docs/EXPLORER-UI.md).

The explorer contains 31 deterministic chart fixtures: multichannel/live signals, one million samples, scatter, monotone cubic/step/area curves, bands, error bars, positive/negative and horizontal stacks, candlestick/OHLC, heatmaps, logarithmic intensity, compared intensity panes, contours, FFT spectra, spectrograms, polar/Smith plots, stacked/segmented axes, logarithmic/date axes, surfaces, waterfalls, spatial bars/lines/meshes, pies, and doughnuts.

A searchable gallery, property inspector, original-data preview, chart-document open/save, bounded synthetic acquisition, legend visibility, pan/orbit, anchored wheel/pinch zoom, rectangle zoom, and viewport undo/redo are included. Unsupported combinations are diagnosed rather than silently substituted.

### Export commands

| Command | Output |
| --- | --- |
| PNG | 2D or depth-correct 3D image, including chart labels, at 2× resolution |
| CSV | Original samples from the first XY data series |
| All data CSV | Every current series, including uncertainty bounds, financial data, grids, spatial points, mesh topology, and pie values |
| SVG | Actual clipped 2D vector geometry and escaped text; depth-tested 3D and interpolated vertex colors are explicitly rejected |
| Save chart / Open chart | Versioned JSON document with all current data, styles, axes, annotations, and camera settings |
| Extract project | Standalone Uno source ZIP containing the exact edited chart JSON; matching NuGet packages are supplied separately |
| Frame JSON | Render metadata for diagnostics; this is not a complete saved chart |

## Simultaneous cursor values

**Cursor table** opens a native, virtualized result table for every eligible line/scatter
and financial series in the hovered pane. It shows original nearest-X samples, supports
Hold, full-precision details and CSV export, and reuses row objects and source indexes.
The query and control are independently reusable. See [the cursor table guide](docs/SERIES-CURSOR-TABLE.md)
for family coverage, snapshot/lifecycle contracts, formatting and validation boundaries.

The compact native selector also offers **Interpolated** tracking for original linear
and stepped segments. Calculated rows are labelled **Calc Y**, retain both original
endpoint indices, and export calculated values separately from source coordinates.
Financial, scatter-only and cubic results remain explicitly original. See the
[interpolated cursor guide](docs/INTERPOLATED-CURSOR-VALUES.md) for numerical, API,
CSV and performance contracts.

The **Format** button provides Compact, Exact and Scientific presets plus independent
X/value numeric formats. Held snapshots, full-precision details and CSV stay unchanged.
See [cursor formatting and index construction](docs/CURSOR-FORMATTING-AND-INDEXES.md)
for the reusable API, validation and cold-memory improvements.

## Axis ticks and labels

XY axes support exact custom tick positions and text, invariant numeric/date formats,
and cached automatic label generation. The compact Properties Editor exposes a
**Ticks and labels** section with atomic Apply/Reset and preserved drafts. JSON,
SVG/PNG and extracted projects retain the settings. See the [axis tick guide](docs/AXIS-TICKS.md)
for the reusable `AxisTickOptions`, `AxisTickText` and `AxisTicksEditor` APIs and styling boundaries.

## Intensity charts

Intensity heatmaps, contours, surfaces, waterfalls and spatial bars share reusable immutable color scales. Choose gradient or stepped palettes, reverse them, use automatic/manual linear or logarithmic color ranges, and display pane-aware colorbars. Regular XY heatmaps support exact stored-cell cursor values and highlighting. The inspector uses the separately reusable `IntensityScaleEditor` control, including custom palette stops and inline validation.

```csharp
var scale = new ColorScale(ColorMap.Thermal,
    range: new AxisRange(0.001, 1000), mode: ColorScaleMode.Logarithmic);
var heat = new HeatmapSeries
{
    Grid = new ScalarGrid(2, 2, 0, 10, 0, 10, [0.001, 1, 10, 1000]),
    ColorScale = scale, ShowColorBar = true,
    ValueTitle = "Power", ValueUnits = "mW"
};
document.Series.Add(heat);
var editor = new IntensityScaleEditor { Series = heat };
```

The same immutable scale can be assigned to several series for calibrated comparisons. NaN/log-domain gaps, out-of-range transparency, empty/constant datasets, saved-document compatibility and 3D vertex-color boundaries are detailed in [the intensity API guide](docs/INTENSITY.md). This is an independently designed API, not LightningChart source/binary compatibility.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| `FastChartsScene.Core` | Coordinates, validated ranges, linear/log/date/category axes, calendar ticks, themes, immutable scalar color scales and maps, observable primitives |
| `FastChartsScene.Data` | Immutable XY/uniform sources, synchronized rolling buffers, searches, interpolation, spike/gap-preserving envelope reduction, CSV |
| `FastChartsScene.SignalProcessing` | FFT/inverse, calibrated spectra, STFT, windows, filtering, SMA/EMA/RSI/Bollinger calculations |
| `FastChartsScene.Charts` | Typed documents/series, cameras, annotations, atomic stack-aware/spatial AutoFit, geometry algorithms |
| `FastChartsScene.Rendering` | Adaptive axis layout, backend-neutral clipped geometry, labels, pane-aware colorbars, hit regions, revision caching |
| `FastChartsScene.Interaction` | Zoom, pan/orbit, rectangle selection, gesture history, original-sample and exact regular-heatmap cell hit testing |
| `FastChartsScene.Rendering.Raster` | Depth-tested CPU rendering with homogeneous clipping, perspective-correct color, scissoring, top-left coverage, alpha blending |
| `FastChartsScene.Rendering.Skia` | 2D vector rendering, text, 3D raster composition, PNG export |
| `FastChartsScene.Controls.Uno` | Reusable Uno control, native OpenGL/browser WebGL2 hosts, input and lifecycle adapters, explicit fallback, bindable intensity scale editor |
| `FastChartsScene.Examples` | Standalone deterministic chart fixtures |
| `FastChartsScene.Serialization` | Bounded, versioned, reflection-free JSON persistence |
| `FastChartsScene.Export` | Streaming all-series CSV and clipped 2D SVG writers |

All twelve projects produce implementation-bearing NuGet packages in CI. Package inventory validation and an isolated desktop/browser consumer check actual package references, not project references. Packages are provided as GitHub Actions artifacts; a nuget.org publication is not implied.

## Build and run

The repository pins Uno.Sdk 6.7.30 and requires .NET 10.

```sh
dotnet workload install wasm-tools
dotnet run --project tests/FastChartsScene.Tests -c Release
dotnet build samples/FastChartsScene.Explorer -c Release -f net10.0-desktop
dotnet run --project samples/FastChartsScene.Explorer -c Release -f net10.0-desktop
dotnet publish samples/FastChartsScene.Explorer -c Release -f net10.0-browserwasm -o artifacts/publish
```

The browser application uses the `/FastChartsScene/` base path. The Validate workflow copies the published site to `artifacts/site/FastChartsScene`; serve `artifacts/site` as the HTTP root. Opening the generated HTML directly from disk is not supported.

```sh
python -m http.server 8000 --directory artifacts/site
# Open http://localhost:8000/FastChartsScene/
cd tests/browser
npm install --ignore-scripts --no-audit --no-fund
npx playwright install chromium
node browser-tests.mjs
node fallback-tests.mjs
```

The browser suites start their own static server by default. `SITE_ROOT`, `TEST_OUTPUT`, and `TEST_URL` override the defaults. The default suite requires real WebGL submissions. The fallback suite executes the same current-build scenarios with explicit Skia rendering, including depth-correct 3D. Neither suite substitutes an older build.

## Use the control

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

Use `using (document.BeginUpdate()) { ... }` to coalesce related changes. Models, the compiler, and controllers are UI-thread-affine. Rolling buffers support synchronized producers; marshal immutable snapshots to the UI thread before replacing a series source. Category and segment bindings copy caller arrays so external mutations cannot silently invalidate cached frames.

```csharp
using FastChartsScene.Serialization;
using FastChartsScene.Export;
using FastChartsScene.Rendering;

string json = ChartJson.Write(document);
using ChartDocument restored = ChartJson.Read(json);
string csv = ChartDataExport.WriteCsv(restored);
string svg = SvgExport.Write(new SceneCompiler().Compile(restored, 800, 600));
```

Persistence regenerates session-only series IDs and preserves axis bindings, data, styles, annotations, and cameras. NaN gaps are encoded as JSON null. No executable type activation or external resource loading is supported. Configure `ChartReadLimits` for input and numeric budgets.

## Rendering and measurement

Auto mode uses native OpenGL on desktop and a chart-owned WebGL2 context on the browser. The browser script is a static asset carried by the reusable control package. It reads standard RGBA pixels and composes them into Uno; it does not replace global WebGL or runtime functions. Skia fallback uses vectors for 2D and the depth-correct CPU raster backend for 3D.

**Presentation currently includes framebuffer readback; it is not zero-copy.** Unchanged frames are cached and ordered lines are reduced against the visible pixel width with extrema/gaps retained. The displayed compile time measures CPU scene compilation only, not frame latency, GPU duration, power, or presentation rate. CI uses a software graphics adapter and does not establish hardware performance parity. See [architecture](docs/ARCHITECTURE.md).

## Validation and deployment

The Validate workflow builds both Uno targets, runs numerical/model/rendering tests, builds and checks every package, compiles isolated consumers, and runs browser pixel/input/export/persistence scenarios at multiple device scales and viewport widths. Evidence includes exact source/tree IDs, results, logs, screenshots, and exported files. Only a successful run for the stated revision is evidence of passing validation.

The Pages workflow deploys **only the browser artifact from a successful main-branch Validate push run** and checks its revision before publication. The target is `https://wieslawsoltes.github.io/FastChartsScene/`. A deployment is live only after the Pages job succeeds. Repository Settings → Pages must use GitHub Actions as its source.

Native desktop runtime execution, native OS file pickers, cross-browser engines, hardware GPU benchmarks, and exact LightningChart visual/API conformance are separate acceptance work; a successful build does not verify them.

## License

MIT. Third-party dependencies retain their respective licenses.
