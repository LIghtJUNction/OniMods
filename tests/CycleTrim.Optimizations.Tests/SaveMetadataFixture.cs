using System;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Original test fixtures, not game assemblies or a copy of ONI serialization.
// The call signatures let real Harmony install the linked production transpiler.
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class SkipSaveFileSerialization : Attribute { }

public class SyntheticSaveComponent
{
    public int Value;
    public virtual void Write(BinaryWriter writer) { writer.Write(Value); }
}

public sealed class SaveLoadRoot
{
    public SyntheticSaveComponent[] Components = new SyntheticSaveComponent[0];

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void SaveWithoutTransform(BinaryWriter writer)
    {
        int count = 0;
        foreach (var component in Components)
            if (component != null && !component.GetType().IsDefined(typeof(SkipSaveFileSerialization), false))
                count++;
        writer.Write(count);
        foreach (var component in Components)
        {
            if (component != null && !component.GetType().IsDefined(typeof(SkipSaveFileSerialization), false))
            {
                writer.Write(component.GetType().ToString());
                component.Write(writer);
            }
        }
    }
}

namespace CycleTrim.Optimizations.Tests
{
    internal static class SaveMetadataFixture
    {
        private static bool originalInstalled;

        internal static void InstallOriginal()
        {
            if (originalInstalled) return;
            new Harmony("cycletrim.tests.save.original").CreateReversePatcher(
                AccessTools.DeclaredMethod(typeof(SaveLoadRoot), "SaveWithoutTransform"),
                new HarmonyMethod(typeof(SaveMetadataFixture), "OriginalSave"))
                .Patch(HarmonyReversePatchType.Original);
            originalInstalled = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static void OriginalSave(SaveLoadRoot root, BinaryWriter writer)
        {
            throw new InvalidOperationException("Original save stand-in not installed");
        }
    }
}
