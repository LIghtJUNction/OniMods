#!/usr/bin/env python3
"""Behavior checks for workflow trigger inspection and verifier isolation."""

from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from workflow_contracts import workflow_paths


class WorkflowContractTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.workflow = self.root / "workflow.yml"

    def write(self, text: str) -> None:
        self.workflow.write_text(text, encoding="utf-8")

    def test_each_event_has_its_own_ordered_paths(self) -> None:
        self.write(
            "on:\n"
            "  pull_request:\n"
            "    paths:\n"
            "      - 'scripts/**'\n"
            '      - "tests/**"\n'
            "      - Cargo.toml\n"
            "  push:\n"
            "    branches: [main]\n"
            "    paths:\n"
            "      - 'mods/**'\n"
            "      - '!mods/excluded/**'\n"
        )
        self.assertEqual(["scripts/**", "tests/**", "Cargo.toml"],
                         workflow_paths(self.workflow, "pull_request"))
        self.assertEqual(["mods/**", "!mods/excluded/**"],
                         workflow_paths(self.workflow, "push"))

    def test_blank_lines_do_not_end_a_path_list(self) -> None:
        self.write("on:\n  push:\n    paths:\n      - one\n\n      - two\n")
        self.assertEqual(["one", "two"], workflow_paths(self.workflow, "push"))

    def test_next_sibling_list_is_not_included(self) -> None:
        self.write(
            "on:\n  push:\n    paths:\n      - one\n"
            "    branches:\n      - main\n"
        )
        self.assertEqual(["one"], workflow_paths(self.workflow, "push"))

    def test_missing_event_fails_closed(self) -> None:
        self.write("on:\n  push:\n    paths:\n      - '**'\n")
        with self.assertRaisesRegex(AssertionError, "missing pull_request trigger"):
            workflow_paths(self.workflow, "pull_request")

    def test_another_events_paths_cannot_satisfy_missing_paths(self) -> None:
        self.write(
            "on:\n  pull_request:\n    branches: [main]\n"
            "  push:\n    paths:\n      - '**'\n"
        )
        with self.assertRaisesRegex(AssertionError, r"missing pull_request\.paths"):
            workflow_paths(self.workflow, "pull_request")

    def test_empty_path_list_stays_empty(self) -> None:
        self.write("on:\n  push:\n    paths:\n    branches:\n      - main\n")
        self.assertEqual([], workflow_paths(self.workflow, "push"))

    def test_missing_file_is_not_reported_as_covered(self) -> None:
        with self.assertRaises(OSError):
            workflow_paths(self.workflow, "push")

    def test_evidence_matrix_import_does_not_load_provenance_tests(self) -> None:
        source = Path(__file__).resolve().parent
        for name in (
            "workflow_contracts.py", "verify_ci_evidence_matrix.py",
            "run_cycletrim_synthetic_performance.py", "mod_test_plan.py",
        ):
            shutil.copyfile(source / name, self.root / name)
        result = subprocess.run(
            [sys.executable, "-c", "import verify_ci_evidence_matrix"],
            cwd=self.root, capture_output=True, text=True, check=False, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("", result.stdout)

    def test_compatibility_compiles_keep_per_project_sdk_paths(self) -> None:
        workflow = (Path(__file__).resolve().parent.parent
                    / ".github/workflows/oni-reference-compat.yml").read_text(
                        encoding="utf-8")
        self.assertNotIn("BaseIntermediateOutputPath", workflow)
        self.assertNotIn('compat_root="$RUNNER_TEMP/', workflow)
        self.assertEqual(
            1,
            workflow.count(
                "dotnet restore mods/CycleTrim/OniSim/managed/"
                "OniSim.Compatibility/OniSim.Compatibility.csproj"),
        )


if __name__ == "__main__":
    unittest.main()
