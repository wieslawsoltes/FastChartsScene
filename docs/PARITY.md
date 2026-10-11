# Compatibility and remaining scope

This is an implementation inventory, not a claim of full LightningChart .NET parity. The public API is FastChartsScene's own API. Presence of a chart family does not imply every original property, interaction, style, or example is supported.

| Area | Implemented | Remaining compatibility work |
| --- | --- | --- |
| Axes | Linear/log/date/category transforms; calendar month/year and time-of-day intervals; inversion; layered/stacked/segmented panes; stable adjacent-value logarithms; owned category/segment arrays | Scale breaks, custom labels/ticks, complete multi-axis placement/editing and original semantics |
| XY lines | Sampled/point lines, scatter, steps, monotone cubic, markers, gaps, area baseline | All line/marker styles, dashes, per-point styles, spline families, joins/caps and original API |
| Data | Immutable sources, synchronized rolling snapshots, ordered searches, interpolation, extrema/gap-preserving screen reduction | Incremental GPU uploads, zero-copy source APIs, additional retention modes and sustained-load guarantees |
| Bars/ranges | Positive/negative stacks, groups, horizontal bars, bands, asymmetric errors; atomic fit includes totals, widths and uncertainty | All grouping/range styles, per-series editing and original APIs |
| Financial | Candlestick/OHLC; SMA/EMA/RSI/Bollinger calculations; all-series data export | Full financial tools/indicators, market calendars, finance-specific picking/editing |
| Intensity | Regular heatmaps and contours with NaN holes | Colorbars, palette controls, GPU scalar textures, irregular grids, intensity picking/editing |
| Polar/Smith | Linear unfilled curves and graticules | All series styles, annotations, clipping and navigation compatibility |
| 3D | Lit surfaces, waterfalls, bars, points/lines, meshes/OBJ; correct spatial fit; depth-correct CPU fallback and PNG | Volume rendering, complete spatial families, translucent sorting, spatial picking/slicing, full axes/annotations |
| Pie | Extruded pie/doughnut, height, explosion, JSON/CSV/PNG | Slice labels/legends, complete picking/layout/style behavior |
| DSP | FFT/inverse, windows, calibrated spectra, STFT, basic low-pass and indicators | Streaming transforms, full filters and DSP API |
| Input | Anchored zoom, pan/orbit, rectangle zoom, XY sample queries, legend toggles, undo/redo, pinch adapter | Picking/editing for every series, additional tools, complete touch/pen/accessibility matrix |
| Annotations | XY text, horizontal/vertical lines and rectangles | Interactive editing, complete annotation set, circular/spatial annotations |
| Persistence | Versioned bounded JSON for every current model; exact data/style roundtrip, duplicate-key checks | LightningChart file compatibility, stable external series IDs, future-version migrations |
| Export | 2D/3D PNG, clipped 2D SVG, first-series and complete-model CSV, chart JSON, frame metadata | 3D vector formats, PDF/document/print/clipboard integration and original export API |
| Rendering | Packed common geometry, native GL, browser-owned WebGL2/RGBA composition, Skia vectors/text and CPU depth renderer | Zero-copy GPU composition, WebGPU/Vulkan/D3D/Metal backends, device-loss recovery, AA/style and cross-device conformance |
| Explorer | 29 fixtures, searchable gallery, inspector, synthetic acquisition, document and export commands | Exact reference application visual comparison, all original explorer examples/features |
| Packaging | Twelve reusable libraries, implementation inventory and isolated desktop/browser NuGet consumer | Public-feed releases, expanded API stability/compatibility testing |
| Deployment | Desktop/browser builds and validated-artifact Pages workflow | Confirm each deployment independently; native runtime/hardware acceptance |

## Explicit boundaries

The chart renderer and controls are an independently designed API, not a binary or source-compatible replacement for LightningChart .NET. There is no versioned, exhaustive reference-API or pixel-equivalence certificate.

OpenGL/WebGL geometry is GPU-rendered but read back before Uno composition. The fallback rasterizer is CPU-based. Software graphics execution in CI is not evidence of hardware throughput. The million-sample fixture tests envelope selection and bounded geometry, not a promised frame rate.

3D source-over transparency follows submitted order; order-independent transparency and arbitrary translucent sorting are not implemented. SVG deliberately rejects depth-tested scenes and interpolated per-vertex colors instead of producing a visually incorrect approximation. Raster output has a 16-megapixel allocation limit; 2D PNG permits up to 64 megapixels.

Frame buffers are exposed to backend consumers and must be treated as immutable. The model/compiler/controller contract is UI-thread-affine. JSON preserves the current model but regenerates transient series IDs. All series CSV is a long-form interchange representation, not an original-product format.

Browser tests exercise real pointers/keys and toolbar commands plus diagnostic exports for fixture selection, state assertions and persistence. They do not yet certify all native file-picker, touch/pen, screen-reader, or cross-browser behavior. Desktop CI compilation is not native runtime testing.

## Acceptance criteria for full parity

Full parity requires a specified reference version and exhaustive API/property inventory, original application visual references, numerical contracts and representative datasets, interaction scripts covering each control/property, and native/browser hardware performance measurements. Remaining entries above are open work, not implemented placeholders.
