# Native order preconditions

This change addresses #328, #304, #296, #288 and #285. The companion
[assignment and filter regression](NativeControlRegression.md) covers #266,
#313, #308 and #321. These tests exercise production C# using host fixtures;
they are not evidence of a running Unity/Mono colony.

## Behavior and source assumptions

The method-body baseline is `Kupie/ONI_Decomp` commit
`8d4ace10c2b6a84cee394653a91b990a5122e636`, which declares ONI 744825.
The separate compile reference set in `ci/oni-reference-assemblies.json`
declares 737790. Neither evidence upgrades the other.

| Issue | Production rule | Fixed source used for review |
| --- | --- | --- |
| #328 | Only exact vanilla `Automatable` components use the manual-hauling interpretation; derived components are rejected before summary reads or writes. | `AutomatableSideScreen.IsValidForTarget` |
| #304 | Nonzero set/add/infinite queue changes require the native research-or-POI predicate. Clear and remove remain available for recovery. Entire batches are prevalidated before any setter, including `clearAll`. | `ComplexRecipe.IsRequiredTechOrPOIUnlocked`, `ComplexFabricator` queue counts |
| #296 | Every recipe slot has one valid material; equal tags across slots share a summed mass requirement, checked in the target world's inventory before add/convert. | `MaterialSelector.GetValidMaterials`, material selection recipe slots, `ReorderableBuilding` |
| #288 | Liquid/gas mass or solid pickup handle must be present before marking and priority writes. | `EmptyConduitWorkable.HasContents`, `EmptySolidConduitWorkable.HasContents` |
| #285 | Natural-tile designation uses native prefab/lifecycle ordering with tile-only flags and construction exclusions; it neither reads nor changes the player's filters. | `DigTool.PlaceDig` |

The current survival-only API policy from #512 remains authoritative. Rocket
materials may use the normal `AllowInsufficientMaterialBuild` game setting;
this change does not restore Sandbox, Instant Build or force-based bypasses.

Batch prevalidation prevents a malformed or locked later item from changing
earlier queue entries. It is not a rollback transaction for arbitrary native
exceptions after the commit phase has begun.

## Host checks

`NativeOrderPolicyRegression` links the three production policies directly.
It checks derived/missing components, unlock-gated queue changes, clearing old
locked queues without reading obsolete unlock data, integer overflow, exact
recipe slots/categories, default materials, individual and summed inventory,
nonfinite values and the normal insufficient-material setting.

Run the shared suite:

```sh
dotnet run --project tests/OniMcp.Tools.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
```

The same suite also runs existing map, survival policy, sweep, farming,
assignment and filter regressions. Additional handler fixtures are recorded
alongside their tests. Downloaded native source and game assemblies are never
packaged with this test project.

## Required ONI acceptance

Before merging, record the actual game build, candidate DLL/commit, save and
loaded mods. Start paused and capture the relevant state before and after.

| Scenario | Required observation |
| --- | --- |
| Custom Automatable subclass | List omits the vanilla control; single/batch write rejects it and preserves its state. Vanilla components retain their native behavior. |
| Locked research/POI recipe | Set/add/infinite fail without changing the queue. Clear/remove work. A batch with clearAll and a later rejected item changes no recipe count. |
| Rocket add and replace | Invalid slots/categories and insufficient target-world mass reject before structural mutation. Valid materials follow the native construction path. |
| Empty gas, liquid and solid conduit | No mark or priority update; counters identify skipped empty targets. Populated segments use the matching native flow manager. |
| Tile filter disabled in the UI | Ordinary and automatic obstruction digs still create tile-only designations. Player filters remain unchanged. |
| Blueprint or failed dig creation | Rejected cells do not become successful targets or contribute successful mass. Existing designations retain their priority-update behavior. |

Resume only for the short interval needed to observe normal task execution,
then pause and verify the actual world state. Include save/load and a second
call where the native object lifecycle could make the first result stale.
