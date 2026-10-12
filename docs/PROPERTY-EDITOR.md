# Compact property editing and lifecycle review

This increment follows the official LightningChart Interactive Examples **Properties
Editor** layout with neutral, compact name/value rows and expandable categories.
FastChartsScene uses its own API and editors; this is not a claim of an exhaustive
LightningChart property tree or pixel equivalence.

## Reusable control

`FastChartsScene.Controls.Uno.PropertyGridControl` accepts caller-supplied native editors:

```csharp
var grid = new PropertyGridControl();
using (grid.BeginUpdate())
{
    grid.Add("Document", "Title", titleTextBox, "Displayed chart title");
    grid.Add("Document", "Visible", visibleCheckBox);
}
grid.Filter = "title";
```

The host owns its model bindings, validation and editor events. Editors have one parent;
`Clear` releases that parent so controls can be reused. Duplicate category/name pairs,
shared editors, and already-parented controls are rejected before attachment. The control
is UI-thread-affine, has a 512-row bound, and is intended for bounded property sheets,
not general-purpose virtualized data tables.

`BeginUpdate` batches construction into one linear filter/layout-visibility pass.
Identical case-insensitive filters do no additional filtering. A changed filter searches
category/name/help text without reconstructing controls, so text drafts survive filtering.
Searching temporarily reveals matching collapsed groups, then restores each group's
previous expansion state when the filter is cleared. `FilterPasses` and row counts are
diagnostics, not frame-rate measurements.

## Explorer integration

The inspector provides Chart, Camera (for spatial views), Axis and Series categories.
It adds editing for subtitle, axis scale/inversion/side/weight/title/units, series color
and axis bindings, line interpolation/markers/baseline, bar layout, financial style,
spatial point size, and pie/doughnut properties. Existing intensity palette/range editors
are reused, not replaced with static displays. The chart view itself is read-only here:
switching to a view incompatible with the current series is not offered as a valid edit.

A native selector chooses the axis and series being edited. Only the selected series'
editors and refresh callbacks are constructed: importing many series no longer creates
a complete form for every series. Selecting another series does not modify chart data.
This bounds editor creation independently of series count; selector metadata still has
O(series count) storage. The browser regression uses a 128-series document.

Min/max inputs form an explicit draft; Apply range validates and commits both bounds in
one AxisRange assignment. An invalid range or logarithmic domain leaves the model and
rendered chart unchanged, with an inline error. Reset draft restores the actual viewport.
Pan/zoom updates clean range fields. Focused text and dirty range drafts are not replaced
by frame refreshes. Ordinary text commits on focus loss only after an edit, avoiding
accidental round-tripping of a high-precision color through its displayed 8-bit hex value.
Model observers supplied by application hosts are not a transactional rollback boundary.

The full reference property tree, data-array editors, arbitrary object reflection,
collection creation/reordering, and graphical annotation manipulation remain separate
work. This adapter edits the stated existing FastChartsScene properties.

## Gallery fixes

Unloading after explicit disposal no longer cancels an already-disposed token source.
Cleanup is idempotent; public mutations after disposal throw before touching UI state.
SetModel validates a selected ID before detaching the previous model. Refreshing previews
walks realized containers rather than only the first 128 source entries, preserving
scrolled-view refresh without a full-catalog scan. Retained selection metadata is released
on disposal. The previously interrupted native favorite symbols were recovered in PR #6;
the chart header now also uses a supported symbol instead of an unavailable text glyph.

## Validation boundaries

Browser checks use real pointers/keys for group expansion, search, title/range edits,
axis inversion and series selection. The direct lifecycle probe explicitly creates,
loads, disposes and removes a real gallery, and verifies property batching/parent release.
Existing chart, intensity, native-package consumer and extracted-project builds remain
enabled. CI browser adapters are software-based; these tests do not establish hardware
throughput or native OS runtime acceptance. Previously excluded renderer changes and
full-product parity are not claimed by this increment.

## Presentation and edit review (2026-10-12)

The official selected-example screenshot shows a compact Properties Editor with a
mode toolbar and a selected-property description below the scrolling property list:
https://lightningchart.com/net-charts/docs/getting%20started/runningDemo/

The reusable property grid now exposes `SortMode` (`Categorized` or `Alphabetical`),
`NameColumnWidth` (64–220 DIPs), `SelectedProperty` / `SelectedPropertyChanged`, and
`SelectProperty(category, name)`. Row dividers support real pointer dragging. Alphabetical
order is ordinal case-insensitive by name, then ordinal by category for equal names.
Switching modes retains the exact editor controls, drafts, and category expansion state.
Filtering does not reparent controls. Hosts should commit/cancel their active input
before programmatically changing presentation when their focus-loss semantics require it.

The explorer adds mode commands and a fixed description area, including in the compact
Inspector dialog. Descriptions refer to the independently implemented FastChartsScene
properties; they are not copied reference-API descriptions. The name/value width is
clamped on narrow hosts to reserve space for values. No per-object default-value reset,
recursive collection editor or complete reference property tree is implied.

Row/editor duplicate checks and editor lookup use dictionaries/identity sets rather than
linear scans. A construction batch performs one layout pass and one filter pass; the
512-row native-control regression checks this explicitly. Alphabetical ordering is cached
until rows change. Repeated unchanged sort/filter settings do not repeat layout/filter
work. Category arrows reuse their rotation objects. These are work-count/allocation-source
improvements, not hardware FPS or end-to-end latency measurements.

The inspector review also prevents callbacks from an obsolete editor generation from
writing to a current document, skips refresh callbacks for detached/disposed documents,
resets axis-specific drafts when the host changes the selected axis, restores rejected
numeric values while displaying the error, and implements Escape-to-cancel text editing.
There is still no general rollback boundary for exceptions thrown by host model observers.
Tests explicitly distinguish real pointer/keyboard edits from host-driven axis selection.

### Intensity refresh review

The intensity editor now retains uncommitted custom-palette, manual-range and
missing-color drafts across unrelated series notifications. Applying one draft
commits only that draft; a separate **Reset editor drafts** action discards drafts
without modifying chart data. Replacing the bound series clears old-object drafts.
Invalid edits remain available for correction and do not mutate the model.

Palette text is formatted only when the immutable palette changes or a reset is
requested. `PaletteTextBuildCount` exposes actual conversions. The browser test
performs 32 same-series host title updates while drafts are present and verifies
zero additional palette conversions, intact drafts, failed-apply atomicity and
explicit reset. This is a work-count regression, not an end-to-end latency claim.
The existing invalid-palette/domain/range tests now wait for completion and stable
control placement before checking the unchanged model; no assertions were removed.
