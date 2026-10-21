#!/usr/bin/env python3
"""Build one native OniSim target, or verify the complete CI release bundle."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]
CORE = ROOT / "mods/CycleTrim/OniSim"
TARGETS = {
    "linux-x64": ("x86_64-unknown-linux-gnu", "liboni_sim_core.so"),
    "windows-x64": ("x86_64-pc-windows-msvc", "oni_sim_core.dll"),
    "macos-x64": ("x86_64-apple-darwin", "liboni_sim_core.dylib"),
}


def source_digest():
    digest = hashlib.sha256()
    paths = [CORE / "rust-toolchain.toml", CORE / "native/Cargo.toml", CORE / "native/Cargo.lock"]
    paths.extend((CORE / "native/src").rglob("*.rs"))
    for path in sorted(paths):
        digest.update(path.relative_to(CORE).as_posix().encode() + b"\0")
        digest.update(path.read_bytes().replace(b"\r\n", b"\n") + b"\0")
    return digest.hexdigest()


def validate_binary(path, target):
    data = path.read_bytes()
    if target == "linux-x64":
        valid = data[:6] == b"\x7fELF\x02\x01" and len(data) >= 20 and struct.unpack_from("<H", data, 18)[0] == 62
    elif target == "macos-x64":
        valid = data[:4] == b"\xcf\xfa\xed\xfe" and len(data) >= 8 and struct.unpack_from("<I", data, 4)[0] == 0x1000007
    else:
        valid = data[:2] == b"MZ" and len(data) >= 64
        if valid:
            offset = struct.unpack_from("<I", data, 60)[0]
            valid = offset + 6 <= len(data) and data[offset:offset + 6] == b"PE\0\0\x64\x86"
    if not valid:
        raise ValueError(f"Wrong native binary format or architecture: {path}")
    return hashlib.sha256(data).hexdigest()


def validate_bundle(directory):
    expected_source = source_digest()
    expected_files = {f"{target}/{name}" for target, (_, library) in TARGETS.items()
                      for name in (library, "build.json")}
    actual_files = set()
    for path in directory.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"Symlinks are not allowed in a native bundle: {path}")
        if path.is_file():
            actual_files.add(path.relative_to(directory).as_posix())
    if actual_files != expected_files:
        raise ValueError("Native bundle must contain exactly three libraries and their build receipts")
    for target, (_, library) in TARGETS.items():
        path = directory / target / library
        receipt = json.loads((directory / target / "build.json").read_text())
        if receipt.get("target") != target or receipt.get("sourceSha256") != expected_source:
            raise ValueError(f"Native receipt does not match current source: {target}")
        if receipt.get("binarySha256") != validate_binary(path, target):
            raise ValueError(f"Native binary changed after CI build: {target}")
        print(f"verified {target}: {receipt['binarySha256']}")


def build(directory):
    system = platform.system()
    if platform.machine().lower() not in ("x86_64", "amd64"):
        raise ValueError("Build on an x64 CI runner; ARM game processes are unsupported")
    target = {"Linux": "linux-x64", "Windows": "windows-x64", "Darwin": "macos-x64"}[system]
    triple, library = TARGETS[target]
    cargo_output = CORE / "native/target/platform-build"
    env = dict(os.environ, RUSTC_WRAPPER="", RUSTC_WORKSPACE_WRAPPER="")
    subprocess.run(["cargo", "build", "--release", "--locked", "--target", triple,
                    "--target-dir", str(cargo_output), "--manifest-path", str(CORE / "native/Cargo.toml")],
                   cwd=CORE, env=env, check=True)
    destination = directory / target
    destination.mkdir(parents=True, exist_ok=True)
    artifact = destination / library
    shutil.copy2(cargo_output / triple / "release" / library, artifact)
    revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True,
                              capture_output=True, check=False)
    receipt = {"schema": 1, "target": target, "triple": triple,
               "sourceSha256": source_digest(), "binarySha256": validate_binary(artifact, target),
               "commit": revision.stdout.strip() if revision.returncode == 0 else "source-archive",
               "gameRuntimeTested": False}
    (destination / "build.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(artifact)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--output-root", type=Path)
    mode.add_argument("--validate-bundle", type=Path)
    args = parser.parse_args()
    if args.validate_bundle:
        validate_bundle(args.validate_bundle.resolve())
    else:
        build(args.output_root.resolve())


if __name__ == "__main__":
    main()
