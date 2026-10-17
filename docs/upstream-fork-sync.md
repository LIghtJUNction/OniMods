# Fork sync snapshot

Inspected on 2026-10-06 in Asia/Taipei (UTC+08:00; UTC date 2026-10-05), against main `49b23f83317363d0ca8edc2331a2232713f85e3b`.
GitHub's fork and branch inventories contained 8 forks and 345 branches. Comparison used commit ancestry, `git cherry`, PR history, and selected production diffs; unmatched patch IDs alone do not prove missing behavior after squash merges or rewrites.

| Fork | Branches | Disposition |
| --- | ---: | --- |
| [zoujave/OniMods](https://github.com/zoujave/OniMods) | 327 | Main is behind upstream. Of 296 branches with unmatched patches, 256 map to merged PRs, 17 to closed PRs, 20 to existing open PRs, and 3 lack a matching PR. See below. |
| [xtatrax/OniMods-MCP](https://github.com/xtatrax/OniMods-MCP) | 1 | No unmatched patch to port. |
| [norarains/OniMods](https://github.com/norarains/OniMods) | 1 | 19 unmatched commits. Selective read-only port below; larger gameplay/API changes tracked in #508. |
| [gr3gg0rk/OniMods](https://github.com/gr3gg0rk/OniMods) | 1 | No unmatched patch to port. |
| [adiatma85/oni-mcp](https://github.com/adiatma85/oni-mcp) | 1 | 4 unmatched commits, including an upstream sync. #506 already ported selected configuration, placement-error and alias improvements. Remaining feature candidates tracked in #508. |
| [vivy1024/OniMods](https://github.com/vivy1024/OniMods) | 10 | No unmatched patch to port, including its security-fix branches. |
| [oopty/OniMods](https://github.com/oopty/OniMods) | 1 | No unmatched patch to port. |
| [DemonBigj781/Oni-Mcp](https://github.com/DemonBigj781/Oni-Mcp) | 3 | 10 unmatched historical commits across archive/backup/main branches. The legacy `mods/oni_mcp/Support/SpriteLoader.cs` target is absent from current main; no obsolete loader or forced-dupe-action API was reintroduced. |

## Selected ports

- [zoujave `d5445b3`](https://github.com/zoujave/OniMods/commit/d5445b3066819dea602aa39a3103b14227d159e5) and [regression `f111731`](https://github.com/zoujave/OniMods/commit/f111731301f2470ffd8fc66e1be8b13dab17826b): distinguish version-marker metadata changes from actual build/reference-assembly drift. Marker changes remain visible as notices; changed builds or DLL blobs still trigger drift warnings. Immutable reference pins stay unchanged.
- [norarains `1e2e96d`](https://github.com/norarains/OniMods/commit/1e2e96d07f6096752fa6d3f8e1fe426741a2fdfc): port only power-connection readiness and temperature-sample readiness helpers plus their observation consumers. Pending observations return null and explicit flags. Keep main's string-valued circuit IDs for settled networks; do not import the fork's research, construction, query or control rewrites. Adapt host cases from its maintenance regression into the existing Tools test project. Review-driven adaptations check graph availability before evaluating native circuit getters, and consult the native sample counter to reject a partial warm-up average after all buffer slots have been filled but before publication.

## Other zoujave candidates

- `codex/onimcp-modern-middleware-isolation`: current main already calls the selected read-only handler directly in `McpHttpServerModernProtocol.cs`, avoiding legacy `OniToolRegistry.CallTool` middleware. No duplicate invoker was added.
- `fix/onimcp-existing-material-identity`: tracked by [#293](https://github.com/LIghtJUNction/OniMods/issues/293). Changes affect placement retries and completion; preserve its runtime acceptance requirements.
- `fix/reference-marker-only-drift`: selected above. Existing open branches remain in their original PRs; this audit does not bypass their merge gates or reopen superseded closed PRs.

## Evidence boundaries

The provenance regression rejects the original main verifier and passes the selected fix. The Tools host suite executes the production readiness helpers against fixtures, including missing/dirty/mismatched power graphs and incomplete/nonfinite temperature samples.

An installed-game assembly build verifies compilation. Static inspection of the installed `Assembly-CSharp.dll` (SHA-256 `6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1`) confirms the temperature sensor's eight-sample buffer and initial unpublished zero average; its source remains transient outside the repository.

These are source/host/build checks, not actual ONI gameplay observations. No mod was deployed and no Workshop item was published by this sync. Gameplay writes and larger fork integrations still require the scenario-specific acceptance in [#508](https://github.com/LIghtJUNction/OniMods/issues/508).
