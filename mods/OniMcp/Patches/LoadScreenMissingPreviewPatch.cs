using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using UnityEngine;

namespace OniMcp.Patches
{
    [HarmonyPatch]
    internal static class LoadScreenMissingPreviewPatch
    {
        private static readonly MethodInfo NativeMigration = AccessTools.Method(typeof(LoadScreen), "MigrateFile",
            new[] { typeof(string), typeof(string), typeof(bool) });
        private static readonly MethodInfo SafeMigration = AccessTools.Method(typeof(LoadScreenMissingPreviewPatch), nameof(MigrateFile));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LoadScreen), "CheckCloudLocalOverlap");
            yield return AccessTools.Method(typeof(LoadScreen), "MigrateSave");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // MigrateFile has an exception filter unsupported by the game's
            // MonoMod emitter. Adapt only its filter-free callers.
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && Equals(instruction.operand, NativeMigration))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = SafeMigration;
                }
                yield return instruction;
            }
        }

        private static bool MigrateFile(LoadScreen screen, string source, string dest, bool ignoreMissing)
        {
            if (LoadScreenPreviewPolicy.IsMissingOptionalPreview(source, ignoreMissing))
            {
                Debug.Log("[OniMcp] Load menu skipped a missing optional save preview.");
                return true;
            }
            try
            {
                return (bool)NativeMigration.Invoke(screen, new object[] { source, dest, ignoreMissing });
            }
            catch (TargetInvocationException error)
            {
                if (error.InnerException != null)
                    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
