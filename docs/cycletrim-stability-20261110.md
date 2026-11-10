# Gas settling and whole-map performance investigation, 2026-11-10

## Reproduced behavior

The user reported roughly 9 FPS when zooming out and supplied screenshots of
gas cells repeatedly changing like a cellular automaton near the top space
boundary. Space loss is expected; strict mass conservation is not the user's
acceptance criterion. The target is useful diffusion, rapid settling, and less
repeated computation, with wake-up after external changes.

Initial diagnostic Release `cfbe61c5`, game U59-744825-SCRPAN, normal graphical mode,
1220x1521, 15 duplicants: changing camera zoom from 19.53 to 160 reproduced
slow full-frame callbacks. A diagnostic window had 57 intervals averaging
133.69 ms, of which `JobManager.Run` self time was 76.92 ms. Simulation was
running during that window despite the initial pause request. A subsequent
paused full-map window averaged 100.41 ms. These are instrumented callback
intervals, not presented-frame measurements or a controlled FPS comparison.

## Initial settling changes (superseded gas path)

- CPU pressure no longer changes a vacuum cell into gas when the transfer is
  zero. The new regression failed before the fix (vacuum element 0 became gas
  element 3 with zero mass).
- Sequential pressure transfers use the current cell masses instead of
  repeatedly applying a stale gradient. A sealed checkerboard remained at
  0.3978869–1.9476447 kg after 512 old sweeps; the new test requires a spread
  below 0.001 kg and an idle sweep.
- Same-gas residual pressures below max(1e-7 kg, 1e-5 relative) settle. Vacuum
  and sink boundaries retain positive transfers; this is not a global gas
  deletion threshold.
- GPU simultaneous faces cap conductance at 1/8, retaining a positive central
  weight with six neighbours, rather than relying only on donor clipping.
- Two bounded GPU caches retain the last no-flow gas result for each scan
  direction. Identical complete inputs and shape skip GPU submission/readback.
  Changes to mass, temperature, walls, coefficients, species or the halo wake
  calculation automatically. This does not yet skip host packing or all the
  other simulation phases, nor sleep individual tiles in a changing region.
- Homogeneous gas with no pressure/displacement work no longer requests a
  redundant post-fluid copy merely because it is gas.

## Measured overlay improvement

At the same full-map camera (88.34, 221.54, orthographic size 160), resolution
1220x1521, oxygen overlay and paused simulation, twelve diagnostic windows
averaged 82.25 ms/frame before Jlib overlay batching and 35.60 ms/frame after.
JobManager self time fell from 58.28 to 7.59 ms/frame. These are instrumented
Game.Update intervals, not presented FPS or a running-simulation benchmark.
Jlib commit `9f5f847` batches 128 overlay cell reads and uses total breathable
mass for oxygen colours. Its 602 assertions and Release build passed.
Evidence: `/tmp/cycletrim-e2e-20261110-oxygen-profile/fullmap-oxygen-before.json`
and `/tmp/cycletrim-e2e-20261110-render-batch/oxygen-after.json`.
An eight-second bounded run advanced 5.375 game seconds: running performance
still needs work.

## Equal-species solver work (not deployed)

The working tree replaces the production gas pressure pass with full-channel
transport, including single-species gas. It retains all transported species
instead of projecting motion into two storage slots. The Grid element remains
a compatibility projection. Space decay scales all species by phase totals;
it no longer drains only the element stored in the first slot. Liquid transport
still needs a separate equality audit and actual space-boundary acceptance.

A region cache skips dispatch when the complete previous settled input matches.
Small mass residuals and temperature residuals settle; changed inputs wake the
region. Host packing still runs, and an active part of a region prevents whole-
region sleep. The normal CPU wrapper does not yet retain this cache across calls.

A new regression exposed artificial flow next to walls: density gradients
sampled solid neighbours as empty gas. CPU and GPU now use the centre density
at blocked faces. Nine CPU transport tests pass, including idle/wake behaviour,
single-species vacuum spreading and storage-order independence. The targeted
Vulkan density-gradient test also passes with blocked-face expectations.
Evidence: `cpu-transport-limited.txt` and `gpu-wall-limited.txt` beneath
`/tmp/cycletrim-e2e-20261110-oxygen-profile/`.

Do not treat earlier suite results as acceptance of the final working tree.
The old 132-pass native result predates this solver overhaul. A parallel native
run later suffered GPU device-loss failures. A serial GPU run reported 48 pass,
6 ignored before its wrapper was interrupted and before the latest wall fix.
After the wall fix, the resource-limited serial native suite passed 136 tests
with 9 ignored in 31.76 seconds (`native-serial-limited.txt` in the same
evidence directory). This is host/Vulkan evidence, not game acceptance.

## Host I/O and resource pressure

After the user stopped the overloaded session, memory was about 7.2 GiB used
of 15 GiB, with 8.1 GiB available. Swap use was approximately 5.4 GiB, mostly
zram; around 0.85 GiB was on physical swapfiles. Kernel logs at 16:49–16:50
showed NVIDIA allocation/out-of-memory failures during overlapping GPU work.
This supports resource contention as a contributor to the earlier freeze;
it does not establish a defective SSD. The filtered recent kernel log contained
no matching disk timeout/reset, I/O error or Btrfs error.

The home filesystem had about 540 GiB free (41% used). A subsequent twenty-
sample, one-second-interval observation recorded 27.9 MiB read / 8.5 MiB written
and 605 ms busy time on home nvme0n1; root nvme1n1 had 2.1 MiB read / 1.2 MiB
written and 35 ms busy time. No discard operations or sampled D-state tasks
appeared. One low-volume home read interval averaged 32 ms; short bursts remain
possible. System I/O PSI full avg10 was still 8.23% at the end. These observations
do not prove that intermittent latency is resolved or identify one persistent
background culprit. No sustained bandwidth saturation was observed.
Raw evidence: `/tmp/cycletrim-e2e-20261110-oxygen-profile/io-sample-20s.json`.

Subsequent checks run serially without ONI, in a user systemd scope with
CPUQuota=100%, MemoryHigh=3G, MemoryMax=4G, MemorySwapMax=0 and IOWeight=10;
Cargo jobs and test threads are both one. The mod-test skill now requires this
resource discipline. No global swap/cache changes or other-user-process stops
were made. CPU cache/wake and the targeted GPU wall tests completed under these
limits with zero current memory-pressure average.

## Remaining acceptance

Build and deploy the final solver, then use OniMcp for bounded game tests of
mixed gas and liquid loss at actual open-space cells, wall sealing, settling
and input wake-up. Verify the queued screenshot fix with consecutive requests.
Record fixed-camera paused and running measurements separately.

The original save is untouched. Runtime multicomponent composition is not
fully preserved by saving/reloading, so a cleaner screenshot after reload is
not evidence that the same gas pattern converged. The deployed native library
is still the earlier settling build (SHA256
`6fc4a5b0d9d10f2d36d683c0390fab89b39059ed2122b819300415c07b0a69b5`);
the new equal-species solver and region cache are not yet installed in ONI.
