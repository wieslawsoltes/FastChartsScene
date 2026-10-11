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
