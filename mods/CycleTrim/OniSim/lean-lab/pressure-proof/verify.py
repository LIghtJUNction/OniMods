#!/usr/bin/env python3
"""Check the proposed models without replacing the existing diamond proof audit."""
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parent
SOURCES = ("DiseaseMerge.lean", "PressureSchedule.lean", "NotificationMembership.lean",
           "PressureEnumeration.lean", "PressureTiles.lean")
ALLOWED_AXIOMS = {"propext", "Classical.choice", "Quot.sound"}


def main():
    receipt = ROOT / "verification.json"
    receipt.unlink(missing_ok=True)
    compiler = os.environ.get("LEAN_BIN", "lean")
    version = subprocess.run([compiler, "--version"], check=True, capture_output=True,
                             text=True, timeout=15).stdout.strip()
    if "version 4.19.0," not in version:
        raise RuntimeError("Use the existing pinned Lean 4.19.0 proof toolchain")
    initial = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in SOURCES}
    modules = []
    for name in SOURCES:
        source = (ROOT / name).read_text()
        if re.search(r"^\s*(?:axiom|sorry|admit)\b", source, re.M):
            raise RuntimeError("Incomplete proof or added axiom: " + name)
        run = subprocess.run([compiler, "-DwarningAsError=true", name], cwd=ROOT,
                             capture_output=True, text=True, timeout=60)
        output = run.stdout + run.stderr
        (ROOT / (name + ".log")).write_text(output)
        if run.returncode:
            raise RuntimeError(name + " did not compile:\n" + output)
        namespace = Path(name).stem
        declared = {namespace + "." + item for item in re.findall(r"^theorem\s+([\w.]+)", source, re.M)}
        reported = set(re.findall(r"'([^']+)' (?:depends on|does not depend on)", output))
        if not declared or declared != reported:
            raise RuntimeError("Missing or extra theorem audit: " + name)
        dependencies = {item.strip() for group in re.findall(r"depends on axioms: \[([^\]]*)\]", output)
                        for item in group.split(",") if item.strip()}
        if dependencies - ALLOWED_AXIOMS:
            raise RuntimeError("Unexpected axioms: " + repr(dependencies - ALLOWED_AXIOMS))
        modules.append(dict(source=name, theorems=sorted(declared), axioms=sorted(dependencies)))
    current = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in SOURCES}
    if current != initial:
        raise RuntimeError("Proof source changed during compilation")
    result = dict(schema=1, checked_at_utc=datetime.now(timezone.utc).isoformat(),
                  lean_version=version, passed=True, modules=modules, source_sha256=current,
                  checked_theorems=sum(len(module["theorems"]) for module in modules),
                  production_pressure_parallel_implemented=False,
                  scope="Pure disease-state equivalence, wave-sort execution equivalence for local transitions, finite coordinate enumeration, fixed skew-tile dependencies and notification membership models; not a Rust ownership, event-merge, machine-overflow, complete tiled-executor or game-equivalence proof")
    result["source_sha256"]["verify.py"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    receipt.write_text(json.dumps(result, indent=2) + "\n")
    print("Lean 4.19.0 checked", result["checked_theorems"], "model theorems; production pressure remains serial")


if __name__ == "__main__":
    main()
