# Multi-component finite-volume GPU operator (experimental)

This is an isolated numerical operator prototype. It is **not** connected to the
OniSim production tick, C# ABI, save format, or rendering pipeline.

## Operator

- State is NHWC: `mass[(y * width + x) * channels + species]`.
- Each cell invocation computes the four shared face fluxes using first-order
  upwind donor selection and updates its own output slot.
- Face flux is evaluated identically by both adjacent cells, so no atomics or
  write races are needed.
- Solid-solid and solid-fluid faces have zero flux. Outer boundaries are closed;
  there is no periodic wraparound.
- The update is conservative by paired face fluxes. It deliberately does not
  clamp negative mass; the caller must enforce a CFL bound and reject invalid
  states instead of silently deleting mass.
- Buoyancy is represented as per-species vertical drift velocity. It is a
  tunable transport term, not a validated equation of state or a complete
  multiphase model.

## Validation

Run the NumPy reference gate:

```sh
python3 mods/CycleTrim/OniSim/gpu/multicomponent_reference.py
```

This checks per-species mass conservation, non-negative mass under the stated
CFL bound, and impermeable solid cells for 100 steps across several grid/channel
shapes. It also reports CPU-only NumPy timings.

The WGSL source is ready for integration into a wgpu pipeline, but no GPU timing
is claimed here. The current environment has PyTorch CPU-only and no Vulkan GPU
adapter. Do not compare this CPU reference timing with the 40–80 microsecond
target or infer RTX 4060 performance.

## Promotion gates

Before connecting to OniSim production:
1. Compile this shader with the pinned wgpu/Naga toolchain and run it on real
   Vulkan hardware.
2. Compare GPU output against the CPU reference, including edge, wall, sparse,
   high-CFL and mixed-species cases.
3. Verify mass and energy conservation over long runs and establish stable
   thermodynamic units.
4. Add pressure projection, phase-change and source/sink operators only after
   independently validated equations and tests exist.
5. Define a deliberate C# compatibility boundary; argmax projection alone is
   not behaviorally equivalent to the game's single-element simulation.
