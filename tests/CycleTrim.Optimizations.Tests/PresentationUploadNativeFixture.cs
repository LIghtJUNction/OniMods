using System;
using System.Runtime.InteropServices;
using System.Text;
using OniSim.Compatibility;

namespace CycleTrim.Optimizations.Tests
{
    // Authored input only. Timing calls the same native message exports that
    // Game.StepTheSim uses, then assigns the five returned descriptor pointers.
    internal sealed class PresentationUploadNativeFixture : IDisposable
    {
        private const int Elements = 5;
        internal readonly NativeApi Api;
        internal readonly OniSimPublicationReader Reader;
        internal readonly int Width, Height;
        internal int LastFramesProcessed;
        private OniSimSession session;
        private GCHandle framePin, visibilityPin, changePin;
        private readonly byte[] change = new byte[28];
        private readonly byte[] comparison;
        private bool pinned;

        internal PresentationUploadNativeFixture(string path, int width, int height)
        {
            if (Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_GPU") != "0")
                throw new InvalidOperationException("The native publication host requires CYCLETRIM_ONISIM_GPU=0.");
            Width = width; Height = height;
            comparison = new byte[checked(width * height * 8)];
            Api = NativeApi.LoadExplicit(path);
            Reader = OniSimPublicationReader.Open(path);
            try
            {
                session = OniSimSession.Open(path);
                session.SendMessage(new OfficialMessage(1108437482, ElementTable()));
                byte[] diseases = new byte[8];
                WriteInt(diseases, 4, Elements);
                session.SendMessage(new OfficialMessage(825301935, diseases));
                session.InitializeFromCells(GridImage());
                session.Start();
                var frame = new byte[28];
                WriteInt(frame, 12, width); WriteInt(frame, 16, height);
                var visibility = new byte[checked(width * height)];
                for (int i = 0; i < visibility.Length; i++) visibility[i] = 255;
                framePin = GCHandle.Alloc(frame, GCHandleType.Pinned);
                visibilityPin = GCHandle.Alloc(visibility, GCHandleType.Pinned);
                changePin = GCHandle.Alloc(change, GCHandleType.Pinned);
                pinned = true;
                WriteInt(change, 0, checked((height / 2) * width + width / 2));
                WriteInt(change, 4, -1);
                WriteFloat(change, 8, 300f);
                change[22] = 1; // Replace the selected cell, without displacement.
                change[23] = 255;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Step()
        {
            Api.HandleMessage(-775326397, 28, framePin.AddrOfPinnedObject());
            CheckStatus();
            PrepareOnly();
        }

        internal void PrepareOnly()
        {
            IntPtr descriptor = Api.HandleMessage(1078620451, Width * Height, visibilityPin.AddrOfPinnedObject());
            CheckStatus();
            PresentationUploadRegression.Check(descriptor != IntPtr.Zero, "Prepare returns its production descriptor");
            LastFramesProcessed = Marshal.ReadInt32(descriptor);
            // Same five assignments as Game.StepTheSim, with no image copied by
            // this host until the production upload gate reaches the Unity stub.
            PropertyTextures.externalFlowTex = Marshal.ReadIntPtr(descriptor, 456);
            PropertyTextures.externalLiquidTex = Marshal.ReadIntPtr(descriptor, 464);
            PropertyTextures.externalLiquidDataTex = Marshal.ReadIntPtr(descriptor, 472);
            PropertyTextures.externalMaterialDataTex = Marshal.ReadIntPtr(descriptor, 480);
            PropertyTextures.externalExposedToSunlight = Marshal.ReadIntPtr(descriptor, 488);
        }

        internal void ChangeAndStep(int value)
        {
            change[20] = 4; // Authored liquid, with a visible fill amount.
            WriteFloat(change, 12, 100f + value % 4 * 200f);
            Api.HandleMessage(-1252920804, change.Length, changePin.AddrOfPinnedObject());
            CheckStatus();
            // Prepare can first return an already-completed batch. The second
            // handover includes the submitted command, regardless of scheduling.
            Step(); Step();
        }

        internal PresentationUpdate Metadata()
        {
            PresentationUpdate update;
            PresentationUploadRegression.Check(Reader.TryGetPresentationUpdate(out update), "current native revision is available");
            return update;
        }

        internal void EqualPixels(PropertyTextures textures)
        {
            for (int i = 0; i < 5; i++)
            {
                int length = Width * Height * PropertyTextures.Stride(i);
                Marshal.Copy(PropertyTextures.Pointer(i), comparison, 0, length);
                byte[] actual = textures.Textures[i].UploadedBytes;
                PresentationUploadRegression.Check(actual.Length == length, "image length " + i);
                for (int at = 0; at < length; at++)
                    if (comparison[at] != actual[at])
                        throw new InvalidOperationException("published/uploaded image differs: image=" + i + ", byte=" + at);
            }
        }

        private void CheckStatus()
        {
            int status = Api.GetLastStatus();
            if (status != 0) throw new InvalidOperationException("native publication fixture status " + status);
        }

        private byte[] GridImage()
        {
            int cells = Width * Height;
            byte[] image = OniSim.Compatibility.GridImage.Create(Width, Height, 42, 0, true).ToArray();
            int diseases = 14 + cells * 16;
            for (int i = 0; i < cells; i++)
            {
                int at = 14 + i * 16;
                image[at] = 3;
                image[at + 3] = 255;
                WriteFloat(image, at + 8, 300f);
                WriteFloat(image, at + 12, 1.5f);
                image[diseases + i * 12] = 255;
            }
            return image;
        }

        private static byte[] ElementTable()
        {
            string[] names = { "Vacuum", "Unobtanium", "Void", "AuthoredGas", "AuthoredLiquid" };
            int[] hashes = { 758759285, 1838482828, -1456075980, 11110001, 11110002 };
            byte[] phases = { 0, 7, 0, 1, 2 };
            byte[][] encoded = new byte[Elements][];
            int size = 4 + Elements * 164;
            for (int i = 0; i < Elements; i++)
            {
                encoded[i] = Encoding.UTF8.GetBytes(names[i]);
                size += 4 + encoded[i].Length;
            }
            var data = new byte[size];
            WriteInt(data, 0, Elements);
            float[] physical = { 1f, 0f, 32f, 25f, 1f, 1f, 0.1f, 1f, 0.01f, 0.01f, 20000f, 0f, 10000f, 100f };
            for (int i = 0; i < Elements; i++)
            {
                int at = 4 + i * 164;
                WriteInt(data, at, hashes[i]);
                data[at + 4] = data[at + 5] = data[at + 6] = data[at + 7] = 255;
                data[at + 8] = (byte)i;
                data[at + 10] = phases[i];
                for (int field = 0; field < physical.Length; field++) WriteFloat(data, at + 12 + field * 4, physical[field]);
                if (phases[i] == 0) WriteFloat(data, at + 52, 0f);
                if (phases[i] == 2) WriteFloat(data, at + 52, 1000f);
                WriteInt(data, at + 68, hashes[0]); WriteInt(data, at + 76, hashes[0]);
                data[at + 84] = data[at + 85] = data[at + 86] = data[at + 87] = 255;
                WriteFloat(data, at + 152, 300f); WriteFloat(data, at + 160, 1f);
            }
            int cursor = 4 + Elements * 164;
            foreach (byte[] name in encoded)
            {
                WriteInt(data, cursor, name.Length); cursor += 4;
                Buffer.BlockCopy(name, 0, data, cursor, name.Length); cursor += name.Length;
            }
            return data;
        }

        private static void WriteInt(byte[] data, int at, int value)
        {
            for (int i = 0; i < 4; i++) data[at + i] = (byte)(value >> (8 * i));
        }

        private static void WriteFloat(byte[] data, int at, float value)
        {
            WriteInt(data, at, new FloatBits { Value = value }.Bits);
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] internal float Value;
            [FieldOffset(0)] internal int Bits;
        }

        public void Dispose()
        {
            if (session != null) { session.Dispose(); session = null; }
            if (pinned) { framePin.Free(); visibilityPin.Free(); changePin.Free(); pinned = false; }
        }
    }
}
