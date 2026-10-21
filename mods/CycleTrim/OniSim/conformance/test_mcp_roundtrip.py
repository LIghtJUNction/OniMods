"""Inert file/API doubles exercise acceptance guards; no ONI process starts."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import mcp_roundtrip as runner


class Fixture:
    def __init__(self, root):
        self.profile = root / "profile"
        self.source = self.profile / "save_files/test.sav"
        self.source.parent.mkdir(parents=True)
        self.source.write_bytes(b"authored inert initial save")
        self.status = dict(pid=7, identity="fixture", runNonce="fixture-run", gameLoaded=True,
                           paused=True, ownedSave=str(self.source), activeSavePath=str(self.source), simulationSeconds=10.0)
        self.before = dict(self.status)
        self.save_calls, self.load_calls, self.steps = 0, 0, 0
        self.escape = False
        self.change_pointer = False
        self.modify_source = False
        self.restore_clock = True
        self.progress = 1.25
        self.reads_after_load = 0

    def read_status(self, name):
        if self.load_calls:
            self.reads_after_load += 1
        return dict(self.status)

    def step(self):
        self.steps += 1
        self.status["simulationSeconds"] += self.progress
        return dict(wallSeconds=1, simulationSecondsAdvanced=self.progress,
                    before=dict(isPaused=True), after=dict(isPaused=True))

    def call(self, tool, args):
        assert tool == "game_control"
        if args["domain"] == "save":
            directory = self.profile.parent if self.escape else self.source.parent
            target = directory / (args["name"] + ".sav")
            if args.get("dryRun"):
                return dict(dryRun=True, exists=False, requiresOverwrite=False,
                            activeSaveBefore=str(self.source), target=str(target))
            self.save_calls += 1
            self.saved_clock = self.status["simulationSeconds"]
            target.write_bytes(b"authored inert checkpoint")
            if self.modify_source:
                self.source.write_bytes(b"unexpected modified input")
            return dict(saved=str(target), target=str(target), overwroteExisting=False,
                        updateActiveSave=False, activeSaveBefore=str(self.source),
                        activeSaveAfter=str(target if self.change_pointer else self.source))
        assert args["domain"] == "launch" and args["forceLoad"] is True and args["resume"] is False
        self.load_calls += 1
        self.status["activeSavePath"] = args["path"]
        if self.restore_clock:
            self.status["simulationSeconds"] = self.saved_clock
        return dict(started=True, target=args["path"])

    def run(self):
        return runner.roundtrip(self, "guest1", self.before, self.profile, self, 1, self.step, {})


class RoundtripTests(unittest.TestCase):
    def test_checkpoint_roundtrip_requires_clock_rollback_and_later_progress(self):
        with tempfile.TemporaryDirectory() as directory:
            f = Fixture(Path(directory))
            result = f.run()
            self.assertEqual(f.steps, 3)
            self.assertEqual(result["checkpoint_clock"], 11.25)
            self.assertEqual(result["step_after_save"]["after"]["simulationSeconds"], 12.5)
            self.assertEqual(result["reloaded"]["simulationSeconds"], 11.25)
            self.assertEqual(result["step_after_reload"]["after"]["simulationSeconds"], 12.5)
            self.assertEqual(f.source.read_bytes(), b"authored inert initial save")

    def test_changed_pid_and_zero_progress_cannot_reach_save(self):
        for mutation in ("pid", "zero"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                f = Fixture(Path(directory))
                if mutation == "pid":
                    f.status["pid"] = 8
                else:
                    f.progress = 0
                with self.assertRaises(RuntimeError):
                    f.run()
                self.assertEqual(f.save_calls, 0)

    def test_escaped_checkpoint_is_rejected_before_write(self):
        with tempfile.TemporaryDirectory() as directory:
            f = Fixture(Path(directory))
            f.escape = True
            with self.assertRaisesRegex(RuntimeError, "escaped"):
                f.run()
            self.assertEqual(f.save_calls, 0)

    def test_existing_checkpoint_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            profile = Path(directory)
            target = profile / "existing.sav"
            target.write_bytes(b"keep")
            with self.assertRaisesRegex(RuntimeError, "already exists"):
                runner.owned_checkpoint(target, profile, "existing")
            self.assertEqual(target.read_bytes(), b"keep")

    def test_save_pointer_and_source_mutations_are_rejected(self):
        for field in ("change_pointer", "modify_source"):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                f = Fixture(Path(directory))
                setattr(f, field, True)
                with self.assertRaises(RuntimeError):
                    f.run()
                self.assertEqual(f.steps, 1)
                self.assertEqual(f.load_calls, 0)

    def test_path_change_without_clock_restore_is_not_a_reload(self):
        with tempfile.TemporaryDirectory() as directory:
            f = Fixture(Path(directory))
            f.restore_clock = False
            with patch.object(runner.time, "monotonic", side_effect=[0, 0, 0, 181]):
                with self.assertRaisesRegex(RuntimeError, "did not reload"):
                    f.run()
            self.assertEqual(f.steps, 2)

    def test_transient_reload_staleness_waits_for_fresh_native_status(self):
        with tempfile.TemporaryDirectory() as directory:
            f = Fixture(Path(directory))
            original = f.read_status
            stale = []

            def read(name):
                if f.load_calls and not stale:
                    stale.append(True)
                    raise RuntimeError("Instance telemetry is stale")
                return original(name)

            f.read_status = read
            with patch.object(runner.time, "sleep"):
                result = f.run()
            self.assertEqual(len(stale), 1)
            self.assertEqual(result["reloaded"]["simulationSeconds"], 11.25)

    def test_new_process_during_reload_is_not_adopted(self):
        with tempfile.TemporaryDirectory() as directory:
            f = Fixture(Path(directory))
            original = f.call

            def call(tool, arguments):
                result = original(tool, arguments)
                if arguments["domain"] == "launch":
                    f.status["pid"] = 8
                return result

            f.call = call
            with self.assertRaisesRegex(RuntimeError, "Process or run changed"):
                f.run()
            self.assertEqual(f.steps, 2)

    def test_missing_mcp_configuration_replaces_old_green_without_game_action(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            group, stage = root / "group", root / "stage"
            group.mkdir()
            stage.mkdir()
            (group / "group.json").write_text("{}")
            managed, native = root / "managed.dll", root / "native.so"
            managed.write_bytes(b"inert managed fixture")
            native.write_bytes(b"inert native fixture")
            for name in ("runtime.py", "runtime_mcp.py", "install_runtime.py"):
                (root / name).write_text("inert backend fixture")
            (stage / "preparation.json").write_text(json.dumps(dict(prepared=True,
                group_sha256=runner.digest(group / "group.json"), managed_sha256=runner.digest(managed),
                native_sha256=runner.digest(native), mcp_profile_configs={})))
            output = root / "result.json"
            output.write_text('{"passed":true}')
            from types import SimpleNamespace
            backend = SimpleNamespace(Controller=lambda path: SimpleNamespace(instances={"guest1": {}}))
            args = ["mcp_roundtrip.py", "--harness", str(root), "--group-root", str(group),
                    "--stage", str(stage), "--managed", str(managed), "--native", str(native),
                    "--name", "guest1", "--output", str(output)]
            with patch("sys.argv", args), patch.object(runner, "load_backend", return_value=(backend, root, {})), \
                 patch.object(runner, "inspect_instance") as audit, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(), 1)
            audit.assert_not_called()
            result = json.loads(output.read_text())
            self.assertFalse(result["passed"])
            self.assertIn("no explicit MCP", result["error"])


if __name__ == "__main__":
    unittest.main()
