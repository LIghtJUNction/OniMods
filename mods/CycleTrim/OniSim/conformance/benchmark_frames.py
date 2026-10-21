#!/usr/bin/env python3
"""Measure complete native frame fixtures; this is not a game FPS benchmark."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import resource
import statistics
import subprocess
import sys
import time

from host import Native, elements, encode_elements, encode_diseases, initialize_world, pack
from run import differences, HARNESS_FILES

ROOT = Path(__file__).resolve().parent
CASES = ("fluid", "thermal", "sealed", "germ_flow", "mixed_germs", "vacuum")


def parse_cpus(value):
    try:
        cpus = sorted({int(item) for item in value.split(",")})
    except ValueError as error:
        raise argparse.ArgumentTypeError("Use comma-separated nonnegative CPU numbers") from error
    if not cpus or cpus[0] < 0:
        raise argparse.ArgumentTypeError("Use comma-separated nonnegative CPU numbers")
    return cpus


def require_no_game():
    games = []
    for directory in Path("/proc").iterdir():
        if not directory.name.isdigit():
            continue
        try:
            if (directory / "comm").read_text().strip().startswith("OxygenNot"):
                games.append(int(directory.name))
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
    if games:
        raise RuntimeError(f"Native timing requires no concurrent ONI game; live PIDs: {sorted(games)}")


def process_memory():
    """Read this exec's peak, excluding getrusage's inherited pre-exec peak."""
    status = dict(line.split(":", 1) for line in Path("/proc/self/status").read_text().splitlines()
                  if ":" in line)
    rollup = dict(line.split(":", 1) for line in Path("/proc/self/smaps_rollup").read_text().splitlines()
                  if ":" in line)
    def kib(fields, key):
        value, unit = fields[key].split()
        if unit != "kB":
            raise RuntimeError("Unexpected Linux memory unit: " + unit)
        return int(value)
    return dict(vm_hwm_kib=kib(status, "VmHWM"), rss_kib=kib(rollup, "Rss"),
                pss_kib=kib(rollup, "Pss"), swap_kib=kib(rollup, "Swap"))


def worker_ticks():
    """Observe actual persistent Rayon workers, not just their environment setting."""
    workers = {}
    for task in Path('/proc/self/task').iterdir():
        try:
            name = (task / 'comm').read_text().strip()
            if name.startswith('OniSim-cell-'):
                fields = (task / 'stat').read_text().rsplit(')', 1)[1].split()
                runtime_ns = int((task / 'schedstat').read_text().split()[0])
                workers[task.name] = dict(name=name, ticks=int(fields[11]) + int(fields[12]),
                                         runtime_ns=runtime_ns)
        except FileNotFoundError:
            continue
    return workers


def host_context():
    cpus = sorted(os.sched_getaffinity(0))
    frequencies = {}
    for cpu in cpus:
        path = Path(f'/sys/devices/system/cpu/cpu{cpu}/cpufreq/scaling_cur_freq')
        if path.exists():
            frequencies[str(cpu)] = int(path.read_text())
    return dict(unix_seconds=time.time(), load_average=os.getloadavg(), allowed_cpus=cpus,
                cpu_frequency_khz=frequencies,
                pressure={kind: Path(f'/proc/pressure/{kind}').read_text().strip()
                          for kind in ('cpu', 'memory', 'io')})


def child(args):
    if args.cpu is not None:
        os.sched_setaffinity(0, {args.cpu})
    elif args.cpus is not None:
        os.sched_setaffinity(0, args.cpus)
    size = args.size
    left_width, right_x = size // 2 - 2, size // 2 + 1
    right_width = size - right_x - 1
    def cell(element, mass, temperature=300):
        return dict(element=element, mass=mass, temperature=temperature)
    case = dict(width=size, height=size, seed=42, diseases=[{}], headless=True)
    if args.case == "vacuum":
        case.update(fill=cell("Vacuum", 0, 0))
    elif args.case == "mixed_germs":
        case.update(diseases=[{}, dict(strength=2)],
                    fill=dict(cell("Oxygen", 1), disease=0, disease_count=10_000),
                    regions=[
                        dict(x=1, y=1, width=left_width, height=size - 2,
                             cell=dict(cell("Oxygen", 4), disease=0, disease_count=10_000)),
                        dict(x=right_x, y=1, width=right_width, height=size - 2,
                             cell=dict(cell("Oxygen", .5), disease=1, disease_count=20_000))])
    elif args.case in ("fluid", "germ_flow"):
        case.update(fill=cell("Oxygen", 1), regions=[
            dict(x=1, y=1, width=left_width, height=size - 2, cell=cell("Oxygen", 4)),
            dict(x=right_x, y=1, width=right_width, height=size * 25 // 96, cell=cell("Water", 800))])
        if args.case == "germ_flow":
            for material in [case["fill"], *(region["cell"] for region in case["regions"])]:
                material.update(disease=0, disease_count=10_000)
    elif args.case == "thermal":
        case.update(fill=cell("Granite", 1000), regions=[
            dict(x=1, y=1, width=left_width, height=size - 2, cell=cell("Granite", 1000, 600))])
    else:
        case.update(fill=cell("Oxygen", 1))
    native = Native(args.library, args.role)
    try:
        items = elements()
        native.send(1108437482, encode_elements(items))
        native.send(825301935, encode_diseases(case, len(items)))
        initialize_world(native, case, items)
        initial = native.snapshot(native.send(-931446686), size * size)
        visible = bytes([255]) * (size * size)
        frame = pack("fiiiiff", 10.0, 0, 0, size, size - 1, 0.0, 0.0)

        def advance():
            native.wait_ready()
            native.send(-775326397, frame)
            pointer = native.send(1078620451, visible)
            if ctypes.c_int.from_address(pointer).value != 0:
                raise RuntimeError("Expected an empty publication before processing")
            native.wait_ready()
            pointer = native.send(1078620451, visible)
            if ctypes.c_int.from_address(pointer).value != 1:
                raise RuntimeError("Expected one completed frame batch")
            return pointer

        for _ in range(2):
            advance()
        workers_before = worker_ticks()
        context_before = host_context()
        before = resource.getrusage(resource.RUSAGE_SELF)
        cpu, wall = time.process_time(), time.perf_counter()
        for _ in range(6):
            pointer = advance()
        cpu, wall = time.process_time() - cpu, time.perf_counter() - wall
        after = resource.getrusage(resource.RUSAGE_SELF)
        workers_after = worker_ticks()
        context_after = host_context()
        memory = process_memory()
        final = native.snapshot(pointer, size * size)
        if args.case == "vacuum" and len(final["events"]["substance"]) < size * size // 2:
            raise RuntimeError("Vacuum fixture did not exercise repeated substance notifications")
        if args.case not in ("sealed", "vacuum") and initial["arrays"] == final["arrays"]:
            raise RuntimeError("Fixture did not change physical state")
        infected = sum(count > 0 for count in final["arrays"]["disease_count"])
        if args.case in ("germ_flow", "mixed_germs") and infected < size * size // 2:
            raise RuntimeError("Germ flow fixture lost its nonzero-germ coverage")
        species = {disease for disease, count in zip(final["arrays"]["disease"],
                                                   final["arrays"]["disease_count"])
                   if disease != 255 and count > 0}
        if args.case == "mixed_germs" and len(species) != 2:
            raise RuntimeError("Mixed-germ fixture did not retain both species")
        report = dict(case=args.case, native_sha256=native.digest,
                      cpu_seconds=cpu, wall_seconds=wall,
                      cpu_affinity=sorted(os.sched_getaffinity(0)),
                      major_faults=after.ru_majflt - before.ru_majflt,
                      max_rss_kib=after.ru_maxrss, memory_before_final_snapshot=memory, final=final)
        report['requested_workers'] = int(os.environ.get('CYCLETRIM_ONISIM_WORKERS', '1'))
        report['workers_before'] = workers_before
        report['workers_after'] = workers_after
        report['host_context_before_timing'] = context_before
        report['host_context_after_timing'] = context_after
        report["final_infected_cells"] = infected
        report["final_disease_species"] = len(species)
        report["final_substance_events"] = len(final["events"]["substance"])
    finally:
        native.lib.SIM_Shutdown()
        native.check()
    args.output.write_text(json.dumps(report))


def parent(args):
    if args.samples < 5:
        raise ValueError("Use at least five alternating samples")
    libraries = dict(reference=args.reference.resolve(), baseline=args.baseline.resolve(),
                     candidate=args.candidate.resolve())
    inputs = {str(path): path for path in libraries.values()}
    inputs.update({name: ROOT / name for name in (*HARNESS_FILES, Path(__file__).name)})
    def hashes():
        return {name: hashlib.sha256(path.read_bytes()).hexdigest()
                for name, path in inputs.items()}
    identity = hashes()
    args.output.mkdir(parents=True, exist_ok=True)
    receipt = args.output / "summary.json"
    receipt.unlink(missing_ok=True)
    failure_receipt = args.output / "failed.json"
    failure_receipt.unlink(missing_ok=True)
    (args.output / "inputs.json").write_text(json.dumps(dict(
        input_sha256=identity, libraries={role: identity[str(path)] for role, path in libraries.items()},
        workers=args.workers, width=args.size, height=args.size, samples_per_role_per_scene=args.samples,
        cpu=args.cpu, cpus=args.cpus, baseline_role=args.baseline_role,
        baseline_workers=args.baseline_workers, candidate_workers=args.candidate_workers), indent=2) + "\n")
    rows, expected = [], {}
    env = dict(os.environ, CYCLETRIM_ONISIM_WORKERS=str(args.workers))

    def run(label, case, name):
        require_no_game()
        output = args.output / (name + ".json")
        role = "reference" if label == "reference" else "candidate"
        if label == "baseline":
            role = args.baseline_role
        workers = (args.baseline_workers if label == 'baseline' else
                   args.candidate_workers if label == 'candidate' else args.workers)
        child_env = dict(env, CYCLETRIM_ONISIM_WORKERS=str(workers))
        command = [sys.executable, str(Path(__file__).resolve()), "--child",
                   "--library", str(libraries[label]), "--role", role,
                   "--case", case, "--size", str(args.size), "--output", str(output)]
        if args.cpu is not None:
            command.extend(["--cpu", str(args.cpu)])
        elif args.cpus is not None:
            command.extend(["--cpus", ",".join(map(str, args.cpus))])
        result = subprocess.run(command, env=child_env, capture_output=True, text=True, timeout=60)
        (args.output / (name + ".log")).write_text(result.stdout + result.stderr)
        result.check_returncode()
        require_no_game()
        return json.loads(output.read_text())

    try:
        require_no_game()
        for case in CASES:
            expected[case] = run("reference", case, "reference-" + case)["final"]
        for sample in range(args.samples):
            order = ("baseline", "candidate") if sample % 2 == 0 else ("candidate", "baseline")
            for case in CASES:
                for label in order:
                    row = run(label, case, f"{sample}-{case}-{label}")
                    delta = differences([expected[case]], [row.pop("final")])
                    if delta:
                        raise RuntimeError(f"{case}/{label} differs: {delta[:3]}")
                    row.update(sample=sample, label=label, differences=0)
                    rows.append(row)
                    print(sample, case, label, round(row["cpu_seconds"], 6), flush=True)
        if hashes() != identity:
            raise RuntimeError("Benchmark inputs changed during measurement")
    except (Exception, KeyboardInterrupt) as error:
        try:
            unchanged = hashes() == identity
        except OSError:
            unchanged = False
        # A child may have written JSON before the post-child game guard fails.
        # Only rows accepted after that guard and exact comparison belong here.
        failure_receipt.write_text(json.dumps(dict(
            passed=False, complete=False, error_type=type(error).__name__, error=str(error),
            input_sha256=identity, inputs_unchanged=unchanged, completed_samples=rows,
            scope="Incomplete native measurement; no performance acceptance. Raw child files may include rejected samples."
        ), indent=2) + "\n")
        raise
    summary = {}
    for case in CASES:
        stats = {}
        for label in ("baseline", "candidate"):
            measurements = [row["cpu_seconds"] for row in rows
                            if row["case"] == case and row["label"] == label]
            stats[label] = dict(median=statistics.median(measurements),
                                minimum=min(measurements), maximum=max(measurements))
        stats["median_speedup"] = stats["baseline"]["median"] / stats["candidate"]["median"]
        summary[case] = stats
    report = dict(scope=f"{args.size}x{args.size} native frame fixtures, not whole-game FPS",
                  width=args.size, height=args.size, workers=args.workers, warmup_batches=2,
                  baseline_workers=args.baseline_workers, candidate_workers=args.candidate_workers,
                  baseline_role=args.baseline_role,
                  timed_batches=6, requested_seconds_per_batch=10,
                  timing="Process CPU includes worker/orchestration; wall includes readiness polling. Snapshots excluded.",
                  memory="Linux VmHWM is this exec's RSS peak through the timed batches; RSS/PSS/swap are instantaneous before final snapshot. Legacy max_rss_kib may include the parent's pre-exec peak and is not used to establish savings.",
                  input_sha256=identity, samples=rows, summary=summary)
    receipt.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(summary, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--child", action="store_true", help=argparse.SUPPRESS)
    for name in ("reference", "baseline", "candidate", "library", "output"):
        parser.add_argument("--" + name, type=Path)
    parser.add_argument("--role", choices=("reference", "candidate"))
    parser.add_argument("--baseline-role", choices=("reference", "candidate"), default="candidate",
                        help="Use reference for the pinned official library as the timed baseline")
    parser.add_argument("--case", choices=CASES)
    parser.add_argument("--samples", type=int, default=6)
    parser.add_argument('--size', type=int, choices=(96, 192, 384), default=96,
                        help='Grid width/height; region proportions scale with the grid')
    affinity = parser.add_mutually_exclusive_group()
    affinity.add_argument("--cpu", type=int, help="Pin each child and its threads to one allowed Linux CPU")
    affinity.add_argument("--cpus", type=parse_cpus, help="Pin children and their workers to a comma-separated CPU set")
    parser.add_argument("--workers", type=int, choices=range(1, 9), default=1)
    parser.add_argument('--baseline-workers', type=int, choices=range(1, 9),
                        help='Override baseline worker count for scaling comparisons')
    parser.add_argument('--candidate-workers', type=int, choices=range(1, 9),
                        help='Override candidate worker count for scaling comparisons')
    args = parser.parse_args()
    args.baseline_workers = args.baseline_workers or args.workers
    args.candidate_workers = args.candidate_workers or args.workers
    required = ("library", "role", "case", "output") if args.child else ("reference", "baseline", "candidate", "output")
    if any(getattr(args, name) is None for name in required):
        parser.error("Missing required arguments: " + ", ".join(required))
    if args.child:
        child(args)
    else:
        parent(args)


if __name__ == "__main__":
    main()
