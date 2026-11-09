# CycleTrim whole-chain validation, 2026-11-09

The [report](../cycletrim-whole-chain-20261109.md) explains the complete update chain, the fixed protocols, all eight performance matrices and the final withdrawal decision. This directory preserves the public plans, runners, complete result summaries and source provenance. Its manifest records both original and published hashes. Three published copies remove one empty line at EOF to satisfy the repository whitespace gate; the original records remain unchanged in the evidence archive.

## Acceptance and scope

All four complete native candidate combinations failed their original performance controls. The four upload ablations returned **FAIL / PASS / FAIL / FAIL**; the second result applies only to its frozen native library and does not validate later combinations. All failed controls and tail results remain in the report and evidence archive.

The final release retains the existing `f40858eb626b8b4d18f0683ce3d0ed17b62fa1ea` production optimizations, the new 22-phase diagnostics and the manual native harness. It removes this iteration's unaccepted production optimizations. The final rejected candidate and its unmodified tests remain at [d374cff01fd2df4b958ef3c08beabdbe15605b33](https://github.com/LIghtJUNction/OniMods/tree/d374cff01fd2df4b958ef3c08beabdbe15605b33).

## Complete raw evidence

The [delivery folder](https://drive.google.com/drive/folders/1Cwoz7F__UUnYR43A371yYK0dw1ZDCalr) holds the complete evidence archive, report, retained Linux package and a separately labelled rejected-candidate reproduction archive. File availability and hashes are recorded in the report's delivery section after verification.

The evidence archive preserves 5,168 native timing-process logs, 41,344 checked native snapshot records and 2,448 upload samples, together with every fixed plan, input identity, prior failure, correctness receipt and final withdrawal validation. The first two native runs produced `summary.json` and `inputs.json`; the later runs also produced a separate `receipt.json`. No missing receipt is reconstructed as a historical record.

The frozen review scripts import the included prior-round review helpers and use the original absolute paths recorded in samples and execution metadata. Extracting the archive beside the scripts is not sufficient after moving it. Reproduction must preserve those paths or supply an explicit filesystem path mapping; use the archive’s original scripts, samples and input records unchanged so their recorded hashes remain meaningful. The Git mirror’s documented EOF-only formatting changes do not replace those frozen inputs. The native harness is part of `mods/CycleTrim/OniSim/native/src/native_chain_tests.rs`; the archived upload harness is part of the pinned candidate test project. Do not connect that archived test project to the final, withdrawn Compatibility implementation and call it the same input.

## Evidence boundaries

CPU native measurements include submission, real worker execution, publication and complete state/image/event checks. Upload measurements use the actual native FFI with instrumented host texture operations. Neither measurement is an ONI runtime capture, a Unity GPU upload measurement or displayed game FPS. Source review identifies existing CPU work and transfer boundaries but does not rank their cost in a real save.

The separately labelled rejected-candidate archive preserves the exact binaries for reproduction, including the final failed candidate package. It is not the retained release. No official game implementation source or compile-only game reference DLL is included in these deliverables.
