# Resizable cursor result panes

The reference TradingChart UI lets users drag segment dividers to allocate workspace:
https://lightningchart.com/net-charts/docs/inDetail/trading-chart/
FastChartsScene adds the same kind of workspace adjustment to its docked multi-series
cursor table. This is an independent layout, not an exact copy of the floating legend
or an implementation of the full original segment-layout API.

## Reusable native control

`CursorResultsTable.ResizeEnabled` is a bindable opt-in. Its six-DIP native Thumb emits
`ResizeRequested` with a `CursorTableResizeEventArgs` describing a height change or reset.
Positive changes grow a bottom-docked pane. The table does not find or mutate a chart,
parent Grid, window or filesystem; a host implements its own size policy.

```csharp
var table = new CursorResultsTable { ResizeEnabled = true };
table.ResizeRequested += (_, e) =>
{
    double current = double.IsNaN(table.Height) ? 210 : table.Height;
    table.Height = e.Reset ? 210 : Math.Clamp(current + e.HeightChange, 140, 520);
};
```

Dragging up requests growth and dragging down requests shrinkage. The grip is keyboard
focusable: Up/Down adjust twelve DIP, Page Up/Down adjust forty-eight DIP, and Home resets.
Double-click also requests reset. Keyboard events are handled by the grip, not propagated
to a chart's Home-to-fit shortcut. Hosts can leave resizing disabled; the grip then has no
layout footprint. Resizing does not change sampled values, Hold or CSV contents. Pointer focus is
established through Thumb's native DragStarted event, since Thumb handles PointerPressed
before ordinary routed event handlers. The explorer accumulates deltas against its
assigned row size, not delayed ActualHeight, so rapid input does not lose adjustments.

## Explorer layout and limits

The explorer starts at 210 DIP. It clamps the table to 140–520 DIP when space permits,
reserving at least 240 DIP for the chart. On unusually short windows, it reserves 55% of
the remaining space for the chart and lets the table shrink below its normal minimum.
The Details panel is subtracted before assigning space. A smaller temporary viewport does
not overwrite the preferred table height; expanding the window restores that height.
Closing/reopening the table preserves its size preference but resets Hold and sampled rows,
as before. Height preference is session-only and does not change chart JSON.

The native row containers are 28 DIP, with an explicit vertical scrollbar. The large-list
regression scrolls the actual viewport to its last row instead of assuming browser wheel
delta values map one-to-one to Uno offsets. Its measured row heights, realized container
count and scrollbar extent are retained in evidence. No diagnostic assigns selection or
scroll offsets to bypass input validation.

## Validation and remaining work

Native pointer tests resize the pane and compare chart and table sizes. Native keyboard
tests cover adjustment and reset. Short-window and Details-toggle tests verify clamping,
restoration, unchanged chart serialization and retained row objects. These run at DPR 1
and 2 with WebGL and explicit Skia, in addition to all existing table tests. The isolated
NuGet consumer compiles the public resize event and property API.

The limits are layout safeguards, not FPS or latency guarantees. Changing chart dimensions
requires its ordinary frame compilation; this is not zero-copy resizing or a GPU throughput
claim. Full property/API/pixel parity, floating legends, arbitrary segment dividers, native
runtime/hardware and wider browser/input acceptance remain open. Previously excluded
renderer/contour/fill-opacity/measured-margin work is unchanged.

## Row update review

Rows now retain each formatted numeric cell when unrelated title, color or axis metadata
changes. Color-only changes also retain full-precision detail text, and changed-row
notifications share immutable event arguments. Changed prices still refresh their own
G9 cells and round-trip detail values; unchanged rows publish no notification. Native
control probes verify string and row identities, swatch changes, exact details and event
counts under each renderer/device-scale pair. This avoids redundant formatting; it does
not claim every changed-row update is allocation-free.
