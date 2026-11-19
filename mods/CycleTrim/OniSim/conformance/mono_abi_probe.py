#!/usr/bin/env python3
"""Compare the production ABI inventory with an installed Unity Mono runtime.

Runs authored probe code. The game assembly is loaded only for reflection:
no game methods, P/Invokes or static fields are invoked by this probe.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import platform
import resource
import subprocess
import tempfile
import time

from mono_mapping_probe import HOST_SOURCE, digest

FIXTURE_SOURCE = r"""using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using OniSim.CycleTrim;
public static class Fixture {
 [StructLayout(LayoutKind.Sequential)]
 private struct DefaultPack { public byte Tag; public long Value; }
 [StructLayout(LayoutKind.Sequential,Pack=8)]
 private struct EightPack { public byte Tag; public long Value; }
 public static int Run() {
  try {
   if(Marshal.SizeOf(typeof(DefaultPack))!=Marshal.SizeOf(typeof(EightPack)) ||
      Marshal.OffsetOf(typeof(DefaultPack),"Value")!=Marshal.OffsetOf(typeof(EightPack),"Value"))
    throw new Exception("Default and explicit eight-byte packing differ on this runtime");
   Console.WriteLine("PASS default/eight-byte effective packing");
   var game=Assembly.LoadFrom(Environment.GetEnvironmentVariable("ONISIM_ABI_ASSEMBLY"));
   string inventory=PortableProfile.CanonicalInventory(game);
   File.WriteAllText(Environment.GetEnvironmentVariable("ONISIM_ABI_INVENTORY"),inventory);
   Console.WriteLine("ABI "+PortableProfile.Fingerprint(game));
   PortableProfile.Validate(game,int.Parse(Environment.GetEnvironmentVariable("ONISIM_ABI_BUILD")));
   Console.WriteLine("PASS inspected game ABI and release metadata");
   return 0;
  } catch(Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
 }
}
"""

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mono-runtime", type=Path, required=True)
    parser.add_argument("--managed-path", type=Path, required=True)
    parser.add_argument("--game-assembly", type=Path, required=True)
    parser.add_argument("--game-build", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
        parser.error("This probe currently covers inspected Linux x64 runtimes.")
    mono = args.mono_runtime.resolve(strict=True)
    managed = args.managed_path.resolve(strict=True)
    game = args.game_assembly.resolve(strict=True)
    repository = Path(__file__).resolve().parents[4]
    profile = repository / "mods/CycleTrim/OniSim/managed/OniSim.Mod/PortableProfile.cs"
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    inventory_output = output.with_suffix(".inventory.txt")
    report = {
        "scope": "Installed Unity Mono reflection of the production ABI inventory; no game execution",
        "mono_sha256": digest(mono), "assembly_sha256": digest(game),
        "mscorlib_sha256": digest(managed / "mscorlib.dll"),
        "production_source_sha256": digest(profile),
        "expected_game_build": args.game_build, "runs": [], "passed": False,
        "unverified": ["Unity Player import timing", "Native takeover and simulation behavior"],
    }
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))

    def run(arguments, work, env=None):
        command = ["rtk", "proxy"] + [str(value) for value in arguments]
        started = time.monotonic()
        result = subprocess.run(command, cwd=work, env=env, capture_output=True,
                                text=True, timeout=45)
        report["runs"].append({"command": command, "exit_code": result.returncode,
            "seconds": time.monotonic() - started, "stdout": result.stdout, "stderr": result.stderr})
        result.check_returncode()
        return result

    try:
        with tempfile.TemporaryDirectory(prefix="onisim-mono-abi-") as temporary:
            work = Path(temporary)
            (work / "host.c").write_text(HOST_SOURCE)
            (work / "Fixture.cs").write_text(FIXTURE_SOURCE)
            config = mono.parent.parent / "etc/mono/config"
            (work / "mono.config").write_text(
                config.read_text().replace("$mono_libdir", str(mono.parent)))
            run(["gcc", "-O2", "-Wall", "-Wextra", "-o", "host", "host.c", "-ldl"], work)
            run(["mcs", "-langversion:latest", "-target:library", "-out:Fixture.dll",
                 "Fixture.cs", profile], work)
            env = os.environ.copy()
            env.update({
                "ONISIM_PROBE_MONO_DIR": str(mono.parent),
                "ONISIM_PROBE_CONFIG_DIR": str(mono.parent.parent / "etc"),
                "ONISIM_PROBE_CONFIG": str(work / "mono.config"),
                "ONISIM_ABI_ASSEMBLY": str(game),
                "ONISIM_ABI_BUILD": str(args.game_build),
                "ONISIM_ABI_INVENTORY": str(work / "inventory.txt"),
            })
            result = run([work / "host", mono, managed, work / "Fixture.dll"], work, env)
            checks = [line[5:] for line in result.stdout.splitlines() if line.startswith("PASS ")]
            if len(checks) != 2:
                raise RuntimeError("Expected effective-packing and canonical-game-ABI checks.")
            inventory_output.write_bytes((work / "inventory.txt").read_bytes())
            report["inventory_path"] = str(inventory_output)
            report["inventory_sha256"] = digest(inventory_output)
            report["checks"] = checks
            report["passed"] = True
    except (OSError, subprocess.SubprocessError, RuntimeError) as error:
        report["error"] = str(error)
    finally:
        output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({"passed": report["passed"], "output": str(output),
                      "inventory_sha256": report.get("inventory_sha256"),
                      "error": report.get("error")}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
