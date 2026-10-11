# Local reconciliation and explorer reference

## Starting point

On 2026-10-11 the connected GitHub branch `main` was read as commit
`3151e7ae040c134f23bccc69fb526a405fdde971`. The available latest source archive,
`FastChartsScene-pr4-source.zip`, has SHA-256
`21d969292d9a290ff5744c8eb99d28bc311ea2fef0263c27c6a2184759d3c030`.
Its 116 source files reconstruct Git tree
`c99baa75aea933847f17a6e07ec03c0e813844d7`, identical to main. Previous feature
branches are merged ancestors. There was no surviving unpushed Git checkout in
this execution environment. Old source/build ZIPs are retained as historical
artifacts, not overlaid onto newer source or merged blindly.

The earlier uncommitted contour-picking and fill-opacity renderer work is not
part of that archive or main. Its dependent renderer publication was blocked;
this increment does not bypass that restriction or claim that work merged.
The opacity defect remains issue #5; remaining compatibility is issue #2.

## UI reference

The visual/workflow reference is the official LightningChart .NET **Interactive
Examples** documentation, observed 2026-10-11:

- [Learning from Demo](https://lightningchart.com/net-charts/docs/getting%20started/runningDemo/)
- [Annotated selected-example screenshot](https://lightningchart.com/net-charts/docs/assets/images/Demo-SelectedExample-76a702600bddf8b20ff32fb23b0d9a50.png)
- [Properties editor overview](https://lightningchart.com/net-charts/docs/beginners%20guide/api_overview/)

Reference characteristics: neutral charcoal shell, yellow accent/command icons,
compact branding with Interactive Examples subtitle, tile/list example browser,
keyword and tag filtering, favorites, selected-example description/tags and back
navigation, chart-first work area, and light property controls. This is an
independent reconstruction of layout and behavior, not a redistribution of
LightningChart branding, artwork, code or binaries. The reference screenshots
are not bundled in the application.

The implementation keeps FastChartsScene's own names/API. It does not advertise
missing reference commands as functioning buttons. Hardware/rendering/API and
exhaustive pixel parity remain separate acceptance work.
