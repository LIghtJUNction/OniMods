using System;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Mass-weighted gas and liquid color blending for multicomponent cells:
    /// C_cell = (sum_i m_i * C_i) / (sum_i m_i).
    /// Fail-closed: when no multicomponent provider is ready, the original
    /// PropertyTextures.UpdateGasColour loop runs untouched.
    /// </summary>
    [HarmonyPatch(typeof(PropertyTextures), "UpdateGasColour")]
    public static class MulticomponentVisualPatches
    {
        public static void ComputeBlendedGasColor(
            MulticomponentCellSnapshot snapshot,
            bool cellAboveValid,
            out byte r,
            out byte g,
            out byte b,
            out byte a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 0;

            if (snapshot.Components == null || snapshot.Components.Length == 0)
                return;

            float gasMass = 0f;
            float gasR = 0f;
            float gasG = 0f;
            float gasB = 0f;

            float liquidMass = 0f;
            float liquidR = 0f;
            float liquidG = 0f;
            float liquidB = 0f;

            for (int i = 0; i < snapshot.Components.Length; i++)
            {
                MulticomponentSpecies item = snapshot.Components[i];
                if (item.Mass <= 0f || item.Element == null || item.Element.substance == null)
                    continue;

                Color32 c = item.Element.substance.colour;
                if (item.Phase == MulticomponentPhase.Gas)
                {
                    gasMass += item.Mass;
                    gasR += item.Mass * c.r;
                    gasG += item.Mass * c.g;
                    gasB += item.Mass * c.b;
                }
                else if (item.Phase == MulticomponentPhase.Liquid)
                {
                    liquidMass += item.Mass;
                    liquidR += item.Mass * c.r;
                    liquidG += item.Mass * c.g;
                    liquidB += item.Mass * c.b;
                }
            }

            if (gasMass > 0f)
            {
                r = ClampByte(gasR / gasMass);
                g = ClampByte(gasG / gasMass);
                b = ClampByte(gasB / gasMass);
                a = byte.MaxValue;
            }
            else if (cellAboveValid && liquidMass > 0f)
            {
                r = ClampByte(liquidR / liquidMass);
                g = ClampByte(liquidG / liquidMass);
                b = ClampByte(liquidB / liquidMass);
                a = byte.MaxValue;
            }
        }

        private static byte ClampByte(float value)
        {
            int rounded = (int)Math.Round(value);
            if (rounded <= 0)
                return 0;
            if (rounded >= 255)
                return 255;
            return (byte)rounded;
        }

        [HarmonyPrefix]
        private static bool Prefix(TextureRegion region, int x0, int y0, int x1, int y1)
        {
            if (!MulticomponentCellAccess.IsReady)
                return true;

            for (int i = y0; i <= y1; i++)
            {
                for (int j = x0; j <= x1; j++)
                {
                    int cell = Grid.XYToCell(j, i);
                    if (!Grid.IsActiveWorld(cell))
                    {
                        region.SetBytes(j, i, 0, 0, 0, 0);
                        continue;
                    }

                    MulticomponentCellSnapshot snapshot;
                    if (MulticomponentCellAccess.TryGetCell(cell, out snapshot))
                    {
                        byte r, g, b, a;
                        ComputeBlendedGasColor(
                            snapshot,
                            Grid.IsValidCell(Grid.CellAbove(cell)),
                            out r, out g, out b, out a);
                        region.SetBytes(j, i, r, g, b, a);
                        continue;
                    }

                    Element element = Grid.Element[cell];
                    if (element.IsGas)
                    {
                        region.SetBytes(j, i, element.substance.colour.r, element.substance.colour.g, element.substance.colour.b, byte.MaxValue);
                    }
                    else if (element.IsLiquid && Grid.IsValidCell(Grid.CellAbove(cell)))
                    {
                        region.SetBytes(j, i, element.substance.colour.r, element.substance.colour.g, element.substance.colour.b, byte.MaxValue);
                    }
                    else
                    {
                        region.SetBytes(j, i, 0, 0, 0, 0);
                    }
                }
            }
            return false;
        }
    }
}
