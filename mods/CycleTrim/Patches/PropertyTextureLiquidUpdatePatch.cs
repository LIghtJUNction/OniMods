using System;
using System.Reflection;
using HarmonyLib;
using Jlib.Runtime;
using UnityEngine;

namespace CycleTrim.Patches
{
    // PropertyTextures.UpdateLiquid runs on the game's worker pool. Keep the
    // original cell order and output bytes, but hoist invariant settings reads
    // and remove the overwritten LiquidAmountOffset calculation.
    [HarmonyPatch]
    internal static class PropertyTextureLiquidUpdatePatch
    {
        private static MethodInfo target;

        private static bool Prepare()
        {
            string reason;
            if (PropertyTextureCallbackTarget.TryResolve(typeof(PropertyTextures), "UpdateLiquid",
                typeof(TextureRegion), out target, out reason))
                return true;
            Console.WriteLine("[CycleTrim] Liquid texture optimization skipped: " + reason);
            return false;
        }

        private static MethodBase TargetMethod() { return target; }

        // U60 stores the callback in a delegate field. Bind by argument position
        // so compiler-generated callback parameter names do not affect patching.
        private static bool Prefix(TextureRegion __0, int __1, int __2, int __3, int __4)
        {
            TextureRegion region = __0;
            int x0 = __1, y0 = __2, x1 = __3, y1 = __4;
            float liquidMaxMass = Lighting.Instance.Settings.LiquidMaxMass;
            for (int x = x0; x <= x1; x++)
            {
                int topCell = Grid.XYToCell(x, y1);
                Element element = Grid.Element[topCell];
                for (int y = y1; y >= y0; y--)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsActiveWorld(cell))
                    {
                        region.SetBytes(x, y, 0, 0, 0, 0);
                    }
                    else
                    {
                        Element current = Grid.Element[cell];
                        if (current.IsLiquid)
                        {
                            Color32 colour = current.substance.colour;
                            float alpha = 1f;
                            if (!element.IsLiquid && !element.IsSolid)
                                alpha = Mathf.Pow(Mathf.Min(Grid.Mass[cell] / liquidMaxMass, 1f), 0.45f);
                            region.SetBytes(x, y, (byte)(alpha * 255f), colour.r, colour.g, colour.b);
                        }
                        else
                        {
                            region.SetBytes(x, y, 0, 0, 0, 0);
                        }
                        element = current;
                    }
                }
            }
            return false;
        }
    }
}
