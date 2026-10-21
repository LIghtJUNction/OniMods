using System;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class ModLoadRegression
    {
        internal static void Run()
        {
            var harmony = new HarmonyLib.Harmony("CycleTrim.Test");
            new ModInfo().OnLoad(harmony);
            if (harmony.Installations != 1 || !harmony.OwnerReadyAtInstall)
            {
                throw new InvalidOperationException(
                    "Mod load must install once, after setting the probe owner.");
            }
            Console.WriteLine("PASS CycleTrim installs patches once with the probe owner ready");
        }
    }
}

// Mirrors the installed ONI 744825 UserMod2.OnLoad contract. The linked
// production ModInfo, rather than a copied initialization policy, is exercised.
namespace KMod
{
    public class UserMod2
    {
        public virtual void OnLoad(HarmonyLib.Harmony harmony)
        {
            harmony.PatchAll();
        }
    }
}

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { Id = id; }
        public string Id { get; }
        public int Installations { get; private set; }
        public bool OwnerReadyAtInstall { get; private set; }

        public void PatchAll()
        {
            Installations++;
            OwnerReadyAtInstall = CycleTrim.Patches.PerformanceProbePatch.Owner == Id;
        }
    }
}

namespace CycleTrim.Patches
{
    internal static class PerformanceProbePatch
    {
        internal static string Owner;
        internal static void SetHarmonyId(string id) { Owner = id; }
    }
}
