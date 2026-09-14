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
ASSEMBLY_NAMES = ("Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll")


def digest_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def digest_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def target_types() -> list[str]:
    found: set[str] = set()
    for root in (ROOT / "mods" / "OniMcp", ROOT / "mods" / "CycleTrim"):
        for path in root.rglob("*.cs"):
            source = path.read_text(encoding="utf-8")
            found.update(match.group("type") for match in PATCH_RE.finditer(source))
    return sorted(found)


def type_candidates(type_name: str) -> list[str]:
    candidates = [type_name]
    if "." in type_name:
        parts = type_name.split(".")
        for split in range(len(parts) - 1, 0, -1):
            candidates.append(".".join(parts[:split]) + "+" + "+".join(parts[split:]))
    return candidates


def decompile_type(ilspycmd: str, assemblies: list[Path], type_name: str) -> tuple[Path, str, str]:
    errors = []
    for assembly in assemblies:
        for candidate in type_candidates(type_name):
            result = subprocess.run(
                [ilspycmd, "-t", candidate, str(assembly)],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
                timeout=60,
            )
            if result.returncode == 0 and result.stdout.strip():
                text = result.stdout.replace("\r\n", "\n").strip() + "\n"
                return assembly, candidate, text
            error = " ".join(result.stderr.split())
            errors.append(f"{assembly.name}:{candidate}: {error}")
    raise RuntimeError(
        f"could not decompile Harmony target {type_name}: " + "; ".join(errors)
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("managed_dir", help="Directory containing pinned ONI game reference assemblies")
    parser.add_argument("--output", default="artifacts/oni-api-surface")
    parser.add_argument("--ilspycmd", default="ilspycmd")
    args = parser.parse_args()

    managed_dir = Path(args.managed_dir)
    assemblies = [managed_dir / name for name in ASSEMBLY_NAMES]
    missing = [str(path) for path in assemblies if not path.is_file()]
    if missing:
        parser.error("required assembly not found: " + ", ".join(missing))

    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)

    entries = []
    for requested in target_types():
        assembly, resolved, text = decompile_type(args.ilspycmd, assemblies, requested)
        safe_name = assembly.stem + "__" + requested.replace(".", "_") + ".cs"
        destination = output / safe_name
        destination.write_text(text, encoding="utf-8")
        entries.append(
            {
                "requested_type": requested,
                "resolved_type": resolved,
                "assembly": assembly.name,
                "file": safe_name,
                "sha256": digest_text(text),
            }
        )
        print(
            f"SURFACE {requested} -> {assembly.name}:{resolved} "
            f"sha256={entries[-1]['sha256']}"
        )

    index = {
        "assemblies": [
            {"name": assembly.name, "sha256": digest_file(assembly)}
            for assembly in assemblies
        ],
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
