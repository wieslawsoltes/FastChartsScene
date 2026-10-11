# Rendering, data and integration review follow-through

This extends the intensity/performance review in PR #3. The review inspected the existing data, model, geometry, interaction, controls, serialization/export, package and CI paths. Findings fixed here are listed explicitly; this does not certify every runtime or establish full LightningChart parity. Existing open boundaries remain in [PARITY.md](PARITY.md).

## Long missing-data runs

`EnvelopeIndex` previously stored every NaN index and returned every missing sample during reduction. A million-sample interval with only its two finite endpoints consequently selected a million indices, despite there being no line to draw across the outage.

The index now stores half-open missing runs. Selection searches directly for the first intersecting run, removes redundant NaN bucket endpoints in that run, retains one original NaN separator and both finite neighbors, and leaves unordered and small unreduced sources unchanged. Source samples are never rewritten. The index's block-count calculation also avoids integer addition overflow and rejects null inputs explicitly.

Regression coverage includes a million-sample outage reducing to exactly three original indices, a viewport entirely within the outage, linear/logarithmic and inverted axes, randomized finite islands, extrema, exact finite run endpoints, a bounded index allocation budget, and dense alternating gaps. Alternating finite/missing data can still require O(N) selected samples to preserve every discontinuity; no unconditional constant-size geometry claim is made. Initial indexing still scans the immutable input.

## 3D binding and control correctness

Surface, waterfall, spatial bar, point/line and mesh geometry now uses each series' `XAxisId`/`YAxisId`, rather than always using the primary axes. Spatial-bar baseline clamping uses its bound Y axis as well. Tests compare complete vertex arrays against equivalent primary-axis scenes with distracting default ranges, inversion, linear and logarithmic secondary scales.

`ShowGrid` now actually toggles the 3D floor graticule while retaining the three coordinate axes. Waterfall tube thickness responds to `StrokeWidth`; zero width suppresses waterfall/point-line strokes without hiding independent point markers. The default waterfall width is calibrated to retain its existing default geometry. These are scene-unit tube widths, not constant screen-space widths; full original 3D styling remains open.

Eight box corners now use stack storage, face indices use static read-only span data, and tube ring trigonometry is computed once instead of for every segment. No hardware speedup is inferred from these implementation changes.

## Spectrogram integration and scratch-buffer reuse

Reviewing all example screenshots exposed a spectrogram that was nearly uniform blue. `Fourier.Spectrogram` already returns frequency-major decibels, but the example transposed the result and applied another logarithm. The example now uses the returned values unchanged and displays a declared -100 to 0 dB color calibration. Its subtitle defines unit peak amplitude as 0 dB and its X axis explicitly names window-start times.

The reusable STFT now allocates one FFT scratch array and one Hann-weight array per call rather than constructing a new spectrum, frequency array and amplitude array for every window. The complete frequency-major output is checked against independently invoking `Spectrum` on each window. Tests cover a known tone's frequency and amplitude, the synthetic chirp's changing frequency ridge, silence at the -240 dB floor, invalid rates/window enums and a bounded allocation budget. Nonfinite samples in an unused incomplete tail are rejected consistently with the finite-input contract.

The CPU benchmark now reports both per-window-spectrum and reused-scratch STFT strategies on the same input, after exact output comparison. It is an isolated CPU measurement, not a GPU/FPS benchmark. Browser regressions check the known chirp ridge values and a visible high-amplitude ridge pixel on each rendering backend.

## Browser and regression evidence

`CompletionReviewTests` covers data/geometry correctness and bounded gap-index allocations. `rendering-review-tests.mjs` runs against both the real WebGL and explicit Skia/raster backends at DPR 2 through the existing browser harness. It compares actual plot pixels for equivalent primary/secondary bindings, checks visible 3D grid/stroke changes, and confirms long-gap JSON samples survive rendering unchanged. Tests wait for a newly presented frame rather than assuming a changed model is already visible.

The review retains the existing GPU recovery, intensity palette editor, precise scalar picking, colorbar, export, persistence, mobile and isolated package-consumer checks. Only results from the final successful candidate/merge revision constitute acceptance evidence. CPU benchmark evidence and its limitations are documented in [PERFORMANCE.md](PERFORMANCE.md).
