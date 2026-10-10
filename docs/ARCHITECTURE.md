# Architecture

## Dependency direction

```text
Core <- Data <- SignalProcessing
  ^       ^
  +---- Charts
           ^
           +-- Serialization
           +-- Examples (+ SignalProcessing)
           +-- Rendering -- Interaction
                    |
                    +-- Rendering.Skia
                              |
                    Controls.Uno (+ Interaction)
                              |
                          Explorer
```

Core, data, chart models, geometry, interaction, fixtures, and persistence do not depend on Uno. The Skia backend does not depend on Uno. The Uno control can be hosted without the explorer. Examples are factories, not hard-coded branches inside the renderer.

## Model and data ownership

`XyData` and `UniformData` own their sample buffers. NaN Y values denote breaks, not interpolated points. A synchronized `RollingBuffer` owns bounded acquisition storage and returns immutable snapshots. The UI owns a `ChartDocument`, its axes, and its series. Changing a series emits document invalidation; nested `BeginUpdate` scopes coalesce notifications.

Documents/controllers are not general-purpose concurrent mutable objects. Acquire data on worker threads, then marshal source replacement to the UI thread. `ChartControl` never disposes a caller-owned document. Replacing examples in the explorer detaches the old document and then disposes it.

Known lifetime and mutation limitations are tracked in the compatibility matrix. In particular, mutating a raw category array in place does not provide collection notifications; replace values under an explicit invalidation when using the current model.

## Frame compilation

`SceneCompiler` maps a document and logical viewport size to a `RenderFrame`. The frame contains a packed vertex array and batches with ranges, clip rectangles, transforms, primitive kinds, and depth-test state. Each vertex contains XYZ, RGBA, and a surface normal (ten floats). Labels and legend hit regions are separate metadata.

Logical coordinate transforms operate in double precision. XY polygon clipping occurs before float packing. Crossing area/band boundaries are split before triangulation. Ordered line sources use weakly cached envelope indexes so rendering scales with visible screen resolution rather than total source length. Unordered source order is retained. NaN gaps are not connected, and cubic marker positions come from original samples rather than generated subdivision points.

An unchanged document identity, revision, width, and height reuses its whole compiled frame. Consumers must treat returned arrays and metadata as immutable. The current public frame surface does not enforce deep read-only memory at every nested array boundary.

Layered axes share a plot. Stacked axes use weighted separate plots; segmented axes map several axes to the same weighted plot. Weights are normalized before summation to avoid overflowing a finite set of individually valid weights.

## Backend adapters

`Rendering.Skia` caches `SKVertices` for 2D geometry and fonts for labels. It handles 2D PNG export through an independent raster surface. It rejects depth-tested batches rather than pretending that a painter-order flattening is correct 3D rendering.

`ChartGlSurface` compiles vertex/fragment shaders, owns a VAO and VBO, uploads changed frames, clips each batch with scissor state, and uses depth testing for 3D. It restores the shared OpenGL state it changes. Its matrices convert the System.Numerics depth convention to OpenGL's clip convention.

`ChartControl` draws the offscreen GL surface at device resolution and maps it back to logical layout units; the Skia overlay uses logical units. It handles model invalidation, input, attachment/detachment, and rendering selection. Rendering diagnostics distinguish GL submissions, Skia overlay draws, and CPU compile time.

The current Uno GL host performs framebuffer readback and bitmap presentation. It is not a direct GPU texture-sharing compositor integration. Adapter loss, suspension, repeated detach/reload, all native backends, and hardware performance require broader validation.

## Interaction

`ChartInteraction` is framework-neutral. It computes viewport changes before applying them, records at most 64 history states, and turns a drag into one undo entry. A canceled gesture restores its initial viewport. Cursor queries return original data samples in screen-space distance rather than decimated representatives. The initial picker covers XY data series, not every financial/grid/mesh primitive.

Uno maps pointer capture, wheel, keyboard, and touch gestures to that controller. Wheel anchoring preserves the value beneath the pointer. Shift-drag selects an XY range. Vertical gestures target the pane under the pointer. Three-dimensional dragging changes camera orientation rather than reinterpreting the scene as a flat plot.

## Persistence and imports

`ChartJson` uses explicit `Utf8JsonWriter` / `JsonDocument` parsing instead of reflection-driven polymorphic activation. It supports the currently implemented series types, axes, themes, segments, annotations, and camera settings. Import budgets limit total input, numeric values, series, axes, and annotations. Duplicate keys, invalid enums, invalid models, and unknown format versions are rejected.

The explorer compiles a prospective imported document before replacing the current document. Failed imports leave the current chart attached. This catches unsupported view/series combinations in addition to structural JSON errors. The format is independent of LightningChart's persistence format and does not read its proprietary project files.
