#!/usr/bin/env python3
"""Linux diagnostics regressions, including real child CPU and termination."""

import copy
import contextlib
import io
import json
import hashlib
import mmap
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

from profile_cycletrim_process import collect, interval, loaded_libraries, process_snapshot, stat_fields
import profile_cycletrim_native as native_profiler


@unittest.skipUnless(Path("/proc/self/stat").exists(), "Linux /proc diagnostics only")
class ProcessProfileTests(unittest.TestCase):
    def test_mapped_underscored_onisim_library_is_fingerprinted(self):
        with tempfile.TemporaryDirectory() as temporary:
            library = Path(temporary) / "liboni_sim_core.so"
            payload = b"read-only mapped diagnostic fixture"
            library.write_bytes(payload)
            with library.open("rb") as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ):
                matches = [row for row in loaded_libraries(os.getpid()) if row["path"] == str(library)]
                self.assertEqual(matches, [{"path": str(library), "sha256": hashlib.sha256(payload).hexdigest()}])

    def test_comm_with_parentheses(self):
        text = Path("/proc/self/stat").read_text()
        fields = text.rsplit(")", 1)[1]
        self.assertEqual(stat_fields(text), stat_fields("1 (space (nested) name)" + fields))

    def test_reused_pid_and_tid_rejected(self):
        first = process_snapshot(os.getpid())
        second = copy.deepcopy(first)
        second["monotonicSeconds"] += 1
        second["startTicks"] += 1
        with self.assertRaisesRegex(ValueError, "identity"):
            interval(first, second, os.sysconf("SC_CLK_TCK"))
        second["startTicks"] = first["startTicks"]
        tid = next(iter(second["threads"]))
        second["threads"][tid]["startTicks"] += 1
        row = interval(first, second, os.sysconf("SC_CLK_TCK"))
        self.assertEqual(row["threadsNotMatched"], 1)
        self.assertNotIn(int(tid), [t["tid"] for t in row["threads"]])

    def test_live_cpu_and_sleep_are_distinguishable(self):
        code = "import time\nend=time.monotonic()+5\nwhile time.monotonic()<end: sum(range(10000))"
        busy = subprocess.Popen([sys.executable, "-c", code])
        sleeping = subprocess.Popen([sys.executable, "-c", "import time;time.sleep(5)"])
        try:
            a = process_snapshot(busy.pid)
            b = process_snapshot(sleeping.pid)
            time.sleep(.6)
            cpu = interval(a, process_snapshot(busy.pid), os.sysconf("SC_CLK_TCK"))
            idle = interval(b, process_snapshot(sleeping.pid), os.sysconf("SC_CLK_TCK"))
            self.assertGreater(cpu["processCpuSeconds"], 0)
            self.assertGreater(cpu["processCpuSeconds"], idle["processCpuSeconds"])
        finally:
            for child in (busy, sleeping):
                child.terminate()
                child.wait(timeout=5)

    def test_short_lived_child_retains_failed_capture(self):
        child = subprocess.Popen([sys.executable, "-c", "import time;time.sleep(.4)"])
        try:
            report = collect(child.pid, 1, .25)
            self.assertFalse(report["complete"])
            self.assertIn("error", report)
            self.assertGreaterEqual(len(report["samples"]), 1)
        finally:
            child.wait(timeout=5)

    def test_cli_failure_and_no_overwrite(self):
        native = Path(__file__).with_name("profile_cycletrim_native.py")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            library = root / "dummy.so"
            library.write_bytes(b"not loaded by the fake fixture")
            import hashlib
            digest = hashlib.sha256(library.read_bytes()).hexdigest()
            # Exercise the actual profiler child runner, especially SystemExit.
            driver = root / "benchmark_subsystems.py"
            driver.write_text("raise SystemExit(7)\n")
            output = root / "capture"
            command = [sys.executable, str(native), "--harness-dir", str(root),
                       "--driver", driver.name, "--library", str(library), "--library-sha256", digest,
                       "--role", "candidate", "--scene", "unused", "--output", str(output)]
            # This fixture only exits; it never loads a native library or game.
            # Keep the production live-game gate while avoiding an unrelated
            # running game invalidating this no-game regression.
            with patch.object(sys, "argv", command[1:]), patch.object(native_profiler, "require_no_game"), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(native_profiler.main(), 1)
            receipt = json.loads((output / "profile.json").read_text())
            self.assertFalse(receipt["complete"])
            self.assertEqual(receipt["exitCode"], 7)
            original = (output / "profile.json").read_bytes()
            with patch.object(sys, "argv", command[1:]), patch.object(native_profiler, "require_no_game"), self.assertRaises(FileExistsError):
                native_profiler.main()
            self.assertEqual((output / "profile.json").read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
