# Native withdrawal review: branch B

**Disposition:** all current-round native production candidates are withdrawn after N4 failed the fixed acceptance gates. Native production source is restored to f40858eb626b8b4d18f0683ce3d0ed17b62fa1ea. Only the manual complete-chain test harness remains. This is restoration/correctness validation, not a new performance experiment or speedup claim.

The validation parent was `d374cff01fd2df4b958ef3c08beabdbe15605b33` with the reviewed withdrawal in the working tree. **That rejected parent commit is not the final release build identity.** The eventual withdrawal commit must be linked to the exact source hashes in native-withdrawal-review.json before packaging.

## Preservation before withdrawal

The root agent archived the actual rejected d374 ZIP, native library, test executable and managed host under rejected-candidate-d374/manifest.json. This review rechecked all 11 archived files against their recorded byte sizes and SHA-256 values before changing source. Manifest SHA-256: `7eb03a44fc44d55fa634974230bb8f625c80e82eb1865edb7d9232708d1bf7dd`. Git commit d374cff01fd2df4b958ef3c08beabdbe15605b33 retains all rejected source and candidate tests, including their full assertions. Earlier raw matrices, failed decisions and protocol snapshots remain untouched.

## Exact source restoration

All 103 files present in the f408 native tree were compared against Git blob bytes. Every original file is identical, except lib.rs has precisely the test-only suffix shown below. Removing that exact suffix yields the original lib.rs bytes. No original test assertion, FFI guard or simulation formula was altered.

```rust
#[cfg(test)]
mod native_chain_tests;
```

Seven original files were restored: frames_data.rs, frames_engine.rs, lib.rs, presentation.rs, state.rs, worker.rs and worker_loop.rs. Five candidate-only files were removed with their features: presentation_history_tests.rs, presentation_identity_tests.rs, presentation_revision.rs, presentation_revision_tests.rs and worker_payload_tests.rs. This removes the new read-sync/revision/content-proof paths, optional metadata export and boxed worker payload ownership. Their rejected behavior and regressions remain available at immutable d374.

The only files added relative to f408 are native_chain_tests.rs, native_chain_fixture.rs and native_chain_snapshot.rs. All three match the fixed baseline checkout harness byte-for-byte. They are reachable only through cfg(test); normal library production builds do not include the manual harness. The retained entry remains an ignored manual test. The complete diff relative to f408 is exactly those three added files plus the test-only lib.rs suffix. `git diff --check` passed.

## Exports and boundary safety

The source export inventory exactly matches f408: **30 API exports**, including **16 official required exports** from official-abi.json. The restored release shared library was independently checked with `nm -D --defined-only`; all required and baseline API names are present. `ONISIM_GetPresentationUpdate` is absent, as intended for removal of the candidate publication gate. This is not removal of an original required export.

Byte equality preserves the existing FFI capacity, integer-overflow and native-storage-overlap checks; species read-lease lifetime; worker locking, readiness barrier, visibility handoff, fault retention and shutdown/drop paths; stable public Grid storage and pointer layout; generation/lifecycle checks; and original physics validation. This review does not strengthen claims about unsupported raw-pointer use or experimental exports. It verifies that their baseline behavior has not been weakened.

## Build identities

| Artifact | SHA-256 | Purpose |
|---|---|---|
| native-withdrawn-tests | `af33dd387765bc9bd1710392f0f68d320a3fb2a9722e7ced5a2e51fcae8e326d` | Fixed Release test ELF for restoration validation |
| native-withdrawn/liboni_sim_core.so | `b8a0242dd4f808ecc280c18bd89b50066daa51b11c6d7c94a5e48b8ddd57e0fb` | Isolated Release library for export/managed absence checks |

Both builds used Rust 1.99.1, Release profile, locked offline dependencies and the existing shared target cache. Build logs are native-withdrawal-test-build.log and native-withdrawal-library-build.log. The isolated artifacts were copied to chain-validation; **the production dist directory was not modified by this work**. Final production packaging remains the root agent’s responsibility after the withdrawal commit.

## Correctness results

- All nineteen fixed native-chain scenarios were run in correctness mode against the fixed f408 baseline executable.
- Thirty-eight fresh processes produced twenty-four complete publication/world snapshots each: every recorded canonical SHA-256 matched. This includes five images, Grid arrays, events, OMC, subsequent state and stable Grid-address assertions.
- The driver finished with unchanged source, HEAD and executable identities. It was not run in benchmark mode. The historical `retained_idle` CLI label is used only for its restored-fluid source check; no retained native candidate path remains and no speed claim is inferred from incidental durations emitted by the harness.
- Default-concurrency full-suite result: **116 passed; 11 failed; 9 ignored**. The eleven failed names exactly match the prior suite and remain unavailable-hardware GPU initialization failures; they are not counted as passes. There are no new failures. No test filters or --test-threads override were used for this suite.

Logs and receipts: native-chain-correctness-withdrawn/{inputs.json,samples.json,summary.json}, its sibling .log, native-suite-withdrawn.log, native-suite-withdrawn-receipt.json, and native-withdrawal-review.json. The last file carries all original/restored source hashes, retained harness hashes, export inventory, validation commands and artifact identities.

## Limits and next handoff

Restoring the baseline does not establish a new whole-chain performance gain. N1–N4 remain rejected under their own fixed protocols, and none of their isolated speedups is promoted into an accepted release claim. Hardware Vulkan behavior and a live ONI/Unity session are not validated by this environment. The review confirms removal of the rejected production paths, preservation of the baseline source and exports, and complete-byte native correctness. The final source commit and packaged native binary must be checked against this manifest after the root agent’s incremental GitHub commit and final build.
