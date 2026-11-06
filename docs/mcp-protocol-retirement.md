# MCP protocol regression coverage

OniMcp supports the `2025-11-25` protocol with `initialize` and a server-issued
`Mcp-Session-Id`. The `2026-07-28` transport has been retired.

## Executable checks

`tests/OniMcp.Server.Tests/RetiredProtocolRegression.cs` runs from the server
test project's normal entry point. Each scenario starts a fresh production HTTP
server and main-thread bridge, with game APIs supplied by host doubles.

| Scenario | Required behavior |
| --- | --- |
| Discovery | Supported tool discovery succeeds; retired discovery is rejected. |
| Headers | Retired protocol headers and metadata are rejected; forged sessions and foreign origins allocate no sessions. |
| Protocol | Unsupported versions fail before JSON parsing; supported initialization, JSON-RPC errors and tool dispatch remain correct. |
| Resources | Retired reads/listing perform no resource or catalog reads; supported reads preserve contents and missing-resource errors. |

The rejection assertions check HTTP status, JSON-RPC error data, supported
versions, tool calls, resource/catalog reads and session counts. Run them with:

```sh
dotnet run --project tests/OniMcp.Server.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
```

These checks exercise the production HTTP transport on the host runtime. Unity
behavior and game-state changes require ONI runtime acceptance separately.

## Historical provenance

The frozen records remain available at repository commit
`90c31fa9826161caa5382edf4e870f6c07adb317`:

- [Historical 2026 mapping and its seven source-only scenarios](https://github.com/LIghtJUNction/OniMods/blob/90c31fa9826161caa5382edf4e870f6c07adb317/tests/onimcp-conformance/2026-07-28.partial.json).
- [Historical protocol test source](https://github.com/LIghtJUNction/OniMods/tree/90c31fa9826161caa5382edf4e870f6c07adb317/tests/OniMcp.ModernProtocol.Tests).
- [Historical header test source](https://github.com/LIghtJUNction/OniMods/tree/90c31fa9826161caa5382edf4e870f6c07adb317/tests/OniMcp.ModernHeaders.Tests).
- [Historical discovery test source](https://github.com/LIghtJUNction/OniMods/tree/90c31fa9826161caa5382edf4e870f6c07adb317/tests/OniMcp.ModernDiscoveryFinalWire.Tests).
- [Historical resource test source](https://github.com/LIghtJUNction/OniMods/tree/90c31fa9826161caa5382edf4e870f6c07adb317/tests/OniMcp.ResourceReadConformance.Tests).

The mapping pins upstream requirements to
[`modelcontextprotocol/conformance@7169291ec0b68eb370fddcd9947313ab0d5e4156`](https://github.com/modelcontextprotocol/conformance/blob/7169291ec0b68eb370fddcd9947313ab0d5e4156/requirements/2026-07-28.yaml),
Git blob `b0c4f8560429e8f4b6c89833cc0b35405bc004ff`. These records document the
retired implementation. Current CI runs the executable supported-protocol and
retirement scenarios above.
