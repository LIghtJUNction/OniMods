# Building placement coordinates

`building_control domain=planning` accepts `x/y` and each `anchors` entry as
the lower-left bound of the requested occupied cells. This remains the public
contract for both previews and construction blueprints.

ONI's native placement origin is different. For example, the 3×3
`AdvancedResearchCenter` has placement offsets X=-1..1 and Y=0..2. A public
anchor of (150,208) therefore becomes native origin (151,208), occupying
X=150..152 and Y=208..210. A neutral two-wide building normally has X=0..1
offsets, so its native origin remains at its public anchor.

The planner rotates the native `PlacementOffsets` first, subtracts their
minimum X/Y from the requested lower-left anchor, and uses that origin for
`BuildingDef.TryPlace`, optional native BuildTool fallback, foundation checks,
power ports and bridge endpoints. The converted `placementPoint` is passed
through once. Preview, obstruction checks and batch reservations use the same
rotated occupied cells, including holes in irregular footprints. Width and
height describe the resulting bounds; `footprintCells` counts occupied cells.

After placement, `actualPlacement.objectX/objectY` retain the native object
origin. `derivedAnchorX/derivedAnchorY` come from the bounds of actual occupied
cells, with definition offsets as a fallback. Do not use the native method name
`Building.GetBottomLeftCell()` as proof that it returns those occupied bounds.

This corrects requested placement. PR #535 separately corrects the obstruction
footprints of existing buildings and blueprints; both fixes are needed for
accurate placement beside a wide existing building. Existing buildings are not
moved by the requested-coordinate correction.

Host regressions execute production coordinate/model/placement helpers with
substituted grid, definition and native-call boundaries. Actual ONI acceptance
still requires a paused preview and placement, verification of the resulting
occupied cells and ports, and a repeated request returning the same blueprint
instead of creating another one.
