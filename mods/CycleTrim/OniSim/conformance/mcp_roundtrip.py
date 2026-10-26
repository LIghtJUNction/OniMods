#!/usr/bin/env python3
"""Validate one owned CycleTrim instance using existing MCP and runtime APIs."""
import argparse
from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

from step_instance import check_first_headless
from verify_instances import digest, inspect_instance, load_backend, loaded_consumer, native_clock, require


def same_run(actual, expected):
    require(all(actual.get(k) == expected.get(k) for k in ("pid", "identity", "runNonce")),
            "Process or run changed during MCP acceptance")


def paused_save(controller, name, before, path):
    status = controller.read_status(name)
    same_run(status, before)
    require(status.get("gameLoaded") is True and status.get("paused") is True
            and status.get("activeSavePath") == str(path), "Exact expected save is not loaded and paused")
    native_clock(status["simulationSeconds"])
    return status


def owned_checkpoint(path, profile, name):
    path, profile = Path(path), Path(profile).resolve()
    require(path.is_absolute() and path.suffix == ".sav" and path.stem == name,
            "Checkpoint target differs from the requested unique save name")
    require(path.resolve().is_relative_to(profile), "Checkpoint escaped the owned instance profile")
    require(not path.exists(), "Checkpoint already exists; overwrites are not permitted")
    return path


def bounded_step(controller, name, before, path, seconds, run_step):
    initial = paused_save(controller, name, before, path)
    record = run_step()
    advanced = native_clock(record.get("simulationSecondsAdvanced"))
    require(advanced > 0 and record.get("wallSeconds") == seconds
            and record.get("before", {}).get("isPaused") is True
            and record.get("after", {}).get("isPaused") is True,
            "Bounded runtime step did not verify positive progress and final pause")
    deadline = time.monotonic() + 3
    while True:
        final = controller.read_status(name)
        same_run(final, before)
        if (final.get("gameLoaded") is True and final.get("paused") is True
                and final.get("activeSavePath") == str(path)
                and native_clock(final["simulationSeconds"]) >= initial["simulationSeconds"] + advanced):
            break
        require(time.monotonic() < deadline, "No matching fresh native pause after the bounded runtime step")
        time.sleep(.05)
    require(native_clock(final["simulationSeconds"]) - initial["simulationSeconds"] == advanced,
            "Runtime step receipt disagrees with the actual native clock")
    return dict(before=initial, after=final, runtime=record)


def roundtrip(controller, name, before, profile, client, seconds, run_step, progress):
    source = Path(before["activeSavePath"])
    source_hash = digest(source)
    progress["before"] = before
    first = bounded_step(controller, name, before, source, seconds, run_step)
    save_clock = first["after"]["simulationSeconds"]
    save_name = "CycleTrim-checkpoint-" + uuid.uuid4().hex
    arguments = dict(domain="save", action="save", name=save_name, overwrite=False,
                     updateActiveSave=False, confirm=True)
    preview = client.call("game_control", dict(arguments, dryRun=True))
    require(preview.get("dryRun") is True and preview.get("exists") is False
            and preview.get("requiresOverwrite") is False
            and preview.get("activeSaveBefore") == str(source), "Save preview did not verify a fresh owned target")
    target = owned_checkpoint(preview["target"], profile, save_name)
    saved = client.call("game_control", arguments)
    require(saved.get("saved") == str(target) and saved.get("target") == str(target)
            and saved.get("overwroteExisting") is False and saved.get("updateActiveSave") is False
            and saved.get("activeSaveBefore") == str(source) and saved.get("activeSaveAfter") == str(source),
            "Save changed the active pointer or did not produce the previewed checkpoint")
    require(target.is_file() and not target.is_symlink() and target.stat().st_size > 0,
            "Checkpoint is missing, linked or empty")
    checkpoint_hash = digest(target)
    require(paused_save(controller, name, before, source)["simulationSeconds"] == save_clock,
            "Clock changed while saving a paused checkpoint")
    require(digest(source) == source_hash, "Original instance save changed during checkpoint creation")

    # Move beyond the saved clock so an old scene with a newly assigned path
    # cannot be mistaken for a completed reload.
    second = bounded_step(controller, name, before, source, seconds, run_step)
    require(second["after"]["simulationSeconds"] > save_clock, "Clock did not move beyond the checkpoint")
    require(digest(target) == checkpoint_hash, "Checkpoint changed before reload")
    loading = client.call("game_control", dict(domain="launch", action="start", path=str(target),
                                               forceLoad=True, resume=False, confirm=True))
    deadline = time.monotonic() + 180
    while True:
        try:
            status = controller.read_status(name)
        except RuntimeError as error:
            if str(error) != "Instance telemetry is stale":
                raise
            require(time.monotonic() < deadline, "Instance telemetry stayed stale through reload")
            time.sleep(.1)
            continue
        same_run(status, before)
        if (status.get("gameLoaded") is True and status.get("paused") is True
                and status.get("activeSavePath") == str(target)
                and status.get("simulationSeconds") == save_clock):
            break
        require(time.monotonic() < deadline, "Checkpoint did not reload its exact saved clock and paused state")
        time.sleep(.1)
    third = bounded_step(controller, name, before, target, seconds, run_step)
    require(digest(target) == checkpoint_hash and digest(source) == source_hash,
            "Source or checkpoint bytes changed during reload/progression")
    return dict(step_before_save=first, saved=saved, checkpoint_sha256=checkpoint_hash,
                checkpoint_clock=save_clock, source_sha256=source_hash,
                step_after_save=second, load_response=loading, reloaded=status,
                step_after_reload=third)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("harness", "group-root", "stage", "managed", "native", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--name", required=True)
    parser.add_argument("--seconds", type=float, default=1)
    parser.add_argument("--first-headless", action="store_true")
    parser.add_argument("--release-native-pause", action="store_true",
                        help="Only for an inspected disposable test save; uses runtime.py's game-owned stop lease")
    args = parser.parse_args()
    if not 0 < args.seconds <= 2:
        parser.error("Use bounded steps of >0 and <=2 wall seconds")
    marker = args.stage / "preparation.json"
    protected = (marker, args.managed, args.native)
    if (args.output.suffix != ".json" or args.output.resolve().is_relative_to(args.group_root.resolve())
            or any(args.output.resolve() == p.resolve() for p in protected)):
        parser.error("Use a distinct JSON receipt outside the instance group and input files")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.unlink(missing_ok=True)
    result = dict(schema=1, passed=False, checked_at_utc=datetime.now(timezone.utc).isoformat(),
                  full_compatibility=False, graphical_acceptance=False, concurrency_validated=False,
                  whole_game_performance_measured=False, scope="One owned process, normal MCP save/load and bounded runtime steps")
    controller, lock, token, progress = None, None, None, {}
    try:
        preparation = json.loads(marker.read_text())
        group_hash = digest(args.group_root / "group.json")
        require(preparation.get("prepared") is True and preparation.get("group_sha256") == group_hash,
                "Group does not match its CycleTrim preparation")
        managed, native = digest(args.managed), digest(args.native)
        require(managed == preparation["managed_sha256"] and native == preparation["native_sha256"],
                "Requested libraries differ from the prepared inputs")
        backend, scripts, hashes = load_backend(args.harness.resolve())
        hashes.update({n: digest(scripts / n) for n in ("runtime.py", "runtime_mcp.py", "install_runtime.py")})
        controller = backend.Controller(args.group_root)
        require(args.name in controller.instances and args.name in preparation.get("mcp_profile_configs", {}),
                "Prepared instance has no explicit MCP acceptance configuration")
        lock = (args.stage / ("cycletrim-step-" + args.name + ".lock")).open("a+")
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if args.first_headless:
            check_first_headless(controller, args.name)
        initial = inspect_instance(controller, backend, args.name, managed, native)
        before = initial["status"]
        require(before.get("gameLoaded") is True and before.get("paused") is True
                and before.get("activeSavePath") == before.get("ownedSave") and before.get("ownedSave"),
                "Load the exact approved disposable save and pause it before MCP acceptance")
        paths = controller.paths(args.name)
        assemblies = json.loads((paths["control"] / "instance-assemblies.json").read_text())["assemblies"]
        mcp = loaded_consumer(assemblies, paths["user"], "OniMcp")
        expected_mcp = preparation["mcp_payload_sha256"]["implementation/OniMcp.dll"]
        require(mcp.get("sha256") == expected_mcp and digest(Path(mcp["location"])) == expected_mcp,
                "Actual loaded OniMcp assembly differs from the prepared isolated dependency")
        config = paths["user"] / "OniMcpConfig.json"
        config_hash = digest(config)
        options = json.loads(config.read_text())
        require(options.get("Host") == "127.0.0.1" and options.get("AuthEnabled") is True
                and options.get("Port") == preparation["mcp_profile_configs"][args.name]["port"],
                "MCP settings differ from the prepared isolated endpoint")
        token = options.get("AuthToken")
        require(isinstance(token, str) and len(token) >= 32, "Instance MCP credential is missing")
        from runtime_mcp import Client
        client = Client(f"http://127.0.0.1:{options['Port']}/mcp/", token)
        active = client.call("game_control", dict(domain="launch", action="status", limit=1))
        require(active.get("loaded") is True and active.get("isPaused") is True
                and active.get("activeSaveFile") == before["activeSavePath"],
                "MCP endpoint did not report the exact loaded paused instance save")
        sequence = 0

        def run_step():
            nonlocal sequence
            sequence += 1
            action = paths["control"] / "action.json"
            action.unlink(missing_ok=True)
            command = ["rtk", "proxy", sys.executable, str(scripts / "runtime.py"),
                       "--output", str(paths["control"]), "--url", client.url,
                       "step", "--seconds", str(args.seconds), "--speed", "1"]
            if args.release_native_pause:
                command.append("--release-native-pause")
            environment = dict(os.environ, ONIHARNESS_MCP_TOKEN=token)
            log = args.output.with_name(args.output.stem + f"-step{sequence}.log")
            with log.open("w") as stream:
                subprocess.run(command, env=environment, stdout=stream, stderr=subprocess.STDOUT,
                               timeout=120, check=True)
            record = json.loads(action.read_text())
            require(record.get("releasedNativePause") is args.release_native_pause,
                    "Runtime native-pause option did not match the explicit request")
            return record

        result.update(managed_sha256=managed, native_sha256=native, backend_sha256=hashes,
                      mcp_config_sha256=config_hash, initial=initial)
        result["roundtrip"] = roundtrip(controller, args.name, before, paths["user"], client,
                                        args.seconds, run_step, progress)
        final = inspect_instance(controller, backend, args.name, managed, native)
        same_run(final["status"], before)
        require(final["status"].get("paused") is True, "Final process is not paused")
        if args.first_headless:
            check_first_headless(controller, args.name)
        require(group_hash == digest(args.group_root / "group.json") and config_hash == digest(config),
                "Group or MCP configuration changed during acceptance")
        require(hashes == {n: digest(scripts / n) for n in hashes}, "Harness source changed during acceptance")
        result.update(passed=True, save_reload_verified=True, first_headless_verified=args.first_headless, final=final)
    except (Exception, KeyboardInterrupt) as error:
        result["error"] = str(error) or "Interrupted"
        result["error_type"] = type(error).__name__
        if controller is not None and progress.get("before") is not None:
            try:
                current = controller.read_status(args.name, cleanup=True)
                same_run(current, progress["before"])
                controller.command(args.name, "pause")
                paused = controller.read_status(args.name)
                same_run(paused, progress["before"])
                result["cleanup_paused"] = paused.get("paused") is True
            except (OSError, RuntimeError, ValueError, KeyError) as cleanup:
                result["cleanup_error"] = str(cleanup)
    finally:
        if lock is not None:
            lock.close()
    encoded = json.dumps(result, indent=2)
    if token:
        encoded = encoded.replace(token, "<redacted>")
    args.output.write_text(encoded + "\n")
    print(json.dumps({k: result[k] for k in ("passed", "scope")}, ensure_ascii=False))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
