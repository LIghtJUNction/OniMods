using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

// Shared host model for the frame probe's PropertyTextures.LateUpdate target.
// This keeps the pinned five LoadRawTextureData call sites and shared Apply;
// it does not implement Unity graphics or the rejected publication gate.
public sealed class PropertyTextures
{
    public static IntPtr externalFlowTex, externalLiquidTex, externalLiquidDataTex,
        externalMaterialDataTex, externalExposedToSunlight;
    public readonly Texture2D[] Textures = new Texture2D[5];
    public int LateUpdates, LerpUpdates, LerpBuffer;
    public bool Loading;

    public PropertyTextures(int width, int height)
    {
        Grid.WidthInCells = width;
        Grid.HeightInCells = height;
        for (int i = 0; i < Textures.Length; i++)
            Textures[i] = new Texture2D(width, height, Stride(i));
    }

    public static int Stride(int index) { return index == 0 ? 8 : index == 4 ? 1 : 4; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LateUpdate()
    {
        LateUpdates++;
        for (int i = 0; i < Textures.Length; i++) UpdateProperty(i);
        LerpUpdates += 3;
        LerpBuffer ^= 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateProperty(int property)
    {
        if (Loading) return;
        int cells = Grid.WidthInCells * Grid.HeightInCells;
        switch (property)
        {
            case 0: Textures[property].LoadRawTextureData(externalFlowTex, cells * 8); break;
            case 1: Textures[property].LoadRawTextureData(externalLiquidTex, cells * 4); break;
            case 2: Textures[property].LoadRawTextureData(externalLiquidDataTex, cells * 4); break;
            case 3: Textures[property].LoadRawTextureData(externalMaterialDataTex, cells * 4); break;
            case 4: Textures[property].LoadRawTextureData(externalExposedToSunlight, cells); break;
        }
        Textures[property].Apply();
    }

    public static void SetPointers(IntPtr[] pointers)
    {
        externalFlowTex = pointers[0]; externalLiquidTex = pointers[1]; externalLiquidDataTex = pointers[2];
        externalMaterialDataTex = pointers[3]; externalExposedToSunlight = pointers[4];
    }
}

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        public int LoadCalls, ApplyCalls;
        public byte[] CpuBytes = Array.Empty<byte>();
        public byte[] UploadedBytes = Array.Empty<byte>();

        public Texture2D() { }
        public Texture2D(int width, int height, int stride)
        {
            CpuBytes = new byte[checked(width * height * stride)];
            UploadedBytes = new byte[CpuBytes.Length];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void LoadRawTextureData(IntPtr pointer, int length)
        {
            if (pointer == IntPtr.Zero || length != CpuBytes.Length)
                throw new ArgumentException("texture input does not match its dimensions");
            LoadCalls++;
            Marshal.Copy(pointer, CpuBytes, 0, length);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Apply()
        {
            ApplyCalls++;
            Array.Copy(CpuBytes, UploadedBytes, CpuBytes.Length);
        }
    }
}

