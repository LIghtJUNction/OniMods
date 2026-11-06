using System;

namespace OniSim.Compatibility
{
    // Little-endian fields of official serialized messages, not a transport envelope.
    internal static class BinaryLayout
    {
        internal const int MaxPayloadBytes = 64 * 1024 * 1024;
        internal const int MaxCells = 1048576;

        internal static void WriteU16(byte[] b, int o, ushort v)
        { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        internal static void WriteU32(byte[] b, int o, uint v)
        { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        internal static void WriteI32(byte[] b, int o, int v)
        { WriteU32(b, o, unchecked((uint)v)); }
        internal static ushort ReadU16(byte[] b, int o)
        { return (ushort)(b[o] | (b[o + 1] << 8)); }
        internal static uint ReadU32(byte[] b, int o)
        { return (uint)b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24); }
        internal static int ReadI32(byte[] b, int o)
        { return unchecked((int)ReadU32(b, o)); }
        internal static float ReadF32(byte[] b, int o)
        { return BitConverter.ToSingle(b, o); }
        internal static void WriteF32(byte[] b, int o, float value)
        { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, o, 4); }
    }
}
