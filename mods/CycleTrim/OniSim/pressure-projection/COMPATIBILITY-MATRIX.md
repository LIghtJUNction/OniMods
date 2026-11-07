# Pressure-model replacement: compatibility matrix

## Compatibility contract

The Poisson projection changes the physical model. Therefore compatibility is
split into (A) exact interface/untouched behavior, (B) conserved physical
invariants, and (C) explicitly approved gameplay deltas. A single "all bytes
equal" claim would be misleading: changed mass distributions and event timing
are expected if the new model genuinely replaces the old one.

| Legacy behavior / data | Source evidence | New-model treatment | Required acceptance |
|---|---|---|---|
| Gas pressure sweep scan order | `pressure_sweep.rs`, `scan_direction` | Removed from pressure math; deterministic GPU stencil | Deterministic repeatability; boundary fixtures |
| Gas pair mass transfer | `clamp(k*(ma-mb), -0.125*mb, 0.125*ma)` | Replaced by pressure projection + conservative advection | Per-species mass budget; bounded per-tick displacement |
| `ElementRecord.flow` | `tables.rs` offset 36 | Must not be silently treated as density/pressure | Calibrated mapping or explicit retained mobility rule |
| Empty-cell element replacement | `pressure_sweep.rs::update_pressure` | Material identity assigned by advection/material adapter | No mass without valid species identity; deterministic tie rules |
| Gas three-cell displacement | `gas.rs::do_gas_pressure_displacement` | Separate compatibility hook or approved behavior change | Gas ordering, vacuum and occupied-target fixtures |
| Liquid pressure displacement | `fluid.rs::do_liquid_pressure_displacement` | Not automatically covered by incompressible gas projection | Liquid displacement, max-mass and obstacle fixtures |
| Temperature transfer | legacy weighted source/destination calculation | Advect thermal energy or temperature with mass; later thermal solver | Total sensible-energy budget and per-cell bounds |
| Disease transfer | legacy proportional count and disease metadata updates | Advect concentration/count consistently with species mass | Total count, nonnegative count, valid disease index |
| Sink/vacuum semantics | special element IDs and gas/liquid helpers | Explicit source/sink boundary adapter | Exact sink removal and vacuum behavior where retained |
| Solid/property blocking | `properties & 1/2`, element state bits | Face coefficients/flux zero at blocked faces | No penetration; no mass entering blocked cells |
| Four-slot `world.flow` | pressure sweep + presentation | Must define conversion from projected face velocity to legacy flow/presentation units | Flow texture and visual acceptance; do not copy velocity bits into mass-flow slots |
| Dirty bits/events | `PressureSweep::notify`, EventQueues | Publish when public state changes | Event schema exact; order policy documented; no missed/duplicate events |
| Tick/region ordering | `simulation_step.rs`, `simulation_step_fluid.rs` | New solver called at a defined phase boundary | Existing non-pressure phase ordering unchanged |
| C# ABI/save format | native public interface and conformance harness | Unchanged | Exact ABI, save image and roundtrip compatibility |

## Acceptance layers

### A. Exact compatibility (must pass)

- C ABI structures, function signatures, status codes and ownership.
- Save image encoding and load/reload behavior.
- Event schema, enum values, payload layout and callback lifetime.
- Non-pressure phases: thermal, disease, radiation, components, post-process,
  and material interactions not explicitly in the approved change list.
- No GPU device requirement for the existing CPU fallback until a separate
  release decision.

### B. Numerical invariants (must pass)

- Per-species mass budget: initial + sources - sinks = final, within a
  predeclared absolute/relative tolerance.
- Thermal energy budget with explicit phase-change/source terms.
- Disease count budget with explicit growth/decay/consumption terms.
- Nonnegative species mass and valid material/disease indices.
- Zero normal velocity at outer and solid walls.
- Post-projection divergence reduction and Poisson residual.
- Deterministic behavior for a fixed input, solver settings and adapter.

### C. Gameplay compatibility (requires explicit envelope)

Fixtures must include:
- two gases with different molar masses and temperatures;
- gas into vacuum, gas next to occupied cells, and the sink element;
- liquid displacement with a full destination and max-mass boundary;
- mixed gas/liquid/solid interfaces and one-cell-thick channels;
- closed box, open/vented boundaries, corners and narrow passages;
- property-bit blocked cells and material-state blocked cells;
- disease-bearing gas/liquid, temperature gradients and phase transitions;
- region edges, scan-direction reversal, empty frames and repeated ticks;
- save/reload during and after a pressure event.

For each fixture record: public CellSOA fields, per-species mass/energy/disease
budgets, event lists, flow/presentation arrays, and behavior outcomes. Define
acceptable ranges before looking at candidate results; do not tune thresholds
after a failure merely to obtain a pass.

## Baseline protocol

1. Freeze the official library identity and native source commit.
2. Run the existing conformance harness on the legacy implementation and retain
   raw snapshots/receipts.
3. Run the candidate on identical fixture inputs and tick sequencing.
4. Compare exact interface and untouched-phase outputs separately from changed
   pressure outputs.
5. Produce a per-fixture delta report with explicit accepted/unaccepted changes.
6. Run save/reload and actual-game acceptance only after isolated fixtures pass.

The existing conformance harness compares all public arrays, 29 ordered event
lists, textures, frame counts and save roundtrips exactly. Keep it unchanged as
the exact baseline. Add a separate semantic comparison for intentionally changed
pressure behavior; never weaken the existing exact harness globally.
