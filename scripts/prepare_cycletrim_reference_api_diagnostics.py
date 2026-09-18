#!/usr/bin/env python3
"""Create a fixed-schema, allowlisted report from reference-API checker output."""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys


CONTRACT_HEADER = "FAIL: CycleTrim pinned reference API contract drifted"
CONTRACT_FAILURE_RE = re.compile(r"^- .+: expected at least [0-9]+, found [0-9]+$")
TOOL_FAILURE_RE = re.compile(
    r"^FAIL: could not inspect metadata for [A-Za-z0-9_.+`]+ \(ilspy exit [0-9]+\)$"
)
REPORT_RE = re.compile(
    r"\Aschema=1\n"
    r"kind=cycletrim-reference-api-diagnostics\n"
    r"status=(?:contract-drift|tool-failure|unknown-failure|pass)\n"
    r"contract_failure_count=[0-9]+\n"
    r"tool_failure_count=[0-9]+\n"
    r"redacted_line_count=[0-9]+\n\Z"
)


def summarize(raw: str) -> str:
    contract_header = False
    contract_failures = 0
    tool_failures = 0
    redacted = 0
    saw_pass = False

    for line in raw.splitlines():
        if not line:
            continue
        if line == CONTRACT_HEADER:
            contract_header = True
        elif CONTRACT_FAILURE_RE.fullmatch(line):
            contract_failures += 1
        elif TOOL_FAILURE_RE.fullmatch(line):
            tool_failures += 1
        elif line.startswith("PASS CycleTrim pinned reference API contract"):
            saw_pass = True
        else:
            redacted += 1

    if contract_header or contract_failures:
        status = "contract-drift"
    elif tool_failures:
        status = "tool-failure"
    elif saw_pass and redacted == 0:
        status = "pass"
    else:
        status = "unknown-failure"

    report = (
        "schema=1\n"
        "kind=cycletrim-reference-api-diagnostics\n"
        f"status={status}\n"
        f"contract_failure_count={contract_failures}\n"
        f"tool_failure_count={tool_failures}\n"
        f"redacted_line_count={redacted}\n"
    )
    if REPORT_RE.fullmatch(report) is None:
        raise ValueError("generated reference-API diagnostic report is outside the allowlist")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", help="raw transient checker log")
    parser.add_argument("output", help="allowlisted report path")
    args = parser.parse_args()

    source = Path(args.input)
    destination = Path(args.output)
    raw = source.read_text(encoding="utf-8", errors="replace")
    report = summarize(raw)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(report, encoding="utf-8")
    print(f"Prepared allowlisted reference-API diagnostic report: {destination.name}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1)
