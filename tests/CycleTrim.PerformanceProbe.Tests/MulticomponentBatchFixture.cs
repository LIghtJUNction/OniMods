using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CycleTrim.Core;
using Jlib.Multicomponent;
using UnityEngine;

namespace CycleTrim.PerformanceProbe.Tests
{
    // Controlled native memory, not a game assembly. Hooks prove that no native
    // lease survives into resolution or drawing, including nested callbacks.
    internal sealed class MulticomponentBatchFixture : IDisposable
    {
        internal readonly NativeMulticomponentCellHeader[] Cells;
        internal readonly NativeMulticomponentSpeciesRecord[] Species;
        internal readonly Element[] Elements;
        internal NativeMulticomponentUpdate Header;
        internal readonly OniSimMulticomponentProvider Provider;
        internal Action AfterRelease;
        internal Action<ushort> BeforeResolve;
        internal int Acquires, Releases;
        internal readonly ThreadLocal<int> Depth = new ThreadLocal<int>();
        private readonly GCHandle cellsPin, speciesPin;
        private readonly IntPtr descriptor;

        internal MulticomponentBatchFixture(int cells = 256, int speciesPerCell = 2, bool versioned = false)
        {
            Cells = new NativeMulticomponentCellHeader[cells];
            Species = new NativeMulticomponentSpeciesRecord[cells * speciesPerCell];
            Elements = new[] { Element(SimHashes.Oxygen, 100, 200, 255),
                Element(SimHashes.CarbonDioxide, 200, 40, 20) };
            for (int cell = 0; cell < cells; cell++)
            {
                Cells[cell] = new NativeMulticomponentCellHeader
                {
                    SpeciesOffset = (uint)(cell * speciesPerCell), SpeciesCount = (ushort)speciesPerCell,
                    Reserved = (ushort)(speciesPerCell == 0 ? 0 : 1), TotalMass = speciesPerCell,
                    GasPressureKPa = speciesPerCell * 100f
                };
                for (int i = 0; i < speciesPerCell; i++)
                    Species[cell * speciesPerCell + i] = new NativeMulticomponentSpeciesRecord
                    {
                        ElementId = (ushort)(i % 2), Phase = 1, Mass = 1f, Temperature = 300f,
                        VolumeFraction = 1f / speciesPerCell, PartialPressureKPa = 100f
                    };
            }
            cellsPin = GCHandle.Alloc(Cells, GCHandleType.Pinned);
            speciesPin = GCHandle.Alloc(Species, GCHandleType.Pinned);
            descriptor = Marshal.AllocHGlobal(40);
            Header = new NativeMulticomponentUpdate
            {
                Version = 1, Flags = 2, Generation = 7, CellCount = (uint)cells,
                TotalSpeciesRecords = (uint)Species.Length,
                Cells = cellsPin.AddrOfPinnedObject(), Species = speciesPin.AddrOfPinnedObject()
            };
            PublishHeader();
            Provider = new OniSimMulticomponentProvider(Acquire, Release, Resolve,
                versioned ? (Func<ulong>)(() => Header.Generation) : null);
        }

        internal void PublishHeader() { Marshal.StructureToPtr(Header, descriptor, false); }
        private IntPtr Acquire()
        {
            Check(Depth.Value == 0, "native read lease was reacquired while held");
            Depth.Value++;
            Interlocked.Increment(ref Acquires);
            return descriptor;
        }
        private void Release()
        {
            Check(Depth.Value == 1, "exactly one native lease is released");
            Depth.Value--;
            Interlocked.Increment(ref Releases);
            AfterRelease?.Invoke();
        }
        private Element Resolve(ushort id)
        {
            BeforeResolve?.Invoke(id);
            return id < Elements.Length ? Elements[id] : null;
        }
        public void Dispose()
        {
            Check(Acquires == Releases && Depth.Value == 0, "fixture leases balance");
            Marshal.FreeHGlobal(descriptor);
            cellsPin.Free(); speciesPin.Free(); Depth.Dispose();
        }

        internal static Element Element(SimHashes id, byte r, byte g, byte b)
        {
            return new Element { id = id, IsGas = true,
                substance = new Substance { colour = new Color32(r, g, b, 255) } };
        }

        // This delegate executes the production Prefix directly. Harmony API
        // types in this host are stubs; its dispatch overhead is not measured.
        internal static readonly Func<TextureRegion, int, int, int, int, bool> Render =
            (Func<TextureRegion, int, int, int, int, bool>)typeof(MulticomponentVisualPatch)
                .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Func<TextureRegion, int, int, int, int, bool>));

        internal static void ConfigureGrid(int width, int height, Element element)
        {
            Grid.WidthInCells = width; Grid.CellCount = width * height;
            Grid.Element = Enumerable.Repeat(element, Grid.CellCount).ToArray();
            Grid.Active = null;
        }

        internal static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Multicomponent batch: " + message);
        }

        internal sealed class SingleCellProvider : IMulticomponentCellProvider
        {
            private readonly IMulticomponentCellProvider inner;
            internal SingleCellProvider(IMulticomponentCellProvider inner) { this.inner = inner; }
            public bool IsReady => inner.IsReady;
            public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
                => inner.TryGetCell(cell, out snapshot);
        }
    }
}
