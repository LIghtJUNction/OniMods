# OniSim publication and sunlight indexing

Two native CPU paths now avoid reconstructing padded-grid coordinates for every
public cell. Neither change adds threads, GPU dependencies or heap buffers.

`PhysicsWorld::publish_to` copies thirteen SoA streams one row at a time. This
removes per-cell division/remainder and interleaved writes to many independent
arrays. It keeps the existing packed-disease setter for infestation/growth, so
other reserved bytes and cell/backwall padding remain unchanged. Destination
arrays and descriptor addresses stay allocated in place. The constructor's
positive dimensions and padded layout remain the internal precondition.

`presentation_sun::update` already knows each row and column, and now computes
the padded cell index directly. Its float expressions, descending column scan,
blocking/override behavior and world-offset order are unchanged. Nonpositive
widths still skip; zero/negative heights retain the native top-row write rule.

## Verification

The [receipt](../mods/CycleTrim/OniSim/conformance/results/linux-744825-publication-sun.json)
contains source/binary hashes, CPU models, host context and all timing samples.

- Four targeted correctness tests cover publication fields, IEEE words, preserved
  reserved/padding bytes, descriptor/address stability, irregular dimensions,
  repeated publications, overlapping/degenerate sunlight rectangles and sixteen
  MXCSR control combinations. Zero/negative height cases are checked separately.
- The complete native suite passes **55 tests**; two ignored performance tests
  are run explicitly and separately, and both pass their fixed gates.
- One-worker and four-worker configurations each match the installed pinned
  official Linux library in **287 cases / 9739 publications**, including all
  public array words and ordered events.
- The checked native artifact is
  `2f343794ebb0e301877c862914235de5f2c2d3c0f770c121261b0561c3e14853`.
- Independent source review found no blocking issue in either valid-state path.
  It also independently checked the previous flow SIMD's bounds/float behavior;
  this does not establish Windows/macOS runtime acceptance.

## Local operator measurements

Both benchmarks call production functions against frozen former implementations.
Fixture creation and comparisons are outside timing. Each uses five warmups and
41 alternating sample pairs. Timing runs are serialized by an advisory lock;
only the owned child is pinned, to CPU0 for sunlight and CPU2 for publication.
No host governor or unrelated process affinity is changed. Host load, CPU model,
governor/frequency context and process RSS are recorded; RSS includes the entire
fixture harness, and is not a measurement of game memory savings.

| Operator | Tested colony-sized widths | Median speedup |
| --- | --- | --- |
| Sunlight image generation | 256, 512, 1024 | 1.09–1.39x |
| World-to-public-grid publication | 96, 256, 257, 512, 1024 | 1.90–3.63x |

The preselected gates require no median slowdown above 3% in colony-sized cases,
and at least one improvement of 10% or more. Small-size overhead remains in the
receipt. Sunlight fixtures cover vacuum, mixed materials/override flags, a
separately blocked gas layout and split/overlapping worlds. Publication fixtures
cover mixed raw data and uniform data. These authored workloads are not colonies.

Do not multiply these operator ratios or treat them as whole-frame/FPS gains.
The previous whole-frame pressure bottleneck is not removed by these changes.
Real ONI graphical/save/mod-combination acceptance and other-platform runtime
checks remain separate. This iteration updates source, not a Steam Workshop
package or the currently running game's loaded binaries. Experimental GPU/FSR
paths are not enabled by these CPU changes.

## Reproduce in order

From `mods/CycleTrim/OniSim/native`:

```sh
rtk proxy cargo test --locked --release presentation::sun::tests
rtk proxy cargo test --locked --release world::publication_tests
rtk proxy cargo test --locked --release benchmark_sun_indexing -- --ignored --nocapture --test-threads 1
rtk proxy cargo test --locked --release benchmark_publication -- --ignored --nocapture --test-threads 1
rtk proxy cargo test --locked --release --lib
rtk proxy cargo build --locked --release
```

Run timers with stable per-child affinity and avoid concurrent compilation or
other benchmarks. The logs use `SUN_CSV`, `PUBHEADER` and `PUBCASE` markers; a
Rust harness prefix can share the first line, so locate each marker within the
line rather than assuming it starts at column zero. Then use the conformance
runner with the exact candidate and fingerprint-matching installed reference,
once for each worker configuration. Never bypass a reference fingerprint gate.
