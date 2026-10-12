# Build-once parallel browser validation

## Reviewed failure

PR #14's original Validate run `38254026120` completed all build, package and
browser checks, but its single Uno job was cancelled during cleanup at its
40-minute timeout. The recorded test results were 127 WebGL, 122 Skia fallback
and 44 axis scenarios, all passing. A cancelled workflow is not a successful
release gate, even when its test steps completed.

The original job spent about 13 minutes reaching browser execution (including a
slow WebAssembly workload install), then about 5 minutes on axis tests, 10 minutes
on WebGL and 11 minutes on Skia. This is one run's diagnostic timing, not a
hardware/application benchmark or a guaranteed future duration.

## New dependency graph

```
core tests + CPU benchmarks ------------------------------+
                                                         |
Uno build -> packages -> isolated consumers -> extraction -+-> evidence gate
               |                                         |
               +-> one browser-site artifact              |
                         |                               |
                         +-> axis browser suite ---------+
                         +-> WebGL browser suite --------+
                         +-> Skia browser suite ---------+
```

The existing source build, package verification, isolated package consumers and
extracted desktop/browser project builds are unchanged. The existing Node browser
scripts are unchanged. There are no skipped scenarios, replacement fixtures,
fallback-to-old-build paths, `continue-on-error` exceptions or reduced thresholds.

The three browser jobs use a matrix with `fail-fast: false`, so one failure does
not cancel the other suites before their evidence is collected. Each has its own
25-minute limit; build and aggregate jobs have separate limits. This avoids
charging all browser durations against a single serial job's timeout. It may
increase concurrent runner use and repeats browser dependency setup; actual
wall-clock savings depend on runner availability and must be measured separately.

## Exact artifact provenance

The build job emits `validation-manifest.json` with its full Git commit/tree and
SHA-256/size of every browser payload file, including revision.json and .nojekyll.
Each suite downloads the numeric artifact ID emitted by that build job, not a
latest-success artifact or a branch-name lookup. It checks the manifest hash from
the build output, source identity and complete file inventory before and after
running its browser script. Missing, changed and extra files are rejected.

The wrapper removes TEST_URL and alternative output/backend environment overrides
before launching the unchanged scripts. Those scripts serve the downloaded site.
A nonzero Node exit fails the job. Passing result files must include complete
counts, unique nonempty scenario names, explicit passing booleans and an empty
unhandled-error list. Minimum counts currently reflect the existing suites:
127 WebGL, 122 Skia and 44 axis scenarios. Additional passing cases are allowed;
intentionally reducing coverage requires an explicit source change.

Each successful suite issues a receipt binding its results digest and count to
the same source commit/tree, manifest hash and artifact ID. A started, cancelled,
failed, partial, stale or missing receipt cannot satisfy the aggregate gate.
This is consistency checking within a trusted workflow, not cryptographic signing
or a security boundary against somebody authorized to change the workflow.

## Evidence and deployment

Per-suite artifacts are `browser-evidence-webgl`, `browser-evidence-skia` and
`browser-evidence-axis`. Build logs and portable results are retained separately.
The final `validation-evidence` artifact contains those directories and a
`validation-summary.json` only after the aggregate checks pass. Every required
job must also report success. Evidence uploads run after failures so diagnosis
does not depend on the remaining suites passing.

The names `source`, `packages` and `browser-site` remain unchanged. Job-owned
artifacts use explicit overwrite on same-job retries; no matrix members write
to the same artifact name. The Pages workflow is unchanged: it downloads the
browser-site from a successful **main push Validate run**, checks revision.json
against that run's SHA, and only then deploys. No cancelled or failed run is
promoted by the new pipeline.

## Portable verifier regression tests

Run without .NET, Node, network access or a browser:

```sh
python3 -m unittest discover -s tests/ci -p 'test_*.py' -v
```

The tests cover payload modification/addition/removal, hidden files, symlinks,
wrong commit/tree/manifest, duplicate JSON keys, incomplete or reduced suites,
missing receipts, wrong artifact IDs, failed/cancelled/skipped dependencies,
changed result files and reuse of pre-existing browser results. The verifier also
accepts the downloaded complete PR #14 result files without modifying them.

Reference behavior:
- GitHub matrix jobs: https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations
- Sharing artifacts: https://docs.github.com/en/actions/tutorials/store-and-share-data
- Numeric artifact-ID downloads: https://github.com/actions/download-artifact/tree/v4
- Artifact overwrite semantics: https://github.com/actions/upload-artifact/tree/v4

No chart renderer, API compatibility, GPU hardware performance or complete
LightningChart UI/feature parity is claimed by this workflow change.
