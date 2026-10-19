#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"

lean_bin="${LEAN_BIN:-lean}"
"$lean_bin" --version > lean-version.txt
if ! grep -Eq 'version 4\.19\.0,' lean-version.txt; then
  cat lean-version.txt
  echo 'Expected the pinned Lean 4.19.0 release.' >&2
  exit 1
fi

if ! "$lean_bin" -DwarningAsError=true -o SimDiamond.olean SimDiamond.lean > compilation.log 2>&1; then
  cat compilation.log >&2
  exit 1
fi
if ! LEAN_PATH=. "$lean_bin" -DwarningAsError=true ProofChecks.lean > axioms.log 2>&1; then
  cat axioms.log >&2
  exit 1
fi

python3 - <<'PY'
import datetime
import hashlib
import json
import pathlib
import re

source = pathlib.Path('SimDiamond.lean').read_text()
log = pathlib.Path('axioms.log').read_text()
declared = {'SimDiamond.' + name for name in re.findall(r'^theorem\s+([\w.]+)', source, re.M)}
reported = set(re.findall(r"'(SimDiamond\.[^']+)' (?:depends on|does not depend on)", log))
if declared != reported:
    raise SystemExit(f'Theorem audit mismatch: missing={declared-reported}; extra={reported-declared}')

allowed = {'propext', 'Classical.choice', 'Quot.sound'}
dependencies = set()
for group in re.findall(r'depends on axioms: \[([^\]]*)\]', log):
    dependencies.update(part.strip() for part in group.split(',') if part.strip())
unexpected = dependencies - allowed
if unexpected:
    raise SystemExit(f'Unexpected logical dependencies: {sorted(unexpected)}')
if re.search(r'^\s*(axiom|sorry|admit)\b', source, re.M):
    raise SystemExit('Unexpected axiom or incomplete-proof declaration in source.')

record = {
    'checked_at_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'lean_version': pathlib.Path('lean-version.txt').read_text().strip(),
    'source_compile_exit_code': 0,
    'axiom_report_exit_code': 0,
    'checked_theorem_count': len(declared),
    'logical_dependencies': sorted(dependencies),
    'source_sha256': {
        name: hashlib.sha256(pathlib.Path(name).read_bytes()).hexdigest()
        for name in ['SimDiamond.lean', 'ProofChecks.lean', 'lean-toolchain', 'lakefile.toml', 'verify.sh']
    },
    'scope': 'Bounded coordinate graph, finite BFS reference, row enumeration, cell-local updates; not the proprietary SimDLL binary or compiled Rust machine code.',
}
pathlib.Path('verification.json').write_text(json.dumps(record, indent=2) + '\n')
print(f'Lean 4.19.0 verified {len(declared)} theorems; all axiom dependencies are standard Lean axioms.')
PY
