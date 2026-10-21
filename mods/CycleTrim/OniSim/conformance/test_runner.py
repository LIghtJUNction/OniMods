"""Driver failures must stop before starting native children or writing cases."""
from pathlib import Path
import os
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from run import build_fixture_clock, differences
import run as conformance_run
from texture_probe import build as probe_build
from texture_probe.validate import LAYOUT, validate_observation
import copy
import ctypes
from types import SimpleNamespace
from host import Native
from benchmark_frames import process_memory, parse_cpus, require_no_game
import benchmark_frames as frame_benchmark
import argparse
import threading
from verify_package import validate_window_geometry


RUNNER = Path(__file__).with_name("run.py")


class WindowGeometryTests(unittest.TestCase):
    def test_observed_scaled_window_matches_pixels_and_rejects_invalid_geometry(self):
        valid = dict(mapped=True, size=[2048, 1280], png_size=[2560, 1600],
                     monitor_geometry=dict(scale=1.25))
        validate_window_geometry(valid)
        invalid = [None, {}, dict(valid, mapped=False), dict(valid, size=[0, 1280]),
                   dict(valid, size=[True, 1280]), dict(valid, png_size=[4096, 1600]),
                   dict(valid, monitor_geometry=None),
                   dict(valid, monitor_geometry=dict(scale=float("nan"))),
                   dict(valid, monitor_geometry=dict(scale=0))]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                validate_window_geometry(value)


class CaseConcurrencyTests(unittest.TestCase):
    def test_cases_overlap_with_bounded_workers_and_keep_receipt_order(self):
        barrier = threading.Barrier(2)
        lock = threading.Lock()
        active, maximum = 0, 0

        def compare(value):
            nonlocal active, maximum
            with lock:
                active += 1
                maximum = max(maximum, active)
            try:
                barrier.wait(timeout=5)
                return value
            finally:
                with lock:
                    active -= 1

        self.assertEqual(conformance_run.compare_cases(list(range(6)), compare, 2), list(range(6)))
        self.assertEqual(maximum, 2)
        self.assertEqual(active, 0)

    def test_error_drains_active_case_before_returning(self):
        started, failed, release, drained, finished = (threading.Event() for _ in range(5))
        errors = []

        def compare(value):
            if value == 0:
                if not started.wait(5):
                    raise AssertionError("Second case never started")
                failed.set()
                raise RuntimeError("Authored failure")
            started.set()
            try:
                if not release.wait(5):
                    raise AssertionError("Active case never released")
            finally:
                drained.set()

        def collect():
            try:
                conformance_run.compare_cases([0, 1], compare, 2)
            except Exception as error:
                errors.append(error)
            finally:
                finished.set()

        caller = threading.Thread(target=collect)
        caller.start()
        try:
            self.assertTrue(failed.wait(5))
            self.assertFalse(finished.is_set())
        finally:
            release.set()
            caller.join(5)
        self.assertFalse(caller.is_alive())
        self.assertTrue(drained.is_set())
        self.assertEqual([str(error) for error in errors], ["Authored failure"])

    def test_parallel_case_still_requires_actual_cell_workers(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            def native(command, **kwargs):
                output = Path(command[command.index("--output") + 1])
                output.write_text(json.dumps(dict(snapshots=[], message_calls={}, cell_workers=[])))
                return subprocess.CompletedProcess(command, 0)

            case = dict(name="authored-parallel", parallel_required=True)
            with patch.object(conformance_run.subprocess, "run", side_effect=native):
                result = conformance_run.compare_case(case, root, root / "ref.so", root / "rust.so", 4, None, {})
                self.assertFalse(result["passed"])
                self.assertIn("Parallel fixture", result["failures"][0]["error"])
                serial = conformance_run.compare_case(case, root, root / "ref.so", root / "rust.so", 1, None, {})
                self.assertTrue(serial["passed"])

    def test_cpu_budget_failure_starts_no_native_child(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = ["run.py", "--reference", str(root / "ref.so"), "--candidate", str(root / "rust.so"),
                    "--output", str(root / "out"), "--jobs", "2", "--workers", "4"]
            with patch("sys.argv", args), patch.object(conformance_run.os, "sched_getaffinity", return_value={0, 1}), \
                 patch.object(conformance_run.subprocess, "run") as child:
                with self.assertRaises(SystemExit):
                    conformance_run.main()
            child.assert_not_called()
            self.assertFalse((root / "out").exists())


class LibraryPreflightTests(unittest.TestCase):
    def test_optional_pressure_context_records_missing_and_denied_counters(self):
        def read(path):
            if path.name == 'cpu':
                raise FileNotFoundError(2, 'PSI not exposed')
            if path.name == 'memory':
                raise PermissionError(13, 'PSI not readable')
            return 'some avg10=0.25 total=42\n'

        with patch.object(frame_benchmark.os, 'sched_getaffinity', return_value={0}), \
             patch.object(Path, 'exists', return_value=False), \
             patch.object(Path, 'read_text', autospec=True, side_effect=read):
            context = frame_benchmark.host_context()
        self.assertEqual(context['pressure'], dict(cpu=None, memory=None,
                                                  io='some avg10=0.25 total=42'))
        self.assertEqual(context['pressure_unavailable'], dict(
            cpu=dict(error='FileNotFoundError', errno=2),
            memory=dict(error='PermissionError', errno=13)))

    def test_available_pressure_context_retains_each_observation(self):
        with patch.object(frame_benchmark.os, 'sched_getaffinity', return_value={0}), \
             patch.object(Path, 'exists', return_value=False), \
             patch.object(Path, 'read_text', autospec=True,
                          side_effect=lambda path: f'{path.name} observation\n'):
            context = frame_benchmark.host_context()
        self.assertEqual(context['pressure'], {kind: f'{kind} observation'
                                              for kind in ('cpu', 'memory', 'io')})
        self.assertEqual(context['pressure_unavailable'], {})

    def test_interrupted_timing_binds_inputs_and_excludes_rejected_child(self):
        for fail_at, mutate in [(1, False), (7, False), (7, True)]:
            with self.subTest(fail_at=fail_at, mutate=mutate), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for name in ("host.py", "benchmark_frames.py", "reference.so", "baseline.so", "candidate.so"):
                    (root / name).write_text("authored fixture; never executed: " + name)
                output = root / "output"
                output.mkdir()
                (output / "summary.json").write_text('{"passed": true}')
                args = SimpleNamespace(reference=root / "reference.so", baseline=root / "baseline.so",
                    candidate=root / "candidate.so", samples=5, output=output, workers=4,
                    cpu=None, cpus=[16, 17, 18, 19], baseline_role="candidate",
                    size=192, baseline_workers=1, candidate_workers=4)
                guard_calls = 0

                def guard():
                    nonlocal guard_calls
                    guard_calls += 1
                    if guard_calls == fail_at:
                        raise RuntimeError("Concurrent ONI test fixture")

                def child(command, **kwargs):
                    target = Path(command[command.index("--output") + 1])
                    self.assertEqual(command[command.index('--size') + 1], '192')
                    expected_workers = '1' if '-baseline' in target.stem else '4'
                    self.assertEqual(kwargs['env']['CYCLETRIM_ONISIM_WORKERS'], expected_workers)
                    target.write_text(json.dumps(dict(case="fluid", cpu_seconds=1, wall_seconds=1,
                                                      final={"authored": "fixture"})))
                    if mutate and target.name == "0-fluid-candidate.json":
                        args.candidate.write_text("changed candidate")
                    return subprocess.CompletedProcess(command, 0, stdout="", stderr="")

                with patch.object(frame_benchmark, "ROOT", root), \
                     patch.object(frame_benchmark, "HARNESS_FILES", ("host.py",)), \
                     patch.object(frame_benchmark, "CASES", ("fluid",)), \
                     patch.object(frame_benchmark, "require_no_game", side_effect=guard), \
                     patch.object(frame_benchmark, "differences", return_value=[]), \
                     patch.object(frame_benchmark.subprocess, "run", side_effect=child):
                    with self.assertRaisesRegex(RuntimeError, "Concurrent ONI"):
                        frame_benchmark.parent(args)
                self.assertFalse((output / "summary.json").exists())
                inputs = json.loads((output / "inputs.json").read_text())
                self.assertEqual(set(inputs["libraries"]), {"reference", "baseline", "candidate"})
                self.assertEqual((inputs['width'], inputs['height']), (192, 192))
                self.assertEqual((inputs['baseline_workers'], inputs['candidate_workers']), (1, 4))
                failure = json.loads((output / "failed.json").read_text())
                self.assertFalse(failure["passed"])
                self.assertFalse(failure["complete"])
                self.assertEqual(failure["inputs_unchanged"], not mutate)
                self.assertEqual(len(failure["completed_samples"]), 0 if fail_at == 1 else 1)
                if fail_at == 7:
                    self.assertTrue((output / "0-fluid-candidate.json").exists())
                    self.assertEqual(failure["completed_samples"][0]["label"], "baseline")

    def test_frame_timing_refuses_a_concurrent_game(self):
        with patch.object(Path, "iterdir", return_value=iter([Path("/proc/self"), Path("/proc/123")])):
            with patch.object(Path, "read_text", return_value="OxygenNotInclud\n"):
                with self.assertRaisesRegex(RuntimeError, "live PIDs: \\[123\\]"):
                    require_no_game()
        with patch.object(Path, "iterdir", return_value=iter([Path("/proc/123")])):
            with patch.object(Path, "read_text", side_effect=ProcessLookupError):
                require_no_game()

    def test_parallel_frame_cpu_set_is_explicit_and_validated(self):
        self.assertEqual(parse_cpus("19,16,17,16,18"), [16, 17, 18, 19])
        for invalid in ["", "16,", "-1,16", "cpu16"]:
            with self.subTest(invalid=invalid), self.assertRaises(argparse.ArgumentTypeError):
                parse_cpus(invalid)

    def test_frame_memory_uses_current_exec_peak_and_separate_pss(self):
        samples = {
            "/proc/self/status": "Name:\tpython\nVmHWM:\t12000 kB\nVmRSS:\t11000 kB\n",
            "/proc/self/smaps_rollup": "Rss: 11004 kB\nPss: 7000 kB\nSwap: 12 kB\n",
        }
        with patch.object(Path, "read_text", autospec=True,
                          side_effect=lambda path: samples[str(path)]):
            self.assertEqual(process_memory(), dict(vm_hwm_kib=12000, rss_kib=11004,
                                                   pss_kib=7000, swap_kib=12))
        samples["/proc/self/status"] = "VmHWM: 12 MB\n"
        with patch.object(Path, "read_text", autospec=True,
                          side_effect=lambda path: samples[str(path)]):
            with self.assertRaisesRegex(RuntimeError, "Unexpected Linux memory unit"):
                process_memory()

    def test_replacing_save_buffers_copies_each_before_the_next_begin(self):
        events = []
        buffer = ctypes.create_string_buffer(3)

        def begin(size, x, y):
            events.append(("begin", x, y))
            buffer.raw = bytes([len(events)]) * 3
            ctypes.cast(size, ctypes.POINTER(ctypes.c_int))[0] = 3
            return ctypes.addressof(buffer)

        fake = SimpleNamespace(lib=SimpleNamespace(SIM_BeginSave=begin,
            SIM_EndSave=lambda: events.append(("end",))), check=lambda: None)
        self.assertEqual(Native.save(fake, 3, (7, -11)), b"\x03" * 3)
        self.assertEqual(events, [("begin", 7, -11)] * 3 + [("end",)] * 2)
        self.assertEqual([item["sha256"] for item in fake.last_save_buffers],
                         [hashlib.sha256(bytes([i]) * 3).hexdigest() for i in (1, 2, 3)])

    def test_invalid_save_buffer_is_released_and_bad_options_never_call_native(self):
        events = []
        fake = SimpleNamespace(lib=SimpleNamespace(
            SIM_BeginSave=lambda *args: events.append("begin"),
            SIM_EndSave=lambda: events.append("end")), check=lambda: None)
        with self.assertRaisesRegex(RuntimeError, "Invalid save buffer"):
            Native.save(fake)
        self.assertEqual(events, ["begin", "end"])
        events.clear()
        buffer = ctypes.create_string_buffer(b"abc")

        def valid_begin(size, *origin):
            events.append("begin")
            ctypes.cast(size, ctypes.POINTER(ctypes.c_int))[0] = 3
            return ctypes.addressof(buffer)

        fake.lib.SIM_BeginSave = valid_begin
        with patch("host.C.string_at", side_effect=RuntimeError("copy failed")):
            with self.assertRaisesRegex(RuntimeError, "copy failed"):
                Native.save(fake)
        self.assertEqual(events, ["begin", "end"])
        for options in [dict(replacements=0), dict(replacements=True), dict(replacements=17),
                        dict(origin=(2**31, 0)), dict(origin=(0,)), dict(origin=(0, True))]:
            events.clear()
            with self.subTest(options=options), self.assertRaises(ValueError):
                Native.save(fake, **options)
            self.assertEqual(events, [])

    def test_changed_inputs_cannot_issue_or_retain_a_green_summary(self):
        for changed_kind in ("harness", "library"):
            with self.subTest(changed_kind=changed_kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for name in conformance_run.HARNESS_FILES:
                    (root / name).write_text("authored harness")
                reference, candidate = root / "reference.so", root / "candidate.so"
                reference.write_bytes(b"authored reference")
                candidate.write_bytes(b"authored candidate")
                output = root / "output"
                output.mkdir()
                summary = output / "summary.json"
                summary.write_text('{"passed": true}')

                def child(command, **kwargs):
                    # Native execution is deliberately replaced here: the
                    # contract under test is the driver's input identity gate.
                    target = Path(command[command.index("--output") + 1])
                    target.write_text(json.dumps(dict(snapshots=[], message_calls={})))
                    if command[command.index("--role") + 1] == "candidate":
                        changed = root / "host.py" if changed_kind == "harness" else candidate
                        changed.write_bytes(b"changed during comparison")
                    return subprocess.CompletedProcess(command, 0)

                args = [str(RUNNER), "--reference", str(reference), "--candidate", str(candidate),
                        "--output", str(output)]
                with patch.object(conformance_run, "ROOT", root), \
                     patch.object(conformance_run, "cases", return_value=[dict(name="identity")]), \
                     patch.object(sys, "argv", args), \
                     patch.object(conformance_run.subprocess, "run", side_effect=child):
                    with self.assertRaisesRegex(RuntimeError, "changed during comparison"):
                        conformance_run.main()
                self.assertFalse(summary.exists())

    def test_comparison_rejects_extra_missing_and_null_fields(self):
        reference = [dict(arrays=dict(temperature=[1]), events={})]
        self.assertEqual(differences(reference, copy.deepcopy(reference)), [])
        for group in ("arrays", "events"):
            extra = copy.deepcopy(reference)
            extra[0][group]["unexpected"] = None
            with self.subTest(group=group):
                self.assertTrue(differences(reference, extra))
                self.assertTrue(differences(extra, reference))
            missing = copy.deepcopy(reference)
            del missing[0][group]
            self.assertTrue(differences(reference, missing))
        extra = copy.deepcopy(reference)
        extra[0]["unexpected"] = None
        self.assertTrue(differences(reference, extra))
        self.assertTrue(differences(extra, reference))

    def test_texture_observation_requires_complete_matching_buffers(self):
        data = dict(request=1, complete=True, passed=True, graphicsDevice="OpenGLCore",
                    width=2, height=3, textures={
                        name: dict(bytes=6 * stride, gpuBytes=6 * stride, format=fmt,
                                   cpuEqual=True, gpuEqual=True, gpuComplete=True,
                                   nativeSha256="a" * 64, unityCpuSha256="a" * 64,
                                   gpuSha256="a" * 64)
                        for name, (stride, fmt) in LAYOUT.items()})
        self.assertEqual(validate_observation(data, 1), data)
        changes = [("request", 2), ("complete", False), ("passed", False),
                   ("graphicsDevice", "Null"), ("width", True), ("height", 0)]
        for key, value in changes:
            with self.subTest(field=key):
                with self.assertRaises(ValueError):
                    validate_observation({**data, key: value}, 1)
        for key, value in [("bytes", 1), ("gpuBytes", 1), ("format", "R8"),
                           ("cpuEqual", False), ("gpuEqual", False),
                           ("gpuComplete", False), ("gpuSha256", "b" * 64),
                           ("nativeSha256", "not a hash")]:
            bad = copy.deepcopy(data)
            bad["textures"]["Flow"][key] = value
            with self.subTest(texture_field=key):
                with self.assertRaises(ValueError):
                    validate_observation(bad, 1)
        bad = copy.deepcopy(data)
        del bad["textures"]["Flow"]
        with self.assertRaises(ValueError):
            validate_observation(bad, 1)

    def test_probe_receipt_rejects_stale_source_binary_and_failed_build(self):
        with tempfile.TemporaryDirectory(prefix="cycletrim-probe-") as directory:
            root = Path(directory)
            for name in probe_build.SOURCES:
                (root / name).write_text("authored test source")
            binary = root / "probe.dll"
            binary.write_bytes(b"authored test artifact")
            receipt = root / "receipt.json"
            data = {"build_exit_code": 0, "output_dll": str(binary),
                    "probe_sha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
                    "source_sha256": {name: hashlib.sha256((root / name).read_bytes()).hexdigest()
                                      for name in probe_build.SOURCES}}
            receipt.write_text(json.dumps(data))
            with patch.object(probe_build, "ROOT", root):
                self.assertEqual(probe_build.verify_receipt(receipt), data)
                (root / "TextureProbe.cs").write_text("changed source")
                with self.assertRaisesRegex(RuntimeError, "source changed"):
                    probe_build.verify_receipt(receipt)
                (root / "TextureProbe.cs").write_text("authored test source")
                binary.write_bytes(b"stale binary")
                with self.assertRaisesRegex(RuntimeError, "DLL differs"):
                    probe_build.verify_receipt(receipt)
                binary.write_bytes(b"authored test artifact")
                receipt.write_text(json.dumps({**data, "build_exit_code": 1}))
                with self.assertRaisesRegex(RuntimeError, "did not succeed"):
                    probe_build.verify_receipt(receipt)

    def test_fixed_realtime_preserves_monotonic_progress(self):
        with tempfile.TemporaryDirectory(prefix="cycletrim-clock-") as directory:
            library = build_fixture_clock(Path(directory))
            result = subprocess.run([sys.executable, "-c", """
import ctypes, time
libc = ctypes.CDLL(None)
libc.time.argtypes = [ctypes.c_void_p]
libc.time.restype = ctypes.c_long
assert libc.time(None) == 1900000000
assert time.time() == 1900000000
before = time.monotonic()
time.sleep(.02)
assert time.monotonic() > before
assert time.time() == 1900000000
"""], env=dict(os.environ, LD_PRELOAD=str(library)), capture_output=True, text=True, timeout=10)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_missing_reference_is_rejected_before_output_creation(self):
        self.check_missing("reference")

    def test_missing_candidate_is_rejected_before_output_creation(self):
        self.check_missing("candidate")

    def check_missing(self, role):
        with tempfile.TemporaryDirectory(prefix="cycletrim-driver-") as directory:
            root = Path(directory)
            present = root / "present.so"
            # This is deliberately not a loadable library. Preflight must fail
            # on the absent peer before any native worker can try to open it.
            present.write_bytes(b"not a shared library")
            missing = root / "missing.so"
            output = root / "output"
            libraries = {"reference": present, "candidate": present, role: missing}
            result = subprocess.run(
                [sys.executable, str(RUNNER), "--reference", str(libraries["reference"]),
                 "--candidate", str(libraries["candidate"]), "--case", "sealed-oxygen",
                 "--output", str(output)],
                capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(result.returncode, 2, result.stderr)
            self.assertIn(f"{role} library does not exist or is not a file:", result.stderr)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
