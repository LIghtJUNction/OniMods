#!/usr/bin/env python3
"""Inventory official ONI ABI evidence without loading or executing game code."""

import argparse
import hashlib
import json
import math
import struct
import subprocess
from pathlib import Path


PROFILE = {
    "nativeSha256": "85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d",
    "managedSha256": "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1",
}
EXPORTS = (
    "SIM_Initialize", "SIM_Shutdown", "SIM_HandleMessage", "SIM_HandleMessages",
    "SIM_BeginSave", "SIM_EndSave", "SIM_DebugCrash",
    "SYSINFO_Acquire", "SYSINFO_Release",
    "ConduitTemperatureManager_Initialize", "ConduitTemperatureManager_Shutdown",
    "ConduitTemperatureManager_Add", "ConduitTemperatureManager_Set",
    "ConduitTemperatureManager_Remove", "ConduitTemperatureManager_Clear",
    "ConduitTemperatureManager_Update",
)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def inventory(game_dir):
    data = game_dir / "OxygenNotIncluded_Data"
    native = data / "Plugins" / "libSimDLL.so"
    managed = data / "Managed" / "Assembly-CSharp.dll"
    for path in (native, managed):
        if not path.is_file():
            raise ValueError("Required official binary missing: " + path.name)
    symbols = subprocess.check_output(
        ["nm", "-D", "--defined-only", str(native)], text=True)
    exported = {line.split()[-1] for line in symbols.splitlines() if line.split()}
    observed = {"nativeSha256": digest(native), "managedSha256": digest(managed)}
    return {
        "evidenceKind": "static-binary-inventory",
        "nativeFileType": subprocess.check_output(
            ["file", "-b", str(native)], text=True).strip(),
        **observed,
        "matchesInspectedProfile": observed == PROFILE,
        "exports": {name: name in exported for name in EXPORTS},
        "runtimeExecuted": False,
        "gameCompatibilityEstablished": False,
    }


def elf_constants(path, addresses):
    """Map ELF virtual addresses through section headers, never seek(vaddr)."""
    data = path.read_bytes()
    if len(data) < 64 or data[:6] != b"\x7fELF\x02\x01":
        raise ValueError("Constant inspection requires a little-endian ELF64 file")
    table = struct.unpack_from("<Q", data, 40)[0]
    entry_size, count = struct.unpack_from("<HH", data, 58)
    if entry_size < 64 or not count or table + entry_size * count > len(data):
        raise ValueError("Unsupported or truncated ELF section table")
    sections = []
    for index in range(count):
        _, kind, flags, address, offset, size, *_ = struct.unpack_from(
            "<IIQQQQIIQQ", data, table + index * entry_size)
        if kind != 8 and flags & 2:  # Allocated, file-backed; exclude SHT_NOBITS.
            if offset + size > len(data):
                raise ValueError("ELF section extends past the file")
            sections.append((address, offset, size))
    results = []
    for address in addresses:
        matches = [offset + address - start for start, offset, size in sections
                   if start <= address and address + 4 <= start + size]
        if len(matches) != 1:
            raise ValueError("No unique file-backed ELF mapping for " + hex(address))
        offset = matches[0]
        raw = data[offset:offset + 4]
        number = struct.unpack("<f", raw)[0]
        results.append({
            "virtualAddress": hex(address), "fileOffset": hex(offset),
            "bytesLittleEndian": raw.hex(), "u32": struct.unpack("<I", raw)[0],
            "f32": number if math.isfinite(number) else str(number),
        })
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--game-dir", type=Path)
    source.add_argument("--native-file", type=Path)
    parser.add_argument("--constant", action="append", type=lambda value: int(value, 0),
                        default=[], help="ELF virtual address of a 4-byte constant")
    args = parser.parse_args()
    try:
        if args.game_dir:
            game_dir = args.game_dir.resolve()
            result = inventory(game_dir)
            native = game_dir / "OxygenNotIncluded_Data/Plugins/libSimDLL.so"
        else:
            if not args.constant:
                parser.error("--native-file requires at least one --constant")
            native = args.native_file.resolve()
            result = {"evidenceKind": "static-elf-constants", "nativeSha256": digest(native),
                      "runtimeExecuted": False, "gameCompatibilityEstablished": False}
        if args.constant:
            result["constants"] = elf_constants(native, args.constant)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        parser.exit(1, "Official ABI inventory failed: " + str(exc) + "\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
