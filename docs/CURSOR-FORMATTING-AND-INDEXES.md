# Cursor number formats and bounded cold indexes

## Native result presentation

The cursor table's **Format** button opens a compact native editor. Choose Compact
(G9), Exact (R), or Scientific (E6), or enter separate invariant .NET numeric formats
for X and Value. For example, X `F2` and Value `0.000 'mV'` customize the two columns.
Apply validates both fields before changing anything. Invalid drafts remain editable;
Cancel leaves the previous formats unchanged. Empty fields reset to G9.

This is display formatting, not data conversion or a unit transformation. Appended
literal units are text supplied by the host/user. Original snapshots, interpolation
endpoints, full-precision details and round-trip CSV are unchanged. Hold remains held
when formats change. The native rows and their selection are retained. Ellipsized
cells and titles expose their full display text in tooltips; selected details remain
full precision. Formatting is session state and is not saved into chart JSON.

The official result-table customization workflow is described at
https://lightningchart.com/net-charts/docs/inDetail/data-cursors/ . This independent
implementation adds bounded per-column numeric formatting; it is not arbitrary result
content templating, a pixel-identical floating legend or full LightningChart parity.

## Reusable APIs

```csharp
var table = new CursorResultsTable
{
    NumberFormatSelectionEnabled = true,
    NumberFormat = new CursorValueFormat("R", "E6")
};
```

`CursorValueFormat` is immutable, value-equatable, portable and exposed by the
Interaction package. `Default`, `RoundTrip` and `Scientific` are shared presets.
`FormatX` and `FormatValue` use invariant culture. The existing axis numeric-format
validator bounds precision, format length and custom output; control characters and
unbounded standard precision are rejected at construction, not at each pointer event.
`CursorResultsTable.NumberFormat` is bindable. A null dependency-property value resets
to Default. The native editor is opt-in for standalone hosts and enabled by Explorer.

Changing one column's format retains the other column's formatted string. Equal
format objects do not notify rows or regenerate text. Format-only edits retain exact
detail strings, source values and swatches, and never query a chart or rebuild geometry.
Changed display text does allocate strings; this is not an allocation-free UI claim.

## Cold freeform cursor index

Unordered interpolation previously built growable segment/node lists and then copied
them to retained arrays. Internal nodes also rescanned all descendant segments. The
revised constructor first counts eligible adjacent pairs and then allocates exactly one
segment array and one correctly sized node array. Leaf bounds inspect each retained
segment once; internal bounds merge child bounds. The sort and deterministic traversal
order remain unchanged. Source values are never reordered or copied into a new source.

All-gap sources retain empty arrays rather than sample-sized buffers. Sparse gaps retain
finite isolated endpoints without introducing connecting edges. Construction remains
O(N log N) because segment indices are sorted; the bounds-building pass is O(N).
Queries retain their existing semantics and zero measured warm managed allocation
contract. Worst-case all-crossing/duplicate workloads remain linear. Cold construction
still costs time and memory and is not incremental streaming indexing.

The pre-change CPU benchmark recorded about 67 MB of allocations for eight cold indexes
over 50,000-sample traces. New portable tests cap one 50,000-sample cold query below
3 MB and all-gap construction below 8 KB, retain warm-allocation assertions and compare
ordered/freeform outputs around leaf-split boundaries. Benchmark runs continue to verify
complete original endpoint/result equality before recording timings. Numeric results
must be taken from a successful run for the stated revision, not inferred from test
presence or used as a GPU/native-rendering throughput claim.

Four new browser scenarios per backend/device scale cover native Apply, held snapshots,
downloaded CSV identity, atomic invalid drafts, Cancel/presets, compact viewport actionability
and isolated retained-row contracts. Existing browser suites remain enabled; their
required totals become 197 WebGL, 192 Skia and 48 axis scenarios (437 altogether).
Native OS/hardware and broader-browser qualification remain separate work.
