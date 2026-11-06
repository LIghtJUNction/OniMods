"""Read the block-style trigger lists used by the repository workflows."""

from pathlib import Path


def workflow_paths(workflow: Path, event_name: str) -> list[str]:
    lines = workflow.read_text(encoding="utf-8").splitlines()
    event_header = f"  {event_name}:"
    try:
        start = lines.index(event_header)
    except ValueError as error:
        raise AssertionError(f"missing {event_name} trigger in {workflow.name}") from error

    paths_start = None
    for index in range(start + 1, len(lines)):
        line = lines[index]
        if line.startswith("  ") and not line.startswith("    ") and line.strip():
            break
        if line == "    paths:":
            paths_start = index + 1
            break
    if paths_start is None:
        raise AssertionError(f"missing {event_name}.paths trigger list in {workflow.name}")

    paths = []
    for line in lines[paths_start:]:
        if not line.startswith("      - "):
            if line.strip():
                break
            continue
        value = line[len("      - "):].strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]
        paths.append(value)
    return paths
