using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Fetch.Tests
{
    internal static class FetchCompatibilityRegression
    {
        private static readonly string[] Cases =
        {
            "ordinary", "disabled", "legacy", "maintained", "fasttrack",
            "efficient", "bundled", "waste"
        };

        internal static void RunAll()
        {
            // Loaded mod types and the one-shot installation state cannot be
            // removed from this process. Each topology therefore starts fresh.
            foreach (var scenario in Cases)
            {
                var executable = Environment.ProcessPath;
                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet",
                    StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add("--compatibility");
                start.ArgumentList.Add(scenario);
                using (var child = Process.Start(start))
                {
                    var output = child.StandardOutput.ReadToEndAsync();
                    var error = child.StandardError.ReadToEndAsync();
                    if (!child.WaitForExit(60000))
                    {
                        child.Kill();
                        child.WaitForExit();
                        throw new InvalidOperationException("fetch compatibility timed out: " + scenario);
                    }
                    Console.Write(output.Result);
                    if (child.ExitCode != 0)
                        throw new InvalidOperationException("fetch compatibility failed: " + scenario + "\n" + error.Result);
                }
            }
        }

        internal static void Run(string scenario, Harmony harmony)
        {
            if (!Cases.Contains(scenario)) throw new ArgumentException("unknown fetch compatibility case");
            CycleTrimPatchOptions.Configure(scenario != "disabled", true, true, true, true, false);
            var target = AccessTools.Method(typeof(FetchManager.FetchablesByPrefabId), "UpdatePickups");
            harmony.PatchAll(typeof(Program).Assembly);
            Check(!HasOwnPrefix(target, harmony.Id), "initial PatchAll must leave the original body authoritative");

            string typeName = null;
            switch (scenario)
            {
                case "legacy": typeName = FetchPatchCompatibility.DeliveryTemperatureLimitUpdatePickupsPatch; break;
                case "maintained": typeName = FetchPatchCompatibility.DeliveryTemperatureLimitSupercooledPickupGroupingType; break;
                case "fasttrack": typeName = "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate"; break;
                case "efficient": typeName = "PeterHan.EfficientFetch.EfficientFetchManager"; break;
                case "bundled": typeName = "QualityOfLifeONI.EfficientFetchManager"; break;
                case "waste": typeName = "PeterHan.NoWasteWant.NoWasteWantPatches"; break;
            }
            if (typeName != null)
            {
                var assembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("FetchCompatibility." + scenario), AssemblyBuilderAccess.Run);
                var builder = assembly.DefineDynamicModule("fixtures").DefineType(typeName,
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
                if (scenario == "legacy")
                {
                    var method = builder.DefineMethod("Transpiler", MethodAttributes.Public | MethodAttributes.Static,
                        typeof(IEnumerable<CodeInstruction>), new[] { typeof(IEnumerable<CodeInstruction>) });
                    var il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ret);
                }
                var fixture = builder.CreateType();
                if (scenario == "legacy")
                    new Harmony("cycletrim.tests.legacy-dtl").Patch(target,
                        transpiler: new HarmonyMethod(fixture.GetMethod("Transpiler")));
            }

            FetchPickupCandidatePatch.InstallAfterAllModsLoaded(harmony);
            bool expected = scenario == "ordinary";
            Check(HasOwnPrefix(target, harmony.Id) == expected, "loaded topology must decide replacement ownership");
            FetchPickupCandidatePatch.InstallAfterAllModsLoaded(harmony);
            int ownPrefixes = Harmony.GetPatchInfo(target)?.Prefixes.Count(patch => patch.owner == harmony.Id) ?? 0;
            Check(ownPrefixes == (expected ? 1 : 0), "repeated loaded-mod callback must not add another prefix");

            var owner = new FetchManager.FetchablesByPrefabId();
            owner.UpdatePickups(null, 0);
            Check(owner.OriginalCalls == (expected ? 0 : 1), "original body execution must follow compatibility decision");
            Console.WriteLine("PASS fetch compatibility " + scenario + ": 4 assertions, real Harmony lifecycle");
        }

        private static bool HasOwnPrefix(MethodBase target, string owner)
        {
            return Harmony.GetPatchInfo(target)?.Prefixes.Any(patch => patch.owner == owner) == true;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
