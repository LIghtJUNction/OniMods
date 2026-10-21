#!/usr/bin/env python3
"""Compile the exact source archive in a fresh tree, without checkout build props."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dist", type=Path, default=ROOT.parents[1] / "dist")
    parser.add_argument("--managed-path", required=True, type=Path)
    parser.add_argument("--package-output", type=Path,
                        help="Retain the package produced by this exact isolated source build; use a new ZIP path")
    args = parser.parse_args()
    dist = args.dist.resolve()
    managed = args.managed_path.resolve()
    if not (managed / "Assembly-CSharp.dll").is_file():
        parser.error("Supply the installed game's Managed directory")
    receipt = dist / "source-build-validation.json"
    receipt.unlink(missing_ok=True)
    package_output = args.package_output.resolve() if args.package_output else None
    if package_output is not None and (package_output.exists() or package_output.suffix != ".zip"):
        parser.error("Use a new ZIP output; existing packages are never replaced")
    archive_path = dist / "CycleTrim-src.tar.gz"
    digest = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    # A tree below the checkout still inherits its ignored Directory.Build.props.
    # Keep the rebuild outside it and disable ambient Directory.Build imports.
    scratch = Path(os.environ.get("XDG_CACHE_HOME", str(Path.home() / ".cache"))) / "cycletrim-source-builds"
    scratch.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="source-build-", dir=scratch) as temporary:
        checkout = Path(temporary).resolve()
        with tarfile.open(archive_path) as archive:
            archive.extractall(checkout, filter="data")
        project = checkout / "mods/CycleTrim/CycleTrim.csproj"
        license_path = (checkout / "mods/CycleTrim/OniSim/native/../../../../LICENSE").resolve()
        if not project.is_file() or license_path != checkout / "LICENSE" or not license_path.is_file():
            raise RuntimeError("Source archive lacks the canonical project/license layout")
        command = ["rtk", "proxy", "dotnet", "build", str(project), "-c", "Release",
                   "--disable-build-servers",
                   "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                   f"-p:OniManagedPath={managed}", "-p:IncludeOniSim=true",
                   f"-p:DistPath={checkout}/dist/", "-p:RestoreIgnoreFailedSources=true",
                   "-p:NuGetAudit=false"]
        with (dist / "source-build.log").open("w") as log:
            result = subprocess.run(command, cwd=checkout, stdout=log, stderr=subprocess.STDOUT, timeout=180,
                                    env=dict(os.environ, RUSTC_WRAPPER="", RUSTC_WORKSPACE_WRAPPER="",
                                             CARGO_BUILD_JOBS="1"))
        if result.returncode:
            raise RuntimeError(f"Extracted source build failed ({result.returncode}); inspect source-build.log")
        rebuilt_package = checkout / "dist/CycleTrim.zip"
        with zipfile.ZipFile(rebuilt_package) as package:
            native = hashlib.sha256(package.read("OniSim/liboni_sim_core.so")).hexdigest()
            wrapper = hashlib.sha256(package.read("CycleTrim.dll")).hexdigest()
        if hashlib.sha256(archive_path.read_bytes()).hexdigest() != digest:
            raise RuntimeError("Source archive changed during its isolated build")
        package_hash = hashlib.sha256(rebuilt_package.read_bytes()).hexdigest()
        if package_output is not None:
            package_output.parent.mkdir(parents=True, exist_ok=True)
            with rebuilt_package.open("rb") as source, package_output.open("xb") as output:
                shutil.copyfileobj(source, output)
            if hashlib.sha256(package_output.read_bytes()).hexdigest() != package_hash:
                raise RuntimeError("Retained package differs from its isolated build")
    report = dict(schema=1, source_archive_sha256=digest,
                  reference_managed_sha256=hashlib.sha256((managed / "Assembly-CSharp.dll").read_bytes()).hexdigest(),
                  canonical_layout=True, license_resolved=True, isolated_build=True,
                  native_compiled=True, managed_compiled=True, exit_code=0,
                  rebuilt_native_sha256=native, rebuilt_managed_sha256=wrapper,
                  rebuilt_package_sha256=package_hash,
                  retained_package=str(package_output) if package_output else None,
                  compiler_wrappers_disabled=True, cargo_build_jobs=1,
                  ambient_directory_build_imports_disabled=True,
                  scope="Compilation of the exact source archive. Rebuilt binaries are not runtime-tested by this check.")
    receipt.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
