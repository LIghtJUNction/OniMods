using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class PowerOverlayOptimizationPatch
    {
        private static readonly MethodInfo UpdateMethod = AccessTools.DeclaredMethod(
            typeof(OverlayModes.Power), "Update", Type.EmptyTypes);
        private static readonly MethodInfo WattsMethod = AccessTools.DeclaredMethod(
            typeof(CircuitManager), "GetWattsNeededWhenActive", new[] { typeof(ushort) });
        private static bool callSiteMatched;
        private static bool reportedMismatch;

        private static bool Prepare()
        {
            if (!CycleTrimPatchOptions.OptimizePowerOverlay)
                return false;
            var valid = UpdateMethod != null && !UpdateMethod.IsStatic
                && UpdateMethod.ReturnType == typeof(void)
                && WattsMethod != null && !WattsMethod.IsStatic && !WattsMethod.IsVirtual
                && WattsMethod.ReturnType == typeof(float);
            if (!valid)
                ReportMismatch();
            return valid;
        }

        private static MethodBase TargetMethod() { return UpdateMethod; }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(bool __runOriginal, out PowerOverlayCache __state)
        {
            __state = null;
            if (__runOriginal)
            {
                // Until there is a repeated query, every value is obtained from
                // the original method. Do not pay for metadata or Harmony scans
                // in an update which has no answer to reuse.
                __state = PowerOverlayCache.Enter(callSiteMatched,
                    PowerOverlayCompatibility.CurrentVersion);
            }
        }

        private static Exception Finalizer(Exception __exception, PowerOverlayCache __state)
        {
            PowerOverlayCache.Exit(__state);
            return __exception;
        }

        [HarmonyPriority(Priority.Last)]
        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var original = new List<CodeInstruction>(instructions);
            var replacementAt = -1;
            var matches = 0;
            for (var index = 0; index < original.Count; index++)
            {
                var instruction = original[index];
                if (!Equals(instruction.operand, WattsMethod))
                    continue;
                matches++;
                if (instruction.opcode == OpCodes.Callvirt
                    && (index == 0 || original[index - 1].opcode.OpCodeType != OpCodeType.Prefix))
                    replacementAt = index;
            }

            callSiteMatched = matches == 1 && replacementAt >= 0;
            if (!callSiteMatched)
            {
                ReportMismatch();
                return original;
            }

            // Copy the instruction, retaining its branch labels and exception
            // boundaries. The receiver and argument already occupy the stack.
            var replacement = new CodeInstruction(original[replacementAt]);
            replacement.opcode = OpCodes.Call;
            replacement.operand = AccessTools.Method(typeof(PowerOverlayOptimizationPatch),
                nameof(ReadWatts), new[] { typeof(CircuitManager), typeof(ushort) });
            original[replacementAt] = replacement;
            return original;
        }

        internal static float ReadWatts(CircuitManager manager, ushort circuit)
        {
            // The replaced callvirt checks its receiver before entering the
            // method; do not turn a null receiver into a cache key.
            if (ReferenceEquals(manager, null))
                throw new NullReferenceException();

            var lease = PowerOverlayCache.Current;
            if (lease == null || !lease.Enabled)
                return manager.GetWattsNeededWhenActive(circuit);

            if (!PowerOverlayCompatibility.IsCurrent(lease.EnvironmentVersion))
            {
                lease.DisableReuse();
                return manager.GetWattsNeededWhenActive(circuit);
            }

            float watts;
            if (lease.TryGet(manager, circuit, out watts))
            {
                if (lease.CompatibilityChecked || ConfirmReuse(lease))
                    return watts;
                // A rejected lease is empty and disabled. Later calls in this
                // update neither rescan compatibility nor retain more answers.
                return manager.GetWattsNeededWhenActive(circuit);
            }

            var version = lease.Version;
            watts = manager.GetWattsNeededWhenActive(circuit);
            lease.Remember(manager, circuit, watts, version);
            return watts;
        }

        private static bool ConfirmReuse(PowerOverlayCache lease)
        {
            var version = lease.Version;
            int environmentVersion;
            var compatible = PowerOverlayCompatibility.CanReuse(out environmentVersion)
                && environmentVersion == lease.EnvironmentVersion
                && !HasForeignPatch(UpdateMethod) && !HasForeignPatch(WattsMethod)
                && PowerOverlayCompatibility.IsCurrent(environmentVersion)
                && ReferenceEquals(PowerOverlayCache.Current, lease) && lease.Version == version;
            if (compatible)
                lease.ConfirmCompatibility();
            else
                lease.DisableReuse();
            return compatible;
        }

        private static bool HasForeignPatch(MethodBase method)
        {
            var patches = Harmony.GetPatchInfo(method);
            return patches != null && (HasForeignPatch(patches.Prefixes)
                || HasForeignPatch(patches.Postfixes) || HasForeignPatch(patches.Transpilers)
                || HasForeignPatch(patches.Finalizers));
        }

        private static bool HasForeignPatch(IEnumerable<Patch> patches)
        {
            foreach (var patch in patches)
            {
                if (patch.PatchMethod.DeclaringType != typeof(PowerOverlayOptimizationPatch))
                    return true;
            }
            return false;
        }

        private static void ReportMismatch()
        {
            if (reportedMismatch)
                return;
            reportedMismatch = true;
            Console.WriteLine("[CycleTrim] Power overlay call shape changed; retaining the original calls.");
        }
    }
}
