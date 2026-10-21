"""Game-free audit tests. API doubles and fake /proc files are not game evidence."""
import contextlib
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
import struct
import zipfile
from types import SimpleNamespace
from unittest.mock import Mock, patch

import verify_instances
from verify_instances import mapped_native, verify_concurrency, inspect_instance
import prepare_instances
import step_instance


class SimulationOptionsTests(unittest.TestCase):
    def test_core_selection_is_an_exact_mod_local_setting(self):
        for core, enabled in (("rust", True), ("official", False)):
            with self.subTest(core=core), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                actual = prepare_instances.stage_simulation_options(root, core)
                path = root / "CycleTrimOptions.json"
                self.assertEqual(json.loads(path.read_text()), {"EnableOniSim": enabled})
                self.assertEqual(actual, verify_instances.digest(path))
                self.assertEqual(list(root.iterdir()), [path])

    def test_existing_settings_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = root / "CycleTrimOptions.json"
            path.write_text("existing private test settings")
            with self.assertRaises(FileExistsError):
                prepare_instances.stage_simulation_options(root, "official")
            self.assertEqual(path.read_text(), "existing private test settings")

    def test_unknown_selection_writes_nothing(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(RuntimeError):
                prepare_instances.stage_simulation_options(Path(folder), "unknown")
            self.assertEqual(list(Path(folder).iterdir()), [])


def group_fixture():
    instances, audited, before, after, responses = {}, {}, {}, {}, {}
    for index in range(4):
        name = "host" if index == 0 else "guest" + str(index)
        instances[name] = dict(graphics="normal" if index == 0 else "headless")
        status = dict(pid=100 + index, identity=name, runNonce="run-" + name,
                      gameLoaded=True, paused=True, simulationSeconds=10,
                      activeSavePath=f"/{name}/Test.sav", ownedSave=f"/{name}/Test.sav",
                      batchMode=index != 0, graphicsDevice="GL" if index == 0 else "Null",
                      cameraRenderCallbacks=1 if index == 0 else 0)
        before[name] = dict(status)
        status["simulationSeconds"] = 11
        after[name] = dict(status)
        audited[name] = dict(status=dict(status))
        responses[name] = dict(instance=name, nonce="run-" + name, state="completed",
                               startedAt="2026-10-21T00:00:00+00:00",
                               completedAt="2026-10-21T00:00:01+00:00")
    receipt = dict(passed=True, kind="complete-game-concurrency", protocolClients=0,
                   completeGameCount=4, before=before, after=after, responses=responses,
                   resources=dict(violations=[]))
    return receipt, instances, audited


class ConcurrencyAuditTests(unittest.TestCase):
    def test_complete_game_scope_and_overlap_are_recomputed(self):
        receipt, instances, audited = group_fixture()
        receipt["overlapSeconds"] = -100  # Never trust the summary flag.
        self.assertEqual(verify_concurrency(receipt, instances, audited),
                         dict(complete_game_count=4, protocol_clients=0, overlap_seconds=1.0))

    def test_false_acceptance_inputs_are_rejected(self):
        mutations = [
            lambda r, i, a: r.update(protocolClients=1),
            lambda r, i, a: r.update(completeGameCount=40),
            lambda r, i, a: r["resources"].update(violations=["swap"]),
            lambda r, i, a: r["before"]["guest1"].update(pid=100),
            lambda r, i, a: r["after"]["guest1"].update(runNonce="restarted"),
            lambda r, i, a: a["guest1"]["status"].update(paused=False),
            lambda r, i, a: a["guest1"]["status"].update(activeSavePath="/another.sav"),
            lambda r, i, a: r["after"]["guest1"].update(simulationSeconds=10),
            lambda r, i, a: r["after"]["guest1"].update(simulationSeconds=float("nan")),
            lambda r, i, a: r["before"]["guest1"].update(simulationSeconds=True),
            lambda r, i, a: r["after"]["guest1"].update(graphicsDevice="OpenGLCore"),
            lambda r, i, a: r["after"]["guest1"].update(cameraRenderCallbacks=1),
            lambda r, i, a: i["guest1"].update(graphics="normal"),
            lambda r, i, a: r["after"]["host"].update(graphicsDevice="Null"),
            lambda r, i, a: r["after"]["host"].update(cameraRenderCallbacks=0),
            lambda r, i, a: r["responses"]["guest1"].update(nonce="different"),
            lambda r, i, a: r["responses"]["guest1"].update(state="accepted"),
            lambda r, i, a: r["responses"]["guest1"].update(
                startedAt="2026-10-21T00:00:02+00:00", completedAt="2026-10-21T00:00:03+00:00"),
        ]
        for index, mutate in enumerate(mutations):
            receipt, instances, audited = group_fixture()
            mutate(receipt, instances, audited)
            with self.subTest(index=index), self.assertRaises(RuntimeError):
                verify_concurrency(receipt, instances, audited)


class NativeMappingAuditTests(unittest.TestCase):
    def test_live_assembly_run_and_pid_must_all_match(self):
        with tempfile.TemporaryDirectory() as directory:
            profile = Path(directory)
            assembly = profile / "CycleTrim.dll"
            assembly.write_bytes(b"authored fixture assembly, never executed")
            sha = hashlib.sha256(assembly.read_bytes()).hexdigest()
            status = dict(pid=123, identity="guest1", runNonce="run", assemblyReceipt="instance-assemblies.json")
            receipt = dict(status, assemblies=[dict(location=str(assembly), sha256=sha)])
            (profile / "instance-assemblies.json").write_text(json.dumps(receipt))
            controller = SimpleNamespace(read_status=Mock(return_value=status),
                verify_hashes=Mock(), paths=lambda name: dict(user=profile, control=profile),
                instances=dict(guest1=dict(graphics="headless")))
            backend = SimpleNamespace(ProcessIdentity=SimpleNamespace(capture=Mock(return_value=(123, 456))))
            with patch("verify_instances.mapped_native", return_value="native"):
                self.assertEqual(inspect_instance(controller, backend, "guest1", sha, "native")["managed_sha256"], sha)
                with self.assertRaisesRegex(RuntimeError, "not the requested candidate"):
                    inspect_instance(controller, backend, "guest1", "0" * 64, "native")
                backend.ProcessIdentity.capture.side_effect = [(123, 456), (123, 789)]
                with self.assertRaisesRegex(RuntimeError, "PID was reused"):
                    inspect_instance(controller, backend, "guest1", sha, "native")

    def test_failure_replaces_a_stale_success_but_never_overwrites_an_input(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            managed, native, output = root / "CycleTrim.dll", root / "liboni_sim_core.so", root / "audit.json"
            managed.write_bytes(b"fixture")
            native.write_bytes(b"fixture")
            output.write_text('{"passed": true}')
            args = ["verify_instances.py", "--harness", str(root / "harness"), "--group-root", str(root / "group"),
                    "--managed", str(managed), "--native", str(native), "--output", str(output)]
            with patch("sys.argv", args), patch("verify_instances.load_backend", side_effect=RuntimeError("fixture not ready")), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(verify_instances.main(), 1)
            self.assertFalse(json.loads(output.read_text())["passed"])
            original = output.read_bytes()
            with patch("sys.argv", args + ["--concurrency-receipt", str(output)]), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit):
                    verify_instances.main()
            self.assertEqual(output.read_bytes(), original)

    def test_mapping_binds_bytes_device_inode_and_profile(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            profile = root / "user profile"
            profile.mkdir()
            library = profile / "liboni_sim_core.so"
            library.write_bytes(b"authored fixture, not a native library")
            digest = hashlib.sha256(library.read_bytes()).hexdigest()
            proc = root / "proc"
            pid = proc / "123"
            pid.mkdir(parents=True)
            stat = library.stat()
            prefix = f"1000-2000 r-xp 00000000 {os.major(stat.st_dev):x}:{os.minor(stat.st_dev):x} {stat.st_ino} "
            maps = pid / "maps"
            valid = prefix + str(library) + "\n"
            maps.write_text(valid)
            self.assertEqual(mapped_native(123, profile, digest, proc), digest)
            for content, expected, owner in [
                ("", digest, profile),
                (valid.rstrip() + " (deleted)\n", digest, profile),
                (valid.replace(f" {stat.st_ino} ", " 1 "), digest, profile),
                (valid, "0" * 64, profile),
                (valid, digest, root / "other-profile"),
            ]:
                maps.write_text(content)
                with self.subTest(content=content), self.assertRaises(RuntimeError):
                    mapped_native(123, owner, expected, proc)


def fixture_package(path):
    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<HH", header, 16, 3, 62)
    with zipfile.ZipFile(path, "w") as archive:
        for name in ("CycleTrim.dll", "mod.yaml", "mod_info.yaml"):
            archive.writestr(name, "authored fixture; never executed")
        archive.writestr("OniSim/liboni_sim_core.so", header)
        archive.writestr("../ignored.txt", "Must not escape the allowlist")
    return bytes(header)


class InstancePreparationTests(unittest.TestCase):
    def test_native_override_is_copied_without_executing_or_changing_its_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package, override = root / "CycleTrim.zip", root / "candidate.so"
            header = fixture_package(package)
            data = header + b"inert candidate payload"
            override.write_bytes(data)
            result = prepare_instances.stage_package(package, root / "stage", override)
            self.assertEqual(result["native_sha256"], hashlib.sha256(data).hexdigest())
            self.assertEqual(override.read_bytes(), data)
            override.write_bytes(b"invalid header")
            with self.assertRaisesRegex(RuntimeError, "Linux x64"):
                prepare_instances.stage_package(package, root / "bad-stage", override)
            self.assertFalse((root / "bad-stage").exists())
            with self.assertRaises(FileNotFoundError):
                prepare_instances.stage_package(package, root / "missing-stage", root / "absent.so")
            self.assertFalse((root / "missing-stage").exists())

    def test_only_runtime_allowlist_is_staged_and_existing_stage_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = root / "CycleTrim.zip"
            fixture_package(package)
            staged = root / "staged"
            report = prepare_instances.stage_package(package, staged)
            self.assertEqual(report["native_sha256"], hashlib.sha256((staged / "OniSim/liboni_sim_core.so").read_bytes()).hexdigest())
            self.assertFalse((root / "ignored.txt").exists())
            with self.assertRaisesRegex(RuntimeError, "new staged"):
                prepare_instances.stage_package(package, staged)

    def test_managed_only_package_cannot_be_used_for_a_rust_test_group(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = root / "managed-only.zip"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("CycleTrim.dll", b"fixture")
            with self.assertRaisesRegex(RuntimeError, "real managed/native"):
                prepare_instances.stage_package(package, root / "staged")
            self.assertFalse((root / "staged").exists())

    def test_real_controller_init_creates_config_without_launching_a_game(self):
        # The actual Python controller receives inert fixture files. No game or
        # native fixture library is executed, and no existing group is touched.
        harness = Path(__file__).resolve().parents[4] / "tools/OniHarness"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game, companion = root / "game", root / "companion"
            game.mkdir()
            companion.mkdir()
            (game / "OxygenNotIncluded").write_text("inert game fixture")
            (companion / "mod_info.yaml").write_text("inert metadata")
            (companion / "mod.yaml").write_text("inert metadata")
            (companion / "OniHarness.Runtime.dll").write_bytes(b"inert fixture; never loaded")
            save = root / "Source.sav"
            save.write_bytes(b"inert private save fixture")
            save.chmod(0o400)
            package = root / "CycleTrim.zip"
            fixture_package(package)
            group, stage = root / "group", root / "stage"
            args = ["prepare_instances.py", "--harness", str(harness), "--group-root", str(group),
                    "--stage", str(stage), "--package", str(package), "--game-path", str(game),
                    "--companion", str(companion), "--save", str(save)]
            with patch("sys.argv", args + ["--native", str(root / "absent.so")]):
                with self.assertRaises(FileNotFoundError):
                    prepare_instances.main()
            self.assertFalse(stage.exists())
            self.assertFalse(group.exists())
            with patch("sys.argv", args), contextlib.redirect_stdout(io.StringIO()):
                prepare_instances.main()
            plan = json.loads((group / "group.json").read_text())
            report = json.loads((stage / "preparation.json").read_text())
            self.assertEqual([i["graphics"] for i in plan["instances"]], ["normal", "headless", "headless", "headless"])
            self.assertEqual([i["threads"] for i in plan["instances"]], [2, 2, 2, 2])
            self.assertEqual(report["game_instances_started"], 0)
            self.assertFalse(report["runtime_validated"])
            self.assertEqual(save.read_bytes(), (stage / "Initial.sav").read_bytes())
            self.assertEqual(save.stat().st_mode & 0o777, 0o400)
            self.assertEqual((stage / "Initial.sav").stat().st_mode & 0o777, 0o600)
            with (stage / "Initial.sav").open("r+b") as native_style_open:
                self.assertEqual(native_style_open.read(), save.read_bytes())

            mcp_package = root / "OniMcp.zip"
            with zipfile.ZipFile(mcp_package, "w") as archive:
                for name in ("OniMcp.dll", "mod.yaml", "mod_info.yaml"):
                    archive.writestr(name, "authored fixture; never loaded")
                archive.writestr("OniMcpConfig.json", '{"AuthToken":"must-not-be-imported"}')
            mcp_group, mcp_stage = root / "mcp-group", root / "mcp-stage"
            mcp_args = list(args)
            mcp_args[mcp_args.index("--group-root") + 1] = str(mcp_group)
            mcp_args[mcp_args.index("--stage") + 1] = str(mcp_stage)
            mcp_args.extend(["--mcp-package", str(mcp_package)])
            with patch("sys.argv", mcp_args), contextlib.redirect_stdout(io.StringIO()):
                prepare_instances.main()
            mcp_plan = json.loads((mcp_group / "group.json").read_text())
            self.assertEqual({m["id"] for m in mcp_plan["mods"]}, {"CycleTrim", "OniMcp", "OniHarness.Runtime"})
            self.assertFalse((mcp_stage / "mcp/OniMcpConfig.json").exists())
            tokens = []
            for instance in mcp_plan["instances"]:
                path = mcp_group / "instances" / instance["name"] / "xdg/unity3d/Klei/Oxygen Not Included/OniMcpConfig.json"
                options = json.loads(path.read_text())
                self.assertEqual(options["Port"], instance["port"])
                self.assertEqual(options["Host"], "127.0.0.1")
                self.assertTrue(options["AuthEnabled"])
                self.assertFalse(options["ScreenshotCleanupEnabled"])
                self.assertEqual(path.stat().st_mode & 0o777, 0o600)
                tokens.append(options["AuthToken"])
            self.assertTrue(len(set(tokens)) == len(tokens))
            mcp_report = json.loads((mcp_stage / "preparation.json").read_text())
            self.assertTrue(all(token not in json.dumps(mcp_report) for token in tokens))
            self.assertEqual(mcp_report["game_instances_started"], 0)

            # Inert probe plus a mocked build validator exercises only staging.
            probe_dll, probe_receipt = root / "probe.dll", root / "probe-receipt.json"
            probe_dll.write_bytes(b"authored probe fixture; never loaded")
            probe_receipt.write_text("inert mocked build receipt")
            probe_group, probe_stage = root / "probe-group", root / "probe-stage"
            probe_args = list(mcp_args)
            probe_args[probe_args.index("--group-root") + 1] = str(probe_group)
            probe_args[probe_args.index("--stage") + 1] = str(probe_stage)
            probe_args.extend(["--texture-probe-receipt", str(probe_receipt)])
            verification = dict(output_dll=str(probe_dll), probe_sha256=prepare_instances.digest(probe_dll))
            with patch("sys.argv", probe_args), patch.object(prepare_instances, "verify_texture_probe", return_value=verification), \
                 contextlib.redirect_stdout(io.StringIO()):
                prepare_instances.main()
            plan = json.loads((probe_group / "group.json").read_text())
            self.assertIn("CycleTrim.TextureProbe", {m["id"] for m in plan["mods"]})
            for member in plan["instances"]:
                self.assertEqual(member.get("gameArgs", []),
                                 ["--cycletrim-texture-probe"] if member["graphics"] == "normal" else [])
            self.assertEqual((probe_stage / "texture-probe/CycleTrim.TextureProbe.dll").read_bytes(), probe_dll.read_bytes())


class BoundedStepTests(unittest.TestCase):
    def fixture(self):
        receipt, instances, _ = group_fixture()
        before, after = receipt["before"]["guest1"], receipt["after"]["guest1"]
        response = dict(receipt["responses"]["guest1"], pid=before["pid"], paused=True,
                        nativeSeconds=1, simulationSecondsBefore=10, simulationSecondsAfter=11)
        controller = SimpleNamespace(instances=instances, state=dict(instances={}),
            live=Mock(return_value=False), command=Mock(return_value=response), read_status=Mock(return_value=after))
        return controller, before, after, response

    def test_one_native_step_verifies_clock_identity_and_headless_first(self):
        controller, before, after, _ = self.fixture()
        progress = {}
        with patch("step_instance.inspect_instance", side_effect=[dict(status=before), dict(status=after)]):
            result = step_instance.exercise(controller, None, "guest1", "managed", "native", 1, True, progress)
        self.assertTrue(result["first_headless_verified"])
        self.assertEqual(progress["before"], before)
        controller.command.assert_called_once_with("guest1", "step", seconds=1)

    def test_bad_step_responses_fail_without_retry(self):
        for changes in [dict(nativeSeconds=0), dict(nativeSeconds=True), dict(nativeSeconds=2),
                        dict(pid=999), dict(nonce="other-run"), dict(paused=False), dict(state="accepted")]:
            controller, before, after, response = self.fixture()
            response.update(changes)
            with patch("step_instance.inspect_instance", side_effect=[dict(status=before), dict(status=after)]):
                with self.subTest(changes=changes), self.assertRaises(RuntimeError):
                    step_instance.exercise(controller, None, "guest1", "managed", "native", 1)
            self.assertEqual(controller.command.call_count, 1)

    def test_first_headless_precondition_sends_no_command(self):
        controller, _, _, _ = self.fixture()
        controller.state["instances"]["host"] = dict(startedAt="already-started")
        progress = {}
        with self.assertRaisesRegex(RuntimeError, "graphical instance has already started"):
            step_instance.exercise(controller, None, "guest1", "managed", "native", 1, True, progress)
        self.assertEqual(progress, {})
        controller.command.assert_not_called()

    def test_cleanup_remains_under_the_test_lock(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stage, group = root / "stage", root / "group"
            stage.mkdir()
            group.mkdir()
            (group / "group.json").write_text("{}")
            (stage / "preparation.json").write_text(json.dumps(dict(prepared=True,
                group_sha256=verify_instances.digest(group / "group.json"))))
            managed, native = root / "CycleTrim.dll", root / "candidate.so"
            managed.write_bytes(b"fixture only")
            native.write_bytes(b"fixture only")
            controller, before, _, _ = self.fixture()
            controller.read_status.return_value = before

            def pause(name, action):
                self.assertEqual((name, action), ("guest1", "pause"))
                with (stage / "cycletrim-step-guest1.lock").open("a+") as competitor:
                    with self.assertRaises(BlockingIOError):
                        fcntl.flock(competitor, fcntl.LOCK_EX | fcntl.LOCK_NB)
                with (stage / "cycletrim-step-guest2.lock").open("a+") as independent:
                    fcntl.flock(independent, fcntl.LOCK_EX | fcntl.LOCK_NB)
                return dict(state="completed")

            controller.command.side_effect = pause
            backend = SimpleNamespace(Controller=lambda root: controller)

            def fail(*args):
                args[-1]["before"] = before
                raise RuntimeError("authored step failure")

            output = root / "result.json"
            args = ["step_instance.py", "--harness", str(root), "--group-root", str(group), "--stage", str(stage),
                    "--managed", str(managed), "--native", str(native), "--name", "guest1", "--output", str(output)]
            with patch("sys.argv", args), patch("step_instance.load_backend", return_value=(backend, root, {})), \
                 patch("step_instance.exercise", side_effect=fail), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(step_instance.main(), 1)
            result = json.loads(output.read_text())
            self.assertFalse(result["passed"])
            self.assertTrue(result["cleanup_paused"])
            controller.command.assert_called_once()


if __name__ == "__main__":
    unittest.main()
