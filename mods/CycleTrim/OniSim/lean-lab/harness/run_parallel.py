#!/usr/bin/env python3
"""Validate the production emitter and compare worker counts without CPU pinning."""
from __future__ import annotations

import csv
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess

LAB = Path(__file__).resolve().parents[1]
ONISIM = LAB.parent
MANIFEST = LAB / "harness/Cargo.toml"
EXE = LAB / "harness/target/release/onisim-diamond-validation"
RESULTS = LAB / "results"


def read_optional(path: str) -> str | None:
    try:
        return Path(path).read_text().strip()
    except OSError:
        return None


def main() -> None:
    subprocess.run(["cargo", "build", "--offline", "--locked", "--release",
                    "--manifest-path", str(MANIFEST)], check=True, cwd=MANIFEST.parent)
    RESULTS.mkdir(exist_ok=True)
    record = {
        "started_utc": datetime.now(timezone.utc).isoformat(),
        "platform": platform.platform(),
        "cpu_model": next(line.split(":", 1)[1].strip() for line in
                          Path("/proc/cpuinfo").read_text().splitlines()
                          if line.startswith("model name")),
        "cpu_affinity": sorted(os.sched_getaffinity(0)),
        "cgroup_cpu_max": read_optional("/sys/fs/cgroup/cpu.max"),
        "process_cgroup": read_optional("/proc/self/cgroup"),
        "rustc": subprocess.check_output(["rustc", "--version"], text=True, cwd=MANIFEST.parent).strip(),
        "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
        "schedule": [1, 4, 4, 1],
        "scope": "Synthetic disease-emitter component, no ONI runtime; release defaults; "
                 "8 AB/BA sample pairs per case after warmup/calibration; ns per update call. "
                 "Each call has 8 ordered emitters; startup and fixture allocations excluded. "
                 "Parallel allocation counts are not measured.",
    }
    sources = list((ONISIM / "native/src").glob("*.rs"))
    sources += list((LAB / "harness/src").glob("*.rs"))
    sources += [ONISIM / "rust-toolchain.toml", MANIFEST, LAB / "harness/Cargo.lock", Path(__file__)]
    record["source_sha256"] = {
        str(path.relative_to(ONISIM)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(sources)
    }
    for workers, label in [(1, "serial"), (4, "parallel")]:
        output = RESULTS / f"current-{label}-correctness.json"
        subprocess.run([str(EXE), "verify", str(output)], check=True,
                       env={**os.environ, "CYCLETRIM_ONISIM_WORKERS": str(workers)})
        observed = json.loads(output.read_text())["worker_count"]
        if observed != workers:
            raise RuntimeError(f"Requested {workers} workers, observed {observed}; "
                               "choose a host with enough available CPUs")
    measurements = {}
    for run, workers in enumerate(record["schedule"]):
        output = RESULTS / f"parallel-bench-{run}-workers{workers}.csv"
        subprocess.run([str(EXE), "benchmark-focused", str(output)], check=True,
                       env={**os.environ, "CYCLETRIM_ONISIM_WORKERS": str(workers)})
        groups: dict[str, list[float]] = {}
        with output.open() as handle:
            for row in csv.DictReader(handle):
                if int(row["pair"]) >= 0:
                    groups.setdefault(row["case_id"] + "/" + row["algorithm"], []).append(
                        float(row["ns_per_call"]))
        measurements[output.name] = {
            name: {"median_ns": statistics.median(values), "min_ns": min(values),
                   "max_ns": max(values), "samples": len(values)}
            for name, values in groups.items()
        }
    record["results"] = measurements
    (RESULTS / "parallel-benchmark.json").write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps(measurements, indent=2))


if __name__ == "__main__":
    main()
