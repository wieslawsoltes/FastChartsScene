# Architecture

## Reusable dependency graph

```text
Core ← Data ← SignalProcessing
Core + Data ← Charts
Charts ← Rendering ← Interaction
Rendering ← Rendering.Raster ← Rendering.Skia
Interaction + Rendering.Skia ← Controls.Uno
Charts + SignalProcessing ← Examples
Charts ← Serialization
Rendering ← Export
Controls.Uno + Examples + Serialization + Export ← Explorer
```

Every library is a separate NuGet project. Numerical/model/rendering/export layers have no Uno dependency. Controls.Uno targets desktop and browser WebAssembly. The package-only test consumer verifies that transitive references and browser assets survive packaging.

## Model and data contract

Documents, series, axes and controllers are UI-thread-affine. A document rewires nested change subscriptions when collections change and detaches all subscriptions on disposal. Nested BeginUpdate scopes coalesce model changes into one revision/Changed event. AutoFit computes a complete candidate plan before mutating any axis, with independent stack/sign accumulators and separate spatial height/Z bounds. Category labels and pane axis bindings own their arrays.

XY/uniform sources own their input buffers. RollingBuffer synchronizes producers and returns immutable snapshots for UI handoff. The line envelope index caches source extrema and preserves spikes/NaN breaks when selecting data for visible pixels. It does not reorder freeform sources. Replacing data creates a new source identity; incremental streaming GPU updates are not yet implemented.

## Frame compilation

SceneCompiler caches a frame by document identity/revision and logical viewport dimensions. Layout resolves layered, stacked and segmented panes. Geometry is clipped in binary64 before conversion to packed float vertices. The common vertex layout is XYZ, RGBA, normal XYZ: ten floats / 40 bytes. Draw batches carry start/count, primitive kind, clip rectangle, depth flag, matrix and point size. Text and interaction regions remain separate metadata.

The compiler renders XY, circular and spatial families explicitly. Unsupported view/series/style combinations throw a descriptive exception. Original-sample hit testing queries the data source, not interpolated or decimated vertices.

## GPU hosts

Desktop uses Uno GLCanvasElement with Silk.NET. The renderer saves and restores changed shared-context state, validates shader/program creation and draws clipped triangles with depth testing and simple face lighting. A valid initial backing size avoids zero-sized initialization; subsequent backing dimensions use device pixels.

Browser uses a separate chart-owned WebGL2 context. Its JavaScript is embedded under WasmScripts in the reusable library and is emitted/loaded as a static Uno Bootstrapper asset. Global imports are qualified function names, not fictitious ES modules. Borrowed MemoryView spans are used only during synchronous calls; uploads and returned pixels are copied into owned storage.

The browser reads the standard RGBA/UNSIGNED_BYTE combination, reverses row order and composes a premultiplied RGBA Skia bitmap into Uno. This avoids the pinned Uno graphics host's unsupported BGRA browser readback without modifying any global WebGL/runtime function. The script validates WebGL errors and bounds allocations. Context loss is reported; explicit automatic recovery is remaining work.

Both hosts use the same frame geometry and shader lighting. Both presentation paths currently involve readback. Neither is a zero-copy GPU compositor.

## Fallback and image export

SkiaFrameRenderer draws 2D packed triangles with SKVertices and overlays text. Its 3D path calls the reusable CPU rasterizer, which performs six-plane homogeneous clipping, reciprocal-W color interpolation, LEQUAL depth, top-left triangle coverage, scissoring and premultiplied source-over blending. Rasterization is two-sided like the GPU path. The result is cached by frame and output dimensions.

PNG reuses this backend at the requested export scale and includes text. SVG emits the actual clipped 2D polygons, clip paths and escaped labels; depth-tested scenes and vertex gradients are rejected. All-data CSV writes each supported model family in a stable long-form schema through a caller-owned TextWriter. The UI also retains the simple first-XY-series CSV command.

## Interaction and persistence

ChartInteraction applies anchor-preserving zoom and viewport plans transactionally. Pointer drags form one history entry; cancellation restores the initial viewport. The Uno adapter handles mouse, keyboard and pinch input, selection rectangles, cursors and legend toggles. Viewport history is separate from general model/document editing history.

ChartJson uses a versioned schema, hand-written UTF-8 JSON writing and JsonDocument reading, so no reflection-based type activation is required. Input character, numeric, series, axis and annotation limits constrain imports. Duplicate keys, invalid bindings, enums and dimensions are rejected. Import compiles the candidate chart before replacing the current document.

## Validation and publication

Validate checks numerical contracts, scenes, serialization, allocation/clip/depth behavior, package inventory, isolated package consumers and current-build browser scenarios. GPU and fallback suites use identical example/input/export coverage and do not download an earlier application. Screenshots exclude legend swatches from basic geometry-presence checks. Pointer zoom assertions use the coordinates delivered by the browser.

CI's graphics adapter is SwiftShader. CPU compilation timing is not GPU timing or end-to-end latency. Native desktop execution and a hardware/cross-browser conformance matrix remain separate work.

A successful main push validation produces revision.json with its commit/tree. Pages downloads only that run's browser artifact and verifies the commit before deployment. Source and packages remain available as validation artifacts independently of Pages.
