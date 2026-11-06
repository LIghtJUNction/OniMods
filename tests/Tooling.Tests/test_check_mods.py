"""Exercise the public runner in an isolated repository with executable checks."""

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


RUNNER = Path(__file__).resolve().parents[2] / "scripts/check_mods.py"


class CheckModsTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.write("scripts/check_mods.py", RUNNER.read_text(encoding="utf-8"))
        self.write("scripts/verify_sample.py", "print('sample source contract')\n")

    def write(self, relative, content):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def run_runner(self, *arguments, environment=None):
        return subprocess.run(
            [sys.executable, str(self.root / "scripts/check_mods.py"), *arguments],
            cwd=self.root,
            env=environment,
            capture_output=True,
            text=True,
            check=False,
            timeout=20,
        )

    def record_check(self, relative, event, *, failure=False):
        self.write(
            relative,
            "from pathlib import Path\n"
            f"with Path('events.txt').open('a') as output: output.write({event!r} + '\\n')\n"
            f"raise SystemExit({int(failure)})\n",
        )

    def add_unittest(self, relative, event):
        self.write(
            relative,
            "from pathlib import Path\nimport unittest\n"
            "class FixtureTest(unittest.TestCase):\n"
            "    def test_behavior(self):\n"
            f"        with Path('events.txt').open('a') as output: output.write({event!r} + '\\n')\n"
            "if __name__ == '__main__': unittest.main()\n",
        )

    def fake_dotnet(self):
        self.write(
            "run",
            "import json, os, sys\nfrom pathlib import Path\n"
            "with Path('dotnet_calls.jsonl').open('a') as output:\n"
            "    output.write(json.dumps(dict(arguments=sys.argv[1:], telemetry=os.environ.get('DOTNET_CLI_TELEMETRY_OPTOUT'))) + '\\n')\n"
            "project = Path(sys.argv[sys.argv.index('--project') + 1])\n"
            "raise SystemExit(int(project.read_text()))\n",
        )
        return sys.executable

    def test_python_discovery_runs_new_suites_once_without_dotnet(self):
        self.record_check("scripts/verify_z.py", "verify")
        self.record_check("scripts/test_extra.py", "script-test")
        self.add_unittest("tests/New.Tests/test_first.py", "first")
        self.add_unittest("tests/New.Tests/test_second.py", "second")
        self.add_unittest("tests/Other.Tests/test_example.py", "other")
        result = self.run_runner("--static-only", "--dotnet", "/missing-sdk")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            (self.root / "events.txt").read_text().splitlines(),
            ["verify", "script-test", "first", "second", "other"],
        )
        self.assertEqual(result.stdout.count("RUN tests/New.Tests\n"), 1)
        self.assertIn("NOT RUN executable C# regressions", result.stdout)

    def test_game_dependent_checks_are_visible_and_never_started(self):
        for name in ("verify_restart_packaging.py", "test_legacy_workshop_package.py"):
            self.record_check("scripts/" + name, "game-check-must-not-run", failure=True)
        result = self.run_runner("--static-only")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.root / "events.txt").exists())
        self.assertIn("NOT RUN test_legacy_workshop_package.py", result.stdout)
        self.assertIn("NOT RUN verify_restart_packaging.py", result.stdout)

    def test_python_failure_is_reported_after_other_checks_finish(self):
        self.record_check("scripts/verify_a_failure.py", "failed", failure=True)
        self.record_check("scripts/verify_z_after.py", "continued")
        result = self.run_runner("--static-only")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual((self.root / "events.txt").read_text().splitlines(), ["failed", "continued"])
        self.assertIn("FAIL scripts/verify_a_failure.py", result.stderr)

    def test_host_projects_keep_their_own_execution_modes(self):
        self.write("tests/Example.Tests/Example.Tests.csproj", "0")
        self.write("tests/OniOnline.Tests/OniOnline.Tests.csproj", "0")
        self.write("tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj", "0")
        result = self.run_runner("--dotnet", self.fake_dotnet(), "--skip-synthetic-performance")
        self.assertEqual(result.returncode, 0, result.stderr)
        calls = [json.loads(line) for line in (self.root / "dotnet_calls.jsonl").read_text().splitlines()]
        self.assertEqual(len(calls), 4)
        self.assertTrue(all(call["telemetry"] == "1" for call in calls))
        performance = [call for call in calls if "CycleTrim.PerformanceProbe.Tests.csproj" in " ".join(call["arguments"])]
        self.assertEqual(len(performance), 1)
        self.assertEqual(performance[0]["arguments"][-2:], ["--", "--skip-synthetic-performance"])
        other_calls = [call for call in calls if call not in performance]
        self.assertTrue(all("--skip-synthetic-performance" not in call["arguments"] for call in other_calls))
        self.assertEqual(sum("-p:OriginAdapterTests=true" in call["arguments"] for call in calls), 1)

    def test_host_failure_cannot_be_reported_as_success(self):
        self.write("tests/Fail.Tests/Fail.Tests.csproj", "7")
        result = self.run_runner("--dotnet", self.fake_dotnet())
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn("FAIL tests/Fail.Tests/Fail.Tests.csproj", result.stderr)

    def test_missing_sdk_fails_before_running_checks(self):
        result = self.run_runner("--dotnet", str(self.root / "missing-sdk"))
        self.assertEqual(result.returncode, 2)
        self.assertIn(".NET 10 SDK was not found", result.stderr)
        self.assertNotIn("checks passed", result.stdout)

    def test_missing_projects_cannot_be_a_successful_host_run(self):
        result = self.run_runner("--dotnet", self.fake_dotnet())
        self.assertEqual(result.returncode, 2)
        self.assertIn("no Mod regression projects found", result.stderr)

    def test_empty_python_discovery_is_an_error(self):
        (self.root / "scripts/verify_sample.py").unlink()
        result = self.run_runner("--static-only")
        self.assertEqual(result.returncode, 2)
        self.assertIn("no Python regression checks found", result.stderr)

    @unittest.skipUnless(sys.platform == "linux", "Linux conformance prerequisite")
    def test_missing_conformance_prerequisites_are_explicit_failures(self):
        self.add_unittest("mods/CycleTrim/OniSim/conformance/test_fixture.py", "conformance")
        result = self.run_runner("--static-only")
        self.assertEqual(result.returncode, 2)
        self.assertIn("git submodule update --init tools/OniHarness", result.stderr)
        self.write("tools/OniHarness/scripts/instance_controller.py", "")
        result = self.run_runner("--static-only", environment=dict(os.environ, PATH=""))
        self.assertEqual(result.returncode, 2)
        self.assertIn("C compiler named cc", result.stderr)
        self.assertFalse((self.root / "events.txt").exists())


if __name__ == "__main__":
    unittest.main()
