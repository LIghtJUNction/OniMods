"""Compile opt-in probes and verify their source-bound build receipts."""
import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


@dataclass(frozen=True)
class ProbeBuild:
    name: str
    root: Path
    sources: tuple[str, ...]

    def source_hashes(self):
        names = (*self.sources, f"{self.name}Probe.csproj", "mod.yaml", "mod_info.yaml",
                 "build.py", "../probe_build.py")
        return {name: sha(self.root / name) for name in names}

    def verify_receipt(self, path):
        receipt = json.loads(Path(path).read_text())
        if receipt["build_exit_code"] != 0:
            raise RuntimeError(f"{self.name} probe build did not succeed")
        if receipt["source_sha256"] != self.source_hashes():
            raise RuntimeError(f"{self.name} probe source changed after its successful build")
        binary = Path(receipt["output_dll"])
        if not binary.is_file() or sha(binary) != receipt["probe_sha256"]:
            raise RuntimeError(f"{self.name} probe DLL differs from its successful build")
        return receipt

    def main(self, argv=None):
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument("--managed-path", required=True, type=Path)
        parser.add_argument("--output", type=Path,
                            default=self.root.parent / f"artifacts/{self.name.lower()}-probe-build")
        args = parser.parse_args(argv)
        managed = args.managed_path.resolve()
        output = args.output.resolve()
        if not (managed / "Assembly-CSharp.dll").is_file():
            parser.error("Supply the installed game's Managed directory")
        output.mkdir(parents=True, exist_ok=True)
        receipt = output / "receipt.json"
        receipt.unlink(missing_ok=True)
        binary = output / f"bin/CycleTrim.{self.name}Probe.dll"
        binary.unlink(missing_ok=True)
        sources = self.source_hashes()
        command = ["dotnet", "build", str(self.root / f"{self.name}Probe.csproj"), "-c", "Release",
                   "--disable-build-servers", "-p:ImportDirectoryBuildProps=false",
                   f"-p:OniManagedPath={managed}", f"-p:OutputPath={output}/bin/",
                   f"-p:BaseIntermediateOutputPath={output}/obj/", "-p:NuGetAudit=false"]
        with (output / "build.log").open("w") as log:
            result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=120)
        if result.returncode or not binary.is_file():
            raise RuntimeError(f"Probe build failed ({result.returncode}); no receipt issued. See {output / 'build.log'}")
        if sources != self.source_hashes():
            raise RuntimeError("Probe source changed during compilation; no receipt issued")
        data = dict(schema=1, output_dll=str(binary), probe_sha256=sha(binary), source_sha256=sources,
                    reference_managed_sha256=sha(managed / "Assembly-CSharp.dll"), build_exit_code=0,
                    scope="Compilation only. This separate probe is not included in CycleTrim.zip.")
        receipt.write_text(json.dumps(data, indent=2) + "\n")
        self.verify_receipt(receipt)
        print(json.dumps(data, indent=2))
