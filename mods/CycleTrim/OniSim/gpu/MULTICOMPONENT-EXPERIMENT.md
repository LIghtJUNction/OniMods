# Multi-component finite-volume GPU operator (experimental)

This is an isolated numerical operator prototype. It is **not** connected to the
OniSim production tick, C# ABI, save format, or rendering pipeline.

## Operator

- State is NHWC: `mass[(y * width + x) * channels + species]` and a matching
  sensible-enthalpy tensor uses the same layout.
- Each channel has a canonical element ID, gas phase, specific heat capacity and
  vertical drift. Element IDs must be unique. The current operator deliberately
  rejects liquid and solid species: its wall mask is a cell obstacle mask, not a
  per-species phase representation.
- Each cell invocation computes the four shared face fluxes using first-order
  upwind donor selection and updates its own output slot.
- Face flux is evaluated identically by both adjacent cells, so no atomics or
  write races are needed.
- Solid-solid and solid-fluid faces have zero flux. Outer boundaries are closed;
  there is no periodic wraparound.
- The update transports mass and sensible enthalpy with the same paired
  upwind face fluxes. Each gas species' mass and enthalpy are conserved.
  Temperature is recovered as `T = H / (m * cp)` for nonempty channels.
  It deliberately does not clamp negative values; the caller must enforce a
  CFL bound and reject invalid states instead of silently deleting mass.
- Buoyancy is represented as per-species vertical drift velocity. It is a
  tunable transport term, not a validated equation of state. There is no
  pressure solve, gas-liquid coexistence, phase change, latent heat, diffusion,
  chemical reaction, or liquid/solid transport in this operator.

## Validation

Run the NumPy reference gate:

```sh
python3 mods/CycleTrim/OniSim/gpu/multicomponent_reference.py
```

This checks per-species mass and sensible-enthalpy conservation, non-negative
state under the stated CFL bound, and impermeable wall cells for 100 steps across
several grid/channel shapes. The Rust GPU bench compares CPU and Vulkan mass /
enthalpy outputs, checks per-species conservation and reports upload/readback
cost separately from resident 32-step execution.

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


## Stage 1 gas enthalpy extension (2026-11-07)

The Rust Vulkan operator now transports sensible enthalpy alongside species mass
using the identical donor-cell face flux. The CPU reference and WGSL kernel are
compared by the GPU bench. Channel metadata contains canonical element ID, phase,
specific heat capacity and vertical drift. This stage explicitly accepts gas
channels only; the single cell obstacle mask is not misrepresented as a
multiphase phase map.

This remains an experimental gas-mixture transport operator. It does not supply
the production solver with a complete equation of state, pressure projection,
liquid coexistence, phase changes or latent heat. It remains separate from
`oni-sim-core` and the production GameData publication path.
