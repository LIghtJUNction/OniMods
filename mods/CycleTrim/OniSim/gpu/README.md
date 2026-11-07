# OniSim Vulkan compute experiments

This is a **local operator test project**, not a GPU-enabled game build. It uses
Rust 1.99.1, wgpu **30.0.1**, hand-written WGSL and the Vulkan backend. OpenCL,
CUDA, OpenGL and software fallback adapters are not used. The shader and resource
layout are vendor-independent; actual AMD hardware acceptance is still required.

The first operator computes the flow presentation image from padded CellSOA
arrays. Each invocation reads one cell and writes one independent output. This
does not move the ordered fluid/thermal solver to the GPU. CPU buffers retained
by the managed game remain CPU addresses, not Vulkan texture handles.

## Run only the targeted checks

From this directory:

```sh
rtk proxy cargo test --locked --release --lib cpu::tests
rtk proxy cargo run --locked --release --bin flow-bench -- /tmp/flow-unique-report.json nvidia
# On an AMD host with a working Vulkan driver, select its real adapter name:
rtk proxy cargo run --locked --release --bin flow-bench -- /tmp/flow-amd-unique-report.json radeon
```

Reports must use a new path: previous observations are never overwritten. Exit
code 0 means all admission gates passed, 2 means the measured candidate failed
correctness or speed gates, and 1 means the experiment could not complete. A
missing adapter is an error, not a skipped passing GPU test.

To check the helper through its **native production compilation**, from `../native`:

```sh
rtk proxy cargo test --locked --release presentation::flow::tests -- --nocapture
```

These commands do not run the full native suite, official-library conformance,
package builds, game launches or deployments.

## Measurement contract

- Original CPU arithmetic/indexing versus the row-sliced production CPU helper
  versus a GPU candidate. Inputs use deterministic seeds and the real padded
  layout. Element u16 arrays upload directly, including odd-length tail handling.
- Seven shapes: 1x1, 3x5, 17x19, 96x96, 256x384, 512x512, 1024x1024. Three seeds
  per shape, repeated writes, transition masks, partial workgroups, wrong-sized
  output rejection, and a separate IEEE edge-value case.
- Five warmups followed by 31 quartets with rotating CPU/GPU sample order. CPU
  timing repeats small cases to amortize timer overhead and retains black-box
  output observations. Individual samples, median, p95 and range are reported.
- GPU timing includes all current-state uploads, dispatch, output copy,
  bounded synchronization, map/readback and the copy into existing CPU output.
  Context/pipeline/buffer creation and fixture allocation are outside timing on
  all sides. GPU buffers and bindings are reused between samples.
- Admission requires **zero differing float words** and at least **1.10x** speedup
  over the row CPU helper for a shape. The aggregate flag deliberately requires
  every tested shape and edge case to pass. No automatic GPU-selection policy
  is implemented or inferred from this experiment.
- NaN payload differences count as bit failures. `semantic_mismatches` excludes
  pairs where both values are NaN; signed-zero differences remain failures.
  Non-finite errors are counted separately instead of serializing infinity as
  JSON null. No tolerance is widened to make a failing shader pass.

## First observation and next boundary

On the local RTX 4060 Laptop, the first flow GPU candidate failed both exact
float-word comparison and end-to-end speed gates. On 256x384 and larger grids,
the row CPU helper was about 2.1x faster than the original CPU indexing, while
the GPU roundtrip was about 12.5x slower than that helper. A follow-up run showed
1.8-2.1x CPU speedup and 10.6-16.3x GPU slowdown at those sizes. This measures only
this flow image pass, not simulation frames or game FPS. Host/GPU contention
must be recorded with each run; these numbers are not a hardware-wide ranking.

Only the CPU helper is connected to `PresentationBuffers::update`. The Vulkan
project has no dependency edge from the native core and is excluded from the
game build. Its failed shader remains an explicit experimental candidate. The
CPU helper still needs full conformance and real-game acceptance before release.

Further GPU work needs a more compute-heavy stage or multiple dependent stages
resident on the GPU, with deterministic arithmetic and controlled synchronization.
The fluid/pressure sweeps mutate neighboring cells in a defined order; replacing
them with an unordered GPU stencil would change the simulation.

DLSS/FSR are a separate rendering integration. The observed Linux game uses
OpenGL, while NVIDIA's DLSS SDK requires a supported DirectX/Vulkan rendering
integration and the appropriate frame inputs. This project adds no DLSS/FSR
switch and claims no upscaling support.

## Follow-up operator revision

The current shader uses 32x8 two-dimensional workgroups, removing per-cell
division/modulo from padded-grid addressing. A bounded integer long division
computes the reciprocal with round-to-nearest-even instead of Vulkan FDiv's
permitted approximation. The normal-data corpus and a 257x257 stress fixture
covering reciprocal exponents 127..252 now compare bit-exactly against CPU.
The existing special-value gate still fails; no GPU production dependency was
added and the original speed/exactness gates remain unchanged.

`upload` and `run_resident` separate snapshot upload from dispatch. Resident use
before upload, and after a rejected upload, is refused. The benchmark now runs
31 rotating **quartets** (original CPU, row CPU, complete GPU roundtrip, and a
resident-input diagnostic which still includes dispatch/sync/readback). The
resident diagnostic reuses one immutable snapshot, so it cannot be reported as
an end-to-end simulation speedup. Even excluding uploads, the measured resident
path is slower than CPU at the tested colony-sized grids.

An external Linux FSR1 launch profile is documented separately in
[`docs/cycletrim-fsr-linux.md`](../../../../../docs/cycletrim-fsr-linux.md).
That profile does not enable this experimental simulation GPU backend.

References: [wgpu 30.0.1](https://docs.rs/wgpu/30.0.1/wgpu/),
[NVIDIA DLSS programming guide](https://raw.githubusercontent.com/NVIDIA/DLSS/main/doc/DLSS_Programming_Guide_Release.pdf),
[AMD FSR1 input model](https://gpuopen.com/fidelityfx-superresolution/).
