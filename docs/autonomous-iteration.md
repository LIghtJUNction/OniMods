# Autonomous iteration: evidence and progress gates

Applies to OniMods maintenance agents and upstream research. Existing gameplay
safety instructions still apply. Scheduled execution is permission to inspect;
it is not a requirement to produce a commit.

## Select work by state, not by repetition

- **Single-branch (`main`) iteration strategy**: Iterate directly on `main` across `OniMods` and its subrepositories (such as `mods/Jlib`). Do not create or split side branches (`feature/*`, `experiment/*`, `release/*`, `cursor/*`) or leave submodules on detached HEADs. Keep `main` green by running relevant host/contract tests before committing.
- Read current main, open work, changed files, review findings and CI for the
  exact head SHA. Previous chat reports are leads, not authoritative results.
- Keep at most one actively implemented PR per role. Reuse an existing PR for
  the same problem. Do not work on files another role is actively editing.
- A PR blocked only on unavailable ONI runtime, credentials or an external
  decision is parked, not repeatedly rebased. Record the blocker, tested SHA,
  required evidence and unblock condition once in its body or one status comment.
- Revisit a parked PR when new evidence, relevant source changes, a review finding
  or a genuine conflict appears; otherwise check at most daily. Main advancing
  in unrelated files is not a reason to force-push, squash history or rerun CI.
- Work on another independent, testable issue while one is parked. Never lower
  its runtime acceptance gate just to make the queue move.
- Do not generate empty commits or unrelated changes to trigger checks. Do not
  overwrite another contributor's commits. Refresh against main when preparing
  to merge or when an actual dependency changes, not on every timer tick.
- No meaningful change means no new issue, PR or repeated status comment.

## Dependency propagation

**共享依赖的改动，以受影响下游闭环为完成条件。** 修改底层库、公共 API、ABI、协议、包或构建契约的 owner，负责枚举直接和间接依赖，包括库、应用、服务、模板、安装套件及发布产物，并追踪每一项到完成或真实阻碍。

1. **先列影响，再分工。** 在 Project Z 仓库执行：
   `rtk proxy python3 scripts/project_z.py impact --repositories <repo/package>`。
   将图中结果与实际依赖声明、子仓固定提交、包引用、锁文件、构建和安装清单核对。图只覆盖已登记关系；发现遗漏下游时登记仓库、依赖边和既有 owner，再继续处理。不得把图中未出现当作不受影响。

2. **沿用已有 owner。** 列明各下游 owner、当前工作和文件范围，先协调再修改。已有 owner 正在处理时交付变更契约、所需版本和精确源码，不抢写、不覆盖。没有明确 owner 的项目由任务负责人指定接手人。传播任务优先沿用既有 owner，避免重复建立并行维护会话。

3. **同步真实依赖。** 对受影响项目更新必要的最低兼容版本、provider 版本、固定源码/子模块提交、包引用、锁文件、CI 引用、模板和安装/发布清单。无需变更的下游记录兼容依据；既不遗漏升级，也不为凑齐版本号盲目抬高最低要求。新接口有新的最低依赖时必须连到实际消费者和模板。

4. **按影响验证并及时推送。** 在精确候选源码上运行相关的构建、契约和回归检查；涉及游戏生命周期、存读档或部署的内容，保留对应实机/运行门禁。按各仓现行规则分批提交推送，跟踪相应 SHA 的 CI。发布包、安装副本与源码分别记录身份；源码推送成功不能替代发布或运行成功。

5. **更新传播状态直到闭环。** 为每个下游记录：owner、输入/输出版本或提交、实际修改、验证命令与结果、推送/产物位置、剩余工作。已交接不等于已完成；继续跟进既有 owner。只有“已更新并验证”“经证据确认无需变更”或“存在明确实际阻碍”三类结果。阻碍必须写明失败或缺失条件、当前精确版本、接手人、下一步动作和解除条件，不能只写“待处理”。

传播过程中继续遵守已有授权、发布限制和凭据保护；禁止为完成状态绕过平台限制、降低验收门槛或宣称未执行的测试通过。汇报区分源码、包、安装环境和实际运行四个层次。

## Evidence has explicit boundaries

Every claim needs its source, revision, command or run URL, outcome and limitation.

| Evidence | What it establishes | What it does not establish |
| --- | --- | --- |
| Source-pattern contract | Expected text is present | Executed behavior or performance |
| Host executable test | Behavior exercised in that host/fixture | Unity/Mono or game state correctness |
| Reference build/API surface | Compatibility with the pinned reference surface | Compatibility with a newer game build |
| Pinned decompiled source contract | Selected method-body assumptions at that source revision | Provenance of a different binary mirror |
| Synthetic benchmark | Work/allocations/timing in a named synthetic workload | Game FPS or real workload frequency |
| ONI runtime capture | The observed build, save, mods and tested scenario | Untested builds, saves or mod combinations |

At the September 16, 2026 audit, the compile mirror at
`Sgt-Imalas/Sgt_Imalas-Oni-Mods@be4a70d8f335fdd8ac7f3b1ccf91b50bb6ddb46d`
declares target build **737790**. Separate pinned source contracts declare
**744825**. Always read the current manifest; neither the source timestamp nor
the official release number upgrades the mirror's binary evidence.

An immutable upstream pin is a reproducibility baseline, not an update detector.
A job that downloads the same pinned commit cannot detect changes to upstream
main. Research must separately compare upstream revisions and propose reviewed
pin updates; do not silently advance hashes or baselines.

Keep downloaded game assemblies and decompiled implementation source transient.
Use assemblies only for compilation/static metadata inspection, never execute
third-party game binaries. Upload allowlisted signatures/reports only after
validation, not raw source, DLLs, credentials or private save data.

## Tests should detect behavior changes

Prefer extending a relevant test project over creating a new executable project
for each assertion. A regression should fail on the known-bad behavior and pass
on the fix; grep markers and a mapping JSON are supporting evidence, not an
executed official conformance suite.

Run actual `git diff --check` for the complete PR/push range. The Mod quality
workflow performs this before expensive tests. If local GitHub DNS is unavailable,
cite that CI step when it has completed; do not claim a local checkout or confuse
a reconstructed excerpt with a complete diff. Missing history is a failure, not
a successful skip.

Do not compress code, remove useful comments or combine statements merely to
meet a line-count rule. Split cohesive responsibilities only when it improves
readability. Do not weaken existing tests, benchmark thresholds or security gates.

For performance work, fix correctness first. Record baseline and candidate
revision, runtime, workload, warmup, sample order, allocation and dispersion.
Choose success criteria before observing results. Do not keep raising thresholds
or adding classifiers to fit one hosted runner's noise. Measure the production
helper where practical instead of maintaining a separate optimistic model.

Default-off instrumentation still needs scheduling, lifetime, concurrency and
observer-overhead review. It is not automatically low-risk because it is a probe.

## Merge and handoff

- Read the full diff and relevant callers, tests and failure paths. Review quota
  exhaustion means no independent review occurred, not approval.
- Verify required checks on the exact candidate SHA. Queued, skipped, cancelled,
  action-required and unrun checks are not passes. A check on a previous SHA is
  not evidence for the current candidate.
- Preserve runtime gates for gameplay writes, save/load, lifecycle changes and
  unsafe patch interactions. No game runtime means no runtime-tested claim.
- Recheck head/base and review state before squash merge. Never bypass repository
  protection. Do not force-push main.
- Record the merge SHA and inspect main CI. Report pending push CI as pending;
  the next iteration checks it before starting other work.
- Persist only material decisions. Reuse one marked status comment
  (`<!-- onimods:iteration-state -->`) when possible. Do not recursively wrap
  bot summaries or turn every unchanged inspection into another comment.

The radar owns cross-project upstream discovery; implementation roles inspect
only task-relevant upstream changes. Shared CI/reference edits belong to the
API/CI role unless a direct user audit is coordinating them.

Use compact reports: actual change, verification level, issue/PR and blocker.
Do not repeatedly announce unchanged upstream versions or promise unavailable
runtime execution. A quiet no-op is preferable to activity without evidence.
