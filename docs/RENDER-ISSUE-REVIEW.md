# Fill opacity and measured XY axis labels

## Issue #5 — composited fill opacity

Area and band fill alpha is now the product of base series alpha and the existing
fill factor (0.25 for areas, 0.32 for bands). Fully opaque series keep their appearance;
zero-alpha fills emit no fill triangles. Stroke alpha stays the original series alpha.
Rectangle annotations had the same defect and now use base alpha times 0.15. No palette,
source data, persisted setting, single-sample picking rule or transparency sorting changes.

All backends and exports consume the same packed straight-alpha geometry. Portable
regressions check every area interpolation mode, band fills, annotation fill/stroke,
raster pixels and SVG alpha. Native browser scenarios compare real OpenGL/Skia chart
pixels, downloaded PNG pixels and downloaded SVG geometry for alpha 0, 0.5 and 1 at
DPR 1 and 2. Skia's eight-bit color conversion permits a small pixel rounding tolerance.
These are source-over tests, not order-independent 3D transparency acceptance.

## Issue #13 — text measurement and margins

`SceneCompiler.TextMeasurer` accepts the host-owned `ITextMeasurer`. Its immutable
`TextMeasurement` supplies baseline-relative ink bounds and advance. Measurements are
validated and retained in a bounded 2048-entry cache; unchanged frame requests return
before layout or measurement. Replacing a measurer invalidates layout but does not
throw away immutable data indexes. Call `ClearCache` after changing fonts in-place.

The Uno control automatically shares the Skia text renderer's **actual fonts** with
its compiler for both OpenGL geometry and explicit Skia fallback. Standalone hosts can
use the same public API without making the core rendering package depend on Skia:

```csharp
using var renderer = new SkiaFrameRenderer();
var compiler = new SceneCompiler { TextMeasurer = renderer };
var frame = compiler.Compile(document, width, height);
byte[] png = renderer.ExportPng(frame);
string svg = SvgExport.Write(frame);
```

A null measurer deliberately preserves legacy fixed margins for backend-neutral clients
that have not supplied font metrics. The native control opts in automatically. No font
files, proprietary chart assets or native measurement dependencies are embedded in the
portable rendering package. `SKFont.MeasureText` provides the ink rectangle and advance:
https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skfont.measuretext

The measured path captures ticks once using the initial viewport, reserves X label
overshoots and Y label/title lanes, then compiles geometry using the final plot. It is a
single bounded pass, not an oscillating layout/automatic-tick feedback loop. Secondary
X rows use 44 logical pixels to separate titles from the next row's tick labels. Layered
Y axes reserve independent lanes; stacked/segmented panes retain their existing weights.

Axis tick marks and grid positions are preserved. Labels are prioritized at the two
extremes, then non-overlapping interior labels are painted. This may reduce the visible
label count without changing any source coordinate, tick value, original string or
number precision. Full decisions (including suppressed strings, metric bounds and
`overlap`/`outside-canvas` reasons) are available through `ChartLayout.AxisLabels`.
Oversized labels on impossibly small canvases are explicitly suppressed, not truncated,
rounded or drawn beyond the surface. The plot keeps positive dimensions. Automatic
margins and deliberate overlap handling align with the reference workflow described at
https://lightningchart.com/net-charts/docs/inDetail/axis-layout-options/
and https://lightningchart.com/net-charts/docs/inDetail/axis-class-properties/ .

PNG and native drawing use the same measured frame. SVG preserves complete strings and
records measured advances using textLength/lengthAdjust, so a viewer's default sans-serif
advance cannot move endpoint labels outside the reserved space. Typeface glyph appearance
can still differ between SVG viewers; full font-shaping/pixel identity is not promised.
This increment does not change circular/spatial axes, legends or colorbar typography.

## Validation and remaining scope

Fourteen additional portable tests cover fill alpha, pixels/export, measured bounds,
collision decisions, original strings, secondary axes, tiny panes, cache invalidation
and source-index retention. Five additional native scenarios per backend/DPR add twenty
axis-suite cases. No existing scenario is removed. Required totals become 382 portable
C# tests and 457 browser scenarios (197 WebGL, 192 Skia, 68 axis), plus 22 verifier tests,
both Uno targets, all twelve packages and isolated/extracted consumers.

Test presence is not validation success; runtime acceptance must reference a completed
run for the exact source revision. The broad tracker #2 remains open for full reference
API/example parity, specialized/volume/maps/spatial features, more series/axis styles,
editing/designer/printing, zero-copy/incremental GPU presentation, transparency sorting,
native OS/hardware/cross-browser/touch/accessibility and public package release policy.

The arranged frame releases its live axis/measurer references, retaining only copied
label decisions. Mutations made by a measurement callback reject the candidate frame
instead of stamping stale geometry with a newer document revision.
