# Reference-style explorer, reusable gallery and project extraction

The visual/workflow reference is LightningChart .NET's official Interactive Examples
application: [Learning from Demo](https://lightningchart.com/net-charts/docs/getting%20started/runningDemo/).
This implementation uses its own FastChartsScene branding, APIs, icons and generated
previews. It does not copy the product's binaries, source, screenshot artwork or logos.

## Application workflow

The initial page is a charcoal/yellow tile gallery with keyword search, category and
AND/OR tag filters, favorite stars and a selected-example preview. Switch to List to
browse compact rows; Enter or Open example opens the selection. Double-click opens
an example directly. Back returns to the retained gallery selection and filters.
Search accepts AND keywords and quoted phrases. Click a selected-example tag to
filter by that tag; Clear removes search/category/tag/favorite filters.

Favorites can be exported/imported as bounded version-one JSON. Unknown/duplicate IDs
or malformed files leave the previous favorites untouched. Favorites and tile/list
preference are stored using the platform's local settings. Persistence failure is
reported to the console and does not crash the chart; settings are not sent over a
network. A corrupt stored list is ignored. The native file-picker flow needs separate
platform acceptance; browser diagnostics test the same atomic import model.

The selected-example view uses a compact description/tag header, yellow back arrow,
compact yellow command labels, a chart-first work area, and a light Properties panel.
Overview/Data/C# usage details are available through Details rather than permanently
consuming chart height. Search properties filters the implemented controls. On narrow
screens the main Properties panel becomes an Inspector dialog; the Examples command
also retains the compact picker. This is not the original exhaustive property grid,
all original commands/examples, or a pixel-equivalence certificate.

## Reusable catalog contract

`FastChartsScene.Core.CatalogBrowserModel` is independent of Uno/charts. It owns
immutable metadata, case-insensitive keyword/phrase/category/tag filters and favorites.
An identical normalized filter returns the same immutable result list. Inputs and
favorites files have explicit limits. `FilterPasses` distinguishes actual scans from
cached queries. A changed query still performs an O(N) metadata scan; no instant-search
performance guarantee is implied for arbitrary large catalogs.

`FastChartsScene.Controls.Uno.CatalogBrowserControl` hosts the model in virtualized
GridView/ListView controls, exposes OpenRequested, selection, favorites import/export
requests and a host-provided PreviewProvider. It does not hardcode example factories,
file paths or navigation destinations. The isolated NuGet consumer compiles this API.

Thumbnails are requested only for realized/selected cards and processed serially, with
a UI yield between requests. At most 64 pending requests and 32 cached thumbnails are
retained. Filtering, changing view or hiding/unloading cancels obsolete requests;
completed cached images survive navigation. A generation check rejects late results.
Failures are not retried in a loop: the host may call InvalidatePreviews after updating
the provider/data. Model event subscriptions detach on unload and reattach on load.

The explorer's provider renders the **actual example document** through the existing
scene compiler and Skia/CPU-raster PNG exporter. It does not create one WebGL context
or acquisition timer per tile, nor substitute generic charts. A cold million-sample
thumbnail still constructs/indexes its source on the calling UI thread; serialized
yields bound concurrency but are not background-worker rendering or a latency promise.
The first selection may therefore cost more than subsequent cached navigation.

## Standalone project extraction

Extract project downloads a source ZIP for the current edited chart, not only its
original fixture. The reusable API is `FastChartsScene.Export.ChartProjectExport.Write`.
The package now references Serialization as well as Rendering. The archive contains
an embedded exact chart.json, App.xaml/C# host, desktop/browser entry points, project
and pinned SDK settings, local-feed NuGet configuration and build instructions.

```csharp
byte[] projectZip = ChartProjectExport.Write(document, "MyChart");
// Or stream into a caller-owned destination:
ChartProjectExport.Write(document, destination, "MyChart");
```

Names are restricted to 1–64 ASCII identifier characters; paths are fixed and relative.
Chart strings are stored in JSON, never interpolated into source or build files. Model
validation, a compatibility frame compile at 800×600, and serialization happen before
writing any output. The document limit is 64 MiB UTF-8. IO failure can leave a partial
output stream, so callers choose atomic filesystem replacement policy themselves.
Archive timestamps and ordering are deterministic for equivalent input.

The ZIP is **source-only**. Copy the twelve matching FastChartsScene 0.1.0 NuGet packages
from the repository's CI packages artifact into its packages folder. This preview does
not assume a nuget.org release. The generated NuGet configuration maps FastChartsScene
names to that local directory and restores other dependencies from nuget.org. The app
targets net10.0-desktop and net10.0-browserwasm with Uno.Sdk 6.7.30. It uses `/` as the
browser base path. CI builds both generated targets against the actual packed libraries;
a successful compile alone does not prove native OS runtime/file-picker behavior.

An acquisition export is a data snapshot, not an instrument connection or restored
running producer. Session-only series IDs regenerate on chart import as before.
The host detaches/disposes its document and chart when its window closes.

## Review and acceptance boundaries

The surviving pre-turn source archive matched merged PR #4 exactly; no older archives
were blindly replayed over it. See RECOVERY-AND-UI.md. The previously blocked contour
and fill-alpha renderer work remains excluded; issue #5 is still open. Full API, all
reference examples, designer/printing, maps/volumes, native/hardware/cross-browser
acceptance and zero-copy composition remain in the parity backlog.

Tests cover catalog normalization and collision-safe cache keys, metadata ownership,
favorites atomicity, large same-query caches, all-family project data preservation,
deterministic archives, option validation, and generated target builds. Actual browser
input covers tiles/list/search/tags/stars, downloads, retained selection/cache, stopping
acquisition on Back and settings after reload, in addition to existing chart tests.

The diagnostic catalog benchmark measures a warm identical filter over 20,000 entries
and checks exact IDs against exhaustive filtering before timing. It is a cache lookup
comparison, not a changed-query benchmark, input-latency/FPS measurement or LightningChart
performance comparison. Existing numerical and GPU/fallback regressions remain enabled.
The sample's acquisition loop also reuses one 64-sample staging array: RollingBuffer
copies the batch synchronously, so later channel fills cannot mutate existing snapshots.
