# Axis tick text input review

This increment corrects the editor/parser contract independently of rendering or axis
margin placement. It does not include the unpublished measured-layout work.

## Correctness

The documented editor syntax accepts CR, LF and CRLF record boundaries. Previously the
parser called `String.ReplaceLineEndings` before splitting lines. That API also normalizes
other Unicode separators. A pasted `0|First` + U+2028 + `1|Second` could therefore become
two valid ticks even though the immutable settings reject line separators inside labels.
The native editor's draft comparer recognizes only CR/LF/CRLF, so the two paths disagreed.

The parser now scans the original text as spans and treats only CR/LF/CRLF as record
boundaries. Vertical tab, form feed, NEL, line separator and paragraph separator are
rejected with a line-numbered message. These characters are never silently reinterpreted
as additional tick records. Label whitespace, additional pipes, null labels (no pipe),
and explicitly empty labels (trailing pipe) retain their prior meanings.

Finite numeric parsing remains invariant and lossless for representable doubles.
Label length and control characters are checked before allocating owned label strings.
The full candidate is still validated/sorted through immutable AxisTickOptions before
the editor assigns it. A failed Apply leaves the chart settings unchanged and keeps the
draft available for correction or explicit Reset.

## Allocation and bounds

Span scanning avoids a normalized whole-input copy, an array for every input line, and
temporary numeric substrings. Blank-line input uses constant temporary storage rather
than allocating in proportion to line count. Valid label/result ownership still allocates
in proportion to at most 512 accepted records; this is not an allocation-free general
parser. Input remains limited to 160 Ki UTF-16 code units and each label to 256 code units.

The benchmark preserves the previous normalization/split algorithm as a reference and
compares the complete parsed results before timing both paths on the same valid input:
40,000 blank lines followed by three ticks. Reported medians and allocations apply to
that input on the recorded .NET runner, not typical keystroke latency, chart FPS or GPU
performance. Ordinary inputs and limit/error cases are independently tested.

## Validation

Portable cases cover five forbidden separators, exact original double bits, mixed line
endings and line numbers, null/empty/Unicode/pipe labels, invalid controls, 512/513-record
bounds, maximum-size blank input allocation, invariant culture and immutable outputs.

The existing dedicated browser suite includes a native keyboard-paste regression under
WebGL and explicit Skia at DPR 1 and 2. It checks inline validation, unchanged exported
chart data, retained invalid draft and Reset. No editor value is assigned through a
diagnostic shortcut. All existing chart, package and export suites remain enabled.

Reference for the normalization behavior:
https://learn.microsoft.com/en-us/dotnet/api/system.string.replacelineendings?view=net-10.0

Full LightningChart API, property-tree and visual parity remains separate work. This
parser correction does not address the outstanding long-label margin/overlap behavior.
