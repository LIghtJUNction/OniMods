#!/usr/bin/env python3
"""Exercise the production image-scoped mapper in an installed Unity Mono host.

Only authored fixture assemblies are executed. Assembly-CSharp is not loaded;
the candidate's 16 official entry points are resolved, never invoked. This
establishes public Mono API behavior, not Unity startup ordering or game readiness.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import resource
import subprocess
import tempfile
import time

HOST_SOURCE = r"""#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>
typedef void (*set_path_t)(const char *);
typedef void (*dirs_t)(const char *,const char *);
typedef void *(*init_t)(const char *, const char *);
typedef void *(*open_t)(void *, const char *);
typedef void *(*one_t)(void *);
typedef void *(*class_t)(void *, const char *, const char *);
typedef void *(*method_t)(void *, const char *, int);
typedef void *(*invoke_t)(void *, void *, void **, void **);
typedef void (*cleanup_t)(void *);
typedef void *(*string_t)(void *, void **);
static void *sym(void *m,const char *s) { void *v=dlsym(m,s); if(!v){fprintf(stderr,"missing %s: %s\n",s,dlerror());exit(2);}return v;}
int main(int argc,char **argv) {
 if(argc!=4)return 2;
 void *m=dlopen(argv[1],RTLD_NOW|RTLD_GLOBAL); if(!m){fprintf(stderr,"%s\n",dlerror());return 3;}
 ((dirs_t)sym(m,"mono_set_dirs"))(getenv("ONISIM_PROBE_MONO_DIR"),getenv("ONISIM_PROBE_CONFIG_DIR"));
 ((set_path_t)sym(m,"mono_config_parse"))(getenv("ONISIM_PROBE_CONFIG"));
 ((set_path_t)sym(m,"mono_set_assemblies_path"))(argv[2]);
 void *d=((init_t)sym(m,"mono_jit_init_version"))("OniSim-authored-fixture","v4.0.30319");if(!d)return 4;
 void *a=((open_t)sym(m,"mono_domain_assembly_open"))(d,argv[3]);if(!a)return 5;
 void *im=((one_t)sym(m,"mono_assembly_get_image"))(a);
 void *cl=((class_t)sym(m,"mono_class_from_name"))(im,"","Fixture");if(!cl)return 6;
 void *method=((method_t)sym(m,"mono_class_get_method_from_name"))(cl,"Run",0);if(!method)return 7;
 void *exc=NULL;void *value=((invoke_t)sym(m,"mono_runtime_invoke"))(method,NULL,NULL,&exc);
 if(exc){void *secondary=NULL;void *str=((string_t)sym(m,"mono_object_to_string"))(exc,&secondary);char *msg=str?(char *)((one_t)sym(m,"mono_string_to_utf8"))(str):"unknown";fprintf(stderr,"managed fixture exception: %s\n",msg);return 8;}
 int result=value?*(int *)((one_t)sym(m,"mono_object_unbox"))(value):9;
 ((cleanup_t)sym(m,"mono_jit_cleanup"))(d);return result;
}
"""

FIXTURE_SOURCE = r"""using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Jlib.Runtime;
public static class Calls {
 [DllImport("FixtureNative",EntryPoint="probe_value",CallingConvention=CallingConvention.Cdecl)] private static extern int Native();
 public static int Read(){return Native();}
}
public static class SimProbe {
 // Names-only resolver fixtures: never invoke these declarations.
 [DllImport("SimDLL", EntryPoint="SIM_Initialize")] internal static extern void SIM_Initialize();
 [DllImport("SimDLL", EntryPoint="SIM_Shutdown")] internal static extern void SIM_Shutdown();
 [DllImport("SimDLL", EntryPoint="SIM_HandleMessage")] internal static extern void SIM_HandleMessage();
 [DllImport("SimDLL", EntryPoint="SIM_HandleMessages")] internal static extern void SIM_HandleMessages();
 [DllImport("SimDLL", EntryPoint="SIM_BeginSave")] internal static extern void SIM_BeginSave();
 [DllImport("SimDLL", EntryPoint="SIM_EndSave")] internal static extern void SIM_EndSave();
 [DllImport("SimDLL", EntryPoint="SIM_DebugCrash")] internal static extern void SIM_DebugCrash();
 [DllImport("SimDLL", EntryPoint="SYSINFO_Acquire")] internal static extern void SYSINFO_Acquire();
 [DllImport("SimDLL", EntryPoint="SYSINFO_Release")] internal static extern void SYSINFO_Release();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Initialize")] internal static extern void ConduitTemperatureManager_Initialize();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Shutdown")] internal static extern void ConduitTemperatureManager_Shutdown();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Add")] internal static extern void ConduitTemperatureManager_Add();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Set")] internal static extern void ConduitTemperatureManager_Set();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Remove")] internal static extern void ConduitTemperatureManager_Remove();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Update")] internal static extern void ConduitTemperatureManager_Update();
 [DllImport("SimDLL", EntryPoint="ConduitTemperatureManager_Clear")] internal static extern void ConduitTemperatureManager_Clear();
}
public static class Fixture {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetMethod(IntPtr image,int token,IntPtr context);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LookupImport(IntPtr method,out IntPtr exceptionClass,out IntPtr exceptionArgument);

 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 public static int Run(){
  try{
   string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
   var mapper=new MonoDllMap(NativeModule.OpenAlreadyLoaded(Environment.GetEnvironmentVariable("ONISIM_PROBE_MONO")));
   var control=Assembly.LoadFile(Path.Combine(root,"Control.dll"));
   IntPtr first=mapper.FindImage(Assembly.GetExecutingAssembly().Location),second=mapper.FindImage(control.Location);
   Check(first!=IntPtr.Zero&&second!=IntPtr.Zero&&first!=second,"exact loaded image identity");
   try{mapper.Insert(IntPtr.Zero,"FixtureNative",Path.Combine(root,"liba.so"));throw new Exception("zero image accepted");}catch(ArgumentException){Console.WriteLine("PASS zero image refused");}
   mapper.Insert(first,"FixtureNative",Path.Combine(root,"liba.so"));
   mapper.Insert(second,"FixtureNative",Path.Combine(root,"libb.so"));
   var read=typeof(Calls).GetMethod("Read");
   Check((int)read.Invoke(null,null)==101,"first image maps to A through managed PInvoke");
   var controlRead=control.GetType("Control").GetMethod("Read");
   Check((int)controlRead.Invoke(null,null)==202,"second image independently maps to B");
   mapper.Insert(first,"FixtureNative",Path.Combine(root,"libb.so"));
   Check((int)read.Invoke(null,null)==101,"resolved PInvoke stays cached after late map");
   Check((int)controlRead.Invoke(null,null)==202,"late first image map leaves second image intact");
   Check(mapper.FindImage(Path.Combine(root,"missing.dll"))==IntPtr.Zero,"missing image rejected");
   var mono=NativeModule.OpenAlreadyLoaded(Environment.GetEnvironmentVariable("ONISIM_PROBE_MONO"));
   var target=NativeModule.OpenTarget(Environment.GetEnvironmentVariable("ONISIM_PROBE_NATIVE"));
   var getMethod=mono.Bind<GetMethod>("mono_get_method");
   var lookup=mono.Bind<LookupImport>("mono_lookup_pinvoke_call");
   mapper.Insert(first,"SimDLL",Environment.GetEnvironmentVariable("ONISIM_PROBE_NATIVE"));
   foreach(var method in typeof(SimProbe).GetMethods(BindingFlags.Static|BindingFlags.NonPublic)) {
    IntPtr exceptionClass,exceptionArgument;
    var resolved=lookup(getMethod(first,method.MetadataToken,IntPtr.Zero),out exceptionClass,out exceptionArgument);
    Check(resolved!=IntPtr.Zero&&resolved==target.RequireSymbol(method.Name),"native destination "+method.Name);
   }
   return 0;
  }catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
 }
}
"""

CONTROL_SOURCE = r"""using System.Runtime.InteropServices;
public static class Control {
 [DllImport("FixtureNative",EntryPoint="probe_value",CallingConvention=CallingConvention.Cdecl)] private static extern int Native();
 public static int Read(){ return Native(); }
}
"""

NATIVE_SOURCE = r"""#ifndef VALUE
#define VALUE 101
#endif
int probe_value(void) { return VALUE; }
"""


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mono-runtime", type=Path, required=True)
    parser.add_argument("--managed-path", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
        parser.error("This fixture currently covers Linux x64 only.")
    mono = args.mono_runtime.resolve(strict=True)
    managed = args.managed_path.resolve(strict=True)
    candidate = args.candidate.resolve(strict=True)
    repository = Path(__file__).resolve().parents[4]
    runtime_source = repository / "mods/Jlib/Jlib/Runtime"
    sources = [runtime_source / name for name in
               ("MonoDllMap.cs", "NativeModule.cs", "NativePlatform.cs")]
    config = mono.parent.parent / "etc/mono/config"
    report = {
        "scope": "Unity Mono public embedding API and authored PInvoke fixtures",
        "mono_sha256": digest(mono),
        "mono_native_sha256": digest(mono.parent / "libmono-native.so"),
        "mscorlib_sha256": digest(managed / "mscorlib.dll"),
        "native_sha256": digest(candidate),
        "production_source_sha256": {
            str(path.relative_to(repository)): digest(path) for path in sources
        },
        "runs": [],
        "passed": False,
        "unverified": [
            "Unity Player startup import timing and previously cached game imports",
            "Actual Assembly-CSharp execution and parameter marshaling",
            "Game simulation, DLC state, save/load and rendering"
        ],
    }
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))

    def run(arguments, directory: Path, env=None):
        command = ["rtk", "proxy"] + [str(value) for value in arguments]
        started = time.monotonic()
        result = subprocess.run(command, cwd=directory, text=True, capture_output=True,
                                timeout=40, env=env)
        report["runs"].append({
            "command": command,
            "exit_code": result.returncode,
            "elapsed_seconds": time.monotonic() - started,
            "stdout": result.stdout,
            "stderr": result.stderr,
        })
        result.check_returncode()
        return result

    try:
        with tempfile.TemporaryDirectory(prefix="onisim-mono-probe-") as temporary:
            work = Path(temporary)
            for name, source in (("host.c", HOST_SOURCE), ("Fixture.cs", FIXTURE_SOURCE),
                                 ("Control.cs", CONTROL_SOURCE), ("native.c", NATIVE_SOURCE)):
                (work / name).write_text(source)
            # Unity supplies this native-library directory while hosting Mono.
            # A standalone embedder must supply it explicitly; do not change the
            # installation's config or install a second runtime.
            (work / "mono.config").write_text(
                config.read_text().replace("$mono_libdir", str(mono.parent)))
            run(["gcc", "-O2", "-Wall", "-Wextra", "-o", "host", "host.c", "-ldl"], work)
            run(["gcc", "-shared", "-fPIC", "-DVALUE=101", "-o", "liba.so", "native.c"], work)
            run(["gcc", "-shared", "-fPIC", "-DVALUE=202", "-o", "libb.so", "native.c"], work)
            run(["mcs", "-target:library", "-out:Control.dll", "Control.cs"], work)
            run(["mcs", "-langversion:latest", "-target:library", "-out:Fixture.dll",
                 "Fixture.cs"] + sources, work)
            env = os.environ.copy()
            env.update({
                "ONISIM_PROBE_MONO": str(mono),
                "ONISIM_PROBE_MONO_DIR": str(mono.parent),
                "ONISIM_PROBE_CONFIG_DIR": str(mono.parent.parent / "etc"),
                "ONISIM_PROBE_CONFIG": str(work / "mono.config"),
                "ONISIM_PROBE_NATIVE": str(candidate),
            })
            result = run([work / "host", mono, managed, work / "Fixture.dll"], work, env)
            checks = [line[5:] for line in result.stdout.splitlines() if line.startswith("PASS ")]
            if len(checks) != 23:
                raise RuntimeError("Expected 7 mapper checks and 16 native destinations.")
            report["checks"] = checks
            report["passed"] = True
    except (OSError, subprocess.SubprocessError, RuntimeError) as error:
        report["error"] = str(error)
    finally:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({
        "passed": report["passed"],
        "checks": len(report.get("checks", [])),
        "output": str(args.output),
        "error": report.get("error")
    }))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
