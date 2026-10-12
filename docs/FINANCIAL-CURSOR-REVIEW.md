# Financial cursor notification and presentation review

This review follows the original OHLC cursor implementation on PR #17. It does not
change candle geometry or republish the separately excluded renderer/layout work.

## Correctness

`FinancialHit` and its text are synchronous model results. `FinancialCursorBounds`
belongs to the later overlay paint, not the model event. A changed hit now clears the
previous hit's rectangle before raising its event; a new rectangle becomes available
when the overlay paints. Hosts must tolerate null bounds while a changed hit is pending
presentation, and can use `RenderCompleted` when rendered bounds are required.

Cursor events may invoke host code. Querying all supported families before notifying
was not sufficient: a `ScalarHitChanged` handler could update the same document's
candle data, after which the control published its already-computed old financial
result. The control now checks document identity, revision, disposal and interaction
identity between callbacks. A same-instance change clears the obsolete batch; document
replacement retains its own cleanup/refresh path. The next valid frame queries the new
original data. No synthetic cursor assignment is used to test this behavior.

The browser host-notification regression places a real heatmap behind a financial series.
Native pointer delivery raises the scalar event; its one-shot host callback replaces
candle volume through `FinancialSeries.SetData`. The test records every subsequent
financial event and rejects the old volume, then verifies the new original volume.
Subscriptions are removed on completion or test failure.

## Browser validation fixes

The earlier WebGL mobile failure derived screen coordinates during resize: control
ActualWidth was new while its queued compiled frame still had the previous size. The
read-only readiness probe now requires browser viewport, page layout, chart size, frame
size and document revision to agree. The probe for source position and chart origin is
read in one JavaScript invocation. All pointer delivery remains native Playwright input.

The earlier Skia secondary-axis failure read a null rectangle between a synchronous hit
and its first paint. Tests now wait for painted readout bounds, rather than assuming two
requestAnimationFrame callbacks imply Uno has painted. Pane containment, original prices,
text pixels, tracking opt-out, independent default-fixture projection and all existing
assertions remain enabled. Scenario totals are unchanged by these extended assertions.

## Performance reporting

The equivalent-output 50,000-candle benchmark still reports 16 warm queries separately
from the newly recorded **single cold first query**. The cold measurement includes the
price-view object, retained index construction and financial-hit result. Fixture creation
and scene compilation are excluded. It is a single measurement, not a cold-query median.
Both indexed and exhaustive warm paths are checked for the same source index, original
candle and distance before measurement. Financial result records still allocate; this is
not a zero-allocation interaction API, a whole-frame rate or a LightningChart comparison.

Full product, original API/pixel, native runtime, hardware-GPU and cross-browser acceptance
remain open. Software graphics CI and framebuffer readback remain explicit boundaries.
