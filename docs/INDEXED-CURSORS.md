# Indexed sample cursors and interaction review

This increment adds an exact **original-sample-center** query API and reviews history/lifecycle behavior. It does not add contour picking, financial-candle body picking, cumulative-stack picking, spatial picking or full LightningChart parity.

## Standalone immutable data index

`FastChartsScene.Data.PointSpatialIndex` has no Uno or renderer dependency. Construct it once for an immutable `IDataSource` and retain it across viewport changes:

```csharp
var index = new PointSpatialIndex(data);
PointQueryResult result = index.Nearest(xAxis, yAxis, plotRectangle, pointerPosition, radius: 12);
if (result.Hit is { } hit)
{
    // hit.Index and hit.Data identify the original sample, not a reduced/interpolated point.
    Console.WriteLine($"{hit.Index}: {hit.Data.X}, {hit.Data.Y}");
}
```

A median-partitioned bounding hierarchy stores original indices, finite data extents, and lowest-index tie information. Axis-range clipping excludes offscreen centers and nonpositive coordinates in logarithmic domains. Node bounds are projected through the current monotonic axis transformations, so linear/logarithmic scale, inversion, resize, pan and transpose do not rebuild the source index. `transpose: true` swaps source X/Y for horizontal-series screen coordinates but preserves the returned original data.

NaN Y samples are excluded without rewriting the source. Equidistant hits choose the lowest original index. Duplicate coordinates remain distinct source samples. Queries return the number of visited nodes and tested samples; no temporary heap storage is allocated by `PointSpatialIndex.Nearest`. The index owns only its index/bounds buffers and retains the source, which must remain immutable. Concurrent queries may share the index when their supplied axes are not mutated concurrently.

**Construction is not free.** It visits the full source and builds a balanced hierarchy; typical construction is O(N log N), with a deterministic sort fallback for pathological quickselect input. Worst-case query cost is O(N), for example overlapping bounds or a degenerate distribution. This is not a guaranteed frame-rate API. The benchmark reports construction time/storage separately from warm query latency.

## Chart interaction adapter

`ChartInteraction.HitTestData` uses direct projected-coordinate binary searches for ordered sources. Small candidate intervals (at most 2,048 samples) are scanned without constructing a whole-source index. This avoids repeated index construction when ordinary rolling traces replace their immutable snapshot each frame. Wider candidate windows and large unordered sources use weak-keyed `PointSpatialIndex` caches. Duplicate-X groups and zero-radius queries are preserved by searching the actual projected coordinates rather than rounding an inverse-transform data window.

`SampleIndexBuildCount`, `LastSampleQueryVisits` and `LastSampleQueryTests` expose diagnostic work counts. `ClearPickingCache()` releases the controller's caches; Uno invokes it on unload. Palette/title/theme/range changes retain the source index. Replacing an immutable data source uses a new cache entry. The first large query may still build an index on the calling thread; frequent replacement of large unordered sources is an unresolved optimization workload.

The adapter searches visible series in reverse draw order. Equal-distance series ties choose the later series, then its earliest original sample. It rejects fully transparent series, entirely unpainted line settings, legend-covered positions, samples outside axis ranges, disposed documents, and frames from an older document revision. The caller must use a frame from the same document.

This remains a source-sample API: a bar's returned value is the source value, not the cumulative stack endpoint. It does not calculate line-segment intersections, filled-body occlusion between different chart families or per-pixel marker coverage. Heatmap queries remain available through `HitTestScalar` separately.

## Reusable Uno cursor state

`ChartControl.DataHit` and `DataHitChanged` expose the current nearest sample. The event only fires when the public state changes. Existing tooltips use this same result. State is cleared on leaving the plot, moving onto a legend, replacing/unloading the document, and failed frames. A new frame refreshes the hit at the current pointer. Cursor-only movement invalidates the overlay rather than rebuilding or uploading chart geometry.

```csharp
chart.DataHitChanged += (_, _) =>
{
    var hit = chart.DataHit;
    readout.Text = hit is null ? "" : $"{hit.Title}: {hit.Data.Y:G9}";
};
```

The caller owns its subscriptions and chart document. Explicit browser control disposal also releases its owned WebGL resources, while normal unloading clears the picking caches and cursor state.

## Viewport undo/redo safety

History snapshots now record the participating axis instances/order, their scale/inversion, chart view/layout, ranges and camera settings. Undo/redo validate compatibility **before** applying any axis or moving stack entries. External scale, inversion, view or axis-topology changes invalidate old history instead of partially restoring invalid ranges. `CanUndo`/`CanRedo` report this, and an attempted incompatible undo clears the obsolete stacks and returns false. `ClearHistory()` is also public.

Range-only edits retain normal viewport undo semantics. A pointer drag still produces one history entry. Intermediate drag events no longer allocate repeated history snapshots; the baseline is captured once at gesture start. Shared axis instances are deduplicated. Gesture cancellation tolerates external topology changes and disposed documents without reviving obsolete objects.

This is viewport history, not arbitrary chart-document edit undo. Exceptions thrown by application-supplied model observers are not a general transactional rollback boundary.

## Other reviewed fixes

`FinancialSeries.Points` reuses a read-only view rather than allocating a wrapper on every access. `SetData` still owns a copy, and earlier views remain valid after source replacement. Invalid marker/interpolation/financial-style enum values and empty axis bindings are rejected before mutation. Scalar cursor queries also reject disposed documents and stale frame revisions.

A separate contour-renderer/picking and fill-alpha change was not accepted by the publication tool and is **not included** in this increment. The existing area/band fill opacity behavior and missing contour picking remain open; no bypass publication is attempted. Other review boundaries remain in [PARITY.md](PARITY.md).

## Validation and measurement

Portable tests compare indexed results against exhaustive screen-distance searches over randomized datasets, linear/logarithmic axes, inversion and transposition. Additional cases cover duplicate coordinates, NaNs, zero-radius/narrow logarithmic spans, immutable source ownership, index reuse, offscreen/transparent/legend exclusion, allocation limits, obsolete history, shared axes and financial view ownership.

Both existing browser backends run additional real-pointer scenarios at DPR 2: dense sample selection, retained index across theme/zoom, unchanged GPU upload counters during pointer-only movement, duplicate-series ties, transparency, legends, secondary inverted logarithmic bindings, replacement/exit clearing, and bounded streaming candidate work. The diagnostic export reads the public state; it does not select samples or mutate control values.

`tests/FastChartsScene.Benchmarks` compares 64 queries over a fixed 100,000-sample random cloud against exhaustive search, verifying the same original indices before timing. Six warmups precede fifteen samples. It reports warm-query median CPU time and allocations, plus the separate one-time index build. CI's `core-results/performance.json` contains revision-specific measurements. These are **CPU microbenchmarks**, not presented FPS, hardware GPU throughput, whole-app latency, or a comparison against LightningChart. Browser CI uses a software graphics adapter; native runtime/hardware and cross-browser acceptance remain separate work.
