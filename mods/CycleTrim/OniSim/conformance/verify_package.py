#!/usr/bin/env python3
"""Bind the delivered ZIP/source archive to native and real-game receipts."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from pathlib import Path
import tarfile
import zipfile

from cases import cases
from run import HARNESS_FILES
from texture_probe.validate import validate_observation

ROOT = Path(__file__).resolve().parent
MOD = ROOT.parents[1]
REPOSITORY = MOD.parents[1]
LICENSE_FILES = ("LICENSE", "LICENSE.MIT", "LICENSING.md", "UPSTREAM.toml")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def validate_window_geometry(data):
    require(isinstance(data, dict) and data.get("mapped") is True,
            "Graphical window was not observed mapped")
    logical, pixels = data.get("size"), data.get("png_size")
    require(all(isinstance(values, list) and len(values) == 2
                and all(type(value) is int and value > 0 for value in values)
                for values in (logical, pixels)), "Invalid window or screenshot dimensions")
    monitor = data.get("monitor_geometry")
    require(isinstance(monitor, dict), "Missing observed monitor geometry")
    scale = monitor.get("scale")
    require(type(scale) in (int, float) and math.isfinite(scale) and scale > 0,
            "Invalid observed display scale")
    require(all(abs(size * scale - pixel) <= 1 for size, pixel in zip(logical, pixels)),
            "Actual scaled window does not match the screenshot")


def verify(dist):
    conformance = []
    expected_cases = {case["name"] for case in cases()}
    expected_messages = {int(value) for value in re.findall(r"\| `(-?\d+)` \|",
        (MOD / "OniSim/OFFICIAL-MESSAGES.md").read_text())}
    require(len(expected_messages) == 62, "Re-audit the pinned official message catalogue")
    with zipfile.ZipFile(dist / "CycleTrim.zip") as package:
        require(not any(Path(name).name == "CycleTrim.TextureProbe.dll" for name in package.namelist()),
                "The opt-in texture probe must not be included in the production mod")
        native = sha(package.read("OniSim/liboni_sim_core.so"))
        managed = sha(package.read("CycleTrim.dll"))
        for workers in (1, 4):
            name = f"results/linux-744825-workers{workers}.json"
            data = package.read("OniSim/conformance/" + name)
            require(data == (ROOT / name).read_bytes(), "Packaged conformance receipt is stale")
            result = json.loads(data)
            require(result["passed"] and all(case["passed"] for case in result["cases"]),
                    "A conformance case failed")
            require(len(result["cases"]) == len(expected_cases)
                    and {case["case"] for case in result["cases"]} == expected_cases,
                    "Conformance receipt omits cases from the current suite")
            require(result["libraries"]["candidate"] == native,
                    "Packaged native library was not tested by this conformance run")
            require(result["workers_requested"] == workers, "Wrong worker receipt")
            require(result["empty_batches_compared"], "Empty batch publications were omitted")
            require(set(result["message_ids_exercised"]) == expected_messages,
                    "Conformance run did not exercise every catalogued message ID")
            for source, digest in result["harness_sha256"].items():
                require(sha((ROOT / source).read_bytes()) == digest,
                        f"Conformance harness changed since verification: {source}")
            conformance.append(dict(workers=workers, cases=len(result["cases"]),
                                    message_ids_exercised=len(result["message_ids_exercised"]),
                                    publications=sum(case["snapshots_compared"] for case in result["cases"])))
        name = "results/linux-744825-runtime.json"
        data = package.read("OniSim/conformance/" + name)
        require(data == (ROOT / name).read_bytes(), "Packaged runtime receipt is stale")
        runtime = json.loads(data)
        require(runtime["native_sha256"] == native, "Runtime used a different native library")
        require(runtime["cycletrim_dll_sha256"] == managed,
                "Runtime used a different managed DLL; re-test or repackage the tested outputs without rebuilding")
        require(runtime["steam_launched"] and runtime["headless"] and runtime["batch_mode"]
                and runtime["graphics_device"] == "Null" and runtime["camera_render_callbacks"] == 0
                and runtime["game_crash"] is False, "Headless runtime acceptance failed")
        require(isinstance(runtime.get("restoration"), dict) and runtime["restoration"]
                and all(value is True for value in runtime["restoration"].values()),
                "Headless cleanup/preservation evidence is missing or failed")
        require(runtime["native_seconds_advanced_before_save"] > 0
                and runtime["native_seconds_advanced_after_reload"] > 0
                and runtime["reload_clock_equal"],
                "Runtime progress, roundtrip or restoration is incomplete")
        require(runtime["cell_workers"] == [f"OniSim-cell-{index}" for index in range(4)],
                "Runtime did not verify four cell workers")
        name = "results/linux-744825-graphics.json"
        data = package.read("OniSim/conformance/" + name)
        require(data == (ROOT / name).read_bytes(), "Packaged graphical receipt is stale")
        graphics = json.loads(data)
        require(graphics["native_sha256"] == native
                and graphics["cycletrim_dll_sha256"] == managed,
                "Graphical runtime used a different native/managed pair")
        validate_observation(graphics["observation"], 1)
        validate_observation(graphics["observation_after_reload"], 2)
        require(graphics["steam_launched"] and graphics["reload_clock_equal"],
                "Graphical Steam launch or save/reload acceptance is incomplete")
        require(graphics.get("cell_workers") == [f"OniSim-cell-{index}" for index in range(4)],
                "Graphical runtime did not verify four cell workers")
        require(graphics.get("visual_inspection_passed") is True
                and re.fullmatch(r"[0-9a-f]{64}", graphics.get("screenshot_sha256", "")),
                "Actual graphical screenshot inspection is incomplete")
        validate_window_geometry(graphics.get("window_geometry"))
        require(graphics["owned_process_stopped"] is True
                and graphics["saved_without_overwrite"] is True
                and graphics["restoration"]
                and all(value is True for value in graphics["restoration"].values()),
                "Graphical runtime cleanup or preservation is incomplete")
        for name, digest in graphics["probe_source_sha256"].items():
            require(name in ("TextureProbe.cs", "TextureProbe.csproj", "mod.yaml", "mod_info.yaml", "build.py"),
                    "Unexpected graphical probe source")
            require(sha((ROOT / "texture_probe" / name).read_bytes()) == digest,
                    f"Graphical probe source changed since observation: {name}")
        require(set(graphics["probe_source_sha256"]) == {
            "TextureProbe.cs", "TextureProbe.csproj", "mod.yaml", "mod_info.yaml", "build.py"},
            "Graphical probe source identity is incomplete")
        for name in ("PROGRESS.md", "RUNTIME-VALIDATION.md", "PARALLEL-VALIDATION.md", "PERFORMANCE.md", "conformance/README.md"):
            require(package.read("OniSim/" + name) == (MOD / "OniSim" / name).read_bytes(),
                    f"Packaged document is stale: {name}")
        for name in LICENSE_FILES:
            require(package.read(name) == (REPOSITORY / name).read_bytes(),
                    f"Packaged license or provenance differs from the repository: {name}")
    with tarfile.open(dist / "CycleTrim-src.tar.gz") as archive:
        names = archive.getnames()
        excluded = {"target", "artifacts", "__pycache__", "obj", "bin", "dist", "history"}
        require(not any(set(Path(name).parts) & excluded for name in names),
                "Source archive contains generated or excluded trees")
        for name in (*HARNESS_FILES, "test_runner.py", "verify_package.py", "build_source_package.py",
                     "prepare_instances.py", "verify_instances.py", "step_instance.py", "mcp_roundtrip.py",
                     "test_instances.py", "test_mcp_roundtrip.py",
                     "texture_probe/TextureProbe.cs", "texture_probe/TextureProbe.csproj", "texture_probe/build.py",
                     "texture_probe/validate.py",
                     "texture_probe/mod.yaml", "texture_probe/mod_info.yaml", "texture_probe/README.md"):
            file = archive.extractfile("mods/CycleTrim/OniSim/conformance/" + name)
            require(file is not None and file.read() == (ROOT / name).read_bytes(),
                    f"Source archive is missing current conformance source: {name}")
        for name in LICENSE_FILES:
            file = archive.extractfile(name)
            require(file is not None and file.read() == (REPOSITORY / name).read_bytes(),
                    f"Source archive is missing current license or provenance: {name}")
    source_build = json.loads((dist / "source-build-validation.json").read_text())
    require(source_build["source_archive_sha256"] == sha((dist / "CycleTrim-src.tar.gz").read_bytes())
            and source_build["exit_code"] == 0 and source_build["isolated_build"]
            and source_build["canonical_layout"] and source_build["license_resolved"]
            and source_build["native_compiled"] and source_build["managed_compiled"]
            and source_build["rebuilt_native_sha256"] == native,
            "The exact source archive has not passed a clean extracted build")
    return dict(native_sha256=native, managed_sha256=managed, conformance=conformance,
                runtime_smoke_covers_this_native_and_managed_pair=True,
                runtime_scope=runtime["acceptance_scope"],
                graphical_full_buffer_comparison_verified=True,
                packaged_docs_match_source=True, source_excludes_generated=True,
                source_archive_build_verified=True,
                game_ready=False, runtime_validated=False,
                artifacts={name: dict(bytes=(dist / name).stat().st_size, sha256=sha((dist / name).read_bytes()))
                           for name in ("CycleTrim.zip", "CycleTrim-src.tar.gz")})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dist", type=Path, default=MOD / "dist")
    args = parser.parse_args()
    (args.dist / "delivery-validation.json").unlink(missing_ok=True)
    result = verify(args.dist)
    (args.dist / "delivery-validation.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
