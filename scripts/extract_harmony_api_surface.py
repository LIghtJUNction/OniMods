#!/usr/bin/env python3
"""Extract stable decompiled surfaces for Harmony target types without loading game DLLs."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH_RE = re.compile(r"HarmonyPatch\s*\(\s*typeof\((?P<type>[A-Za-z_][A-Za-z0-9_.]*)\)")


def digest_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def target_types() -> list[str]:
    found: set[str] = set()
    for root in (ROOT / "mods" / "OniMcp", ROOT / "mods" / "CycleTrim"):
        for path in root.rglob("*.cs"):
            source = path.read_text(encoding="utf-8")
            found.update(match.group("type") for match in PATCH_RE.finditer(source))
    return sorted(found)


def decompile_type(ilspycmd: str, assembly: Path, type_name: str) -> tuple[str, str]:
    candidates = [type_name]
    if "." in type_name:
        parts = type_name.split(".")
        for split in range(len(parts) - 1, 0, -1):
            candidates.append(".".join(parts[:split]) + "+" + "+".join(parts[split:]))
    errors = []
    for candidate in candidates:
        result = subprocess.run(
            [ilspycmd, "-t", candidate, str(assembly)],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
            timeout=60,
        )
        if result.returncode == 0 and result.stdout.strip():
            return candidate, result.stdout.replace("\r\n", "\n").strip() + "\n"
        errors.append((candidate, result.stderr.strip()))
    detail = "; ".join(f"{candidate}: {error}" for candidate, error in errors)
    raise RuntimeError(f"could not decompile Harmony target {type_name}: {detail}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assembly", help="Path to the pinned Assembly-CSharp.dll reference assembly")
    parser.add_argument("--output", default="artifacts/oni-api-surface")
    parser.add_argument("--ilspycmd", default="ilspycmd")
    args = parser.parse_args()

    assembly = Path(args.assembly)
    if not assembly.is_file():
        parser.error(f"assembly not found: {assembly}")
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)

    entries = []
    for requested in target_types():
        resolved, text = decompile_type(args.ilspycmd, assembly, requested)
        safe_name = requested.replace(".", "_") + ".cs"
        destination = output / safe_name
        destination.write_text(text, encoding="utf-8")
        entries.append(
            {
                "requested_type": requested,
                "resolved_type": resolved,
                "file": safe_name,
                "sha256": digest_text(text),
            }
        )
        print(f"SURFACE {requested} -> {resolved} sha256={entries[-1]['sha256']}")

    index = {
        "assembly": assembly.name,
        "assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        "target_count": len(entries),
        "targets": entries,
    }
    (output / "index.json").write_text(
        json.dumps(index, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Wrote {len(entries)} Harmony target surfaces to {output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
