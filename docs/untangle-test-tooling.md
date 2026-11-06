# Untangle: test invocation and evidence boundaries

Baseline: `90c31fa9826161caa5382edf4e870f6c07adb317` (PR #562 merged).
Implementation: `3255703419ad7590f2f42c887875335f87f3d750` and
`3f63831427bdb8fa236ed14480d387558ddda791` in [PR #565](https://github.com/LIghtJUNction/OniMods/pull/565).

## Decisions and problems resolved

- Put host-test invocation policy in one process-free module, `mod_test_plan.py`.
  - The ordinary runner and synthetic reporter duplicated the Release,
    build-isolation and warnings-as-errors arguments and performance-project path.
  - Discovery's filesystem input was hidden in the orchestration module's global
    root. Callers can now supply the checkout root explicitly.
- Keep filesystem discovery separate from subprocess execution.
  - OniOnline's adapter build and collector are genuine extra entry points.
    Their order, optional-file guards and failure propagation are preserved.
  - Host-only and synthetic-only modes remain separate evidence classes. The
    exit-code-plus-stderr-marker advisory policy and timing thresholds are unchanged.
- Put reusable workflow inspection in `workflow_contracts.py`.
  - The evidence-matrix verifier imported another executable verifier, loading
    unrelated provenance code and fixtures just to read workflow path lists.
  - Both verifiers now depend directly on the small shared reader. Its function
    body is unchanged; it reads the existing block style, not arbitrary YAML.
- Move lower-level tests to the behavior they protect, after checking equivalence.
  - Selection and reporter tests move out of the CI wiring verifier and gain
    temporary-checkout, actual-child-process and failure-propagation coverage.
  - Existing CI wiring, provenance, SDK pin and compatibility checks remain.
    No test is removed merely because it fails; no threshold is relaxed.

The initial decision list was recorded before implementation. Comments cleanup
removes the redundant discovery docstring with the old wrapper; external reasons,
evidence boundaries and safety explanations remain. This is not blanket comment
or source-contract deletion.

## Local verification

Python 3.13.5, Linux x86-64; selected sources reconstructed through the GitHub
connector and checked against Git blob IDs. The published Python files were
read back by blob ID before the final local verification.

| Check | Result | Boundary |
| --- | --- | --- |
| `python scripts/verify_mod_test_plan.py -v` | 13 tests pass | Python discovery/execution policy, not .NET execution |
| `python scripts/verify_workflow_contracts.py -v` | 8 tests pass | Workflow fixtures and isolated module import |
| Baseline/candidate command comparison | 128 host plans and 64 probe commands equal | Optional-file combinations, two modes and SDK paths with spaces |
| Deliberately broken candidates | 7 rejected | Strict flags, mode leak, adapter loss, swallowed errors, transitive import and discarded paths |
| AST comparison | Equal | Moved reader and every remaining provenance assertion unchanged |
| Python compilation | Pass | Syntax/import compilation only |

The standard runner discovers both new `verify_*.py` files without a workflow
change. Full-checkout whitespace, Rust, managed-host and pinned-reference results
must be read from the exact candidate's GitHub Actions checks in PR #565; local
focused checks are not a substitute.

## Scope and retained requirements

This increment improves shared verification infrastructure, not every runtime
module. Runtime-blocked PRs are untouched. No gameplay patch, simulation rate,
package rule, benchmark threshold or release artifact is changed.

The sandbox cannot resolve GitHub DNS and has no Rust, .NET or RTK installation.
It is not a complete local checkout, and no full local repository test run is
claimed. No ONI process or downloaded game assembly was executed. CPU, memory,
GC and in-game performance were not measured; no speedup is claimed.
