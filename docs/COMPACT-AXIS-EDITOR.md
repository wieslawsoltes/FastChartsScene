# Compact axis tick properties

The reusable `AxisTicksEditor` now presents Mode, Numeric format and Date format
in the same compact, aligned native property/value grid used by the main inspector.
It reuses `PropertyGridControl` with an 88-DIP requested name column, resize support,
selection/help metadata and batched construction. The multiline tick records remain
full width, as do the existing Apply, Reset and inline validation controls.

This replaces three stacked heading/editor pairs with three adjacent property rows.
All editors retain their existing instances, draft tracking, automation names,
validation and axis binding. Merely arranging, selecting or scrolling them does not
write the chart model. The Date format help explicitly describes UTC/invariant text.

The existing browser input suite is retained. Four additional scenarios (WebGL/Skia,
DPR 1/2) verify aligned columns, usable value widths, compact row heights, unchanged
settings and screenshots after actual native input. These are layout and interaction
regressions, not an exhaustive pixel comparison with LightningChart.

Reference workflow: the official Interactive Examples Properties Editor:
https://lightningchart.com/net-charts/docs/beginners%20guide/api_overview/

No renderer or axis-margin change is part of this increment. Long-label overlap,
full reference tick styling, API parity and native/hardware qualification remain open.
