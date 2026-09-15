#!/usr/bin/env python3
"""Validate and summarize a complete CycleTrim NavGrid workload probe capture."""

import argparse
import json
from pathlib import Path
import re
import sys


RESOLVED_MARKER = "[CycleTrim][NavGridProbe] requested; NavGrid.UpdateGraph() resolved."
TARGET_MARKER = "[CycleTrim][NavGridProbe] target reached; aggregate sampling started."
FASTTRACK_MARKER = (
    "[CycleTrim][NavGridProbe] target reached but sampling is disabled: "
    "FastTrack's NavGrid.UpdateGraph replacement is active."
)
CAPTURE_PREFIX = "[CycleTrim][NavGridProbeCapture] "
BUCKET_RE = re.compile(
    r"dirty=(?P<dirty>[^,]+),rx=(?P<rx>[^,]+),ry=(?P<ry>[^,]+),"
    r"density=(?P<density>[^:]+):(?P<count>[0-9]+)"
)

# Keep these in sync with NavGridAdaptiveGateBenchmark. The probe histogram is
# intentionally coarser than this candidate gate, so the analyzer reports a
# lower/upper bound instead of pretending every captured bucket maps exactly.
CANDIDATE_MIN_DIRTY_CELLS = 20
CANDIDATE_MIN_SHORT_RANGE = 2
CANDIDATE_MIN_LONG_RANGE = 4


class CaptureError(ValueError):
    pass


def parse_bucket_interval(label: str) -> tuple[int, int | None]:
    if label.endswith("+"):
        try:
            return int(label[:-1]), None
        except ValueError as error:
            raise CaptureError(f"invalid open-ended bucket label: {label!r}") from error

    if "-" in label:
        lower_text, separator, upper_text = label.partition("-")
        if not separator:
            raise CaptureError(f"invalid bucket label: {label!r}")
        try:
            lower = int(lower_text)
            upper = int(upper_text)
        except ValueError as error:
            raise CaptureError(f"invalid bucket label: {label!r}") from error
        if lower > upper:
            raise CaptureError(f"invalid descending bucket label: {label!r}")
        return lower, upper

    try:
        value = int(label)
    except ValueError as error:
        raise CaptureError(f"invalid bucket label: {label!r}") from error
    return value, value


def candidate_gate_matches(dirty: int, range_x: int, range_y: int) -> bool:
    short_range = min(range_x, range_y)
    long_range = max(range_x, range_y)
    return (
        dirty >= CANDIDATE_MIN_DIRTY_CELLS
        and short_range >= CANDIDATE_MIN_SHORT_RANGE
        and long_range >= CANDIDATE_MIN_LONG_RANGE
    )


def upper_endpoint(interval: tuple[int, int | None]) -> int:
    lower, upper = interval
    return upper if upper is not None else max(
        lower,
        CANDIDATE_MIN_DIRTY_CELLS,
        CANDIDATE_MIN_LONG_RANGE,
    )


def summarize_candidate_gate(buckets: list[dict], calls: int) -> dict:
    lower_bound = 0
    upper_bound = 0
    for bucket in buckets:
        dirty_interval = parse_bucket_interval(bucket["dirty"])
        range_x_interval = parse_bucket_interval(bucket["rx"])
        range_y_interval = parse_bucket_interval(bucket["ry"])
        count = bucket["count"]

        guaranteed = candidate_gate_matches(
            dirty_interval[0],
            range_x_interval[0],
            range_y_interval[0],
        )
        possible = candidate_gate_matches(
            upper_endpoint(dirty_interval),
            upper_endpoint(range_x_interval),
            upper_endpoint(range_y_interval),
        )
        if guaranteed:
            lower_bound += count
        if possible:
            upper_bound += count

    ambiguous = upper_bound - lower_bound
    return {
        "minDirtyCells": CANDIDATE_MIN_DIRTY_CELLS,
        "minShortRange": CANDIDATE_MIN_SHORT_RANGE,
        "minLongRange": CANDIDATE_MIN_LONG_RANGE,
        "eligibleCallsLowerBound": lower_bound,
        "eligibleCallsUpperBound": upper_bound,
        "ambiguousCalls": ambiguous,
        "eligibleFractionLowerBound": lower_bound / calls,
        "eligibleFractionUpperBound": upper_bound / calls,
    }


def parse_capture_summary(summary: str) -> dict:
    header, separator, bucket_text = summary.partition(", top=[")
    if not separator or not bucket_text.endswith("]"):
        raise CaptureError("capture summary is missing the complete top=[...] payload")

    fields = {}
    for part in header.split(", "):
        key, separator, value = part.partition("=")
        if not separator or not key:
            raise CaptureError(f"invalid summary field: {part!r}")
        fields[key] = value

    try:
        calls = int(fields["calls"])
        empty = int(fields["empty"])
        nonzero_buckets = int(fields["nonzeroBuckets"])
    except (KeyError, ValueError) as error:
        raise CaptureError("capture summary is missing integer calls/empty/nonzeroBuckets") from error

    buckets = []
    payload = bucket_text[:-1]
    if payload:
        for item in payload.split("; "):
            match = BUCKET_RE.fullmatch(item)
            if match is None:
                raise CaptureError(f"invalid bucket payload: {item!r}")
            bucket = match.groupdict()
            bucket["count"] = int(bucket["count"])
            buckets.append(bucket)

    if calls <= 0:
        raise CaptureError("capture has zero calls; the NavGrid target was not exercised")
    if len(buckets) != nonzero_buckets:
        raise CaptureError(
            "capture is truncated: parsed "
            f"{len(buckets)} buckets but summary reports {nonzero_buckets} nonzero buckets"
        )

    bucket_calls = sum(bucket["count"] for bucket in buckets)
    if bucket_calls != calls:
        raise CaptureError(
            "capture is incomplete: bucket counts sum to "
            f"{bucket_calls}, expected calls={calls}"
        )

    result = {
        "calls": calls,
        "empty": empty,
        "nonzeroBuckets": nonzero_buckets,
        "bucketCallTotal": bucket_calls,
        "buckets": buckets,
        "candidateGate": summarize_candidate_gate(buckets, calls),
    }
    for field in ("avgDirty", "avgSeedBBox"):
        if field in fields:
            try:
                result[field] = float(fields[field])
            except ValueError as error:
                raise CaptureError(f"invalid {field} value: {fields[field]!r}") from error
    return result


def analyze_log(text: str) -> dict:
    resolved_at = text.rfind(RESOLVED_MARKER)
    if resolved_at < 0:
        raise CaptureError("missing NavGrid.UpdateGraph() resolved marker")

    current_run = text[resolved_at:]
    if FASTTRACK_MARKER in current_run:
        raise CaptureError("FastTrack replacement was active; baseline sampling was disabled")
    if TARGET_MARKER not in current_run:
        raise CaptureError("missing target reached marker; the probe did not prove runtime reachability")

    capture_lines = [
        line.split(CAPTURE_PREFIX, 1)[1].strip()
        for line in current_run.splitlines()
        if CAPTURE_PREFIX in line
    ]
    if not capture_lines:
        raise CaptureError(
            "no complete capture line found; start ONI with "
            "CYCLETRIM_NAVGRID_PROBE_CAPTURE=1 in addition to CYCLETRIM_NAVGRID_PROBE=1"
        )

    result = parse_capture_summary(capture_lines[-1])
    result["captureLines"] = len(capture_lines)
    return result


def read_text(path: str) -> str:
    if path == "-":
        return sys.stdin.read()
    return Path(path).read_text(encoding="utf-8", errors="replace")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", help="ONI Player.log path, or - for stdin")
    parser.add_argument("--pretty", action="store_true", help="pretty-print JSON output")
    args = parser.parse_args()

    try:
        result = analyze_log(read_text(args.log))
    except (OSError, CaptureError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    json.dump(
        result,
        sys.stdout,
        indent=2 if args.pretty else None,
        sort_keys=True,
        separators=None if args.pretty else (",", ":"),
    )
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
