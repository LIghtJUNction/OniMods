# Selected improvements from adiatma85/oni-mcp

Source: [adiatma85/oni-mcp](https://github.com/adiatma85/oni-mcp), reviewed at
`3739187516f068266c0a74dccd67be2ecae486e3`. Original implementation by
Koernia (adiatma85), including commit `e1a53b7`. These changes are selectively
adapted to OniMods main rather than importing the fork's squashed upstream sync.

## Included

- Correct `.mcp.json` to use the `mcpServers` wrapper and the server's default
  port 8788.
- Classify build failures using actual obstruction entries and the structured
  research-unlocked flag. Empty obstruction fields no longer hide research,
  material or footprint failures. Explicit reason codes retain precedence.
- Recognize flush toilets, basins versus plumbed sinks, planter boxes, farm
  tiles and hydroponic farms in Chinese/English planning aliases and direct
  prefab aliases.

## Adaptations

The fork's research-keyword fallback did not cover the current upstream
`Building is locked by research` message. This port also recognizes a whole
`locked` word, while excluding `unlocked` and `blocked`. Unsupported errors
retain their existing precedence. Invalid-footprint failures retain the primary
reason selected by placement validation even when obstruction entries coexist. JSON objects and boolean values do not by
themselves establish an obstruction.

Ambiguous aliases such as `花盆` (a decoration rather than a PlanterBox),
`水池` and `洗手台` are omitted. Existing `洗手盆`/`洗手池` mappings stay
separate. No placement validation, research gate or write authorization changes.

## Validation boundary

The existing Tools host test project links the actual classifier, alias table
and prefab resolver. New executable cases cover empty/nonempty obstruction
collections, JSON values, research/message fallbacks, explicit-code precedence,
and basin/sink and farming aliases. The known-bad classifier must fail the
research-lock regression before the candidate is accepted.

Host tests and compilation against locally installed ONI assemblies do not
establish live-game placement behavior. Before merging, smoke-test preview and
build-plan resolution for these building families in a paused, Steam-launched
ONI session. Do not publish binaries or restart an active colony as part of this
source-only port.

The fork's strategy advisor, farming writes, overlay controls and utility routing
are outside this patch; they need separate current-main review and game evidence.
