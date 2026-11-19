using System;
using System.Collections.Generic;
using System.Reflection;
using Jlib.Multicomponent;
using OniSim.Compatibility;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class MulticomponentAvailabilityRegression
    {
        private static int assertions;

        internal static int Run()
        {
            assertions = 0;
            CapabilityIsExplicit();
            NativeSingleAndBatchAgree();
            VentAndHoverRequireKnownValues();
            return assertions;
        }

        private static void CapabilityIsExplicit()
        {
            var acceptedInvalid = new List<string>();
            foreach (string json in new[] { null, "", "{}", "null", "[]",
                "{\"liquidVolumeFraction\":false}", "{\"liquidVolumeFraction\":\"true\"}",
                "{\"liquidVolumeFraction\":1}", "{\"liquidVolumeFraction\":true",
                "{\"liquidVolumeFraction\":true,\"nested\":{\"x\":1}",
                "{\"liquidVolumeFraction\":true} trailing",
                "{\"liquidVolumeFraction\":true}{}",
                "{\"liquidVolumeFraction\":true,}",
                "{\"liquidVolumeFraction\":true \"ignored\":0}",
                "{\"liquidVolumeFraction\":true,\"liquidVolumeFraction\":false}",
                "{\"nested\":{\"liquidVolumeFraction\":true}}" })
            {
                assertions++;
                if (OniSimPublicationReader.HasLiquidVolumeFractionCapability(json))
                    acceptedInvalid.Add(json);
            }
            if (acceptedInvalid.Count != 0)
                throw new InvalidOperationException("availability accepted invalid capabilities: "
                    + string.Join(" | ", acceptedInvalid));
            Check(OniSimPublicationReader.HasLiquidVolumeFractionCapability(
                "{\"abiVersion\":1,\"liquidVolumeFraction\":true,\"ignored\":{\"x\":2}}"),
                "explicit root boolean capability is accepted");
            Check(OniSimPublicationReader.HasLiquidVolumeFractionCapability(
                "{\"liquidVolumeFraction\":true,\"ignored\":\"comma, and brackets ]} inside a string\"}"),
                "string contents do not affect delimiter or trailing-comma validation");
        }

        private static void NativeSingleAndBatchAgree()
        {
            foreach (bool available in new[] { false, true })
            using (var fixture = new MulticomponentBatchFixture(1, 2, liquidVolumeFraction: available))
            {
                fixture.Cells[0].LiquidVolumeFraction = 0.75f;
                fixture.Species[0].VolumeFraction = 0f;
                fixture.Species[1].Phase = 2;
                fixture.Species[1].VolumeFraction = 0.75f;
                fixture.Elements[1].IsGas = false;
                fixture.Elements[1].IsLiquid = true;
                Check(fixture.Provider.TryGetCell(0, out var single), "single reads valid inventory");
                Check(single.LiquidVolumeFractionAvailable == available && !single.PressureAvailable,
                    "cell availability follows fill capability; OniSim never claims calibrated kPa");
                Check(single.Components[1].VolumeFractionAvailable == available
                    && !single.Components[0].VolumeFractionAvailable,
                    "only liquid species have an occupied-volume definition");
                Check(!single.Components[0].PartialPressureAvailable
                    && !single.Components[1].PartialPressureAvailable, "wire pressure is not an observation");
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var batch), "batch copies valid inventory");
                using (batch)
                {
                    Check(batch.TryGetCell(0, out var copied), "batch reads valid inventory");
                    Check(copied.LiquidVolumeFractionAvailable == single.LiquidVolumeFractionAvailable
                        && copied.PressureAvailable == single.PressureAvailable, "batch cell availability matches single");
                    for (int i = 0; i < 2; i++)
                        Check(copied.Components[i].VolumeFractionAvailable == single.Components[i].VolumeFractionAvailable
                            && copied.Components[i].PartialPressureAvailable == single.Components[i].PartialPressureAvailable
                            && copied.Components[i].Mass == single.Components[i].Mass,
                            "batch species availability and inventory match single");
                }
            }
        }

        private static void VentAndHoverRequireKnownValues()
        {
            var water = new Element { id = SimHashes.Water, name = "Water", IsLiquid = true };
            var oxygen = new Element { id = SimHashes.Oxygen, name = "Oxygen", IsGas = true };
            var components = new[] {
                new MulticomponentSpecies(water, MulticomponentPhase.Liquid, 1000f, 300f, 1f, 123f, true, false),
                new MulticomponentSpecies(oxygen, MulticomponentPhase.Gas, 0.5f, 300f, 0f, 456f, false, false)
            };
            var provider = new Provider {
                Snapshot = new MulticomponentCellSnapshot(components, 2, 1000.5f, 456f, 123f, 1f, false, false)
            };
            bool[] oldSolid = Grid.Solid;
            Check(MulticomponentCellAccess.RegisterProvider(provider), "availability provider registers");
            try
            {
                Grid.Solid = new bool[1];
                MethodInfo prefix = typeof(MulticomponentBuildingPatch.VentValidOutputCellPatch)
                    .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
                var vent = new Vent { conduitType = ConduitType.Gas, overpressureMass = 2f };
                object[] args = { vent, 0, true };
                Check((bool)prefix.Invoke(null, args) && (bool)args[2],
                    "unknown fill retains vanilla vent decision and does not change the result");
                provider.Snapshot = new MulticomponentCellSnapshot(components, 2, 1000.5f, 0f, 0f, 1f, true, false);
                args[2] = true;
                Check(!(bool)prefix.Invoke(null, args) && !(bool)args[2],
                    "known flooded liquid inventory blocks the gas vent");
                string text = MulticomponentHoverPatch.FormatCompositionSuffix(provider.Snapshot);
                Check(!text.Contains("kPa") && text.Contains("1000") && text.Contains("0.5"),
                    "unknown partial pressures do not appear in the mass/composition hover");
            }
            finally
            {
                Grid.Solid = oldSolid;
                Check(MulticomponentCellAccess.UnregisterProvider(provider), "availability provider unregisters");
            }
        }

        private static void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("availability: " + message);
        }

        private sealed class Provider : IMulticomponentCellProvider
        {
            internal MulticomponentCellSnapshot Snapshot;
            public bool IsReady { get { return true; } }
            public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
            { snapshot = Snapshot; return true; }
        }
    }
}
