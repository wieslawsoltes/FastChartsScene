# Original-price nearest-X tracking

This increment extends the independent financial data cursor with explicit horizontal
snapping. Reference workflows are documented in the official LightningChart .NET
[data cursor](https://lightningchart.com/net-charts/docs/inDetail/data-cursors/),
[line-series cursor](https://lightningchart.com/net-charts/docs/inDetail/line-series-cursor/)
and [TradingChart](https://lightningchart.com/net-charts/docs/inDetail/trading-chart/) guides.
Their full interpolation, multi-cursor and styling APIs are not reproduced by this mode.

## Model and exact selection contract

```csharp
financial.CursorSnapMode = FinancialCursorSnapMode.NearestX;
financial.CursorField = FinancialValueField.Close;
FinancialDataHit? result = interaction.HitTestFinancial(frame, pointer);
// result.Sample is an original OHLC/volume record, not an interpolated price.
```

`NearestPoint` remains the default and uses the existing circular logical-pixel tolerance.
`NearestX` selects a visible original candle anywhere in that series's assigned pane.
It minimizes screen-X distance first, then screen-Y distance, then original index.
Both coordinates of the selected price must be inside their bound axis ranges. A closer
X whose selected price is clipped is skipped; nonpositive logarithmic coordinates and
unsupported/hidden/untracked series remain ineligible. The original OHLC record can still
contain other fields outside the viewport. Gaps between candles do not create new values.
At the ends of the data, snapping may select an existing visible endpoint; it does not
extend the data or manufacture a value at the pointer's X coordinate.

Selection uses the bound axis projections, including inversion and logarithmic scales.
Duplicate-X groups are not averaged. Original coordinates, source order and candle index
remain intact. The result's `SnapMode` identifies the method and `Distance` is still the
full Euclidean screen distance, not only its horizontal component.

Each financial series first supplies its candidate according to its own mode. Candidates
from different financial series and the existing XY cursor then compete by full screen
distance, with the later drawn series winning exact ties. This is not a globally X-ranked
all-series result table. Mixed modes never enlarge another series's nearest-point radius.
`CursorTrackEnabled = false` excludes a series without hiding its geometry.

## Reusable data query and performance

`PointSpatialIndex.NearestX(xAxis, yAxis, plot, pointer, radius, transpose)` exposes the
same lexicographic screen-X/screen-Y/index query independently of charts and Uno. Its
optional radius limits horizontal distance only; the default allows the whole plot.
Transposition changes coordinate mapping, not the meaning of screen-X priority. It uses
the same immutable nodes as `Nearest`, with no per-query heap storage or numeric copies.
Axis changes, inversion and viewport size do not rebuild the index. The source must stay
immutable. Worst-case queries can still visit every finite point.

Financial queries on ordered sources binary-search projected X and scan outward only
until the nearest eligible group is resolved. Ordinary isolated coordinates need no
spatial index, avoiding its cold construction cost. The initial scan is capped at 2,048
samples before falling back to the exact source-space tree for large duplicate/clipped
groups. This is a bound on the preliminary scan, not a bound on total index query work.
Large unordered sources use a retained index. Previously built indexes are reused across
snap modes, pan/zoom/resize and field revisits of the same immutable price view. Replacing
source data creates a fresh view and index; clearing picking caches releases retained work.

The benchmark compares full original-record/index results against an exhaustive oracle
for 50,000 ordered and unordered candles. It reports a separate single cold first query
and median warm batches of sixteen queries. Cold includes view/index/result construction;
fixture creation and rendering are excluded. Financial hit records still allocate. These
are CPU diagnostics, not chart FPS, GPU throughput or a LightningChart comparison.

## Native explorer controls and presentation

The compact Properties Editor exposes **Series → CursorSnapMode** beside **CursorField**.
The existing native ComboBox, name/value layout, contextual help, selected-series lifetime
guards and validation paths are reused. In nearest-X mode the readout's coordinate row is
labelled **Nearest X**, clarifying that it shows the selected sample, not the pointer.
The existing source-price crosshair, field highlight, seven-line OHLC table, accessible
text and pane confinement remain active. Pointer-only movement reuses source indexes and
unchanged text; it does not compile or upload chart geometry. Editing the model's mode
still performs the usual revision notification and chart refresh.

To try it: open Candlestick chart or OHLC bars, select NearestX, and move vertically away
from a candle. It continues tracking its selected original price. NearestPoint restores
the old proximity behavior. Numeric prices, source data and candle geometry are unchanged.

## Persistence and validation

Version-one financial JSON adds `cursorSnapMode`. Old files without it use NearestPoint;
unknown/case-mismatched/nonnumeric-enum strings and non-string values are rejected by the
existing strict enum reader. Saved documents and source-only extracted Uno projects retain
the mode with the exact candle data. Session-only series identifiers retain the existing
regeneration behavior. An older application may ignore the additive setting.

Portable checks cover exhaustive equivalence across ordered/unordered/log/inverted and
transposed data, clipped/gap handling, horizontal-radius boundaries, duplicates, old-mode
tolerance, no-index ordered queries, retained indexes, source identity, enum validation,
geometry retention and saved/extracted settings. Browser tests exercise the actual native
mode selector, far-pointer readout, duplicate/clipped fixtures, secondary panes, index/text/
upload retention, file downloads, invalid import atomicity, mobile resize and exit cleanup.
All existing browser scenarios remain enabled. The new scenarios run at DPR 1 and 2 under
WebGL and explicit Skia using the single revision-verified CI artifact.

This does not add interpolated line tracking, persistent cursors, candle-body picking,
all-series tables, full original visual/API parity or native hardware qualification.
Previously excluded renderer/contour/fill-opacity/measured-margin work remains excluded.
GPU composition still includes framebuffer readback; CI uses a software graphics adapter.
