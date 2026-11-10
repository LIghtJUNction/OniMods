using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CycleTrim.Core;
using OniSim.Compatibility;

namespace CycleTrim.PerformanceProbe.Tests
{
    /// <summary>
    /// Authors a native publication without loading game assemblies. Construct
    /// and dispose outside timed samples; reads use the production publication
    /// reader and its native publication lease.
    /// Launch the host with CYCLETRIM_ONISIM_GPU=0 before loading the library.
    /// </summary>
    internal sealed class NativeMulticomponentFixture : IDisposable
    {
        internal const ushort VacuumElementId = 0;
        internal const ushort PrimaryElementId = 3;
        internal const ushort SecondaryElementId = 4;
        internal const float PrimaryMass = 1.5f;
        internal const float SecondaryMass = 0.5f;
        internal const float Temperature = 300f;

        private const int ElementCount = 5;
        private const int CreateElementsMessage = 1108437482;
        private const int CreateDiseasesMessage = 825301935;
        private const int ModifyCellMessage = -1252920804;
        private const int MaximumMessageBatch = 4096;
        private const int MaximumQueuedCommands = 32768;

        private OniSimSession session;
        private OniSimPublicationReader reader;

        internal string NativePath { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int CellCount { get; }
        internal int MixturePercent { get; }
        internal int MixedCellCount { get; private set; }
        internal uint TotalSpeciesRecords { get; private set; }
        internal ulong PublicationGeneration { get; private set; }

        internal NativeMulticomponentFixture(string nativePath, int width, int height, int mixturePercent)
        {
            if (width <= 0 || height <= 0 || (long)width * height > 1048576)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (mixturePercent < 0 || mixturePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(mixturePercent));
            if (Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_GPU") != "0")
                throw new InvalidOperationException("Launch the native fixture host with CYCLETRIM_ONISIM_GPU=0.");

            NativePath = Path.GetFullPath(nativePath);
            if (!File.Exists(NativePath))
                throw new FileNotFoundException("The authored OniSim native library was not found.", NativePath);
            Width = width;
            Height = height;
            CellCount = checked(width * height);
            MixturePercent = mixturePercent;

            try
            {
                session = OniSimSession.Open(NativePath);
                session.SendMessage(new OfficialMessage(CreateElementsMessage, CreateElementTable()));
                byte[] diseases = new byte[8];
                WriteI32(diseases, 4, ElementCount);
                session.SendMessage(new OfficialMessage(CreateDiseasesMessage, diseases));
                session.InitializeFromCells(CreateGridImage());
                session.Start();

                AddMixturesAndPublish();
                reader = OniSimPublicationReader.Open(NativePath);
                VerifyPublication();
            }
            catch (Exception initializationError)
            {
                if (session != null)
                {
                    try { session.Dispose(); }
                    catch (Exception cleanupError)
                    {
                        throw new AggregateException("Native fixture initialization and cleanup both failed.",
                            initializationError, cleanupError);
                    }
                    session = null;
                }
                throw;
            }
        }

        internal bool IsMixedCell(int cell)
        {
            if (cell < 0 || cell >= CellCount)
                throw new ArgumentOutOfRangeException(nameof(cell));
            return cell % 100 < MixturePercent;
        }

        internal OniSimMulticomponentProvider CreateProvider(Func<ushort, Element> resolver)
        {
            if (session == null)
                throw new ObjectDisposedException(nameof(NativeMulticomponentFixture));
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));
            // Do not add counters or the session facade's managed GlobalGate to
            // these delegates: this is the exact production native read path.
            return new OniSimMulticomponentProvider(reader.Acquire, reader.Release, resolver);
        }

        internal void VerifyLeaseDuringPublicationAndShutdown()
        {
            IntPtr pointer = reader.Acquire();
            Task worker = null;
            try
            {
                var old = Marshal.PtrToStructure<NativeMulticomponentUpdate>(pointer);
                var oldCell = Marshal.PtrToStructure<NativeMulticomponentCellHeader>(old.Cells);
                Require(reader.ContentRevision == old.Generation, "current revision matches acquired descriptor");
                worker = Task.Run(() =>
                {
                    byte[] payload = new byte[28];
                    WriteI32(payload, 4, -1);
                    WriteF32(payload, 8, Temperature);
                    WriteF32(payload, 12, SecondaryMass);
                    WriteU16(payload, 20, SecondaryElementId);
                    payload[23] = byte.MaxValue;
                    session.SendMessage(new OfficialMessage(ModifyCellMessage, payload));
                    Publish(new[] { new ActiveRegion(0, 0, Width, Height) }, new byte[CellCount]);
                });
                Require(worker.Wait(5000), "a held reader must not block worker publication");
                Require(reader.ContentRevision > old.Generation, "mutation publishes a new content revision");
                Require(Marshal.PtrToStructure<NativeMulticomponentCellHeader>(old.Cells).TotalMass == oldCell.TotalMass,
                    "held old publication stays immutable after worker completes");
                worker = Task.Run(() => session.Dispose());
                Require(worker.Wait(5000), "a held reader must not block shutdown");
                session = null;
                Require(reader.ContentRevision == 0, "shutdown invalidates the revision");
                Require(Marshal.PtrToStructure<NativeMulticomponentCellHeader>(old.Cells).TotalMass == oldCell.TotalMass,
                    "reader retains its data after shutdown");
            }
            finally
            {
                reader.Release();
                if (worker != null && !worker.IsCompleted)
                    worker.Wait(5000);
            }
        }

        private void AddMixturesAndPublish()
        {
            var messages = new List<OfficialMessage>(MaximumMessageBatch);
            var regions = new[] { new ActiveRegion(0, 0, Width, Height) };
            var visibility = new byte[CellCount];
            Array.Fill(visibility, byte.MaxValue);
            int queued = 0;

            for (int cell = 0; cell < CellCount; cell++)
            {
                if (!IsMixedCell(cell))
                    continue;
                byte[] payload = new byte[28];
                WriteI32(payload, 0, cell);
                WriteI32(payload, 4, -1); // No callback index.
                WriteF32(payload, 8, Temperature);
                WriteF32(payload, 12, SecondaryMass);
                WriteU16(payload, 20, SecondaryElementId);
                payload[23] = byte.MaxValue; // No disease; replace_type=0 adds a species.
                messages.Add(new OfficialMessage(ModifyCellMessage, payload));
                queued++;

                if (messages.Count == MaximumMessageBatch)
                {
                    session.SendBatch(messages);
                    messages.Clear();
                }
                if (queued == MaximumQueuedCommands)
                {
                    Publish(regions, visibility);
                    queued = 0;
                }
            }
            if (messages.Count != 0)
                session.SendBatch(messages);
            // Also publish an empty fixture, and settle the final worker handoff.
            Publish(regions, visibility);
        }

        private void Publish(ActiveRegion[] regions, byte[] visibility)
        {
            session.NewFrame(0f, regions);
            // The first Prepare can return the worker's already completed batch.
            // The second returns the batch containing the submitted zero-time
            // frame. Commands execute, but no physics can alter the fixture.
            session.Prepare(visibility);
            session.Prepare(visibility);
        }

        private byte[] CreateGridImage()
        {
            byte[] image = GridImage.Create(Width, Height, 42, 0, true).ToArray();
            int diseases = 14 + 16 * CellCount;
            for (int cell = 0; cell < CellCount; cell++)
            {
                int offset = 14 + cell * 16;
                WriteU16(image, offset, PrimaryElementId);
                image[offset + 3] = byte.MaxValue; // Insulation.
                WriteF32(image, offset + 8, Temperature);
                WriteF32(image, offset + 12, PrimaryMass);
                image[diseases + cell * 12] = byte.MaxValue;
            }
            // Zeroed backwalls refer to VacuumElementId with zero mass/temperature.
            return image;
        }

        private static byte[] CreateElementTable()
        {
            // Same authored wire layout as OniSim/conformance/host.py encode_elements:
            // count, 164-byte records, then all length-prefixed UTF-8 names.
            string[] names = { "Vacuum", "Unobtanium", "Void", "AuthoredPrimaryGas", "AuthoredSecondaryGas" };
            int[] hashes = { 758759285, 1838482828, -1456075980, 11110001, 11110002 };
            byte[] phases = { 0, 7, 0, 1, 1 };
            byte[][] encodedNames = new byte[ElementCount][];
            int size = 4 + ElementCount * 164;
            for (int i = 0; i < ElementCount; i++)
            {
                encodedNames[i] = Encoding.UTF8.GetBytes(names[i]);
                size += 4 + encodedNames[i].Length;
            }
            byte[] payload = new byte[size];
            WriteI32(payload, 0, ElementCount);
            float[] physical = { 1f, 0f, 32f, 25f, 1f, 1f, 0.1f, 1f, 0.01f, 0.01f, 20000f, 0f, 10000f, 100f };
            for (int i = 0; i < ElementCount; i++)
            {
                int offset = 4 + i * 164;
                WriteI32(payload, offset, hashes[i]);
                WriteU16(payload, offset + 4, ushort.MaxValue);
                WriteU16(payload, offset + 6, ushort.MaxValue);
                WriteU16(payload, offset + 8, (ushort)i);
                payload[offset + 10] = phases[i];
                for (int field = 0; field < physical.Length; field++)
                    WriteF32(payload, offset + 12 + field * 4, physical[field]);
                if (phases[i] == 0)
                    WriteF32(payload, offset + 52, 0f);
                WriteI32(payload, offset + 68, hashes[VacuumElementId]);
                WriteI32(payload, offset + 76, hashes[VacuumElementId]);
                WriteU16(payload, offset + 84, ushort.MaxValue);
                WriteU16(payload, offset + 86, ushort.MaxValue);
                WriteF32(payload, offset + 152, Temperature);
                WriteF32(payload, offset + 160, 1f);
            }
            int nameOffset = 4 + ElementCount * 164;
            foreach (byte[] name in encodedNames)
            {
                WriteI32(payload, nameOffset, name.Length);
                nameOffset += 4;
                Buffer.BlockCopy(name, 0, payload, nameOffset, name.Length);
                nameOffset += name.Length;
            }
            return payload;
        }

        private void VerifyPublication()
        {
            IntPtr pointer = reader.Acquire();
            try
            {
                var update = Marshal.PtrToStructure<NativeMulticomponentUpdate>(pointer);
                Require(update.Version == OniSimMulticomponentProvider.CurrentVersion
                    && update.Flags == OniSimMulticomponentProvider.FlagMulticomponentCoexistence
                    && update.Generation != 0 && update.CellCount == CellCount
                    && update.Cells != IntPtr.Zero && update.Species != IntPtr.Zero,
                    "native publication descriptor");
                int mixed = 0;
                for (int cell = 0; cell < CellCount; cell++)
                {
                    bool expectedMixture = IsMixedCell(cell);
                    var header = Marshal.PtrToStructure<NativeMulticomponentCellHeader>(
                        IntPtr.Add(update.Cells, checked(cell * 24)));
                    int expectedCount = expectedMixture ? 2 : 1;
                    Require(header.SpeciesCount == expectedCount && header.Reserved == 1
                        && (ulong)header.SpeciesOffset + header.SpeciesCount <= update.TotalSpeciesRecords
                        && header.TotalMass == PrimaryMass + (expectedMixture ? SecondaryMass : 0f),
                        "native cell " + cell);
                    for (int record = 0; record < expectedCount; record++)
                    {
                        int offset = checked((int)(header.SpeciesOffset + (uint)record) * 20);
                        var species = Marshal.PtrToStructure<NativeMulticomponentSpeciesRecord>(
                            IntPtr.Add(update.Species, offset));
                        Require(species.ElementId == (record == 0 ? PrimaryElementId : SecondaryElementId)
                            && species.Phase == 1 && species.Reserved == 0 && species.Temperature == Temperature
                            && species.Mass == (record == 0 ? PrimaryMass : SecondaryMass),
                            "native species in cell " + cell);
                    }
                    if (expectedMixture)
                        mixed++;
                }
                Require(update.TotalSpeciesRecords == CellCount + mixed, "native species record count");
                MixedCellCount = mixed;
                TotalSpeciesRecords = update.TotalSpeciesRecords;
                PublicationGeneration = update.Generation;
            }
            finally { reader.Release(); }
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition)
                throw new InvalidDataException("Native multicomponent fixture does not match its authored input: " + detail);
        }

        private static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteI32(byte[] data, int offset, int value)
        {
            for (int i = 0; i < 4; i++)
                data[offset + i] = (byte)(value >> (8 * i));
        }

        private static void WriteF32(byte[] data, int offset, float value)
        {
            WriteI32(data, offset, BitConverter.SingleToInt32Bits(value));
        }

        public void Dispose()
        {
            if (session == null)
                return;
            session.Dispose();
            session = null;
            reader = null;
        }
    }
}
