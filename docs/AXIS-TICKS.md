# Custom axis ticks, formatting and retained generation

This increment implements independent equivalents of the tick-position and label
customization described in the official [LightningChart .NET axis documentation](https://lightningchart.com/net-charts/docs/inDetail/axis-class-properties/).
It retains FastChartsScene's API and renderer. It is not a source/binary-compatible
implementation of CustomAxisTick or an exhaustive tick-style/property-grid port.

## Immutable core contract

```csharp
axis.TickOptions = new AxisTickOptions(
    AxisTickMode.Custom,
    [new(0, "Start"), new(2.5, "Quarter"), new(5), new(10, "End")],
    numericFormat: "F2");
```

The options own, sort and expose a read-only list of at most 512 unique finite tick
positions. Positions are original data coordinates, not normalized screen values.
Custom mode emits exactly the configured positions inside the axis range; there is
no density reduction or insertion of automatic ticks. An empty custom list deliberately
hides every tick. A null label uses the axis formatter; an empty string suppresses the
text but retains the tick/grid line. Labels are single-line text of at most 256 characters.
Offscreen and nonpositive log-domain positions do not appear. Inversion changes the
projection, not original values or source ordering. Secondary axes use their own settings.

Automatic mode preserves the existing tick-placement algorithms. The optional invariant
numeric format accepts G, F, N, E or P with precision 0–17, or R; empty means the original
adaptive format. Bounding precision prevents a format such as F999999999 from allocating
an enormous label. DateTime axes accept a validated single-line .NET date/time format of
at most 64 characters, evaluated in UTC with invariant culture; empty retains adaptive
calendar/time formatting. Category axes retain category labels for null/automatic text.
A custom explicit label takes precedence over both numeric and date format settings.

`AxisTickText.Parse` and `Write` are reusable, bounded conversion APIs. Each line is
`value|label`. With no pipe, the label is null. A trailing pipe preserves an explicitly
empty label; additional pipes belong to the text. Label whitespace is preserved. CR,
LF and CRLF are equivalent. Values use invariant round-trippable floating-point text.
Invalid values, duplicate positions, control characters and oversized input are rejected.

## Performance and correctness

`Axis.Ticks(target)` returns an immutable retained list for unchanged range, scale,
inversion, owned categories, option identity and normalized target. Custom mode ignores
automatic density. Warm identical calls allocate no temporary objects. Title, units,
axis side, pane weight and series updates do not affect tick content. Changing relevant
settings creates a new list; earlier callers' lists remain valid. `TickGenerationCount`
reports actual generation passes, not API call count.

Logarithmic normalization is computed when range/scale changes rather than once per
mapped point. It is updated **before** synchronous PropertyChanged observers execute,
so an observer never receives the new range with the previous normalization factor.
The existing adjacent-value stable logarithm/exp arithmetic is retained.

The benchmark compares 128 unchanged automatic axes to a frozen equivalent generation
loop and verifies the same full tick results before measuring. It reports diagnostic CPU
medians and allocations, not FPS, presented latency, hardware GPU speed or a LightningChart
comparison. A changed viewport still requires new ticks; a different numeric target can
also cause a cache miss. The cache, axis and scene/compiler contract remains UI-thread-affine.

## Reusable Uno editor and explorer

`AxisTicksEditor` is a reusable control with a bindable, caller-owned `Axis` property.
It provides native mode/format/custom-tick controls with an explicit Apply and Reset.
The whole candidate is parsed and validated before one TickOptions replacement. Invalid
input displays an inline error without changing the axis or rendered chart. Unrelated
model notifications retain pending drafts and cached text. Changing the edited axis or
resetting explicitly discards the previous draft, preventing edits reaching a new target.
The control detaches subscriptions on unload and releases them on explicit disposal.

The explorer adds a collapsed **Ticks and labels** section to its compact XY axis
property sheet. This keeps normal property navigation compact while exposing the new
settings when requested. No placeholder controls or pre-rendered chart image is used:
applying labels changes the shared compiled scene rendered by GPU and Skia/export paths.

## Persistence and export

The additive `tickOptions` object is written for X, Y and Z axes in version-one chart
JSON. Missing objects in old files retain automatic defaults. Reading validates each
option and charges tick coordinates against the document's shared numeric input budget.
Null versus empty labels survives exact JSON roundtrip. SVG escapes text, PNG uses the
rendered labels, and standalone project extraction embeds the edited settings through
the same chart JSON. Older FastChartsScene versions may ignore additive fields; forward
compatibility is not promised.

## Boundaries and validation

This increment does not add custom tick colors, lengths, grid-line styles, per-tick fonts,
multiline labels, overlap avoidance, tick-label rotation or scale breaks. The existing
3D scene has no full spatial tick-label rendering; storing Z-axis settings does not claim
that missing rendering. Circular views retain their existing limited graticule behavior.
The explorer editor is offered for XY views. Complete reference API, visual and interaction
parity remains open, as do the separately excluded renderer findings in issues #2/#5.

Portable regressions cover immutable ownership, exact clipping, logarithmic/inverted and
adjacent-extreme positions, null/empty labels, invariant formats, invalid option rejection,
cache invalidation/allocation, synchronous observers, bounded text parsing and saved-file
compatibility. The dedicated browser suite uses native pointers/keys on the current build
under both WebGL and explicit Skia at DPR 1 and 2. It checks changed label pixels, draft
retention, target changes, actual downloads, and bound logarithmic SVG positions. Direct
host-update probes only exercise public model notifications; they do not assign editor
values or bypass the Apply path. Existing browser suites remain enabled independently.

A successful build alone is not native OS runtime/file-picker or hardware acceptance.
CI uses a software graphics adapter and GPU presentation still includes readback.
