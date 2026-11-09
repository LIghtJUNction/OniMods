using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    internal static class PropertyTexturePublicationPatch
    {
        private static bool supported;

        [HarmonyPatch(typeof(PropertyTextures), "LateUpdate")]
        internal static class PublicationScope
        {
            private static void Prefix(out PresentationUploadGate.Scope __state)
            {
                __state = PresentationUploadGate.Begin(supported);
            }

            private static Exception Finalizer(Exception __exception, PresentationUploadGate.Scope __state)
            {
                PresentationUploadGate.End(__state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(PropertyTextures), "UpdateProperty")]
        internal static class ExternalUploads
        {
            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                MethodInfo load = AccessTools.Method(typeof(Texture2D), "LoadRawTextureData", new[] { typeof(IntPtr), typeof(int) });
                MethodInfo apply = AccessTools.Method(typeof(Texture2D), "Apply", Type.EmptyTypes);
                // Preserve unknown or already-replaced method bodies. The pinned
                // production body has five external loads and one shared Apply.
                supported = load != null && apply != null
                    && code.Count(item => item.Calls(load)) == 5 && code.Count(item => item.Calls(apply)) == 1;
                if (!supported)
                    return code;
                MethodInfo guardedLoad = AccessTools.Method(typeof(PresentationUploadGate), "LoadRawTextureData");
                MethodInfo guardedApply = AccessTools.Method(typeof(PresentationUploadGate), "Apply");
                foreach (var instruction in code)
                {
                    if (instruction.Calls(load))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = guardedLoad;
                    }
                    else if (instruction.Calls(apply))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = guardedApply;
                    }
                }
                return code;
            }
        }
    }
}
