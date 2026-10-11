# Performance and reliability review

This increment reviews the existing package/data/rendering/input/persistence paths and fixes the problems listed below. It is not a claim that every possible defect, platform, reference API or workload has been certified.

## Changes and regression coverage

| Path | Finding | Implementation and regression |
| --- | --- | --- |
| Intensity editor | Native Uno multiline input can contain bare carriage returns; the custom-stop parser only split line feeds | Accept CR, LF and CRLF. Real keyboard multiline entry must produce the expected custom palette and measured colors at DPR 1 and 2. |
| Editor updates | Synchronous model notifications could refresh the editor during an edit, rebuild its palette list, and erase invalid drafts | Suppress reentrant refresh during Apply, retain invalid drafts, and reuse unchanged palette item sources. Validation errors leave the model unchanged. |
| Render cache | A failed validation assigned an invalid frame into the cache; a second compile could return it | Validate a local candidate before publishing the cache. Repeated invalid compiles must both fail; a corrected model can compile again. |
| Document lifetime | Returning a cached frame bypassed disposed-document validation | Check disposal before the cache fast path. |
| Heatmaps | Every frame visited every grid cell, even outside the viewport, and repeatedly transformed shared row/column edges | `IntensityViewport` binary-searches exact cell boundaries, projects visible row/column intervals once, then emits only intersecting cells. Tests compare all rectangles to exhaustive projection under inversion, log clipping and singleton dimensions. No visible scalar values are decimated. |
| Scalar calibration | New palette/settings identities rescanned the same immutable grid | Lazily published `ScalarGrid.Statistics` shares finite and positive extents across scales and series. No-op `With*` settings preserve identity; logarithmic scale spans are cached. Auto-range still uses the entire source, not only visible cells. |
| Rolling data | Snapshots copied their sample array twice; append/snapshot loops used per-sample modulo | One or two span copies handle wraparound. A newly allocated private snapshot array transfers ownership internally; public UniformData construction still copies caller data. Randomized FIFO, NaN, oversized-batch and snapshot-immutability tests check semantics. |
| Contours | Each cell/level allocated temporary arrays/lists; view/style changes regenerated unchanged topology | Four-corner stack buffers eliminate per-cell heap objects. Weak-keyed topology caches invalidate on grid/level replacement, not pan/resize/palette changes. Finite reference output is compared exactly. Extreme interpolation/saddle arithmetic avoids overflow. |
| WebGL uploads | Each changed frame allocated a full upload copy and reallocated GPU buffer storage | Retain geometrically grown CPU staging and GPU capacity; copy borrowed spans into owned storage, then use bufferSubData. Eight actual theme edits verify stable allocation counters; pointer-only overlays must not upload geometry. |
| WebGL loss | Restoring JavaScript resources left managed cached frames and fallback state stale | Browser loss/restoration notifications enqueue managed state transitions. Recreate GL resources, invalidate uploaded/drawn identities, and upload the latest document. Lifecycle generations reject callbacks queued before unload. Real WEBGL_lose_context tests cover unchanged frames, edits during loss and destroyed contexts. |

## Reproduce measurements

```sh
dotnet run --project tests/FastChartsScene.Benchmarks -c Release -- artifacts/performance.json
```

The benchmark checks equivalent output before measuring. Six warmups precede fifteen samples; the report contains median elapsed CPU milliseconds and current-thread allocated bytes. CI publishes it with the portable test results. There is no wall-clock pass/fail threshold: correctness and bounded-allocation regressions are separate from noisy timings.

One measured run at commit `e130e93ecf9d7675eaa6d3811bfea7d9b17df321`, Validate run `38142856577`, .NET 10.0.12 on a Linux CI runner:

| Microbenchmark | Previous loop | Revised loop |
| --- | ---: | ---: |
| 1,048,576-cell heatmap, 484 visible cells: projection and scalar mapping | 72.5164 ms | 0.0472 ms |
| 100,000-sample rolling snapshot | 1.0995 ms | 0.3227 ms |
| Snapshot allocated bytes | 1,600,048 | 800,064 |

These are **isolated CPU microbenchmarks**, not full scene-build time, presented FPS, input latency, hardware GPU throughput, or a comparison with LightningChart. Heatmap statistics and input are initialized before timing. The visible-cell method allocates 2,320 bytes for its retained projected intervals in that sample; the exhaustive loop allocates zero but touches the entire grid. The snapshot baseline reproduces the previous two-copy algorithm over equivalent samples, not a separate installed release.

The current run's `performance.json` is authoritative for that run; timings vary with runtime and hardware. The contour regression checks that an empty 256×256, three-level result allocates under 4 KiB, and verifies cached topology generation counts. It does not claim a measured contour frame rate.

## GPU lifecycle and limits

`FastChartsSceneWebGL.inspect()` returns read-only context-generation, upload and allocation counters. It exposes no GL objects or chart samples. CPU staging and VBO capacity grow only when necessary and are retained until release; each is capped at 256 MiB. RGBA framebuffers are limited to 16 million pixels. Resource exhaustion is diagnosed, not presented as successful GPU rendering.

Context restoration follows the [WebGL resource invalidation contract](https://registry.khronos.org/webgl/specs/latest/1.0/). Tests use the standard [WEBGL_lose_context extension](https://registry.khronos.org/webgl/extensions/WEBGL_lose_context/) against the real context; test-only observation does not replace production browser APIs. A restored context must re-upload geometry even when the document did not change. Explicit Skia selection remains explicit; Auto returns to WebGL when restoration succeeds. Browsers decide whether a lost context can be restored. Failed creation or unrecoverable restoration continues to use fallback in Auto mode.

## Remaining performance and review work

GPU presentation **still reads pixels back for Uno composition**. This work is not a zero-copy compositor and does not implement incremental append-only GPU geometry updates. A full-viewport dense heatmap still emits all visible cells; scalar texture rendering and level-of-detail aggregation remain future work. Contour generation remains proportional to cells times levels when source topology changes, and the resulting visible output still needs projection/tessellation.

The new WebGL tests use Chromium with a software adapter. Native OpenGL loss recovery, native Windows/macOS/Linux runtime behavior, other browser engines, long-duration stress, hardware profiling, complete accessibility/touch/pen behavior and exhaustive LightningChart API/visual conformance remain open. See [PARITY.md](PARITY.md) and issue #2.
