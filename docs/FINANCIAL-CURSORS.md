# Original OHLC/volume data cursors

This independently implemented increment follows the documented selected-price and
per-series tracking workflows in LightningChart's [data cursor guide](https://lightningchart.com/net-charts/docs/inDetail/data-cursors/)
and [trading chart guide](https://lightningchart.com/net-charts/docs/inDetail/trading-chart/).
It retains FastChartsScene's own API, implementation and branding. It is not a binary,
source or exhaustive visual/API-compatible TradingChart implementation.

## Reusable model and immutable price views

```csharp
var candles = new FinancialSeries {
    Title = "Instrument", CursorField = FinancialValueField.Close,
    CursorTrackEnabled = true
};
candles.SetData([new OhlcPoint(4, 10, 16, 8, 12, 12345)]);
IDataSource closes = candles.GetPriceData(FinancialValueField.Close);
```

`Open`, `High`, `Low` and `Close` select an original price, with Close as the default.
`GetPriceData` returns a cached immutable XY view over the owned candle snapshot, not a
copy of its numeric arrays. It preserves the original X coordinates, order and indices.
An earlier view remains valid after `SetData` replaces the series. Invalid replacements
leave the previous data and views intact. New views are installed before synchronous
model notifications. Obtain views on the model's UI thread; the returned data is immutable.

`Series.CursorTrackEnabled` defaults to true. Turning it off excludes the series from
supported XY sample, regular heatmap and financial cursor queries, without hiding or
changing its geometry. This property does not add picking to unsupported series/views.

## Exact selected-price queries

```csharp
var interaction = new ChartInteraction(document);
FinancialDataHit? hit = interaction.HitTestFinancial(frame, chartLocalPointer, 12);
if (hit is not null) {
    OhlcPoint original = hit.Sample; // Original X, OHLC and volume, not display rounding.
    int originalIndex = hit.Index;
}
```

The tolerance is a Euclidean distance in chart-local device-independent coordinates.
Queries return the nearest eligible original selected-price coordinate, not a price
interpolated between candles or a candle-body intersection. Each candidate uses its
series's bound X and Y axes and pane. Logarithmic/inverted axes, secondary bindings and
stacked panes retain original values. Offscreen selected-price coordinates, stale frames,
legend overlays and hidden/untracked series are excluded. OHLC strokes of width zero
are not pickable; filled candlestick bodies still exist at zero stroke width.

Equal distances prefer the last drawn series and then its lowest original candle index.
The control arbitrates financial and ordinary XY hits by the same distance/draw-order
rule. A financial hit includes the entire original `OhlcPoint`, chosen field, data/screen
coordinates, distance, series identity, bound axis IDs and pane bounds.

## Query and presentation costs

Nondecreasing X data uses binary searches for the visible range and projected tolerance
window. Windows of at most 2,048 candidates are scanned directly. Larger windows,
including large duplicate-X groups, and large unordered data sets use a retained
source-space `PointSpatialIndex`. At most four current price views are retained per
series snapshot; each queried field can have its own index. Initial index construction
has a nontrivial data-sized CPU/memory cost. Queries are not unconditionally logarithmic:
dense/degenerate input may require more work. Source arrays are never reordered.

Indexes are keyed weakly by immutable source identity, survive viewport/theme/title
changes, and can be released explicitly with `ClearPickingCache`. Replacement gives new
source identities. Uno unload clears query caches. `FinancialIndexBuildCount`,
`LastFinancialQueryTests` and `LastFinancialQueryVisits` expose actual query work.

The overlay formats its text only when the selected candle/field or document revision
changes. Pointer jitter over the same candle reuses the text and does not upload chart
geometry. `FinancialTextFormatCount` records formatting passes. The diagnostic benchmark
compares 16 queries over 50,000 unordered candles against exhaustive original-price
lookup, verifying identical full results before timing. It excludes initial indexing and
scene rendering; it is not a frame-rate, hardware-GPU or reference-product comparison.

## Native control and Properties Editor

`ChartControl.FinancialHit`, `FinancialHitChanged`, `FinancialCursorText` and
`FinancialCursorBounds` expose the selected original data and displayed readout. The
seven-line table contains title/index, X, Open, High, Low, Close and Volume; the selected
price is highlighted. Price/X formatting follows the bound axes, while raw values remain
available in `Sample`. The readout is also exposed through the control's automation item
status. The cursor crosshair and table are confined to the relevant pane. On small panes
the table is clipped or omitted rather than drawn outside the pane; the public readout
still contains the full text. Very long titles/formats may be clipped inside the table.

The compact Properties Editor exposes `CursorField` for financial series and
`CursorTrackEnabled` for supported XY series. These are actual native choice/checkbox
controls. Pointer exit, document replacement, unload or loss of a valid sample clears the
financial readout. Host data replacement refreshes the selected original candle. Cursor
overlays are transient: PNG/SVG chart exports exclude them, as existing cursor exports do.

## Persistence and package reuse

Version-one chart JSON adds optional `cursorTrackEnabled` and financial `cursorField`.
Legacy files retain true/Close defaults. Invalid booleans and field names are rejected by
the existing atomic reader. Candle arrays and volume remain unchanged. Saved/extracted
projects carry the settings through the same serializer; all-data CSV retains original
OHLC/volume. Public API use is compiled in the isolated package-only consumer.

## Validation and remaining scope

Portable regressions cover all four fields, immutable ownership/no per-call copies,
replacement notification order, duplicates/ties, clipping and stale/invalid queries,
secondary/log/inverted/stacked axes, ordered-window bounds, cache lifetime, randomized
exhaustive-oracle comparison, opt-out geometry preservation and legacy JSON defaults.
Native browser scenarios use real pointers, choice selection, checkboxes and downloads
under WebGL and same-build Skia at DPR 1/2. They check displayed pixels/text, all fields,
retained indexes/text, cross-series priority, pane confinement, host replacement, saved
settings/data, narrow layout and exit/replacement cleanup. Read-only diagnostic probes
report the public state and coordinates; none assign a synthetic hit or bypass an editor.

The original suites remain enabled, with expected browser totals increased to 145 WebGL,
140 Skia and 48 axis scenarios. Source, result receipts and aggregate validation continue
to bind the exact single build artifact. A build or added test is not a claim of a passed
runtime check; completed run evidence is recorded in the pull request.

This does not implement simultaneous multi-series cursor tables, X-only/interpolated
tracking modes, retained crosshair collections, trading indicators, calendar-session
compression, 3D financial picking or the complete reference TradingChart UI. It includes
no previously blocked renderer, contour, fill-opacity or measured-axis-margin changes.
Existing candle geometry is untouched. GPU composition still uses readback; software CI
is not native-OS, hardware-performance or broader-browser acceptance. Full parity remains
unfinished.
