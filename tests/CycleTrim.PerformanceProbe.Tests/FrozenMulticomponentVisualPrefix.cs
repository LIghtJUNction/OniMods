/*
 * Copyright 2026 Jlib contributors
 * SPDX-License-Identifier: MIT
 * The full permission and warranty notice is in mods/Jlib/LICENSE.
 */
using Jlib.Multicomponent;
using static Jlib.Multicomponent.MulticomponentVisualPatch;

namespace CycleTrim.PerformanceProbe.Tests
{
    // Frozen production Prefix from Jlib 2769c7efa7285949d85366ae31d1a9739cef71bf,
    // Jlib/Multicomponent/MulticomponentPatches.cs. Only the container and access
    // modifier differ. Its shared color helper and single-cell provider path are
    // unchanged by the batching patch. Keep this body frozen for old/new tests.
    internal static class FrozenMulticomponentVisualPrefix
    {
        internal static bool Prefix(TextureRegion region, int x0, int y0, int x1, int y1) {
            if (!MulticomponentCellAccess.IsReady)
                return true;

            for (int i = y0; i <= y1; i++) {
                for (int j = x0; j <= x1; j++) {
                    int cell = Grid.XYToCell(j, i);
                    if (!Grid.IsActiveWorld(cell)) {
                        region.SetBytes(j, i, 0, 0, 0, 0);
                        continue;
                    }

                    Element element = Grid.Element[cell];
                    bool isGas = element.IsGas;
                    bool isLiquidSurface = element.IsLiquid && Grid.IsValidCell(Grid.CellAbove(cell));

                    // Early out for solids, deep liquids, and vacuum without incurring TryGetCell lock/PInvoke overhead
                    if (!isGas && !isLiquidSurface) {
                        region.SetBytes(j, i, 0, 0, 0, 0);
                        continue;
                    }

                    if (MulticomponentCellAccess.TryGetCell(cell, out var snapshot) && snapshot.ComponentCount > 1) {
                        byte r, g, b, a;
                        ComputeBlendedGasColor(
                            snapshot,
                            Grid.IsValidCell(Grid.CellAbove(cell)),
                            out r, out g, out b, out a);
                        if (isGas)
                            a = byte.MaxValue; // In ONI property textures, gas opacity is modulated by GasPressure texture
                        region.SetBytes(j, i, r, g, b, a);
                        continue;
                    }

                    if (isGas || isLiquidSurface) {
                        region.SetBytes(j, i, element.substance.colour.r, element.substance.colour.g, element.substance.colour.b, byte.MaxValue);
                    } else {
                        region.SetBytes(j, i, 0, 0, 0, 0);
                    }
                }
            }
            return false;
        }
    }
}
