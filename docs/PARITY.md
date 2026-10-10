# Compatibility and remaining scope

This is an implementation inventory, not a claim of full LightningChart .NET parity. The public API is currently FastChartsScene's own API. A chart family being present does not mean every property, interaction, rendering style, or original example is supported.

| Area | Implemented | Important remaining scope |
| --- | --- | --- |
| Axes | Linear/log/date/category transforms and ticks; inversion; layered/stacked/segmented layouts | Breaks, richer date/category intervals, complete multi-axis behavior, custom labels, interactive axis editing, strict compatibility semantics |
| XY lines | Sampled/point lines, scatter, step-before/after, monotone cubic, markers, gap handling, area baseline | All line/marker styles, dashes, per-point styles, spline families, full joins/caps and original API |
| Data | Owned immutable sources, rolling snapshots, ordered search, interpolation, spike/gap-preserving screen reduction | Incremental GPU streaming, full zero-copy source APIs, broader retention modes and large-source stress guarantees |
| Bars and ranges | Stacks, negative/positive groups, horizontal bars, bands, asymmetric error intervals | All grouping/range styles, interactive editing, fully stack-aware fit bounds |
| Financial | Candlestick and OHLC primitives; SMA/EMA/RSI/Bollinger calculations | Full financial tools, all indicators, market calendars, finance-specific picking/editing and axes |
| Intensity | Regular heatmaps and contours with NaN holes | All palettes, GPU scalar textures, colorbars, irregular grids, intensity picking/editing |
| Polar/Smith | Linear unfilled curves and graticules | Full chart styles, annotations, all clipping/interaction cases and original API |
| 3D | Lit surfaces, waterfall tubes, grid bars, point/line meshes, triangle meshes, basic OBJ input | Volume rendering, complete spatial families, robust translucent sorting, spatial picking, slicing, complete axes and annotations |
| Pie | Extruded pie and annular sectors, height, explosion | Slice labels/legends, all layout and picking behavior, full style parity |
| Signal processing | FFT/inverse, windows, calibrated spectra, STFT, basic low-pass and indicators | Full DSP library, streaming transforms, complete filter families and original API |
| Interaction | Anchored zoom, pan/orbit, rectangle zoom, original XY sample lookup, legend toggles, history | Picking/editing for every series type, additional navigation tools, complete touch/pen/accessibility matrix |
| Annotations | XY text, horizontal/vertical lines, rectangles | Interactive editing, all annotations, circular/spatial annotations, export parity |
| Persistence | Versioned JSON for all current models; bounds and duplicate-key validation | LightningChart file compatibility, stable external series identities, schema migrations beyond version 1 |
| Export | 2D PNG, first XY source CSV, frame metadata, complete current-model JSON | 3D PNG, vector/document/print exports, full multi-series data export, clipboard and platform print integration |
| Rendering | Packed geometry, OpenGL/ES control, Skia text and 2D fallback, device-scale sizing | Zero-copy presentation, WebGPU/Vulkan/Metal/D3D backends, robust device-loss handling and cross-device image conformance |
| Explorer | 29 fixtures, gallery/search/filter, inspector, live synthetic acquisition, tabs, input and file commands | Exact visual comparison to a versioned original application, complete original explorer content and feature parity |
| Packaging | Ten reusable source projects, artifact verification, isolated package-consumer checks | Successful results must be confirmed in CI; no public NuGet-feed release is claimed |
| Deployment | Desktop/browser targets and CI browser artifacts | GitHub Pages deployment and native runtime validation must be verified separately |

## Known boundaries

The advanced stacked/spatial AutoFit and model-lifetime changes attempted during development were reverted after failing compilation. Their proposed correction is not part of the active branch. The existing fit method should not be assumed to account for stacked totals, horizontal bar widths, every error/band extent, or all 3D Z/height coordinates.

Category and segment array mutation, long-running document lifecycle stress, 3D transparency, view-domain clipping, and every unsupported series/style combination require additional hardening. Frame geometry arrays are caller-visible and must be treated as immutable.

The GPU host's readback path and a software WebGL CI adapter prevent performance-parity conclusions. No stated source-point count should be interpreted as a guaranteed frame rate. The one-million-sample fixture checks reduction behavior, not a hardware benchmark.

Neither a passing unit-test suite nor a screenshot is evidence of full parity. Compatibility acceptance requires a versioned reference application, a property/API inventory, visual references across configurations, interaction scripts, numerical contracts, representative datasets, and native/browser performance measurements.
