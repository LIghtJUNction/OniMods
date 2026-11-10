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
| OniSim native `cargo test --lib` | 140 passed; 0 failed; 9 ignored | Includes 11 isolated hardware Vulkan cases; not game-level GPU or FPS acceptance |
| `CycleTrim.Optimizations.Tests` | All isolated host families passed | Includes 27 diagnostic phases; no game FPS claim |
| `CycleTrim.PerformanceProbe.Tests --skip-synthetic-performance` | Passed | Publication/render cache, lifecycle, concurrent counters and scoped invalidation |
| `--regression-native-lease <release library>` | Passed | Real native library: held reader does not block worker publication/shutdown, old data remains valid |
| `OniSim.Loader.Tests <bundle root>` | Passed on Linux | Native exports, initialization, shutdown and resident module lookup |
| Performance probe source/session/analyzer checks | Passed | Source and analyzer contracts |
| `onim -m Jlib build --release`, `onim -m CycleTrim build --release` | Passed | Local production builds |

Detailed local logs are in `.cache/cpu-rewrite-20261110/`. The frame diagnostic fixture initially omitted the job-stage hooks; it was corrected and the complete optimization suite passed afterward. That earlier failure is retained in the local logs.

## Release CI

- [Three-platform native build and loader checks](https://github.com/LIghtJUNction/OniMods/actions/runs/42813291812) passed for Windows, macOS and Linux from `bbbaa289`. All three source and binary fingerprints were validated before packaging.
- [Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42813291983) passed for the release implementation.
- Hosted proof workflows initially failed because they attempted hardware Vulkan tests without a hardware adapter. Their CPU filters now match the native release workflow; the GPU operator job explicitly excludes its 27 hardware cases while still compiling them. The [diamond](https://github.com/LIghtJUNction/OniMods/actions/runs/42814487279) and [transport](https://github.com/LIghtJUNction/OniMods/actions/runs/42814487140) workflows then passed. Hardware tests remain in the default local suite; excluded CI tests are not counted as passed.
- Reference CI identified an omitted `JobManager` signature baseline for the diagnostic hook. The added `Run(IWorkItemCollection)` target was reviewed against the artifact from the same pinned assembly hashes. The full baseline comparison passed locally after adding only that target, and [reference compatibility CI](https://github.com/LIghtJUNction/OniMods/actions/runs/42815197427) then passed.

## Real-game boundary

The installed versions before testing were CycleTrim 0.4.12 and Jlib 0.3.2. A disposable copy of the save was used to start the 0.4.13 / 0.3.3 baseline via Steam and OniMcp. Startup and short running samples were affected by paging and another project's compilation. The user requested closing the game before the candidate run; the process was terminated and its exit verified. The 0.4.14 candidate has not completed an ONI runtime acceptance run.

The public release retains the existing all-platform native bundle requirement. Platform CI build/loader results, Legacy upload verification and actual ONI runtime compatibility are separate evidence. Full liquid species equality, complete mixture persistence and end-to-end open-space loss acceptance remain pending.

## Publication and local delivery

Both existing public Workshop items were updated through the Legacy single-ZIP publisher. The client subsequently downloaded each package and its SHA-256 matched the uploaded ZIP:

| Item | Version | ZIP SHA-256 |
| --- | --- | --- |
| [CycleTrim](https://steamcommunity.com/sharedfiles/filedetails/?id=3807166689) | 0.4.14 | `e1e5a8236bfb169e64ab927c7634842fe95c4ebb0188b8a803a9191bca1c7e6d` |
| [Jlib](https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839) | 0.3.4 | `3297e65cc13f8d2659ffab06bdc319fa05eb6b2aacc579dca5e6ed1de8fe5346` |

CycleTrim's first two client download checks returned Steam timeouts after the upload succeeded. The verifier now retries a timeout at ten-second intervals, up to five attempts within its existing five-minute deadline, and still requires a successful callback and matching ZIP hash. Other download failures still stop immediately. Publisher host regressions and its Release build passed; the subsequent consumer download verification succeeded without another upload.

The existing local Dev copies were synchronized to these exact release ZIP contents, with all 130 packaged files verified. The game remained closed throughout publication and delivery.
