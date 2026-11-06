#!/usr/bin/env python3
"""Compare pinned official and Rust ABI publications in isolated children."""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from functools import partial
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

from cases import cases

ROOT = Path(__file__).resolve().parent
HARNESS_FILES = ("host.py", "run.py", "cases.py", "conduits.py", "cell_cases.py",
                 "physics_cases.py", "pressure_cases.py", "soak_cases.py", "thermal_boundary_cases.py", "fixed_clock.c")


def fingerprints(paths):
    return {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()}


def build_fixture_clock(output):
    library = output / "fixed-clock.so"
    subprocess.run(["cc", "-shared", "-fPIC", "-std=c11", "-Wall", "-Wextra", "-Werror",
                    str(ROOT / "fixed_clock.c"), "-ldl", "-o", str(library)],
                   check=True, capture_output=True, timeout=30)
    return library.resolve()


def differences(reference, candidate):
    result = []
    if len(reference) != len(candidate):
        return [dict(path="snapshots", reference=len(reference), candidate=len(candidate))]
    for frame, (expected, actual) in enumerate(zip(reference, candidate)):
        for group in ("arrays", "events"):
            if not isinstance(expected.get(group), dict) or not isinstance(actual.get(group), dict):
                result.append(dict(frame=frame, path=group, error="Missing or invalid data group"))
                continue
            for field in sorted(expected[group].keys() | actual[group].keys()):
                if field not in expected[group] or field not in actual[group]:
                    result.append(dict(frame=frame, path=f"{group}.{field}",
                                       reference_present=field in expected[group],
                                       candidate_present=field in actual[group]))
                    continue
                left, right = expected[group][field], actual[group].get(field)
                if left == right:
                    continue
                item = dict(frame=frame, path=f"{group}.{field}")
                if isinstance(left, list) and isinstance(right, list):
                    changed = [(i, a, b) for i, (a, b) in enumerate(zip(left, right)) if a != b]
                    item.update(reference_length=len(left), candidate_length=len(right),
                                different_entries=len(changed) + abs(len(left) - len(right)),
                                first=changed[:3])
                else:
                    item.update(reference=left, candidate=right)
                result.append(item)
        for field in (expected.keys() | actual.keys()) - {"arrays", "events"}:
            if field not in expected or field not in actual:
                result.append(dict(frame=frame, path=field, reference_present=field in expected,
                                   candidate_present=field in actual))
            elif expected[field] != actual[field]:
                result.append(dict(frame=frame, path=field, reference=expected.get(field), candidate=actual.get(field)))
    return result


def compare_case(case, output, reference, candidate, workers, clock, env):
    folder = output / case["name"]
    folder.mkdir(exist_ok=True)
    spec = folder / "case.json"
    spec.write_text(json.dumps(case, indent=2) + "\n")
    snapshots = {}
    failures = []
    child_env = dict(env)
    if case.get("allocate_only"):
        child_env.update(LD_PRELOAD=str(clock), CYCLETRIM_FIXTURE_CLOCK="1900000000")
    for role, library in (("reference", reference), ("candidate", candidate)):
        child_output = folder / f"{role}.json"
        # Never consume an old result after a failed child.
        child_output.unlink(missing_ok=True)
        with (folder / f"{role}.log").open("w") as log:
            try:
                run = subprocess.run([sys.executable, str(ROOT / "host.py"), "--role", role,
                    "--library", str(library.resolve()), "--case", str(spec.resolve()),
                    "--output", str(child_output.resolve())], stdout=log, stderr=subprocess.STDOUT,
                    env=child_env, timeout=20)
                if run.returncode:
                    failures.append(dict(role=role, exit_code=run.returncode))
                else:
                    snapshots[role] = json.loads(child_output.read_text())
            except subprocess.TimeoutExpired:
                failures.append(dict(role=role, timeout_seconds=20))
    diff = differences(snapshots["reference"]["snapshots"], snapshots["candidate"]["snapshots"]) if not failures else []
    if not failures and snapshots["reference"]["message_calls"] != snapshots["candidate"]["message_calls"]:
        failures.append(dict(error="Reference and candidate received different message counts"))
    cell_workers = snapshots.get("candidate", {}).get("cell_workers", [])
    if case.get("parallel_required") and workers > 1 and not cell_workers:
        failures.append(dict(role="candidate", error="Parallel fixture did not create cell workers"))
    passed = not failures and not diff
    result = dict(case=case["name"], passed=passed, failures=failures,
                        fixture_sha256=hashlib.sha256(spec.read_bytes()).hexdigest(),
                        snapshots_compared=len(snapshots.get("reference", {}).get("snapshots", [])),
                        cell_workers=cell_workers, differences=diff,
                        message_ids_exercised=sorted(int(key) for key in snapshots.get("candidate", {}).get("message_calls", {})))
    print(f"{'PASS' if passed else 'FAIL'} {case['name']}: {len(diff)} field/frame differences", flush=True)
    return result


def compare_cases(selected, compare, jobs):
    if jobs == 1:
        return [compare(case) for case in selected]
    executor = ThreadPoolExecutor(max_workers=jobs)
    try:
        futures = [executor.submit(compare, case) for case in selected]
        # Keep the receipt order deterministic while isolated children run concurrently.
        return [future.result() for future in futures]
    finally:
        # Cancel unstarted cases and drain active cases on interruption/error. Each
        # native child already has a 20-second timeout; no unrelated processes stop.
        executor.shutdown(wait=True, cancel_futures=True)


def compare_output(args, selected):
    # A failed or interrupted replacement run must not leave an old green summary.
    (args.output / "summary.json").unlink(missing_ok=True)
    harness_paths = {name: ROOT / name for name in HARNESS_FILES}
    library_paths = {"reference": args.reference, "candidate": args.candidate}
    harness_hashes = fingerprints(harness_paths)
    library_hashes = fingerprints(library_paths)
    clock = build_fixture_clock(args.output) if any(case.get("allocate_only") for case in selected) else None
    env = dict(os.environ, CYCLETRIM_ONISIM_WORKERS=str(args.workers))
    comparison_started = time.monotonic()
    results = compare_cases(selected, partial(compare_case, output=args.output,
        reference=args.reference, candidate=args.candidate, workers=args.workers,
        clock=clock, env=env), args.jobs)
    comparison_seconds = time.monotonic() - comparison_started
    if harness_hashes != fingerprints(harness_paths) or library_hashes != fingerprints(library_paths):
        raise RuntimeError("Harness or native library changed during comparison; no summary issued")
    report = dict(schema=1, jobs_requested=args.jobs, comparison_seconds=comparison_seconds,
                  concurrency_scope="Isolated native fixture children, not complete ONI instances or a solver speed benchmark", utc=datetime.now(timezone.utc).isoformat(), workers_requested=args.workers,
                  harness_sha256=harness_hashes,
                  allocation_clock="Fixture-only time/CLOCK_REALTIME fixed at 1900000000; monotonic unchanged",
                  comparison="Exact IEEE words, every public cell and ordered event record; ABI padding excluded",
                  scheduling="Observe ready without release; queue frame; publish empty batch; observe ready; publish frame",
                  empty_batches_compared=True,
                  libraries=library_hashes,
                  message_ids_exercised=sorted({message for item in results for message in item["message_ids_exercised"]}),
                  cases=results, passed=all(item["passed"] for item in results))
    (args.output / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    # Retain all evidence on any failed/interrupted comparison or summary-write
    # error. Passing runs keep their fixtures, logs and compact hashed receipt.
    if report["passed"] and not args.keep_success_snapshots:
        for item in results:
            for role in ("reference", "candidate"):
                (args.output / item["case"] / f"{role}.json").unlink(missing_ok=True)
    return 0 if report["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--workers", type=int, default=1, choices=range(1, 9))
    parser.add_argument("--jobs", type=int, default=1, choices=range(1, 9),
                        help="Concurrent isolated native case pairs; default 1 minimizes memory. Not full games.")
    parser.add_argument("--case", action="append", help="Select exact case names; default is all")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    parser.add_argument("--keep-success-snapshots", action="store_true",
                        help="Keep raw reference/candidate snapshots after a passing summary; failures always retain them.")
    args = parser.parse_args()
    if args.jobs > 1 and args.jobs * args.workers > len(os.sched_getaffinity(0)):
        parser.error("jobs * workers exceeds the current CPU affinity; reduce explicit native concurrency")
    for role, library in (("reference", args.reference), ("candidate", args.candidate)):
        if not library.is_file():
            parser.error(f"{role} library does not exist or is not a file: {library}")
    selected = [case for case in cases() if not args.case or case["name"] in args.case]
    if not selected or (args.case and set(args.case) != {case["name"] for case in selected}):
        parser.error("Unknown or empty case selection")
    if any(case.get("allocate_only") for case in selected) and os.environ.get("LD_PRELOAD"):
        parser.error("Allocate fixtures require an isolated LD_PRELOAD environment")
    args.output.mkdir(parents=True, exist_ok=True)
    # Hold one owner across comparison, receipt writing and raw-file cleanup.
    # A second runner must not replace evidence while the first removes it.
    with (args.output / ".run.lock").open("a") as output_lock:
        try:
            fcntl.flock(output_lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            parser.error(f"Another comparison is using output directory: {args.output}")
        return compare_output(args, selected)


if __name__ == "__main__":
    raise SystemExit(main())
