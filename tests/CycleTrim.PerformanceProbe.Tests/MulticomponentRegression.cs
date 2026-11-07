using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CycleTrim.Core;
using CycleTrim.Patches.Multicomponent;
using Jlib.Multicomponent;
using UnityEngine;
using MulticomponentCellAccess = CycleTrim.Core.MulticomponentCellAccess;
using MulticomponentBreathingPatch = CycleTrim.Patches.Multicomponent.MulticomponentBreathingPatch;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class MulticomponentRegression
    {
        private static int assertions;

        internal static void Run()
        {
            VerifyNativeStructSizes();
            VerifyFailClosedAccessAndValidation();
            VerifyOniSimProviderAndZeroAllocHotPath();
            VerifyPhysiologyVisualPlantNavBuildingHoverEvaluators();
            Console.WriteLine("PASS CycleTrim multicomponent provider and patch regressions: "
                + assertions + " assertions");
        }

        private static void VerifyNativeStructSizes()
        {
            Check(Marshal.SizeOf(typeof(NativeMulticomponentSpeciesRecord)) == 20,
                "NativeMulticomponentSpeciesRecord must be 20 bytes");
            Check(Marshal.SizeOf(typeof(NativeMulticomponentCellHeader)) == 24,
                "NativeMulticomponentCellHeader must be 24 bytes");
            Check(Marshal.SizeOf(typeof(NativeMulticomponentUpdate)) == 40,
                "NativeMulticomponentUpdate must be 40 bytes");
        }

        private static void VerifyFailClosedAccessAndValidation()
        {
            MulticomponentCellSnapshot snapshot;
            Check(!MulticomponentCellAccess.IsReady, "provider is not ready when unregistered");
            Check(!MulticomponentCellAccess.TryGetCell(0, out snapshot), "TryGetCell fails closed without provider");

            Element oxygen = CreateElement(SimHashes.Oxygen, "Oxygen", isGas: true, isLiquid: false, breathable: true, 100, 200, 255);
            var validSnapshot = new MulticomponentCellSnapshot(
                new[] { new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 1.5f, 300f, 1.0f, 150f) },
                1, 1.5f, 150f, 0f, 0f);

            var mock = new StubProvider { Ready = true, Snapshot = validSnapshot };
            Check(MulticomponentCellAccess.RegisterProvider(mock), "registers provider");
            try
            {
                Check(MulticomponentCellAccess.IsReady, "provider reports ready");
                Check(MulticomponentCellAccess.TryGetCell(0, out snapshot), "reads valid cell");
                Check(Math.Abs(snapshot.TotalMass - 1.5f) < 1e-5f, "preserves total mass");

                // Mass conservation mismatch must fail closed.
                mock.Snapshot = new MulticomponentCellSnapshot(
                    new[] { new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 1.0f, 300f, 1.0f, 100f) },
                    1, 2.0f, 100f, 0f, 0f);
                Check(!MulticomponentCellAccess.TryGetCell(0, out snapshot),
                    "mass aggregate mismatch must fail closed");
            }
            finally
            {
                Check(MulticomponentCellAccess.UnregisterProvider(mock), "unregisters provider");
            }
        }

        private static void VerifyOniSimProviderAndZeroAllocHotPath()
        {
            Element oxygen = CreateElement(SimHashes.Oxygen, "Oxygen", isGas: true, isLiquid: false, breathable: true, 100, 200, 255);
            Element co2 = CreateElement(SimHashes.CarbonDioxide, "Carbon Dioxide", isGas: true, isLiquid: false, breathable: false, 80, 80, 80);
            Element water = CreateElement(SimHashes.Water, "Water", isGas: false, isLiquid: true, breathable: false, 0, 120, 255);
            var table = new Dictionary<ushort, Element>
            {
                { 1, oxygen },
                { 2, co2 },
                { 3, water }
            };

            var cells = new[]
            {
                new NativeMulticomponentCellHeader
                {
                    SpeciesOffset = 0,
                    SpeciesCount = 2,
                    Reserved = 1,
                    TotalMass = 2.0f,
                    GasPressureKPa = 202.65f,
                    LiquidPressureKPa = 0f,
                    LiquidVolumeFraction = 0f
                },
                new NativeMulticomponentCellHeader
                {
                    SpeciesOffset = 2,
                    SpeciesCount = 2,
                    Reserved = 1,
                    TotalMass = 10.5f,
                    GasPressureKPa = 50.0f,
                    LiquidPressureKPa = 98.0f,
                    LiquidVolumeFraction = 0.01f
                }
            };

            var species = new[]
            {
                new NativeMulticomponentSpeciesRecord
                {
                    ElementId = 1, Phase = 1, Mass = 1.5f, Temperature = 295f, VolumeFraction = 0.75f, PartialPressureKPa = 151.99f
                },
                new NativeMulticomponentSpeciesRecord
                {
                    ElementId = 2, Phase = 1, Mass = 0.5f, Temperature = 295f, VolumeFraction = 0.25f, PartialPressureKPa = 50.66f
                },
                new NativeMulticomponentSpeciesRecord
                {
                    ElementId = 1, Phase = 1, Mass = 0.5f, Temperature = 293f, VolumeFraction = 0.99f, PartialPressureKPa = 50.0f
                },
                new NativeMulticomponentSpeciesRecord
                {
                    ElementId = 3, Phase = 2, Mass = 10.0f, Temperature = 293f, VolumeFraction = 0.01f, PartialPressureKPa = 0.0f
                }
            };

            GCHandle cellsPin = GCHandle.Alloc(cells, GCHandleType.Pinned);
            GCHandle speciesPin = GCHandle.Alloc(species, GCHandleType.Pinned);
            IntPtr updateMem = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMulticomponentUpdate)));
            try
            {
                var legacyUpdate = new NativeMulticomponentUpdate
                {
                    Version = OniSimMulticomponentProvider.CurrentVersion,
                    Flags = OniSimMulticomponentProvider.FlagLegacySingleElement,
                    Generation = 5,
                    CellCount = (uint)cells.Length,
                    TotalSpeciesRecords = (uint)species.Length,
                    Cells = cellsPin.AddrOfPinnedObject(),
                    Species = speciesPin.AddrOfPinnedObject()
                };
                Marshal.StructureToPtr(legacyUpdate, updateMem, false);

                int acquired = 0;
                int released = 0;
                var strictProvider = new OniSimMulticomponentProvider(
                    () => { acquired++; return updateMem; },
                    () => { released++; },
                    id => table.TryGetValue(id, out var elem) ? elem : null);
                Check(!strictProvider.IsReady,
                    "strict provider must remain inactive when only LEGACY_SINGLE_ELEMENT is advertised");

                var coexistenceUpdate = legacyUpdate;
                coexistenceUpdate.Flags = OniSimMulticomponentProvider.FlagMulticomponentCoexistence;
                Marshal.StructureToPtr(coexistenceUpdate, updateMem, false);

                Check(strictProvider.IsReady,
                    "provider becomes ready when FLAG_MULTICOMPONENT_COEXISTENCE is advertised");

                MulticomponentCellSnapshot snap0;
                Check(strictProvider.TryGetCell(0, out snap0), "reads multicomponent cell 0");
                Check(snap0.ComponentCount == 2, "cell 0 has 2 coexisting species");
                Check(snap0.Components[0].Element.id == SimHashes.Oxygen, "species 0 is Oxygen");
                Check(snap0.Components[1].Element.id == SimHashes.CarbonDioxide, "species 1 is CarbonDioxide");

                // Warm up thread-static pool and verify zero heap allocations on hot path.
                for (int i = 0; i < 64; i++)
                    strictProvider.TryGetCell(0, out snap0);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++)
                    strictProvider.TryGetCell(0, out snap0);
                long after = GC.GetAllocatedBytesForCurrentThread();
                Check(after - before == 0, "reusable TryGetCell must allocate 0 bytes on hot path");
                Check(acquired == released, "every native read lease must be released");
            }
            finally
            {
                Marshal.FreeHGlobal(updateMem);
                speciesPin.Free();
                cellsPin.Free();
            }
        }

        private static void VerifyPhysiologyVisualPlantNavBuildingHoverEvaluators()
        {
            Element oxygen = CreateElement(SimHashes.Oxygen, "Oxygen", isGas: true, isLiquid: false, breathable: true, 100, 200, 255);
            Element pollutedO2 = CreateElement(SimHashes.ContaminatedOxygen, "Polluted Oxygen", isGas: true, isLiquid: false, breathable: true, 120, 180, 60);
            Element co2 = CreateElement(SimHashes.CarbonDioxide, "Carbon Dioxide", isGas: true, isLiquid: false, breathable: false, 200, 40, 20);
            Element water = CreateElement(SimHashes.Water, "Water", isGas: false, isLiquid: true, breathable: false, 0, 120, 255);

            // 1. Physiology breathing patch: sums O2 (0.4 kg) + Polluted O2 (0.8 kg) = 1.2 kg, ignores CO2 (1.5 kg), picks Polluted O2 as dominant breathable.
            var mixedGas = new MulticomponentCellSnapshot(
                new[]
                {
                    new MulticomponentSpecies(co2, MulticomponentPhase.Gas, 1.5f, 300f, 0.55f, 150f),
                    new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 0.4f, 300f, 0.15f, 40f),
                    new MulticomponentSpecies(pollutedO2, MulticomponentPhase.Gas, 0.8f, 300f, 0.30f, 80f)
                },
                3, 2.7f, 270f, 0f, 0f);

            SimHashes breathableElement;
            float breathableMass;
            MulticomponentBreathingPatch.EvaluateBreathableCell(mixedGas, out breathableElement, out breathableMass);
            Check(Math.Abs(breathableMass - 1.2f) < 1e-5f, "MulticomponentBreathingPatch sums only breathable gas components");
            Check(breathableElement == SimHashes.ContaminatedOxygen, "MulticomponentBreathingPatch selects dominant breathable gas");

            // 2. Visual color blending patch: 75% Oxygen (100, 200, 255) + 25% CO2 (200, 40, 20) -> (125, 160, 196, 255).
            var twoGasBlend = new MulticomponentCellSnapshot(
                new[]
                {
                    new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 1.5f, 300f, 0.75f, 150f),
                    new MulticomponentSpecies(co2, MulticomponentPhase.Gas, 0.5f, 300f, 0.25f, 50f)
                },
                2, 2.0f, 200f, 0f, 0f);
            byte r, g, b, a;
            MulticomponentVisualPatches.ComputeBlendedGasColor(twoGasBlend, true, out r, out g, out b, out a);
            Check(r == 125 && g == 160 && b == 196 && a == 255, "MulticomponentVisualPatches computes mass-weighted RGBA gas blend");

            // 3. Plant pressure patch: safe partial mass (1.5 kg O2) vs overpressure total fluid mass (11.5 kg > 10 kg warningHigh).
            float effectivePressure;
            Element dominantSafe;
            Element fallback;
            Check(MulticomponentPlantPatches.EvaluateCellAtmosphere(
                twoGasBlend,
                elem => elem.id == SimHashes.Oxygen,
                10f,
                out effectivePressure,
                out dominantSafe,
                out fallback), "MulticomponentPlantPatches detects safe Oxygen component in mixed cell");
            Check(Math.Abs(effectivePressure - 1.5f) < 1e-5f && dominantSafe == oxygen,
                "MulticomponentPlantPatches uses safe partial mass under normal pressure");

            var overpressureBlend = new MulticomponentCellSnapshot(
                new[]
                {
                    new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 1.5f, 300f, 0.13f, 150f),
                    new MulticomponentSpecies(co2, MulticomponentPhase.Gas, 10.0f, 300f, 0.87f, 1000f)
                },
                2, 11.5f, 1150f, 0f, 0f);
            Check(MulticomponentPlantPatches.EvaluateCellAtmosphere(
                overpressureBlend,
                elem => elem.id == SimHashes.Oxygen,
                10f,
                out effectivePressure,
                out dominantSafe,
                out fallback), "MulticomponentPlantPatches detects safe element under overpressure");
            Check(Math.Abs(effectivePressure - 11.5f) < 1e-5f,
                "MulticomponentPlantPatches reports total fluid mass when overpressurized");

            // 4. Swim navigation patch: 40% entry, 20% exit hysteresis.
            Check(!MulticomponentNavigationPatches.EvaluateSwimState(true, false, 0.30f, true, 0f),
                "30% liquid does not enter swim from floor");
            Check(MulticomponentNavigationPatches.EvaluateSwimState(true, false, 0.40f, true, 0f),
                "40% liquid enters swim");
            Check(MulticomponentNavigationPatches.EvaluateSwimState(true, true, 0.30f, true, 0f),
                "30% liquid stays in swim once swimming (hysteresis)");
            Check(!MulticomponentNavigationPatches.EvaluateSwimState(true, true, 0.19f, true, 0f),
                "19% liquid exits swim");

            // 5. Building patches: ElementConsumer & Vent trace-droplet non-blocking.
            var traceDropletCell = new MulticomponentCellSnapshot(
                new[]
                {
                    new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 0.5f, 293f, 0.99f, 50f),
                    new MulticomponentSpecies(water, MulticomponentPhase.Liquid, 10.0f, 293f, 0.01f, 0f)
                },
                2, 10.5f, 50f, 98f, 0.01f);
            Check(MulticomponentBuildingPatches.EvaluateElementAvailable(
                traceDropletCell, ElementConsumer.Configuration.Element, SimHashes.Oxygen, 0.25f),
                "ElementConsumer finds Oxygen component in mixed cell");
            Check(MulticomponentBuildingPatches.EvaluateVentOutputValid(
                traceDropletCell, ConduitType.Gas, 2.0f, isSolidOrClosed: false),
                "trace liquid droplet (1% volume) does not false-overpressure gas vent");
            Check(!MulticomponentBuildingPatches.EvaluateVentOutputValid(
                new MulticomponentCellSnapshot(traceDropletCell.Components, 2, 10.5f, 50f, 98f, 0.96f),
                ConduitType.Gas, 2.0f, isSolidOrClosed: false),
                "flooded cell (96% liquid volume) blocks gas vent");

            // 6. Hover text suffix patch.
            string hoverSuffix = MulticomponentHoverPatches.FormatCompositionSuffix(twoGasBlend);
            Check(hoverSuffix == " [Oxygen 75%, Carbon Dioxide 25%]",
                "MulticomponentHoverPatches formats species breakdown percentages");
        }

        private static Element CreateElement(
            SimHashes id, string name, bool isGas, bool isLiquid, bool breathable, byte r, byte g, byte b)
        {
            return new Element
            {
                id = id,
                name = name,
                IsGas = isGas,
                IsLiquid = isLiquid,
                breathable = breathable,
                substance = new Substance { colour = new Color32(r, g, b, 255) }
            };
        }

        private static void Check(bool value, string message)
        {
            assertions++;
            if (!value)
                throw new InvalidOperationException("assertion failed: " + message);
        }

        private sealed class StubProvider : IMulticomponentCellProvider
        {
            public bool Ready;
            public MulticomponentCellSnapshot Snapshot;
            public bool IsReady { get { return Ready; } }
            public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
            {
                snapshot = Snapshot;
                return Ready;
            }
        }
    }
}

public enum SimHashes
{
    Vacuum = 0,
    Oxygen = 1,
    ContaminatedOxygen = 2,
    CarbonDioxide = 3,
    Water = 4
}

public struct Tag
{
    public string Name;
}

public static class GameTags
{
    public static readonly Tag Breathable = new Tag { Name = "Breathable" };
}

public sealed class Substance
{
    public UnityEngine.Color32 colour;
}

public sealed class Element
{
    public SimHashes id;
    public string name;
    public bool IsGas;
    public bool IsLiquid;
    public bool breathable;
    public Substance substance;

    public bool HasTag(Tag tag)
    {
        return tag.Name == "Breathable" && breathable;
    }
}

public static class ElementLoader
{
    public static List<Element> elements = new List<Element>();
}

public struct CellOffset
{
    public int x;
    public int y;
}

public static class Grid
{
    public static Element[] Element = Array.Empty<Element>();
    public static float[] Mass = Array.Empty<float>();
    public static bool[] Solid = Array.Empty<bool>();

    public static int XYToCell(int x, int y) { return y * 64 + x; }
    public static void CellToXY(int cell, out int x, out int y) { x = cell % 64; y = cell / 64; }
    public static int ClampX(int x) { return x < 0 ? 0 : x; }
    public static int ClampY(int y) { return y < 0 ? 0 : y; }
    public static bool IsActiveWorld(int cell) { return cell >= 0; }
    public static bool IsValidCell(int cell) { return cell >= 0; }
    public static bool IsWorldValidCell(int cell) { return cell >= 0; }
    public static bool IsSolidCell(int cell) { return false; }
    public static bool IsLiquid(int cell) { return false; }
    public static int CellAbove(int cell) { return cell + 64; }
    public static int OffsetCell(int cell, CellOffset offset) { return cell + offset.y * 64 + offset.x; }
}

public sealed class GasBreatherFromWorldProvider { }
public sealed class PropertyTextures { }

public sealed class TextureRegion
{
    public void SetBytes(int x, int y, byte r, byte g, byte b, byte a) { }
}

public sealed class OccupyArea
{
    public CellOffset[] OccupiedCellsOffsets = Array.Empty<CellOffset>();
}

public sealed class PressureVulnerable
{
    public bool testAreaElementSafe;
    public Element currentAtmoElement;
    public float pressureWarning_High = 10f;
    public bool allCellsMustBeSafe;

    public T GetComponent<T>() where T : class { return null; }
    public bool IsSafeElement(Element element) { return true; }
    public void Trigger(int hash) { }
}

public enum NavType
{
    Swim = 0
}

public sealed class NavTable
{
    public bool IsValid(int cell, NavType navType) { return false; }
    public void SetValid(int cell, NavType navType, bool valid) { }
}

public class NavTableValidator
{
    public Action<int> onDirty;
    protected bool IsClear(int cell, CellOffset[] boundingOffsets, bool is_dupe) { return true; }
}

public sealed class GameNavGrids
{
    public sealed class DupeSwimValidator : NavTableValidator { }
}

public enum ConduitType
{
    Gas = 1,
    Liquid = 2
}

public sealed class Structure
{
    public bool IsEntombed() { return false; }
}

public sealed class ElementConsumer
{
    public enum Configuration
    {
        Element,
        AllLiquid,
        AllGas
    }

    public Configuration configuration;
    public SimHashes elementToConsume;
    public float minimumMass;
    private int GetSampleCell() { return 0; }
}

public sealed class Vent
{
    public Structure structure;
    public ConduitType conduitType;
    public float overpressureMass;
    private bool Closed() { return false; }
}

public sealed class HoverTextHelper { }

namespace UnityEngine
{
    public struct Color32
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute { }

    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name)
        {
            return type?.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        public static FieldInfo Field(Type type, string name)
        {
            return type?.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
