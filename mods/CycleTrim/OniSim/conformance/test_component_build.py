"""Exercise the build policy used by the existing component CI job."""
import importlib.util
from pathlib import Path
import unittest

RUNNER = Path(__file__).resolve().parents[1] / "lean-lab/harness/run.py"
SPEC = importlib.util.spec_from_file_location("component_build_runner", RUNNER)
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class ComponentBuildTests(unittest.TestCase):
    def test_fresh_build_can_fetch_locked_dependencies(self):
        command = runner.cargo_build_command(False)
        self.assertEqual(command[:3], ["cargo", "build", "--release"])
        self.assertEqual(command.count("--locked"), 1)
        self.assertNotIn("--offline", command)
        self.assertEqual(command[command.index("--manifest-path") + 1], str(runner.MANIFEST))

    def test_explicit_offline_build_still_uses_the_lockfile(self):
        command = runner.cargo_build_command(True)
        self.assertEqual(command.count("--locked"), 1)
        self.assertEqual(command.count("--offline"), 1)


if __name__ == "__main__":
    unittest.main()
