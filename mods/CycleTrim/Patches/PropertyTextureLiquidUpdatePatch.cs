using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    // PropertyTextures.UpdateLiquid runs on the game's worker pool. Keep the
    // original cell order and output bytes, but hoist invariant settings reads
    // and remove the overwritten LiquidAmountOffset calculation.
    [HarmonyPatch(typeof(PropertyTextures), "UpdateLiquid")]
    internal static class PropertyTextureLiquidUpdatePatch
    {
        private static bool Prefix(TextureRegion region, int x0, int y0, int x1, int y1)
        {
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
