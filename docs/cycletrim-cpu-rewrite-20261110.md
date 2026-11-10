# CycleTrim 0.4.14 / Jlib 0.3.4 CPU implementation and validation

This release implements the CPU work identified in the [source audit](cycletrim-cpu-audit-20261110.md). It follows local testing first. The user stopped the subsequent baseline game run, then explicitly authorized committing, pushing and publishing from the local validation results. No candidate game FPS or complete gameplay acceptance is claimed.

## Retained changes

- Fetch work: parent-world item partitions, event-driven offset refresh, one pickup job barrier, and ordered per-tag candidates. Rocket parent changes, item removal, tag refresh and unknown scopes invalidate or bypass reuse. Dynamic eligibility and reservations are evaluated per query. Existing foreign fetch prefixes/transpilers and known competing fetch owners keep their pipeline.
- Navigation: select only the four eligible origins consumed by the existing scheduler, retaining its age, failure and ownership behavior.
- Chore refresh: parent-world invalidation; unknown scope and periodic freshness retain global invalidation. This does not eliminate every global refresh or replace the chore scoring model.
- Gas: compare settled region inputs before allocating/preparing the dense staging layout. Species, heat and geometry changes wake the solver. At most four idle region snapshots are retained.
- Publication: pin immutable buffers instead of the mutable simulation mutex. Readers may outlive a newer publication or shutdown. Reuse exact unchanged records, expand species storage, and copy only changed blocks of the stable Grid arrays. Block comparisons still read the arrays.
- Rendering: optional monotonic publication revisions allow reuse of gas colours. Provider identity, grid size and rendering context participate in invalidation; providers without revisions keep the prior path.
- Diagnostics: distinguish offset, pickup and chore jobs; attribute fetch callbacks to their actual main/worker context. Inclusive elapsed times remain non-additive.

## Completed local validation

| Check | Result | Boundary |
| --- | --- | --- |
| `CycleTrim.Fetch.Tests` | 5,903 assertions; 1,462 differential cases passed | Production helpers and real Harmony with synthetic game APIs |
| `CycleTrim.CpuWork.Tests` | Production hooks and foreign-owner case passed | Includes movement, rocket parent changes, compaction, offsets, tags, dynamic eligibility and exception cleanup |
| `Jlib.Tests` | 602 assertions passed, including bounded selection and candidate index coverage | No game DLL execution |
| OniSim native `cargo test --lib` | 140 passed; 0 failed; 9 ignored | Local CPU route; not hardware GPU acceptance |
| `CycleTrim.Optimizations.Tests` | All isolated host families passed | Includes 27 diagnostic phases; no game FPS claim |
| `CycleTrim.PerformanceProbe.Tests --skip-synthetic-performance` | Passed | Publication/render cache, lifecycle, concurrent counters and scoped invalidation |
| `--regression-native-lease <release library>` | Passed | Real native library: held reader does not block worker publication/shutdown, old data remains valid |
| `OniSim.Loader.Tests <bundle root>` | Passed on Linux | Native exports, initialization, shutdown and resident module lookup |
| Performance probe source/session/analyzer checks | Passed | Source and analyzer contracts |
| `onim -m Jlib build --release`, `onim -m CycleTrim build --release` | Passed | Local production builds |

Detailed local logs are in `.cache/cpu-rewrite-20261110/`. The frame diagnostic fixture initially omitted the job-stage hooks; it was corrected and the complete optimization suite passed afterward. That earlier failure is retained in the local logs.

## Real-game boundary

The installed versions before testing were CycleTrim 0.4.12 and Jlib 0.3.2. A disposable copy of the save was used to start the 0.4.13 / 0.3.3 baseline via Steam and OniMcp. Startup and short running samples were affected by paging and another project's compilation. The user requested closing the game before the candidate run; the process was terminated and its exit verified. The 0.4.14 candidate has not completed an ONI runtime acceptance run.

The public release retains the existing all-platform native bundle requirement. Platform CI build/loader results, Legacy upload verification and actual ONI runtime compatibility are separate evidence. Full liquid species equality, complete mixture persistence and end-to-end open-space loss acceptance remain pending.
