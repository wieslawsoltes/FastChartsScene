# Interpolated cursor values with original endpoint provenance

The official LightningChart line-series cursor distinguishes point snapping from
interpolated tracking between adjacent samples:
https://lightningchart.com/net-charts/docs/inDetail/line-series-cursor/
Its data-cursor workflow also combines series colors and names with numerical readouts:
https://lightningchart.com/net-charts/docs/inDetail/data-cursors/

FastChartsScene independently implements an opt-in **Interpolated** mode in its native
bottom-docked cursor table. This is workflow alignment, not binary/API compatibility,
a pixel-identical floating legend, or full LightningChart feature coverage.

## Explorer workflow

Open **Cursor table**, then choose **Nearest X** or **Interpolated** with the native
compact selector. Nearest X remains the default and returns original samples. In
Interpolated mode, eligible linear/stepped line rows display **Calc Y**, the calculated
X/value and both original endpoint indices. Other rows explicitly remain original.
Column headings change to **Result X / Result value** rather than mislabelling a
calculation as an original sample. Select a row for full-precision details containing
the result, method, original endpoint indices and original endpoint coordinates.

Hold copies the displayed values, including their provenance; CSV exports that snapshot.
Changing tracking mode clears Hold and the old table before requesting fresh values.
Document replacement/closing still clears snapshots. Resizing the table and changing
mode do not mutate chart JSON, source data or single-cursor financial settings. The mode
is session UI state; extracted chart documents do not silently acquire a mode property.
The selector is keyboard accessible and rows remain virtualized at 28 DIP.

## Reusable query and control APIs

```csharp
var interaction = new ChartInteraction(document);
var values = new List<SeriesCursorValue>();
interaction.QuerySeriesCursorValues(frame, pointer, values,
    SeriesCursorTrackingMode.Interpolated);

foreach (var value in values)
{
    // Always an original sample, or the FIRST original interpolation endpoint:
    var firstIndex = value.Index;
    var firstPoint = value.Data;
    // The display result. Equal to Data for original-only rows:
    var result = value.ResultData;
    if (value.Interpolation is { } calculation)
    {
        var secondIndex = calculation.EndIndex;
        var secondPoint = calculation.EndData;
        var method = calculation.Method;
    }
}
```

The existing three-argument query and nine-argument `SeriesCursorValue` constructor
remain available. The three-argument query still means original nearest-X sampling.
The additional interpolation record contains value copies, not mutable source references.
Reuse the caller-owned list for warm queries; make a copy for a retained snapshot.
Invalid arguments/modes leave the list unchanged; stale/off-plot queries clear results.
The document, controller and output remain UI-thread-affine.

`ChartControl.CursorValuesTrackingMode` is a bindable enum property. It defaults to
`NearestX`; the existing `CursorValuesEnabled` opt-in and read-only live result/event API
remain unchanged. `CursorResultsTable.TrackingMode` and `TrackingModeChanged` communicate
the requested mode; opt into the native selector with `TrackingModeSelectionEnabled`.
The table neither finds nor owns a chart: a host wires the mode event to its chart's
property and forwards the chart's values. The explorer additionally clears on owner
replacement. Subscribe/unsubscribe according to the host's lifetime.

Invalid enum values are rejected by both wrappers and raw dependency-property writes.
Raw invalid writes restore the previous mode without clearing valid rows or publishing
a mode event. Normal queries reuse one draft list. A reentrant host/source callback gets
a separate nested draft; generation, owner, revision and mode guards reject older work
without erasing a newer successful publication. Nesting is bounded to eight queries.
Pathological deeper recursion clears results instead of overflowing the stack.

## Numerical and family contract

For a visible, tracked XY line with a nonzero visible line stroke, the mode intersects
its **original adjacent segments** with the pointer's projected screen-X coordinate.
Linear, StepBefore and StepAfter are supported. On log/inverted axes, interpolation
follows projected straight/step edges, not unqualified arithmetic in raw data units.
Thus a log-X line from (1,2) to (100,20) crosses X=10 at Y=11 on a linear Y axis.
Exact step plateau values are copied from their original endpoints rather than needlessly
round-tripping through an axis inverse. Finite off-plot endpoints may form an in-plot
crossing, but an invalid logarithmic endpoint never forms an edge across a domain gap.

NaN gaps are not bridged and finite source runs are not extrapolated. A finite isolated
knot is returned only at its exact projected X. Multiple crossings are ranked by vertical
screen distance; exact original knots win equal-distance ties, then the lowest original
point/segment index wins. Vertical steps/duplicate-X segments choose the nearest visible
point on that vertical edge and retain both source endpoints when the result is calculated.
The hovered pane, each series' bound axes, visibility/tracking opt-out and legend exclusion
remain in force. Across series, document order is retained rather than choosing one winner.

**Financial, scatter-only and MonotoneCubic series remain explicitly original nearest-X
results.** Cubic tracking is not replaced by a straight-line approximation, and OHLC or
volume data is never interpolated. Unsupported other families retain their existing
exclusion. Full-source linear/step intersections are not a promise to hit every triangle
of a reduced/decimated displayed polyline or the painted stroke's thickness/fill area.
No persistent graphical multicursor, user-defined expression, data editing or complete
reference cursor API is introduced.

## CSV compatibility and safe metadata

When every row is original, the existing 13-column header remains unchanged:
`SeriesId,SeriesTitle,Index,X,Value,Field,XAxis,YAxis,Open,High,Low,Close,Volume`.

When any row is calculated, seven columns are appended:
`ResultKind,ResultX,ResultValue,EndIndex,EndX,EndValue,Interpolation`.
The existing Index/X/Value columns **always retain the first original endpoint**.
ResultKind says `Original` or `Interpolated`; calculated coordinates are only in the
separate result columns. End columns and method are empty for original-only rows.
Doubles use invariant round-trip `R` formatting. Quotes/commas/newlines are escaped.
Potential spreadsheet formulas in textual IDs/titles/axis names are prefixed with an
apostrophe (including leading-whitespace formula prefixes); numeric columns are not
converted to text or rounded. JSON/source data and on-screen titles are unchanged.

## Performance and validation scope

Ordered sources use binary searches on projected X rather than inverse-transforming a
pointer and potentially rounding across a knot. Ordinary queries test only the adjacent
segment(s), with no cold segment-index construction. Dense duplicate groups can require
linear work. Unordered sources retain a weak-key conservative segment-bounds index that
sorts indices, never source values. It is reused across viewport/axis changes, discarded
on explicit `ClearPickingCache`, and replaced naturally with a new immutable source.
Cold construction allocates; a worst-case freeform dataset with every segment crossing
the cursor still needs O(N) work. The index is balanced, with leaves of at most 16 segments.

Portable regressions cover provenance, steps, duplicate X, clipping/gaps, log/inverted
secondary axes, copied snapshots, deterministic seeded ordered/freeform agreement,
retained indexes and zero measured warm managed query allocation. UI regressions exercise
actual native selector pointer/keyboard input, painted labels, Hold, downloaded CSV,
row/text/upload reuse, compact viewports and a same-owner reentrant mode callback.
Read-only diagnostics do not assign native selection or pointer positions. Isolated
native probes check enum rollback, formula-safe CSV and exact row/provenance contracts.
All pre-existing tests remain enabled; browser minima grow by six scenarios per backend
and device scale. A test's presence is not a claim that an uncompleted CI run passed.

The diagnostic benchmark compares a full original-segment scan with the reusable query
for sixteen pointer positions over eight 50,000-sample traces. Both paths verify complete
original endpoint/result equality before timing. It records separate single cold queries
and warm medians, allocations and tested segments. The unordered fixture swaps adjacent
X pairs; it is localized freeform data, not a worst-case all-crossing test. Scene generation,
GPU rendering, native table updates and pointer-to-display latency are excluded. No
LightningChart throughput comparison or universal speedup is claimed.

Previously excluded renderer/contour/fill-opacity/measured-margin work is unchanged.
GPU composition still includes readback. Native OS/hardware, wider-browser and complete
accessibility/touch qualification remain separate acceptance work.
