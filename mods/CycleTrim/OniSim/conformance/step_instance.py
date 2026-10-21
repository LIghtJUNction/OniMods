#!/usr/bin/env python3
"""Exercise one prepared CycleTrim instance through its native bounded step."""
import argparse
from datetime import datetime, timezone
import fcntl
import json
from pathlib import Path
import time

from verify_instances import digest, inspect_instance, load_backend, native_clock, require


def check_first_headless(controller, name):
    require(controller.instances[name]["graphics"] == "headless", "First acceptance must use a headless instance")
    state_path = getattr(controller, "state_path", None)
    if state_path is not None:
        controller.state = json.loads(state_path.read_text())
    graphical = [n for n, item in controller.instances.items() if item["graphics"] == "normal"]
    require(all(not controller.state.get("instances", {}).get(n, {}).get("startedAt")
                and not controller.live(n) for n in graphical), "A graphical instance has already started")


def exercise(controller, backend, name, managed, native, seconds, first_headless=False, progress=None):
    require(0 < seconds <= 2, "CycleTrim smoke steps must be >0 and <=2 wall seconds")
    require(name in controller.instances, "Unknown instance")
    if first_headless:
        check_first_headless(controller, name)
    initial = inspect_instance(controller, backend, name, managed, native)
    before = initial["status"]
    require(before.get("gameLoaded") is True and before.get("paused") is True
            and before.get("activeSavePath") == before.get("ownedSave") and before.get("ownedSave"),
            "Load and pause the exact owned disposable save before stepping")
    start = native_clock(before["simulationSeconds"])
    if progress is not None:
        progress["before"] = before
    response = controller.command(name, "step", seconds=seconds)
    require(response.get("state") == "completed" and response.get("instance") == before["identity"]
            and response.get("nonce") == before["runNonce"] and response.get("pid") == before["pid"]
            and response.get("paused") is True, "Native step response has wrong identity, state or pause")
    require(native_clock(response["nativeSeconds"]) > 0
            and native_clock(response["simulationSecondsBefore"]) == start
            and native_clock(response["simulationSecondsAfter"]) > start,
            "Native step did not advance the observed game clock")
    require(response["simulationSecondsAfter"] - start == response["nativeSeconds"], "Native progress fields disagree")
    deadline = time.monotonic() + 3
    while True:
        after = controller.read_status(name)
        require(all(after.get(k) == before.get(k) for k in ("pid", "identity", "runNonce", "ownedSave", "activeSavePath")),
                "Process, run or save changed during the native step")
        if after.get("paused") is True and native_clock(after["simulationSeconds"]) >= response["simulationSecondsAfter"]:
            break
        require(time.monotonic() < deadline, "No fresh paused positive-clock observation after step")
        time.sleep(.05)
    final = inspect_instance(controller, backend, name, managed, native)
    require(all(final["status"].get(k) == before.get(k) for k in ("pid", "identity", "runNonce", "ownedSave", "activeSavePath"))
            and final["status"].get("paused") is True and final["status"].get("gameLoaded") is True
            and native_clock(final["status"]["simulationSeconds"]) >= response["simulationSecondsAfter"],
            "Final library audit crossed a process, world or pause boundary")
    if first_headless:
        check_first_headless(controller, name)
    return dict(before=initial, after=final, response=response, first_headless_verified=first_headless)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("harness", "group-root", "stage", "managed", "native", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--name", required=True)
    parser.add_argument("--seconds", type=float, default=1)
    parser.add_argument("--first-headless", action="store_true")
    args = parser.parse_args()
    marker = args.stage / "preparation.json"
    protected = (args.managed, args.native, marker)
    if args.output.suffix != ".json" or args.output.resolve().is_relative_to(args.group_root.resolve()) or any(
            args.output.resolve() == path.resolve() for path in protected):
        parser.error("Use a distinct JSON output outside the instance group and input files")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.unlink(missing_ok=True)
    result = dict(schema=1, passed=False, checked_at_utc=datetime.now(timezone.utc).isoformat(),
                  save_reload_verified=False, graphical_acceptance=False, full_compatibility=False,
                  whole_game_performance_measured=False, scope="One instance, actual loaded bytes and bounded native clock progress")
    controller, lease, progress = None, None, {}
    try:
        preparation = json.loads(marker.read_text())
        require(preparation.get("prepared") is True and preparation.get("group_sha256") == digest(args.group_root / "group.json"),
                "This is not the group configuration owned by the supplied CycleTrim preparation")
        backend, scripts, hashes = load_backend(args.harness.resolve())
        controller = backend.Controller(args.group_root)
        require(args.name in controller.instances, "Unknown instance")
        lease = (args.stage / ("cycletrim-step-" + args.name + ".lock")).open("a+")
        fcntl.flock(lease, fcntl.LOCK_EX | fcntl.LOCK_NB)
        managed, native = digest(args.managed), digest(args.native)
        result.update(managed_sha256=managed, native_sha256=native, backend_sha256=hashes)
        result["step"] = exercise(controller, backend, args.name, managed, native, args.seconds, args.first_headless, progress)
        require(hashes == {name: digest(scripts / name) for name in hashes}, "Harness source changed during the step")
        require(preparation["group_sha256"] == digest(args.group_root / "group.json"), "Group configuration changed during the step")
        result["passed"] = True
    except (OSError, RuntimeError, ValueError, KeyError, TypeError, AttributeError, KeyboardInterrupt) as error:
        result["error"] = str(error) or "Interrupted"
        before = progress.get("before")
        if controller is not None and before is not None:
            try:
                current = controller.read_status(args.name)
                if all(current.get(k) == before.get(k) for k in ("pid", "identity", "runNonce")):
                    result["cleanup_pause"] = controller.command(args.name, "pause")
                    paused = controller.read_status(args.name)
                    result["cleanup_paused"] = paused.get("paused") is True and all(
                        paused.get(k) == before.get(k) for k in ("pid", "identity", "runNonce"))
                    require(result["cleanup_paused"], "Cleanup pause has no matching fresh paused observation")
            except (OSError, RuntimeError, ValueError, KeyError) as cleanup:
                result["cleanup_error"] = str(cleanup)
    finally:
        if lease is not None:
            lease.close()
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({key: result[key] for key in ("passed", "scope", "error") if key in result}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
