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

    generation = None
    if "captureGeneration" in fields:
        try:
            generation = int(fields["captureGeneration"])
        except ValueError as error:
            raise CaptureError("captureGeneration must be an integer") from error
        if generation <= 0:
            raise CaptureError("captureGeneration must be positive")

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
    if generation is not None:
        result["captureGeneration"] = generation
    for field in ("avgDirty", "avgSeedBBox"):
        if field in fields:
            try:
                result[field] = float(fields[field])
            except ValueError as error:
                raise CaptureError(f"invalid {field} value: {fields[field]!r}") from error
    return result


def bucket_key(bucket: dict) -> tuple[str, str, str, str]:
    return (
        bucket["dirty"],
        bucket["rx"],
        bucket["ry"],
        bucket["density"],
    )


def subtract_capture(current: dict, baseline: dict) -> dict:
    current_generation = current.get("captureGeneration")
    baseline_generation = baseline.get("captureGeneration")
    if current_generation is None or baseline_generation is None:
        raise CaptureError(
            "controlled workload windows require captureGeneration metadata; "
            "regather the capture with the updated NavGrid probe"
        )
    if current_generation != baseline_generation:
        raise CaptureError(
            "NavGrid capture generation changed across the requested workload window"
        )

    calls = current["calls"] - baseline["calls"]
    empty = current["empty"] - baseline["empty"]
    if calls <= 0:
        raise CaptureError("controlled workload window has no fresh NavGrid calls")
    if empty < 0:
        raise CaptureError("capture empty-call count moved backwards within one generation")

    current_buckets = {bucket_key(bucket): bucket["count"] for bucket in current["buckets"]}
    baseline_buckets = {bucket_key(bucket): bucket["count"] for bucket in baseline["buckets"]}
    for key, baseline_count in baseline_buckets.items():
        if current_buckets.get(key, 0) < baseline_count:
            raise CaptureError(
                "capture bucket count moved backwards within one generation: " + str(key)
            )

    buckets = []
    for bucket in current["buckets"]:
        key = bucket_key(bucket)
        count = bucket["count"] - baseline_buckets.get(key, 0)
        if count <= 0:
            continue
        window_bucket = dict(bucket)
        window_bucket["count"] = count
        buckets.append(window_bucket)

    bucket_calls = sum(bucket["count"] for bucket in buckets)
    if bucket_calls != calls:
        raise CaptureError(
            "controlled workload bucket deltas sum to "
            f"{bucket_calls}, expected fresh calls={calls}"
        )

    return {
        "captureGeneration": current_generation,
        "baselineCalls": baseline["calls"],
        "cumulativeCalls": current["calls"],
        "calls": calls,
        "empty": empty,
        "nonzeroBuckets": len(buckets),
        "bucketCallTotal": bucket_calls,
        "buckets": buckets,
        "candidateGate": summarize_candidate_gate(buckets, calls),
        "windowed": True,
    }


def select_capture(capture_lines: list[str], after_calls: int | None) -> dict:
    if after_calls is None:
        return parse_capture_summary(capture_lines[-1])
    if after_calls <= 0:
        raise CaptureError("--after-calls must be a positive capture call count")

    captures = [parse_capture_summary(line) for line in capture_lines]
    latest = captures[-1]
    latest_generation = latest.get("captureGeneration")
    if latest_generation is None:
        raise CaptureError(
            "--after-calls requires captureGeneration metadata; "
            "regather the capture with the updated NavGrid probe"
        )

    baseline_indices = [
        index for index, capture in enumerate(captures[:-1])
        if capture.get("captureGeneration") == latest_generation
        and capture["calls"] == after_calls
    ]
    if not baseline_indices:
        raise CaptureError(
            f"baseline calls={after_calls} was not found in latest NavGrid capture "
            f"generation {latest_generation}"
        )

    baseline_index = baseline_indices[-1]
    fresh_captures = [
        capture for capture in captures[baseline_index + 1:]
        if capture.get("captureGeneration") == latest_generation
        and capture["calls"] > after_calls
    ]
    if not fresh_captures:
        raise CaptureError(
            f"no fresh NavGrid capture was emitted after baseline calls={after_calls}; "
            "the deferred UIScheduler report may not have flushed yet"
        )
    return subtract_capture(fresh_captures[-1], captures[baseline_index])


def analyze_log(text: str, after_calls: int | None = None) -> dict:
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

    result = select_capture(capture_lines, after_calls)
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
    parser.add_argument(
        "--after-calls",
        type=int,
        help=(
            "return the exact complete-histogram delta after an existing baseline calls value "
            "from the latest capture generation"
        ),
    )
    args = parser.parse_args()

    try:
        result = analyze_log(read_text(args.log), after_calls=args.after_calls)
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
