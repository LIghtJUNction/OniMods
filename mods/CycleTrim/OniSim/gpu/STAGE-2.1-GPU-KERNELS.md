# Stage 2.1 — GPU Poisson pressure projection kernels

## Summary

This increment adds the first Vulkan execution path for the continuum pressure-projection reference. It implements six WGSL entry points and a wgpu runner. The implementation remains isolated from the production simulation tick, CellSOA publication, C# ABI and save format.

## Kernel pipeline

| Order | WGSL entry point | Operation |
|---|---|---|
| 1 | `prepare_u_faces` | Build shared horizontal face velocity and inverse face density |
| 2 | `prepare_v_faces` | Build shared vertical face velocity and inverse face density |
| 3 | `compute_divergence` | Compute finite-volume divergence and Poisson RHS |
| 4 | `rbgs_pressure` | Update one checkerboard color of the variable-coefficient pressure field |
| 5 | `rbgs_pressure` | Update the other color after the preceding compute pass |
| 6 | `project_u_faces` | Correct horizontal face velocity |
| 7 | `project_v_faces` | Correct vertical face velocity |

All stages run in one command submission. Each RBGS color is a separate compute pass, so the second color observes the completed writes of the first color. The pressure field is stored in a device-local storage buffer throughout the iteration.

## Discretization

The operator uses shared face values and coefficients:

`beta_f = 2 / (rho_left + rho_right)`

`div(beta grad p) = div(u*) / dt`

`u_new = u* - dt * beta_f * grad(p)`

The GPU solver prefers a discrete Vulkan adapter and falls back to an integrated adapter. It fixes the pressure gauge at the first fluid cell supplied by the host. The CPU reference uses a zero-mean pressure gauge. These gauges differ only by an additive pressure constant in the exact system; velocity correction depends on pressure differences. GPU/CPU parity must compare corrected face velocities and residuals, or normalize the pressure offset before comparing pressure arrays.

The GPU RHS is formed from shared closed-boundary faces. The CPU reference explicitly removes round-off mean from the RHS before solving; the GPU path currently relies on the shared-face telescoping sum and a pinned pressure gauge. The residual report must be checked for large domains and irregular solid geometry before accepting this numerical difference.

## API and benchmark

Rust API:
- `PressureProjectionGpu::new(width, height)`
- `PressureProjectionGpu::run(input, dt, dx, dy, iterations, omega)`

Benchmark:
```sh
cd mods/CycleTrim/OniSim/gpu
rtk proxy cargo run --locked --release --bin pressure-projection-bench
```

The benchmark selects a real discrete or integrated Vulkan adapter only. It reports adapter identity, median/P95 end-to-end time, and before/after divergence norms for 96×96 and 256×384. It uses five warmups and 31 samples. The default RBGS iteration count is 256 and can be changed with `ONISIM_PRESSURE_ITERATIONS`.

## Correctness and current limitations

- Naga parses and validates the pressure WGSL as part of the Rust library test.
- The existing Python CPU reference continues to test residual, divergence reduction, solid/outer-wall impermeability and invalid inputs.
- The GPU benchmark hard-fails if scaled CPU/GPU corrected-face error exceeds 1e-4 or if projected divergence is not lower than the input divergence. It reports the ratio but does not yet fail on a calibrated Poisson residual threshold.
- The GPU pressure gauge is pinned-cell rather than zero-mean; compare pressure gradients, not raw pressure words.
- No GPU hardware execution result is claimed until the benchmark is run on a Vulkan adapter.
- The current API uploads density, provisional cell velocity and solid mask per call, then keeps all pressure iterations and velocity correction on the GPU. It is not yet connected to a fully GPU-resident multi-physics timestep.
- No material-to-density mapping exists yet. Density must be supplied explicitly; do not infer it directly from mass or `ElementRecord.flow`.
- RBGS is a correctness-first baseline. Large grids may require many iterations; multigrid or a preconditioned solver should be evaluated after correctness is established.

## Safety boundary

No production pressure sweep is replaced. Gas three-cell displacement, liquid displacement, temperature/disease transfer, sink semantics, events, presentation flow and CellSOA publication remain untouched. The pressure-projection runner is an isolated research API.

## Claim/evidence map

- Claim: WGSL contains divergence, RBGS and velocity-correction stages. Evidence: six named WGSL entry points and the Rust pipeline bindings. Status: source implemented; CI compilation pending.
- Claim: RBGS color updates are ordered. Evidence: distinct compute passes in one command encoder. Status: source implemented; GPU validation pending.
- Claim: pressure iterations remain device-local. Evidence: storage pressure buffer is read/written by RBGS passes; only final output is copied to readback. Status: source implemented.
- Claim: GPU pressure solve improves performance. Evidence: no hardware run yet. Status: needs evidence.
- Claim: new pressure model preserves game behavior. Evidence: no material adapter or semantic differential run yet. Status: unsupported.

## Adversarial self-review

1. Contribution: the first integrated GPU pressure projection pipeline is now present, but RBGS itself is a standard solver, not a novel numerical method.
2. Reproducibility: workgroup size, stencil, gauge, iteration count, relaxation and benchmark protocol are explicit.
3. Numerical strength: CPU reference tests pass; GPU parity and residual thresholds remain open.
4. Evaluation completeness: add GPU/CPU corrected-face parity, irregular geometry, high density-ratio and long-run tests.
5. Method soundness: the density-to-material mapping and compatibility adapter are still unresolved; do not promote this into production.
