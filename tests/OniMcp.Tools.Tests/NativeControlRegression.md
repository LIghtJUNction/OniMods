# Native control handler regressions

Run with the repository's .NET SDK:

```sh
dotnet run --project tests/OniMcp.Tools.Tests/OniMcp.Tools.Tests.csproj
```

`NativeControlRegression` executes the linked production duplicant command and
assignment routers, slot resolver, medical assignment handler, storage resolver,
filter routers, and shared eligibility helpers. `NativeControlTestDoubles` and
`NativeFilterTestDoubles` replace game components and registries at those boundaries.
Unrelated routes throw rather than return invented success.

## Source assumptions

The fixture contracts were inspected at
[`Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636`](https://github.com/Kupie/ONI_Decomp/tree/8d4ace10c2b6a84cee394653a91b990a5122e636).
The repository manifest identifies this as a **744825 source contract**. It is
separate from the **737790 reference assembly** build. A fixed source revision
provides reproducibility; it does not detect upstream changes.

| Issue | Inspected native contract | Production behavior exercised |
| --- | --- | --- |
| #266 | `StandardChoreBase.Cancel` removes the provider chore; `ChoreDriver.StopChore` reaches `Fail`; `Fail` cancels transient chores and ends persistent work execution | Worker stop preserves a persistent job for another worker; current transient move follows native failure; cancel-and-move routing and confirmation |
| #313 | `Assignable.CanAssignTo` evaluates registered predicates only for `MinionAssignablesProxy`; native assignment UI respects `CanBeAssigned`; Clinic registers its health-threshold predicate | Generic and medical handlers reject unavailable proxies, disabled targets, or rejected candidates before assignment; accepted candidates receive proxy identity; invalid medical requests leave the threshold unchanged |
| #308 | `BionicSideScreen` checks the exact monitor slot's `IsLocked`; native two-argument `Assignable.Assign` does not enforce that specific lock | Locked or unmappable native slots fail before assignment; unlocked slots and ordinary equipment still work; locked-slot unassign/none remains available |
| #321 | Tree UI expands target storage categories through `DiscoveredResources`, excludes forbidden resources; `TreeFilterable.UpdateFilters` can drop contents; **each** `FlatTagFilterable.SelectTag` immediately updates the tree | Both storage/filter paths reject invalid additions/replacements before any filter write; removal of historical values works; flat updates retain the intersection and add before removing so valid stored contents survive add and unchanged replace |

The native source and downloaded DLLs are transient inspection inputs and are not
included in this test project. The host fixture executes no third-party game binary.

## Regression sensitivity

During development, the six affected production handler files were temporarily
restored from `main@46bd1b2` while retaining these tests. The host run failed in all
six groups: worker cancellation, generic assignment, medical assignment, bionic
slots, tree filters, and flat filters. The candidate sources were restored and
the same host suite passed.

The flat fixture's immediate `SelectTag` callback also reproduced a separate
failure in an intermediate candidate: clearing and reselecting tags during a
storage `add` ejected previously accepted Dirt. The differential update passes
that content-and-mass assertion and unchanged-filter assertions on both routes.

## Runtime acceptance still required

These are host tests plus source inspection. They do not establish Unity/Mono,
save/load, chore scheduling, mod compatibility, or behavior in an installed ONI
build. Record the actual game build, candidate SHA, save, enabled mods, object IDs,
and pre/post state while running the following cases on an authorized test save:

- A works a persistent designation and B is eligible: `cancel_all` and
  `cancel_all_and_move` release A while B can still take the job. A current direct
  move stops without leaving a stuck move state.
- Healthy/ineligible clinic patient and unrelated-world owner: reject with no
  assignment or threshold change. Injured eligible patient and an ordinary
  same-world assignable: succeed. Unassign remains possible. If a changed clinic
  threshold is needed to permit the patient, set it separately before assignment.
- Locked bionic slot with a valid booster: error, unchanged assignment, no new
  installation chore. Unlocked slot: success. Locked unassign/none and ordinary
  equipment: retain their native behavior.
- Storage containing items: invalid replacement/addition through both public
  paths leaves tags, item locations and mass unchanged. Valid tree/flat changes
  match the native side screen; flat add and identical replacement retain
  currently accepted contents. Observe any intended removal separately.
