#!/usr/bin/env python3
"""Run Mod source contracts and executable regressions without installing ONI."""

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
GAME_CHECKS = {
    "verify_cycletrim_target_contract.py": "requires the installed game's Assembly-CSharp.dll and ilspycmd",
    "verify_cycletrim_release_binary.py": "requires a built CycleTrim Release DLL and ilspycmd",
    "verify_cycletrim_navgrid_source_contract.py": "requires the pinned online ONI 744825 decompilation source",
    "verify_restart_packaging.py": "requires an OniMcp Debug build and distribution archive",
    "test_legacy_workshop_package.py": "requires a built Steam publisher and the installed game's Steamworks files",
}
SYNTHETIC_PERFORMANCE_PROJECT = (
    ROOT / "tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj"
)
LINUX_TEST_DIRECTORIES = (
    "mods/CycleTrim/OniSim/conformance",
    "mods/CycleTrim/OniSim/conformance/memory_probe",
)


def python_regression_checks() -> list[tuple[str, list[str]]]:
    scripts = sorted((ROOT / "scripts").glob("verify_*.py"))
    scripts.extend(sorted((ROOT / "scripts").glob("test_*.py")))
    checks = [
        (str(path.relative_to(ROOT)), [sys.executable, str(path)])
        for path in scripts
        if path.name not in GAME_CHECKS
    ]
    directories = {path.parent for path in (ROOT / "tests").glob("*/test_*.py")}
    if sys.platform == "linux":
        directories.update(
            ROOT / relative for relative in LINUX_TEST_DIRECTORIES
            if any((ROOT / relative).glob("test_*.py"))
        )
    checks.extend(
        (
            str(path.relative_to(ROOT)),
            [sys.executable, "-m", "unittest", "discover", "-s", str(path), "-p", "test_*.py", "-v"],
        )
        for path in sorted(directories)
    )
    return checks


def validate_python_prerequisites() -> None:
    conformance = ROOT / LINUX_TEST_DIRECTORIES[0]
    if sys.platform != "linux" or not any(conformance.glob("test_*.py")):
        return
    if not (ROOT / "tools/OniHarness/scripts/instance_controller.py").is_file():
        raise ValueError(
            "Linux conformance regressions require the pinned OniHarness submodule; "
            "run git submodule update --init tools/OniHarness"
        )
    if shutil.which("cc") is None:
        raise ValueError("Linux conformance regressions require a C compiler named cc for the authored clock fixture")


def build_project_command(
    path: Path,
    dotnet: str,
    skip_synthetic_performance: bool,
) -> list[str]:
    command = [
        dotnet,
        "run",
        "--project",
        str(path),
        "--configuration",
        "Release",
        "-p:ImportDirectoryBuildProps=false",
        "-p:TreatWarningsAsErrors=true",
    ]
    if skip_synthetic_performance and path == SYNTHETIC_PERFORMANCE_PROJECT:
        command.extend(["--", "--skip-synthetic-performance"])
    return command


def host_regression_checks(dotnet: str, skip_synthetic_performance: bool) -> list[tuple[str, list[str]]]:
    """Discover host projects and their optional integration entry points."""
    projects = sorted((ROOT / "tests").glob("*/*.csproj"))
    if not projects:
        return []

    checks = [
        (str(path.relative_to(ROOT)), build_project_command(path, dotnet, skip_synthetic_performance))
        for path in projects
    ]
    adapter = ROOT / "tests/OniOnline.Tests/OniOnline.Tests.csproj"
    if adapter in projects:
        checks.append((
            "OniOnline native-origin adapter regressions",
            build_project_command(adapter, dotnet, skip_synthetic_performance)
            + ["-p:OriginAdapterTests=true"],
        ))
    return checks


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--static-only", action="store_true", help="run Python source contracts and fixture tests only")
    parser.add_argument("--dotnet", default="dotnet", help=".NET 10 SDK executable name or path")
    parser.add_argument(
        "--skip-synthetic-performance",
        action="store_true",
        help="run host regressions but skip wall-clock synthetic performance assertions",
    )
    args = parser.parse_args()
    checks = python_regression_checks()
    if not checks:
        parser.error("no Python regression checks found")
    try:
        validate_python_prerequisites()
    except ValueError as error:
        parser.error(str(error))
    if not args.static_only:
        dotnet = shutil.which(args.dotnet)
        if dotnet is None:
            parser.error(".NET 10 SDK was not found; install it or pass --dotnet PATH")
        host_checks = host_regression_checks(dotnet, args.skip_synthetic_performance)
        if not host_checks:
            parser.error("no Mod regression projects found under tests/")
        checks.extend(host_checks)

    failures = []
    environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
    for name, command in checks:
        print(f"\nRUN {name}", flush=True)
        try:
            result = subprocess.run(command, cwd=ROOT, env=environment, check=False, timeout=300)
            if result.returncode != 0:
                failures.append(name)
        except (OSError, subprocess.TimeoutExpired) as error:
            print(f"FAIL {name}: {error}", file=sys.stderr)
            failures.append(name)
    for name, reason in GAME_CHECKS.items():
        print(f"NOT RUN {name}: {reason}")
    if sys.platform != "linux":
        for name in LINUX_TEST_DIRECTORIES:
            print(f"NOT RUN {name}: requires Linux process and file-lock APIs")
    if args.static_only:
        print("NOT RUN executable C# regressions (--static-only)")
    if args.skip_synthetic_performance and not args.static_only:
        print("NOT RUN CycleTrim synthetic wall-clock performance gate: reported separately by CI")
    print(f"\n{len(checks) - len(failures)}/{len(checks)} checks passed")
    for name in failures:
        print(f"FAIL {name}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
