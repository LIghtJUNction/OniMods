using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

// Independent host model of the external upload call shape. Unity graphics
// drivers, cameras and the game's simulation are not implemented by this stub.
public sealed class PropertyTextures
{
    public static IntPtr externalFlowTex, externalLiquidTex, externalLiquidDataTex,
        externalMaterialDataTex, externalExposedToSunlight;
    public readonly Texture2D[] Textures = new Texture2D[5];
    public int LateUpdates, LerpUpdates, LerpBuffer;
    public bool Loading;
    public Action BeforeProperties;
    public Action AfterProperties;

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
        BeforeProperties?.Invoke();
        for (int i = 0; i < Textures.Length; i++) UpdateProperty(i);
        AfterProperties?.Invoke();
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RenderUnpatched()
    {
        LateUpdates++;
        BeforeProperties?.Invoke();
        for (int i = 0; i < Textures.Length; i++) UpdatePropertyOriginal(i);
        AfterProperties?.Invoke();
        LerpUpdates += 3;
        LerpBuffer ^= 1;
    }

    // Frozen unpatched call shape: the benchmark changes only whether the five
    // loads/shared Apply pass through the production gate, not the host loop.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdatePropertyOriginal(int property)
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

    public static IntPtr Pointer(int index)
    {
        switch (index)
        {
            case 0: return externalFlowTex;
            case 1: return externalLiquidTex;
            case 2: return externalLiquidDataTex;
            case 3: return externalMaterialDataTex;
            default: return externalExposedToSunlight;
        }
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
        public int width { get; private set; } = 1;
        public int height { get; private set; } = 1;
        public uint updateCount { get; private set; }
        public int LoadCalls, ApplyCalls;
        public long LoadedBytes;
        public bool ThrowLoad, ThrowApply;
        public byte[] CpuBytes = Array.Empty<byte>();
        public byte[] UploadedBytes = Array.Empty<byte>();
        public Action BeforeLoad, BeforeApply;

        public Texture2D() { }
        public Texture2D(int width, int height, int stride)
        {
            Reinitialize(width, height, stride);
        }

        public void Reinitialize(int newWidth, int newHeight, int stride)
        {
            width = newWidth; height = newHeight;
            CpuBytes = new byte[checked(width * height * stride)];
            UploadedBytes = new byte[CpuBytes.Length];
            updateCount++;
        }

        public void ModifyExternally(byte value)
        {
            if (UploadedBytes.Length != 0) UploadedBytes[0] = value;
            updateCount++;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void LoadRawTextureData(IntPtr pointer, int length)
        {
            LoadCalls++;
            BeforeLoad?.Invoke();
            if (ThrowLoad) throw new InvalidOperationException("injected texture load failure");
            if (pointer == IntPtr.Zero || length != CpuBytes.Length)
                throw new ArgumentException("texture input does not match its dimensions");
            Marshal.Copy(pointer, CpuBytes, 0, length);
            LoadedBytes += length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Apply()
        {
            ApplyCalls++;
            BeforeApply?.Invoke();
            if (ThrowApply) throw new InvalidOperationException("injected texture apply failure");
            Array.Copy(CpuBytes, UploadedBytes, CpuBytes.Length);
            updateCount++;
        }
    }
}
