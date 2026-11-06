#!/usr/bin/env python3
"""Compile the proof and inspect every theorem's actual logical dependencies."""
from pathlib import Path
import json
import os
import re
import subprocess

ROOT = Path(__file__).resolve().parent

def main():
    lean = os.environ.get('LEAN', 'lean')
    version = subprocess.check_output([lean, '--version'], text=True).strip()
    if not re.search(r'version 4\.19\.0,', version):
        raise SystemExit('Expected Lean 4.19.0, got: ' + version)
    source = (ROOT / 'Transport.lean').read_text()
    # Scan code, not comments; keep primitive axiom declarations out of this proof.
    clean = re.sub(r'/-.*?-/', '', source, flags=re.S)
    clean = re.sub(r'--[^\n]*', '', clean)
    if re.search(r'\b(sorry|admit|axiom|native_decide)\b', clean):
        raise SystemExit('Unproved/trusted shortcut in proof source')
    env = dict(os.environ, LEAN_PATH=str(ROOT))
    subprocess.run([lean, '-DwarningAsError=true', '-o', 'Transport.olean', 'Transport.lean'],
                   cwd=ROOT, env=env, check=True)
    result = subprocess.check_output([lean, '-DwarningAsError=true', 'ProofChecks.lean'],
                                     cwd=ROOT, env=env, text=True)
    names = json.loads((ROOT / 'theorems.json').read_text())
    found = re.findall(r"^'(Transport\.[^']+)'", result, re.M)
    if sorted(found) != sorted(names) or len(re.findall(r"^theorem ", clean, re.M)) != len(names):
        raise SystemExit('Missing theorem dependency results')
    dependencies = set()
    for block in re.findall(r'depends on axioms: \[(.*?)\]', result, re.S):
        dependencies.update(x.strip() for x in block.split(',') if x.strip())
    if dependencies - {'propext', 'Classical.choice', 'Quot.sound'}:
        raise SystemExit('Unexpected trusted dependency: ' + str(dependencies))
    (ROOT / 'axioms.log').write_text(result)
    record = {'lean':version, 'theorems':len(names), 'dependencies':sorted(dependencies), 'passed':True}
    (ROOT / 'verification.json').write_text(json.dumps(record,indent=2)+'\n')
    print(json.dumps(record))

if __name__ == '__main__':
    main()
