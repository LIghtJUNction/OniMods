#!/usr/bin/env python3
"""Read-only CycleTrim library audit for an existing OniHarness instance group."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
BACKEND_FILES = ("instance_controller.py", "instance_installation.py", "instance_resources.py",
                 "instance_network.py", "steam_instance_entry.py")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def mapped_native(pid, profile, expected, proc=Path("/proc")):
    """Bind the mapped inode/device to current bytes, including paths with spaces."""
    mapped = {}
    for line in (proc / str(pid) / "maps").read_text().splitlines():
        fields = line.split(maxsplit=5)
        if len(fields) != 6 or "liboni_sim_core.so" not in fields[5]:
            continue
        require(not fields[5].endswith(" (deleted)"), "Mapped OniSim library was deleted or replaced")
        path = Path(fields[5])
        require(path.name == "liboni_sim_core.so", "Unexpected OniSim mapping name")
        require(path.resolve().is_relative_to(profile.resolve()), "Mapped OniSim is outside its instance profile")
        major, minor = (int(part, 16) for part in fields[3].split(":"))
        with path.open("rb") as stream:
            before = os.fstat(stream.fileno())
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
            after = os.fstat(stream.fileno())
        require(before.st_ino == int(fields[4]) and before.st_dev == os.makedev(major, minor),
                "OniSim mapping inode/device differs from the file being hashed")
        require((before.st_ino, before.st_dev, before.st_size, before.st_mtime_ns, before.st_ctime_ns) ==
                (after.st_ino, after.st_dev, after.st_size, after.st_mtime_ns, after.st_ctime_ns),
                "OniSim file changed while hashing")
        require(actual == expected, "Actual mapped OniSim hash does not match the candidate")
        mapped[str(path)] = actual
    require(len(mapped) == 1, "Require exactly one mapped OniSim library; a managed-only CycleTrim mod does not validate the Rust core")
    return next(iter(mapped.values()))


def inspect_instance(controller, backend, name, managed, native):
    status = controller.read_status(name)
    pid = status["pid"]
    identity = backend.ProcessIdentity.capture(pid)
    paths = controller.paths(name)
    native_hash = mapped_native(pid, paths["user"], native)
    controller.verify_hashes(name, status)
    require(status.get("assemblyReceipt") == "instance-assemblies.json", "Unexpected assembly receipt path")
    receipt = json.loads((paths["control"] / "instance-assemblies.json").read_text())
    require(all(receipt.get(k) == status.get(k) for k in ("pid", "identity", "runNonce")),
            "Assembly receipt crosses process or run identity")
    loaded = [item for item in receipt.get("assemblies", [])
              if Path(item.get("location", "")).name == "CycleTrim.dll"]
    require(len(loaded) == 1 and not loaded[0].get("error"), "Require one successfully loaded CycleTrim assembly")
    assembly = loaded[0]
    location = Path(assembly["location"])
    require(location.resolve().is_relative_to(paths["user"].resolve()), "Loaded CycleTrim is outside its instance profile")
    require(assembly.get("sha256") == managed and digest(location) == managed,
            "Actually loaded CycleTrim assembly is not the requested candidate")
    final = controller.read_status(name)
    require(all(final.get(k) == status.get(k) for k in ("pid", "identity", "runNonce")),
            "Instance restarted during the library audit")
    require(backend.ProcessIdentity.capture(pid) == identity, "PID was reused during the library audit")
    require(mapped_native(pid, paths["user"], native) == native_hash, "OniSim changed during the audit")
    return dict(pid=pid, identity=status["identity"], runNonce=status["runNonce"],
                managed_sha256=managed, mapped_native_sha256=native_hash,
                mode=controller.instances[name]["graphics"], status=final)


def native_clock(value):
    require(type(value) in (int, float) and math.isfinite(value), "Require a finite native simulation clock")
    return value


def verify_concurrency(receipt, instances, audited):
    require(receipt.get("passed") is True and receipt.get("kind") == "complete-game-concurrency",
            "Require a successful complete-game concurrency receipt")
    require(type(receipt.get("protocolClients")) is int and receipt["protocolClients"] == 0,
            "Protocol clients cannot count as game instances")
    before, after, responses = (receipt[key] for key in ("before", "after", "responses"))
    names = set(before)
    require(names == set(after) == set(responses) == set(audited) and len(names) >= 4,
            "Require at least four complete audited games in the same receipt")
    require(receipt.get("completeGameCount") == len(names), "Complete-game count disagrees with the receipt")
    require(receipt.get("resources", {}).get("violations") == [], "Missing or failed resource-limit observation")
    graphical = [name for name in names if instances[name]["graphics"] == "normal"]
    require(len(graphical) == 1 and len(names) - len(graphical) >= 3,
            "Require one graphical instance and at least three headless instances")
    require(len({before[name]["pid"] for name in names}) == len(names), "Game PIDs are not distinct")
    intervals = []
    for name in names:
        a, b, live = before[name], after[name], audited[name]["status"]
        for key in ("pid", "identity", "runNonce"):
            require(a.get(key) == b.get(key) == live.get(key) and live.get(key) is not None,
                    "Receipt does not belong to the currently audited process: " + name)
        require(a.get("gameLoaded") is True and b.get("gameLoaded") is True
                and live.get("gameLoaded") is True and a.get("paused") is True
                and b.get("paused") is True and live.get("paused") is True,
                "Both observations must show a loaded paused colony: " + name)
        require(a.get("activeSavePath") and a["activeSavePath"] == a.get("ownedSave")
                == b.get("activeSavePath") == b.get("ownedSave")
                == live.get("activeSavePath") == live.get("ownedSave"), "Exact disposable save changed: " + name)
        require(native_clock(b["simulationSeconds"]) > native_clock(a["simulationSeconds"]),
                "No positive native clock progress: " + name)
        require(native_clock(live["simulationSeconds"]) >= b["simulationSeconds"],
                "Live clock predates the concurrency receipt: " + name)
        if name not in graphical:
            for observed in (a, b, live):
                require(observed.get("batchMode") is True and observed.get("graphicsDevice") == "Null"
                        and observed.get("cameraRenderCallbacks") == 0, "Headless telemetry failed: " + name)
        else:
            for observed in (a, b, live):
                require(observed.get("batchMode") is False and observed.get("graphicsDevice") not in (None, "", "Null")
                        and type(observed.get("cameraRenderCallbacks")) is int and observed["cameraRenderCallbacks"] > 0,
                        "Graphical instance has no verified graphics device or camera renders: " + name)
        require(responses[name].get("instance") == live["identity"]
                and responses[name].get("nonce") == live["runNonce"]
                and responses[name].get("state") == "completed", "Native response crosses instance or run: " + name)
        start = datetime.fromisoformat(responses[name]["startedAt"].replace("Z", "+00:00"))
        end = datetime.fromisoformat(responses[name]["completedAt"].replace("Z", "+00:00"))
        require(start.tzinfo is not None and end.tzinfo is not None and end > start, "Invalid native step interval")
        intervals.append((start, end))
    overlap = (min(end for _, end in intervals) - max(start for start, _ in intervals)).total_seconds()
    require(overlap > 0, "Native step intervals did not overlap")
    return dict(complete_game_count=len(names), protocol_clients=0, overlap_seconds=overlap)


def load_backend(harness):
    scripts = harness / "scripts"
    hashes = {name: digest(scripts / name) for name in BACKEND_FILES}
    sys.path.insert(0, str(scripts))
    spec = importlib.util.spec_from_file_location("cycletrim_instance_backend", scripts / "instance_controller.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module, scripts, hashes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--harness", required=True, type=Path)
    parser.add_argument("--group-root", required=True, type=Path)
    parser.add_argument("--managed", required=True, type=Path)
    parser.add_argument("--native", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--name", action="append", help="Inspect selected instances without claiming four-game acceptance")
    parser.add_argument("--concurrency-receipt", type=Path)
    args = parser.parse_args()
    protected = [args.managed, args.native, args.concurrency_receipt]
    if args.output.suffix != ".json" or args.output.resolve().is_relative_to(args.group_root.resolve()) or any(
            p is not None and args.output.resolve() == p.resolve() for p in protected):
        parser.error("Audit output must be outside the instance group and distinct from input files")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.unlink(missing_ok=True)
    result = dict(schema=1, passed=False, checked_at_utc=datetime.now(timezone.utc).isoformat(),
                  scope="Read-only loaded CycleTrim/Rust identity and optional complete-game concurrency audit",
                  headless_then_graphics_acceptance=False, full_compatibility=False, whole_game_performance_measured=False)
    try:
        managed, native = digest(args.managed), digest(args.native)
        result.update(managed_sha256=managed, native_sha256=native)
        backend, scripts, hashes = load_backend(args.harness.resolve())
        result["backend_sha256"] = hashes
        group_files = [args.group_root / name for name in ("group.json", "controller.json", "state.json")]
        group_hashes = {path.name: digest(path) for path in group_files}
        controller = backend.Controller(args.group_root)
        names = args.name or list(controller.instances)
        require(len(set(names)) == len(names) and set(names) <= controller.instances.keys(), "Unknown or duplicate instance selection")
        audits = {}
        result["instances"] = audits
        for name in names:
            audits[name] = inspect_instance(controller, backend, name, managed, native)
        if args.concurrency_receipt:
            document = json.loads(args.concurrency_receipt.read_text())
            result["concurrency"] = verify_concurrency(document, controller.instances, audits)
            result["concurrency_receipt_sha256"] = digest(args.concurrency_receipt)
        require(hashes == {name: digest(scripts / name) for name in BACKEND_FILES}, "Harness source changed during the audit")
        require(group_hashes == {path.name: digest(path) for path in group_files}, "Instance group changed during the audit")
        result["group_sha256"] = group_hashes
        result.update(passed=True, backend_sha256=hashes)
    except (OSError, RuntimeError, ValueError, KeyError, TypeError, AttributeError) as error:
        result["error"] = str(error)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({key: result[key] for key in ("passed", "scope", "error") if key in result}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
