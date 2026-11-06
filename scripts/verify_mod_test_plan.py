#!/usr/bin/env python3
"""Behavior regressions for host-test discovery, execution and timing evidence."""

from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import check_mods
from mod_test_plan import build_probe_command, host_regression_checks
import run_cycletrim_synthetic_performance as reporter


class TestPlanTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix="onimods test plan ")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)

    def create(self, name: str, content: str = "") -> Path:
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def project(self, name: str) -> Path:
        return self.create(f"tests/{name}/{name}.csproj")

    def test_empty_checkout_has_no_host_checks(self) -> None:
        self.assertEqual([], host_regression_checks("dotnet", False, self.root))
        self.create("tests/OniOnline.Tests/test_multiplayer_runtime.py")
        self.assertEqual([], host_regression_checks("dotnet", False, self.root))

    def test_projects_are_sorted_and_nested_outputs_are_not_discovered(self) -> None:
        self.project("Z.Tests")
        self.project("A.Tests")
        self.create("tests/A.Tests/obj/Generated.csproj")
        self.create("tests/NotAProject.txt")
        checks = host_regression_checks("custom dotnet", False, self.root)
        self.assertEqual(["tests/A.Tests/A.Tests.csproj", "tests/Z.Tests/Z.Tests.csproj"],
                         [Path(name).as_posix() for name, _ in checks])
        for _, command in checks:
            self.assertEqual("custom dotnet", command[0])
            self.assertEqual("Release", command[command.index("--configuration") + 1])
            self.assertIn("-p:ImportDirectoryBuildProps=false", command)
            self.assertIn("-p:TreatWarningsAsErrors=true", command)
            self.assertNotIn("--", command)

    def test_only_performance_project_receives_host_only_argument(self) -> None:
        performance = self.project("CycleTrim.PerformanceProbe.Tests")
        ordinary = self.project("OniMcp.Core.Tests")
        for skip in (False, True):
            with self.subTest(skip=skip):
                checks = host_regression_checks("dotnet", skip, self.root)
                commands = {command[command.index("--project") + 1]: command for _, command in checks}
                self.assertNotIn("--skip-synthetic-performance", commands[str(ordinary)])
                self.assertEqual(skip, "--skip-synthetic-performance" in commands[str(performance)])
                if skip:
                    self.assertEqual(["--", "--skip-synthetic-performance"], commands[str(performance)][-2:])

    def test_online_entry_points_are_independent_and_ordered(self) -> None:
        self.project("OniMcp.Core.Tests")
        self.assertEqual(1, len(host_regression_checks("dotnet", False, self.root)))
        collector = self.create("tests/OniOnline.Tests/test_multiplayer_runtime.py")
        checks = host_regression_checks("dotnet", False, self.root)
        self.assertEqual(2, len(checks))
        self.assertEqual([sys.executable, str(collector), "-v"], checks[-1][1])
        adapter = self.project("OniOnline.Tests")
        checks = host_regression_checks("dotnet", True, self.root)
        self.assertEqual(4, len(checks))
        self.assertEqual("OniOnline native-origin adapter regressions", checks[-2][0])
        self.assertIn(str(adapter), checks[-2][1])
        self.assertEqual("-p:OriginAdapterTests=true", checks[-2][1][-1])
        self.assertNotIn("--skip-synthetic-performance", checks[-2][1])
        collector.unlink()
        self.assertEqual(3, len(host_regression_checks("dotnet", False, self.root)))

    def test_reporter_selects_only_synthetic_evidence(self) -> None:
        command = build_probe_command("custom dotnet", self.root)
        self.assertEqual("custom dotnet", command[0])
        self.assertIn(str(self.root / "tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj"), command)
        self.assertEqual(["--", "--synthetic-performance-only"], command[-2:])
        self.assertIn("-p:ImportDirectoryBuildProps=false", command)
        self.assertIn("-p:TreatWarningsAsErrors=true", command)
        command.clear()
        self.assertTrue(build_probe_command("custom dotnet", self.root))

    def run_cli(self, *arguments: str) -> subprocess.CompletedProcess:
        source = Path(__file__).resolve().parent
        for name in ("check_mods.py", "mod_test_plan.py"):
            target = self.root / "scripts" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source / name, target)
        return subprocess.run(
            [sys.executable, str(self.root / "scripts/check_mods.py"), *arguments],
            cwd=self.root, capture_output=True, text=True, check=False, timeout=30,
        )

    def test_static_mode_runs_python_without_sdk_and_skips_game_checks(self) -> None:
        self.create("scripts/verify_local.py", "import os\nassert os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] == '1'\nprint('local check executed')\n")
        for name in check_mods.GAME_CHECKS:
            self.create("scripts/" + name, "raise AssertionError('game-only check was executed')\n")
        result = self.run_cli("--static-only", "--dotnet", str(self.root / "missing-sdk"))
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("local check executed", result.stdout)
        self.assertIn("1/1 checks passed", result.stdout)
        self.assertIn("NOT RUN executable C# regressions", result.stdout)

    def test_real_child_failure_does_not_prevent_remaining_checks(self) -> None:
        self.create("scripts/verify_a.py", "raise SystemExit(7)\n")
        self.create("scripts/verify_z.py", "print('later check executed')\n")
        result = self.run_cli("--static-only")
        self.assertEqual(1, result.returncode)
        self.assertIn("later check executed", result.stdout)
        self.assertIn("1/2 checks passed", result.stdout)
        self.assertIn("FAIL verify_a.py", result.stderr)

    def test_missing_sdk_fails_before_any_check(self) -> None:
        self.create("scripts/verify_local.py", "print('must not run')\n")
        result = self.run_cli("--dotnet", str(self.root / "missing-sdk"))
        self.assertEqual(2, result.returncode)
        self.assertIn("SDK was not found", result.stderr)
        self.assertNotIn("must not run", result.stdout)

    def test_empty_host_discovery_is_an_error_not_a_pass(self) -> None:
        result = self.run_cli("--dotnet", sys.executable)
        self.assertEqual(2, result.returncode)
        self.assertIn("no Mod regression projects found", result.stderr)

    def test_process_errors_fail_and_continue(self) -> None:
        self.create("scripts/verify_a.py")
        self.create("scripts/verify_z.py")
        for error in (OSError("unavailable"), subprocess.TimeoutExpired("test", 300)):
            with self.subTest(error=type(error).__name__):
                output, errors = StringIO(), StringIO()
                with patch.object(check_mods, "ROOT", self.root), \
                     patch.object(sys, "argv", ["check_mods.py", "--static-only"]), \
                     patch.object(check_mods.subprocess, "run", side_effect=[error, subprocess.CompletedProcess([], 0)]) as run, \
                     redirect_stdout(output), redirect_stderr(errors):
                    result = check_mods.main()
                self.assertEqual(1, result)
                self.assertEqual(2, run.call_count)
                self.assertEqual(300, run.call_args.kwargs["timeout"])
                self.assertIn("1/2 checks passed", output.getvalue())
                self.assertIn("FAIL verify_a.py", errors.getvalue())


class SyntheticReporterTests(unittest.TestCase):
    def test_only_matching_exit_code_and_stderr_marker_are_advisory(self) -> None:
        marker = "ONIMODS_SYNTHETIC_TIMING_REGRESSION: threshold"
        cases = (
            (0, "", 0), (2, marker, 0), (2, "before\r\n" + marker + "\r\n", 0),
            (2, "MSBUILD tool failure", 2), (2, "prefix " + marker, 2),
            (1, marker, 1), (137, "", 137), (-9, marker, -9),
        )
        for code, stderr, expected in cases:
            with self.subTest(code=code, stderr=stderr):
                self.assertEqual(expected, reporter.classify_probe_exit_code(code, stderr))

    def test_actual_child_output_and_exit_are_preserved(self) -> None:
        marker = "ONIMODS_SYNTHETIC_TIMING_REGRESSION: threshold"
        cases = ((0, "ok", 0), (2, marker, 0), (2, "MSBUILD failure", 2), (1, marker, 1))
        for code, stderr, expected in cases:
            with self.subTest(code=code, stderr=stderr):
                child = [sys.executable, "-c", f"import sys; print('probe output'); print({stderr!r}, file=sys.stderr); sys.exit({code})"]
                output, errors = StringIO(), StringIO()
                with patch.object(reporter, "build_probe_command", return_value=child), \
                     redirect_stdout(output), redirect_stderr(errors):
                    result = reporter.main()
                self.assertEqual(expected, result)
                self.assertIn("probe output", output.getvalue())
                self.assertIn(stderr, errors.getvalue())
                self.assertEqual(code == 2 and expected == 0, "ADVISORY:" in errors.getvalue())

    def test_marker_on_stdout_does_not_hide_tool_failure(self) -> None:
        child = [sys.executable, "-c", "import sys; print('ONIMODS_SYNTHETIC_TIMING_REGRESSION: threshold'); sys.exit(2)"]
        with patch.object(reporter, "build_probe_command", return_value=child), \
             redirect_stdout(StringIO()), redirect_stderr(StringIO()):
            self.assertEqual(2, reporter.main())


if __name__ == "__main__":
    unittest.main()
