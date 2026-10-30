using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;

// Original synthetic model of the public field-writing and version protocol.
// It uses ordinary managed arrays, not the game's overlapping-reference array
// trick, and contains no game binaries or rendering implementation.
namespace UnityEngine
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
}

public static class KAnim
{
    public static class Build
    {
        public struct SymbolFrameInstance
        {
            public int buildImageIdx;
            public UnityEngine.Vector2 bboxMin, bboxMax, uvMin, uvMax;
        }
    }
}

public sealed class KBatchGroupData
{
    public List<KAnim.Build.SymbolFrameInstance> symbolFrameInstances
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get;
        private set;
    }
    public KBatchGroupData(params KAnim.Build.SymbolFrameInstance[] frames)
    {
        symbolFrameInstances = new List<KAnim.Build.SymbolFrameInstance>(frames);
    }
}

public sealed class SymbolOverrideInfoGpuData
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SymbolOverrideInfo
    {
        public float atlas, isoverriden, unused1, unused2;
        public UnityEngine.Vector2 bboxMin, bboxMax, uvMin, uvMax;
    }

    private readonly SymbolOverrideInfo[] entries;
    private SymbolOverrideInfo[] symbolOverrideInfos
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return entries; }
    }
    public int version
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get;
        [MethodImpl(MethodImplOptions.NoInlining)] private set;
    }

    public SymbolOverrideInfoGpuData(int symbols)
    {
        entries = new SymbolOverrideInfo[symbols];
        version = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MarkDirty() { version = unchecked(version + 1); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetSymbolOverrideInfo(int symbol_idx, ref KAnim.Build.SymbolFrameInstance frame)
    {
        ref var entry = ref symbolOverrideInfos[symbol_idx];
        entry.atlas = frame.buildImageIdx;
        entry.isoverriden = 1f;
        entry.bboxMin = frame.bboxMin;
        entry.bboxMax = frame.bboxMax;
        entry.uvMin = frame.uvMin;
        entry.uvMax = frame.uvMax;
        MarkDirty();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetSymbolOverrideInfo(int symbol_start_idx, int symbol_num_frames, int atlas_idx,
        KBatchGroupData source_data, int source_start_idx, int source_num_frames)
    {
        for (int offset = 0; offset < symbol_num_frames; offset++)
        {
            var frame = source_data.symbolFrameInstances[
                source_start_idx + Math.Min(source_num_frames - 1, offset)];
            ref var entry = ref symbolOverrideInfos[symbol_start_idx + offset];
            entry.atlas = atlas_idx;
            entry.isoverriden = 1f;
            entry.bboxMin = frame.bboxMin;
            entry.bboxMax = frame.bboxMax;
            entry.uvMin = frame.uvMin;
            entry.uvMax = frame.uvMax;
        }
        MarkDirty();
    }

    internal SymbolOverrideInfo[] Entries { get { return entries; } }
    internal void SetVersionForTest(int value) { version = value; }
    internal int ByteCount { get { return entries.Length * 48; } }
    internal void CopyBytesTo(byte[] destination)
    {
        MemoryMarshal.AsBytes(entries.AsSpan()).CopyTo(destination);
    }
    internal byte[] Snapshot()
    {
        var result = new byte[ByteCount];
        CopyBytesTo(result);
        return result;
    }
}

namespace CycleTrim.Optimizations.Tests
{
    internal static class AnimationSymbolFixture
    {
        internal static readonly MethodInfo One = AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData),
            "SetSymbolOverrideInfo", new[] { typeof(int), typeof(KAnim.Build.SymbolFrameInstance).MakeByRefType() });
        internal static readonly MethodInfo Range = AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData),
            "SetSymbolOverrideInfo", new[] { typeof(int), typeof(int), typeof(int), typeof(KBatchGroupData),
                typeof(int), typeof(int) });
        private static bool originalsInstalled;

        internal static void InstallOriginalCopies()
        {
            if (originalsInstalled) return;
            var harmony = new Harmony("cycletrim.tests.animation.original");
            harmony.CreateReversePatcher(One, new HarmonyMethod(typeof(AnimationSymbolFixture),
                "OriginalOne")).Patch(HarmonyReversePatchType.Original);
            harmony.CreateReversePatcher(Range, new HarmonyMethod(typeof(AnimationSymbolFixture),
                "OriginalRange")).Patch(HarmonyReversePatchType.Original);
            originalsInstalled = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void OriginalOne(SymbolOverrideInfoGpuData instance, int index,
            ref KAnim.Build.SymbolFrameInstance frame)
        {
            throw new InvalidOperationException("Original animation stand-in not installed");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void OriginalRange(SymbolOverrideInfoGpuData instance, int start, int count,
            int atlas, KBatchGroupData source, int sourceStart, int sourceCount)
        {
            throw new InvalidOperationException("Original animation stand-in not installed");
        }

        internal static KAnim.Build.SymbolFrameInstance Frame(int seed)
        {
            return new KAnim.Build.SymbolFrameInstance
            {
                buildImageIdx = seed % 17,
                bboxMin = new UnityEngine.Vector2(seed + 0.25f, seed + 0.5f),
                bboxMax = new UnityEngine.Vector2(seed + 1.25f, seed + 1.5f),
                uvMin = new UnityEngine.Vector2(seed * 0.01f, seed * 0.02f),
                uvMax = new UnityEngine.Vector2(seed * 0.03f, seed * 0.04f)
            };
        }

        internal sealed class TextureConsumer
        {
            private SymbolOverrideInfoGpuData owner;
            private int uploadedVersion;
            internal readonly byte[] Bytes;
            internal long Copies, BytesCopied;
            internal TextureConsumer(int symbols) { Bytes = new byte[symbols * 48]; }

            // Model only the game's object-identity + version copy contract.
            // Copying managed bytes is not a GPU upload or a graphics benchmark.
            internal void Update(SymbolOverrideInfoGpuData data)
            {
                if (ReferenceEquals(owner, data) && uploadedVersion == data.version) return;
                data.CopyBytesTo(Bytes);
                owner = data;
                uploadedVersion = data.version;
                Copies++;
                BytesCopied += Bytes.Length;
            }
        }
    }
}
