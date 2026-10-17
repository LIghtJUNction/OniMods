#!/usr/bin/env python3
"""Behavior contract for safe CycleTrim reference-API failure diagnostics."""

from __future__ import annotations

from pathlib import Path
import subprocess
import sys
import tempfile

import check_cycletrim_reference_api as reference_api


ROOT = Path(__file__).resolve().parents[1]
PREPARE = ROOT / "scripts/prepare_cycletrim_reference_api_diagnostics.py"
WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def verify_checker_does_not_echo_tool_stderr() -> None:
    class FailedTool:
        returncode = 1
        stdout = ""
        stderr = "TOP_SECRET_FROM_TOOL\npublic void LeakedBody() {}"

    original_run = reference_api.subprocess.run
    reference_api.subprocess.run = lambda *args, **kwargs: FailedTool()
    try:
        try:
            reference_api.decompile(Path("unused-reference.dll"), "Navigator")
        except RuntimeError as error:
            message = str(error)
            require("TOP_SECRET_FROM_TOOL" not in message, "ILSpy stderr leaked into CI diagnostics")
            require("LeakedBody" not in message, "tool output resembling source leaked into CI diagnostics")
        else:
            raise AssertionError("failed ILSpy invocation must fail closed")
    finally:
        reference_api.subprocess.run = original_run


def verify_allowlisted_report() -> None:
    require(PREPARE.is_file(), "reference-API failure artifact needs an allowlisting preparation step")
    raw = "\n".join(
        (
            "FAIL: CycleTrim pinned reference API contract drifted",
            "- Navigator.NavGrid: expected at least 1, found 0",
            "TOP_SECRET_FROM_TOOL",
            "public void LeakedBody() {}",
            "",
        )
    )
    with tempfile.TemporaryDirectory() as tmp:
        source = Path(tmp) / "raw.log"
        report = Path(tmp) / "report.txt"
        source.write_text(raw, encoding="utf-8")
        result = subprocess.run(
            [sys.executable, str(PREPARE), str(source), str(report)],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
        require(result.returncode == 0, f"diagnostic report preparation failed: {result.stderr}")
        text = report.read_text(encoding="utf-8")
        require("TOP_SECRET_FROM_TOOL" not in text, "allowlisted artifact copied arbitrary tool stderr")
        require("LeakedBody" not in text, "allowlisted artifact copied source-like text")
        require("status=contract-drift" in text, "sanitized report lost failure classification")
        require("contract_failure_count=1" in text, "sanitized report lost failure count")
        require("redacted_line_count=2" in text, "sanitized report did not account for rejected lines")


def verify_workflow_uploads_only_prepared_report() -> None:
    text = WORKFLOW.read_text(encoding="utf-8")
    require(
        "prepare_cycletrim_reference_api_diagnostics.py" in text,
        "reference CI must prepare an allowlisted failure report before upload",
    )
    require(
        "path: ${{ runner.temp }}/cycletrim-reference-api-diagnostics.txt" in text,
        "reference CI must upload the prepared failure report",
    )
    require(
        "path: ${{ runner.temp }}/cycletrim-reference-api.log" not in text,
        "reference CI must not upload the raw checker log",
    )


def main() -> int:
    failures = []
    for check in (
        verify_checker_does_not_echo_tool_stderr,
        verify_allowlisted_report,
        verify_workflow_uploads_only_prepared_report,
    ):
        try:
            check()
        except AssertionError as error:
            failures.append(str(error))

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("PASS: CycleTrim reference-API failure diagnostics are sanitized and allowlisted before upload")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
