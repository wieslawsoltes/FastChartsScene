# Intensity color scales, colorbars and scalar picking

This guide describes FastChartsScene's independent API. It does not imply source, binary, pixel or exhaustive property compatibility with LightningChart .NET.

## Share a calibrated scale

`ColorMap`, `ColorStop`, `ColorScale` and `ResolvedColorScale` are in the renderer-independent Core package. Heatmap-derived series expose `ColorScale`, `ShowColorBar`, `ValueTitle` and `ValueUnits`. A scale is immutable and safe to share across several models. Replace a series' scale to edit it; the document revision then invalidates the compiled frame.

```csharp
using FastChartsScene.Charts;
using FastChartsScene.Core;

var shared = new ColorScale(ColorMap.Diverging, new AxisRange(-5, 5));
var reference = new HeatmapSeries
{
    Grid = new ScalarGrid(2, 2, 0, 10, 0, 10, [-5, -1, 2, 5]),
    ColorScale = shared,
    ShowColorBar = true,
    ValueTitle = "Reference",
    ValueUnits = "V"
};
var comparison = new HeatmapSeries
{
    Grid = new ScalarGrid(2, 2, 0, 10, 0, 10, [-3, 0, 1, 4]),
    ColorScale = shared,
    ShowColorBar = true,
    ValueTitle = "Comparison",
    ValueUnits = "V"
};
// Assign to document.Series and bind to the desired Y axes/panes.
reference.ColorScale = shared.WithReversed(true); // comparison retains shared
```

The two series share the same numeric calibration, not merely the same palette. With separate automatic ranges, equal displayed colors need not mean equal values.

## Custom maps and bands

Maps own their input stops and expose a read-only collection. Stops must be finite, strictly increasing, include offsets 0 and 1, and contain finite RGBA channels in [0,1]. A map accepts 2–256 stops. Presets are Heat (the original default palette), Grayscale, Blue–white–red and Thermal.

```csharp
var bands = new ColorMap("Alarm levels",
[
    new ColorStop(0, ColorRgba.Parse("2E9B5A")),
    new ColorStop(.6, ColorRgba.Parse("F2C94C")),
    new ColorStop(.85, ColorRgba.Parse("D84747")),
    new ColorStop(1, ColorRgba.Parse("D84747"))
], ColorInterpolation.Stepped);
reference.ColorScale = new ColorScale(bands, new AxisRange(0, 100));
```

Gradient interpolation is linear in the stored RGBA channels. Stepped interpolation is right-continuous: a stop's color applies from its offset up to the next stop. In the example, green applies below 60, yellow from 60 to below 85, and red from 85 onward. The stop at 1 is the exact maximum endpoint. `Reversed` changes the palette direction; numeric axis direction and numeric tick positions do not change.

## Ranges, logarithms and missing samples

A null `ColorScale.Range` requests automatic limits from the immutable grid. Linear mode includes every finite value. Logarithmic mode includes only finite positive values. A manually specified logarithmic range must have a positive minimum; invalid candidates throw before altering the model.

```csharp
reference.ColorScale = new ColorScale(
    ColorMap.Thermal,
    mode: ColorScaleMode.Logarithmic,
    missingColor: new ColorRgba(1, 0, 1),
    outOfRange: ColorOutOfRange.Transparent);
```

Automatic empty grids expose `HasValues == false`, no numeric ticks, and a “No data” bar label. A constant grid keeps its exact minimum/maximum, maps to the palette midpoint and gets a single real tick. A manual range stays available even when the grid has no valid samples, because it is an explicit calibration.

`MissingColor` defaults to transparent and covers NaN, non-finite lookups, and nonpositive logarithmic inputs. `ColorOutOfRange.Clamp` uses endpoint colors for finite outliers. `Transparent` hides finite values outside the chosen range; it does not rewrite the underlying data. The grid constructor still rejects infinite stored samples.

Color resolution is cached by grid/settings identity. Automatic ranges use the full grid, not just the currently visible viewport. Panning/zooming therefore does not silently change the calibration. Linear wide spans and narrow logarithmic spans have dedicated numerical handling and regression tests.

## Layout and rendering contract

Requested visible colorbars occupy a right-hand gutter separate from the data plot. Bars align with their Y-axis panes; several bars in one pane share its gutter vertically. Hiding a series or its bar releases unused gutter space. The bar includes title, units, numeric endpoints and scale direction. Constant/empty datasets have distinct presentations rather than invented extrema.

Regular heatmaps render flat cells centered on grid samples. The first and last cells terminate at the declared extents. A one-row or one-column grid covers its full declared dimension. Cell bounds are clipped to the data-axis range before logarithmic projection, so a partly visible cell is not dropped solely because its sample center is outside the log domain.

The same scale controls heatmap cells, contour levels, 3D surface vertices, waterfall endpoints and spatial-bar base colors. Colorbars show the unlit base colors. Surfaces/tubes interpolate those mapped vertex colors and apply lighting: they do **not** evaluate a nonlinear or stepped scalar transfer function at every fragment. Heatmap bilinear interpolation, GPU scalar textures, exact per-fragment spatial bands and irregular grids remain separate work.

The GPU and software raster paths discard fully transparent fragments without updating depth. This does not solve arbitrary partial-transparency sorting. Gradient colorbars use bounded tessellation (128 uniform subdivisions plus explicit stops); stepped bar boundaries use the actual stops.

## Exact regular-heatmap cursor queries

```csharp
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

var frame = new SceneCompiler().Compile(document, 900, 600);
var input = new ChartInteraction(document);
ScalarDataHit? hit = input.HitTestScalar(frame, new PointD(350, 250));
if (hit is not null)
{
    Console.WriteLine($"Cell {hit.Column},{hit.Row}; stored value = {hit.Value}");
    Console.WriteLine($"Stored position {hit.Data}; visible bounds {hit.CellBounds}");
}
```

Lookup is constant-time per eligible regular heatmap. It respects axis bindings, inversion, data clipping and pane membership. Boundary ties select the higher row/column. Heatmaps are checked in reverse draw order; fully transparent cells permit picking a heatmap underneath. Legend-covered cells are not reported. A painted NaN cell has a null value rather than an invented number. `Screen` is the visible cell center, while `Data` is the original stored sample position.

This query does not implement contour-line, spatial-surface or all-series scene picking. It ignores non-heatmap series except for legend occlusion. The Uno control exposes `ScalarHit` and `ScalarHitChanged`, displays a cell highlight/value tooltip, and refreshes the query when a frame changes under the cursor.

## Reuse the editor

```csharp
using FastChartsScene.Controls.Uno;

var editor = new IntensityScaleEditor { Series = reference };
// Add to any Uno page/panel. Series is a bindable dependency property.
```

The editor includes palette/interpolation/domain selection, reversal, automatic/manual limits, clamping/transparency, colorbar visibility/title/units, missing-color entry, and normalized custom stops. It reports invalid candidates inline through `LastError` and leaves the model unchanged. Numeric bounds and custom stops are applied explicitly through their buttons. Models and controls remain UI-thread-affine. The caller owns the series; the editor detaches subscriptions on unload/disposal.

The package-only consumer compiles this editor and scalar-picking code against produced NuGet packages for desktop and browser. Source-level references are not used by that consumer.

## Persistence and exports

Chart JSON stores normalized palette stops, interpolation, domain, reversal, optional range, missing/outlier rules, and colorbar visibility/title/units. These are additive version-1 fields. Older documents without the fields load with the default heat palette and hidden colorbar. An older application may ignore the fields; do not assume forward round-trip compatibility.

The reader applies existing character/numeric budgets plus the 256-stop limit, rejects invalid maps/domains, and does not activate executable types or load external palettes. Current-format write/read/write is canonical; session series IDs are intentionally regenerated as before.

PNG includes bars and labels. SVG includes the 2D bars and cells; its existing restrictions on depth-tested geometry and vertex gradients remain. All-data CSV preserves raw grid values and includes the mapped base color for each cell, including missing/outlier visibility.

## Validation scope

Portable tests cover mapping endpoints, thresholds, ownership, numerical extremes, log domains, empty/constant grids, shared cell geometry, clipping, inversion, pane placement, persistence, vector/data export and transparent depth behavior. The browser suite uses real mouse/keyboard edits, verifies known pixel colors and original sample values, and runs alongside all examples on GPU and explicit fallback builds. This is functional evidence on CI's software graphics adapter, not native runtime/hardware-performance or full reference-parity certification.
