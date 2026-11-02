using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class SaveTypeMetadataPatch
    {
        private static bool Prepare()
        {
            if (!CycleTrimPatchOptions.OptimizeSaveMetadata)
                return false;
            if (TargetMethod() != null)
                return true;
            Console.WriteLine("[CycleTrim] Save metadata optimization skipped: unsupported save method.");
            return false;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(SaveLoadRoot), "SaveWithoutTransform",
                new[] { typeof(BinaryWriter) });
        }

        // Run after other save transpilers so they still see the original calls.
        // No serialization loop, object state or written byte is cached here.
        [HarmonyPriority(Priority.Last)]
        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var original = new List<CodeInstruction>(instructions);
            var attributeQuery = AccessTools.Method(typeof(MemberInfo), "IsDefined",
                new[] { typeof(Type), typeof(bool) });
            var objectType = AccessTools.Method(typeof(object), "GetType", Type.EmptyTypes);
            var objectString = AccessTools.Method(typeof(object), "ToString", Type.EmptyTypes);
            var checks = new List<int>(2);
            var names = new List<int>(1);
            var prefixedCall = false;

            for (var index = 0; index < original.Count; index++)
            {
                var instruction = original[index];
                if (instruction.opcode != OpCodes.Callvirt)
                    continue;
                if (instruction.Calls(attributeQuery))
                {
                    checks.Add(index);
                    prefixedCall |= index > 0 && original[index - 1].opcode.OpCodeType == OpCodeType.Prefix;
                }
                else if (instruction.Calls(objectString) && index > 0
                    && original[index - 1].Calls(objectType))
                    names.Add(index);
            }

            // Apply as one unit. A changed game body or another optimization
            // keeps its complete instruction stream instead of a partial rewrite.
            if (checks.Count != 2 || names.Count != 1 || prefixedCall)
            {
                Console.WriteLine("[CycleTrim] Save metadata optimization skipped: expected "
                    + "2 attribute queries and 1 type name, found " + checks.Count + " and "
                    + names.Count + (prefixedCall ? " with an unsupported IL prefix." : "."));
                return original;
            }

            var cachedQuery = AccessTools.Method(typeof(SaveTypeMetadataCache), "IsDefined");
            var cachedName = AccessTools.Method(typeof(SaveTypeMetadataCache), "TypeName");
            foreach (var index in checks)
                original[index] = ReplaceCall(original[index], cachedQuery);
            foreach (var index in names)
                original[index] = ReplaceCall(original[index], cachedName);
            return original;
        }

        private static CodeInstruction ReplaceCall(CodeInstruction instruction, MethodInfo method)
        {
            // Keep branches and exception-region boundaries on the same instruction.
            return new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = method };
        }
    }
}
