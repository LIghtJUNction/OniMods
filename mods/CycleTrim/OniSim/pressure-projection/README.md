# OniSim GPU Poisson Pressure Projection — Stage 2 research

## Decision

Investigate replacing the legacy gas/material pressure-transfer sweep with a
GPU-resident incompressible pressure-projection pipeline. This is a research
branch only. The production solver, C# ABI, save format, and game dispatch remain
unchanged until the compatibility gates in this document pass.

The replacement is **not** a source-level substitution. The current algorithm
changes CellSOA mass and sometimes element identity directly; a Poisson
projection changes a velocity field. A conservative advection/material adapter
is therefore required between the new velocity field and OniSim's material
state.

## Source-derived legacy behavior

The current call order is visible in
[native/src/simulation_step.rs](../native/src/simulation_step.rs) and
[native/src/simulation_step_fluid.rs](../native/src/simulation_step_fluid.rs):

1. Each region runs gas pressure first, then gas displacement.
2. Liquid update and liquid pressure displacement follow as a separate phase.
3. The frame continues through disease diffusion, components, post-processing,
   and disease post-processing.
4. The world carries old/read and active/write CellSOA snapshots, a four-slot
   per-cell `flow` accumulator, a tick seed, and directional scan state.

The legacy gas/material pressure sweep in
[native/src/pressure_sweep.rs](../native/src/pressure_sweep.rs) traverses cells
serially in scan-direction order. For a candidate pair it computes

`flow = clamp(k * (m_a - m_b), -0.125 * m_b, 0.125 * m_a)`

where `k` is the source material's `ElementRecord.flow` for gas, or the
destination record's flow for non-gas phases. It moves mass between read/write
CellSOA storage, may replace an empty destination's element identity, transfers
temperature and disease counts, clears exhausted disease state, handles the
sink element, and emits substance-change/dirty notifications.

Gas displacement is a separate operation in
[native/src/gas.rs](../native/src/gas.rs). It checks the old snapshot's mass
ordering, moves a neighboring gas into a third cell, then replaces the middle
cell using a fixed 12.5% source-mass amount. Liquid displacement is separate
again in `fluid::do_liquid_pressure_displacement`; it uses remaining mass after
directional flow accounting, compares destination/neighbor masses, and can
displace another liquid before replacing the destination element.

These operations are order-sensitive and material-aware. They are not a
discretization of `div(beta grad p) = div(u*) / dt`. In particular,
`ElementRecord.flow` is a transfer coefficient, not a documented density or
pressure in SI units. The element table separately carries molar mass,
viscosity, heat capacity, transition data and default pressure, but the current
pressure-transfer expression does not use molar mass, temperature, or the
`default_pressure` field to calculate its transfer amount. The record has no
explicit cell-density field; converting mass to density requires a calibrated
cell-volume convention and a decision about phase-specific volume.

## Proposed numerical model

Use a finite-volume projection with shared face-normal velocities:

```text
div(β ∇p) = div(u*) / Δt
β_f = 1 / ρ_f
u_f^(n+1) = u_f* - Δt · β_f · (∇p)_f
```

The first reference uses a cell-centred provisional velocity, arithmetic
face-density averaging, closed outer walls, zero normal velocity on
solid-fluid faces, Neumann pressure boundaries, and a zero-mean pressure gauge.
Red-Black Gauss-Seidel updates one checkerboard color per pass; a device memory
barrier is required between colors. Pressure, density, face coefficients,
velocity and solid masks remain device-resident across iterations.

This equation is a candidate for a continuum-flow replacement, not an
assumption that the game uses SI units. The mapping from material metadata and
cell mass/temperature to `rho`, cell volume, viscosity and EOS pressure must be
calibrated against the pinned official game before a production adapter exists.

## Pipeline

1. Snapshot material fields and classify fluid/solid/empty cells.
2. Construct per-cell density and provisional face velocities using a
   documented material adapter.
3. Enforce impermeable outer/solid faces.
4. Compute finite-volume divergence.
5. Solve variable-coefficient Poisson pressure using GPU RBGS ping-pong or
   same-buffer red/black passes with explicit pass ordering.
6. Correct shared face velocities.
7. Advect each material mass and its temperature/disease concentrations using
   conservative shared-face fluxes.
8. Apply legacy-only material rules (phase transitions, sinks, displacement,
   interactions, events) through explicit compatibility hooks, not inside the
   pressure equation.
9. Publish CellSOA and event changes through the existing boundary.

## Current deliverable

- `reference/pressure_projection_reference.py`: standard-library CPU reference
  for variable-density finite-volume projection.
- `reference/test_pressure_projection.py`: numerical tests for divergence
  reduction, residual, wall impermeability, invalid density and zero-velocity
  fixed point.
- `reference/semantic_compare.py`: snapshot comparator for per-material mass,
  sensible-energy proxy, disease budgets, changed cells, event counts and texture
  word differences. It deliberately returns UNASSESSED without a reviewed
  fixture-specific policy.
- `reference/test_semantic_compare.py`: comparator self-tests.
- `COMPATIBILITY-MATRIX.md`: legacy-to-new behavior map and promotion gates.

The existing exact conformance corpus already contains dedicated pressure
fixtures: paired Oxygen/Water mass-boundary cases, 16 deterministic mixed
pressure worlds, IEEE flow-coefficient word cases (including signed zero,
subnormals, infinities and NaNs), and gas/liquid/vacuum/heat/disease scenarios.
The source currently defines 16 Oxygen/Water mass-boundary pair cases, 16
seeded mixed pressure worlds, 24 IEEE flow-coefficient word cases and four
thermal ordering cases in `pressure_cases.py`; the broader corpus also contains
gas-pressure, gas-vacuum, water-drop, water-gas, heat and disease fixtures.
The exact runner remains the frozen legacy baseline; the semantic comparator is
for candidate-vs-baseline deltas after the new pressure model is connected.

The CPU reference validates the mathematical operator only. It does not claim
that the operator reproduces the official game's pressure behavior.

## Promotion rule

No production switch until:
- the CPU reference and WGSL kernel agree within a declared tolerance;
- the Poisson residual and post-projection divergence gates pass on varied
  geometries and density ratios;
- material mass, species identity, thermal energy and disease budgets are
  accounted for;
- all existing public conformance cases and new pressure-specific gameplay
  scenarios pass their agreed semantic acceptance envelopes;
- actual-game save/reload, event, visual and performance acceptance passes.

Exact bitwise equality with the legacy pressure sweep is not a valid expectation
for a different continuum model. Exact compatibility remains required for
unchanged ABI, save encoding, event schema, and all code paths not explicitly
approved for changed physical behavior.


## Stage 2.1 — GPU kernels

The follow-up research branch `experiment/onisim-pressure-wgsl-kernels` adds
WGSL face preparation, divergence, Red-Black Gauss-Seidel and velocity
projection entry points, plus a wgpu Vulkan runner and a hardware benchmark.
See [Stage 2.1 GPU kernel report](../gpu/STAGE-2.1-GPU-KERNELS.md).

This remains an isolated projection API: it uploads density and provisional
velocity for each call, then keeps the pressure iterations and projection on
the GPU. It is not yet a full multi-physics GPU-resident game timestep. No
material-to-density mapping, legacy pressure replacement or GPU performance
claim is included.

## Stage 2.2 — validation hardening

The follow-up branch `experiment/onisim-pressure-projection-validation` extends
the pressure benchmark with a normalized Poisson residual computed from the
returned GPU pressure field and the original finite-volume RHS. It reports
residual alongside CPU/GPU corrected-face parity and divergence reduction.
The hard gates remain parity <= 1e-4 and strictly reduced divergence; a
residual threshold will be calibrated after representative Vulkan hardware
runs rather than guessed.


## Stage 2.4 — explicit gas EOS calibration

The GPU crate now includes `gpu/src/material_eos.rs`, a first-principles ideal-gas adapter that requires explicit unit scales. It can:
- infer an effective cell volume from a gas element's default mass, molar mass, temperature and pressure;
- compare multiple gas records and reject inconsistent inferred volumes;
- compute mixture density and total pressure from species masses and molar masses;
- preserve a vacuum as zero density/pressure rather than silently inventing a density floor.

The utility maps the native `ElementRecord` fields `molar_mass`,
`default_mass`, `default_temperature`, and `default_pressure`. Unit scales
must be supplied explicitly; the implementation does not assume the game's
wire values are SI. Six synthetic tests cover the ideal-gas equations,
cross-record volume consistency, vacuum, and invalid inputs.

This is a calibration tool, not yet a production material adapter. It has not
been run against an official in-game element table. A zero-density vacuum is
still outside the current Poisson solver's positive-density domain and needs
an explicit free-surface/open-boundary treatment or a separately calibrated
density-floor policy before gas-to-vacuum projection can be enabled. Liquid
and solid densities still require their own measured/configured material data.
