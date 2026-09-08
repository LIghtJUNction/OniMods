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
    "verify_restart_packaging.py": "requires an OniMcp Debug build and distribution archive",
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--static-only", action="store_true", help="run only Python source contracts")
    parser.add_argument("--dotnet", default="dotnet", help=".NET 10 SDK executable name or path")
    args = parser.parse_args()
    checks = [
        (path.name, [sys.executable, str(path)])
        for path in sorted((ROOT / "scripts").glob("verify_*.py"))
        if path.name not in GAME_CHECKS
    ]
    if not args.static_only:
        dotnet = shutil.which(args.dotnet)
        if dotnet is None:
            parser.error(".NET 10 SDK was not found; install it or pass --dotnet PATH")
        projects = sorted((ROOT / "tests").glob("*/*.csproj"))
        if not projects:
            parser.error("no Mod regression projects found under tests/")
        projects.append(ROOT / "benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj")
        checks.extend(
            (str(path.relative_to(ROOT)), [dotnet, "run", "--project", str(path),
                                         "--configuration", "Release",
                                         "-p:ImportDirectoryBuildProps=false",
                                         "-p:TreatWarningsAsErrors=true"])
            for path in projects
        )

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
    if args.static_only:
        print("NOT RUN executable C# regressions (--static-only)")
    print(f"\n{len(checks) - len(failures)}/{len(checks)} checks passed")
    for name in failures:
        print(f"FAIL {name}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
