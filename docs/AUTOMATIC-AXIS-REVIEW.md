# Automatic axis precision and two-density caching

The official LightningChart .NET [automatic label formatting documentation](https://lightningchart.com/net-charts/docs/inDetail/axis-class-properties/#autoformatlabels-format-string)
describes avoiding identical rounded labels for adjacent divisions. This review closes
specific FastChartsScene gaps in that behavior without changing explicit user formats.

## Correctness

Normal automatic division placement and labels remain unchanged where valid and
unambiguous. Generated divisions are checked against the actual finite axis range and
projected [0,1] domain, rather than allowing the placement loop's roundoff tolerance to
leak an out-of-range label or grid line. Duplicate coordinates are removed.

When fewer than two valid numeric divisions can be represented, two **exact range
endpoints** are displayed. This occurs, for example, where a nice decimal interval has
no representable interior coordinate at a large-magnitude, adjacent-double range. The
implementation does not invent distinct sub-ULP coordinates, change the range, or alter
source samples. Linear and logarithmic inversion retain the same data-space positions.
Category and date scales retain their existing placement rules.

For automatic numeric formatting, repeated text or rounding beyond a narrow range
triggers progressively more significant digits, up to invariant G17. A single precision
is selected for the whole set. Explicit NumericFormat strings, explicit DateTimeFormat
strings, category labels and custom tick text are never rewritten; repeated text may be
intentional in those cases. This is distinguishable numerical text, not a guarantee that
long labels fit without overlap in every plot or fixed margin.

Automatic calendar labels that repeat only a year are refined to year/month, date,
time, then fractions as necessary, while preserving original calendar tick positions.
Out-of-date-range values retain their diagnostic text. DateTime's 100-nanosecond storage
resolution remains a boundary; the code does not fabricate finer-resolution dates.

## Performance

Each axis retains at most **two density variants** for its current range, scale,
inversion, categories and immutable tick options. Alternating two chart sizes or
screen/export targets no longer evicts the only cached list on every call. Warm lookups
allocate no temporary objects and preserve the read-only list identities. Relevant
model changes discard obsolete variants; previous returned lists remain valid for their
callers. More than two alternating densities may still miss the cache. There is no
unbounded per-size dictionary and the axis remains UI-thread-affine.

The existing 128-axis CPU benchmark is extended with an alternating 4/12 density workload.
It checks complete tick-result equivalence against the previous generation loop before
reporting medians and allocation counts. The reference measurement excludes the old
single-entry cache's small wrapper overhead, so it is conservative generation cost,
not a byte-for-byte execution of the old implementation. Cold label refinement adds
bounded formatting/validation work; unchanged lookups bypass it. Results do not measure
whole-frame latency, native hardware GPU throughput or LightningChart performance.

## Validation

Portable tests include 500 deterministic exponent/ULP neighborhoods, adjacent subnormal
and extreme finite endpoints, logarithmic/inverted coordinates, clipped rounding,
month ticks in multi-year ranges, explicit-format preservation, cache identity and zero
warm allocations. Scene/SVG tests assert rendered labels and unchanged saved data.

Additional actual-browser scenarios run in the existing axis suite under WebGL and
explicit Skia at DPR 1 and 2. They check distinct large-offset labels, painted bottom-axis
text, exact SVG labels, calendar context and explicit repeated formats. They use the
real document import and toolbar download paths. All previous numerical, package,
extracted-project, gallery/property, chart and axis-editor scenarios remain enabled.

This is an independent correctness/performance increment, not exhaustive reference
property/API/visual parity. Label overlap avoidance, adaptive label margins, tick styling,
scale breaks, full spatial axes and the previously excluded renderer changes remain
separate work. CI software graphics is not native-runtime or hardware certification.
