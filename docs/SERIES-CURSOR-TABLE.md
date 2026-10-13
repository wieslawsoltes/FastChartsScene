# Same-pane original-value cursor table

The **Cursor table** toolbar command opens a native docked table under the chart. It
shows one nearest-X original value for each eligible line/scatter or financial series
in the hovered pane, rather than discarding every series except the single closest one.
The reference workflow is LightningChart's [same-segment trading and indicator readout](https://lightningchart.com/net-charts/docs/inDetail/trading-chart/).
This is independent code, API and layout; the table is docked rather than a pixel-copy of
the reference's floating legend, and does not claim complete TradingChart parity.

## Reusable query and control APIs

```csharp
var values = new List<SeriesCursorValue>();
interaction.QuerySeriesCursorValues(frame, pointer, values);
// Reuse the list for the next pointer event. Copy it for a retained snapshot.
var table = new CursorResultsTable();
table.SetValues(values);

chart.CursorValuesEnabled = true; // off by default: do no extra queries until needed
chart.SeriesCursorValuesChanged += (_, _) =>
    table.SetValues(chart.SeriesCursorValues, chart.CursorValuesAreActive);
```

The query supports line/scatter (`LineSeries`) and financial (`FinancialSeries`) sources.
Bars, bands, error ranges, heatmaps, contours, circular/spatial plots and annotations are
not silently converted into these value types. Within each eligible series it minimizes
projected horizontal distance, vertical distance, then original index. It is original
sample selection, never interpolation, and never invents a value at the pointer's X.
Each series can return a different original X coordinate. Only the hovered pane and
visible source prices inside their own bound axis ranges are considered; opt-out via
`CursorTrackEnabled` is respected. Financial `CursorField` controls the selected price.
The table always uses nearest-X, independently of the single-cursor snap mode. Document
order is retained, and no cross-series winner filtering is applied.

The caller-owned list is replaced on each query. Invalid arguments leave it unchanged;
stale frames, legend hits and out-of-plot positions produce no values. Exceptions from
misbehaving custom sources are not a transactional rollback boundary. The existing
UI-thread-affine model and immutable `IDataSource` contracts still apply.

`SeriesCursorValue` is a value snapshot containing series identity/title/color, original
index and coordinates, axis IDs, selected field and optional complete OHLC/volume.
It contains no mutable source/axis references. A copied value survives future source
replacement. `ChartControl.SeriesCursorValues` is a read-only **live view**, not a frozen
list: use `ToArray()` for retention. Owner/active state and value changes raise
`SeriesCursorValuesChanged`; distance-only pointer changes do not. The control rechecks
ownership/revision after earlier cursor callbacks so their mutations cannot publish an
obsolete all-series result. Reentrant document replacement during clearing is guarded.

## Native table behavior

The standalone `CursorResultsTable` does not own or subscribe to a chart. `SetValues`
copies values into retained row objects; unchanged rows are not formatted or notified
again. The ListView virtualizes native row containers and supports vertical scrolling
and selection. The wide result columns scroll horizontally on compact viewports.
Colored series markers, aligned index/X/value/field columns and full-precision selected
record details make simultaneous values readable without rebuilding chart geometry.
The compact grid displays invariant G9 numbers; selected details and exported CSV use
round-trippable `R` numbers. The table does not yet honor arbitrary axis label formatting.

Moving outside the chart leaves the last sample explicitly labelled **Last sample**, so
its rows can be selected and exported. **Hold** freezes the displayed snapshot while
chart tracking continues; release it and move over the chart to refresh. This is a
value snapshot, not a persistent graphical cursor. The explorer clears snapshots and
Hold when the document is replaced or the table is closed. `Clear()` supplies the same
behavior to reusable-control hosts. Host code should call it on document replacement.

CSV contains all displayed source rows and complete OHLC/volume fields where available,
with quoted text and invariant numbers. Exporting a held table exports the held values,
not newly sampled chart data. It is a data interchange file, not a chart serialization.
Table open/Hold state is session UI state and is not written into chart documents.

## Performance review

The existing projected-X search is shared rather than duplicated. Ordered isolated
samples use binary search and a bounded outward scan without building an index. Dense
unordered or duplicate groups reuse the same source indexes as single-cursor queries.
The output list and table rows retain capacity. Warm query regression checks zero
managed allocation for mixed ordered-line/unordered-financial data. Cold source-index
construction, output growth, copied snapshots, CSV and native UI updates do allocate.

Axis lookup no longer allocates a capturing LINQ predicate or enumerator on every call.
It still scans the live axis collection and observes replacement/reordering immediately;
it does not introduce a stale ID cache. The benchmark checks original index/coordinate/
series equivalence before measuring 16 queries across eight 50,000-sample lines, with
first-query cost recorded separately. Measurements exclude scene/native rendering and
do not claim GPU throughput, pointer-to-display latency or reference-product performance.

## Validation boundaries

Portable tests cover values, clipping, opt-out, fields, secondary/log/inverted panes,
owned snapshots, invalid/stale inputs and allocations. Browser tests use native toolbar,
Hold, scroll, selection and download actions on the same revision-verified build in
WebGL and explicit Skia at DPR 1 and 2. They include a 128-series virtualized table,
mobile layout, row/text/upload retention, document replacement and a real host callback
that disables an overlay while financial cursor notifications are being delivered.
No prior browser suite is removed. Runtime success must be established for each revision.

Full API/property/pixel parity, all-family tables, interpolated or persistent cursors,
long-label layout and native/hardware/cross-browser qualification remain open. Previously
excluded renderer/contour/fill-opacity/measured-margin work is not included. GPU
composition still includes framebuffer readback and CI uses software graphics.
