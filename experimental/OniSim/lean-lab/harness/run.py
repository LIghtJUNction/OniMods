#!/usr/bin/env python3
"""Reproduce this native component experiment with Rust 1.99.1 and no crates."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys


LAB = Path(__file__).resolve().parents[1]
ROOT = LAB.parents[2]
RESULTS = LAB / "results"
MANIFEST = LAB / "harness/Cargo.toml"
SOURCE = ROOT / "experimental/OniSim/native/src"


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_hashes() -> dict[str, str]:
    paths = list((LAB / "harness/src").glob("*.rs"))
    paths += list((LAB / "baseline").glob("*.rs"))
    paths += [MANIFEST, LAB / "harness/Cargo.lock", SOURCE / "disease_emitters.rs", SOURCE / "diamond.rs"]
    return {str(path.relative_to(ROOT)): sha(path) for path in sorted(paths)}


def read_optional(path: str) -> str | None:
    try:
        return Path(path).read_text().strip()
    except OSError:
        return None


def run(command: list[str], log_name: str) -> None:
    print("RUN", json.dumps(command), flush=True)
    with (RESULTS / log_name).open("w") as log:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        print((RESULTS / log_name).read_text(), file=sys.stderr)
        raise SystemExit(result.returncode)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=["all", "build", "verify", "benchmark", "allocations", "summary"], default="all", nargs="?")
    parser.add_argument("--target-dir", type=Path, default=Path("/tmp/onisim-diamond-build"))
    parser.add_argument("--cpu", type=int)
    args = parser.parse_args()
    RESULTS.mkdir(parents=True, exist_ok=True)
    allowed = sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else []
    selected = args.cpu if args.cpu is not None else (allowed[-1] if allowed else None)
    affinity_error = None
    if selected is not None:
        try:
            os.sched_setaffinity(0, {selected})
        except OSError as error:
            affinity_error = str(error)
    compiler = subprocess.check_output(["rustc", "--version", "--verbose"], text=True)
    if not compiler.startswith("rustc 1.99.1 "):
        raise SystemExit("This recorded experiment requires Rust 1.99.1; select that rustup toolchain first.")
    if os.environ.get("RUSTFLAGS") or os.environ.get("CARGO_ENCODED_RUSTFLAGS"):
        raise SystemExit("Unset RUSTFLAGS/CARGO_ENCODED_RUSTFLAGS to reproduce release defaults.")
    cargo = subprocess.check_output(["cargo", "--version"], text=True).strip()
    timing_target = args.target_dir / "timing"
    allocation_target = args.target_dir / "allocation"
    executable = timing_target / "release/onisim-diamond-validation"
    allocation_executable = allocation_target / "release/onisim-diamond-validation"
    build_base = ["cargo", "build", "--release", "--offline", "--manifest-path", str(MANIFEST)]
    if args.phase in ("all", "build"):
        run([sys.executable, str(LAB / "harness/prepare_sources.py")], "prepare-sources.log")
        run(build_base + ["--target-dir", str(timing_target)], "build-timing.log")
        run(build_base + ["--locked", "--features", "allocation_stats", "--target-dir", str(allocation_target)], "build-allocation.log")
    commands = {
        "build_timing": build_base + ["--target-dir", str(timing_target)],
        "build_allocation": build_base + ["--locked", "--features", "allocation_stats", "--target-dir", str(allocation_target)],
        "verify": [str(executable), "verify", str(RESULTS / "correctness.json")],
        "benchmark": [str(executable), "benchmark", str(RESULTS / "raw-samples.csv")],
        "allocations": [str(allocation_executable), "allocations", str(RESULTS / "allocations.csv")],
        "summary": [sys.executable, str(LAB / "harness/summarize.py")],
    }
    if args.phase in ("all", "verify"):
        run(commands["verify"], "correctness.log")
    if args.phase in ("all", "benchmark"):
        correctness_path = RESULTS / "correctness.json"
        if not correctness_path.exists() or json.loads(correctness_path.read_text()).get("status") != "passed":
            raise SystemExit("The correctness suite must pass before timing.")
        before = source_hashes()
        metadata = {
            "started_utc": datetime.now(timezone.utc).isoformat(),
            "os": platform.platform(),
            "architecture": platform.machine(),
            "rustc": compiler.strip(),
            "cargo": cargo,
            "allowed_cpus_before_pin": allowed,
            "selected_cpu": selected,
            "actual_cpu_affinity": sorted(os.sched_getaffinity(0)) if allowed else None,
            "affinity_error": affinity_error,
            "cpu_model": next((line.split(":", 1)[1].strip() for line in (read_optional("/proc/cpuinfo") or "").splitlines() if line.startswith("model name")), None),
            "cpu_governor": read_optional(f"/sys/devices/system/cpu/cpu{selected}/cpufreq/scaling_governor"),
            "cgroup_cpu_max": read_optional("/sys/fs/cgroup/cpu.max"),
            "clock": "Rust std::time::Instant; elapsed nanoseconds",
            "profile": "Cargo release defaults; no RUSTFLAGS; no fast-math; default target CPU",
            "allocation_counter_in_timing_binary": False,
            "commands": commands,
            "source_sha256": before,
            "protocol_sha256": sha(LAB / "harness/PROTOCOL.md"),
            "timing_binary_sha256": sha(executable),
        }
        (RESULTS / "environment.json").write_text(json.dumps(metadata, indent=2) + "\n")
        run(commands["benchmark"], "benchmark.log")
        after = source_hashes()
        metadata["finished_utc"] = datetime.now(timezone.utc).isoformat()
        metadata["source_unchanged_during_timing"] = before == after
        (RESULTS / "environment.json").write_text(json.dumps(metadata, indent=2) + "\n")
        if before != after:
            raise SystemExit("Compiled source changed during measurement; retain data but do not accept this run.")
    if args.phase in ("all", "allocations"):
        run(commands["allocations"], "allocation.log")
    if args.phase in ("all", "summary"):
        run(commands["summary"], "summary.log")
    print("Completed", args.phase, "results:", RESULTS, flush=True)


if __name__ == "__main__":
    main()
