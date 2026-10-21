#!/usr/bin/env python3
"""Build the opt-in probe and bind its DLL to the exact source/reference files."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent
SOURCES = ("MemoryProbe.cs", "MemoryRequest.cs", "MemoryRequestTests.cs", "MemoryProbe.csproj", "mod.yaml", "mod_info.yaml", "build.py")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_receipt(path):
    receipt = json.loads(Path(path).read_text())
    if receipt["build_exit_code"] != 0:
        raise RuntimeError("Memory probe build did not succeed")
    if receipt["source_sha256"] != {name: sha(ROOT / name) for name in SOURCES}:
        raise RuntimeError("Memory probe source changed after its successful build")
    binary = Path(receipt["output_dll"])
    if not binary.is_file() or sha(binary) != receipt["probe_sha256"]:
        raise RuntimeError("Memory probe DLL differs from its successful build")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--managed-path", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=ROOT.parent / "artifacts/memory-probe-build")
    args = parser.parse_args()
    managed = args.managed_path.resolve()
    output = args.output.resolve()
    if not (managed / "Assembly-CSharp.dll").is_file():
        parser.error("Supply the installed game's Managed directory")
    output.mkdir(parents=True, exist_ok=True)
    receipt = output / "receipt.json"
    receipt.unlink(missing_ok=True)
    binary = output / "bin/CycleTrim.MemoryProbe.dll"
    binary.unlink(missing_ok=True)
    sources = {name: sha(ROOT / name) for name in SOURCES}
    command = ["dotnet", "build", str(ROOT / "MemoryProbe.csproj"), "-c", "Release",
               "--disable-build-servers", "-p:ImportDirectoryBuildProps=false",
               f"-p:OniManagedPath={managed}", f"-p:OutputPath={output}/bin/",
               f"-p:BaseIntermediateOutputPath={output}/obj/", "-p:NuGetAudit=false"]
    with (output / "build.log").open("w") as log:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=120)
    if result.returncode or not binary.is_file():
        raise RuntimeError(f"Probe build failed ({result.returncode}); no receipt issued. See {output / 'build.log'}")
    if sources != {name: sha(ROOT / name) for name in SOURCES}:
        raise RuntimeError("Probe source changed during compilation; no receipt issued")
    data = dict(schema=1, output_dll=str(binary), probe_sha256=sha(binary), source_sha256=sources,
                reference_managed_sha256=sha(managed / "Assembly-CSharp.dll"), build_exit_code=0,
                scope="Compilation only. This separate probe is not included in CycleTrim.zip.")
    receipt.write_text(json.dumps(data, indent=2) + "\n")
    verify_receipt(receipt)
    print(json.dumps(data, indent=2))


if __name__ == "__main__":
    main()
