#!/usr/bin/env python3
"""Package allowlisted CycleTrim evidence without reference binaries or source."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


TEXT_SUFFIXES = {".json", ".log", ".md", ".py", ".patch", ".txt", ".rs", ".asm", ".s", ".yml"}
EVIDENCE_PREFIXES = (
    "native-chain-benchmark-", "native-chain-correctness-", "native-chain-smoke-",
    "fixture-v1-rejected", "identity-negative-source", "native-assembly-review",
    "worker-payload-", "rejected-candidate-d374", "ci-87e070c-raw", "ci-0056559-raw",
)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validation", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = args.validation.resolve()
    output = args.output.resolve()
    if output.exists():
        raise FileExistsError("Refusing to overwrite a previous evidence package: " + str(output))

    entries = {}
    for path in sorted(root.iterdir()):
        if path.is_file() and not path.is_symlink():
            if path.suffix in TEXT_SUFFIXES and "commit-input" not in path.name:
                entries["validation/" + path.name] = path.read_bytes()
        elif path.is_dir() and path.name.startswith(EVIDENCE_PREFIXES):
            for child in sorted(path.rglob("*")):
                if child.is_file() and not child.is_symlink() and child.suffix in TEXT_SUFFIXES:
                    entries["validation/" + child.relative_to(root).as_posix()] = child.read_bytes()
    entries["report/" + args.report.name] = args.report.read_bytes()

    # Frozen experiment inputs identify their own candidate binaries and sources.
    # Executables, native libraries, baseline checkouts, protected decompiled
    # implementation files and compile-only game DLLs never enter this archive.
    manifest = {
        "scope": "Raw and derived host/native evidence; not an ONI runtime capture",
        "excluded": ["native/test binaries", "baseline repository checkout",
                     "game/reference DLLs", "decompiled game implementation",
                     "GitHub mutation payloads", "credentials and transfer helpers"],
        "files": {name: {"bytes": len(data), "sha256": sha256(data)}
                  for name, data in sorted(entries.items())},
    }
    entries["manifest.json"] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(entries.items()):
            info = zipfile.ZipInfo(name, (2026, 11, 9, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    with zipfile.ZipFile(output) as archive:
        failure = archive.testzip()
        if failure is not None:
            raise RuntimeError("ZIP CRC validation failed: " + failure)
        for name, expected in manifest["files"].items():
            actual = archive.read(name)
            if len(actual) != expected["bytes"] or sha256(actual) != expected["sha256"]:
                raise RuntimeError("Evidence manifest validation failed: " + name)
    receipt = {"path": str(output), "bytes": output.stat().st_size,
               "sha256": sha256(output.read_bytes()), "entries": len(entries),
               "crc_and_manifest_verified": True}
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
