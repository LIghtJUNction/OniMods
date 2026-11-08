using System;
using System.Reflection;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class ModLoadRegression
    {
        internal static void Run()
        {
            var harmony = new HarmonyLib.Harmony("CycleTrim.Test");
            var mod = new ModInfo();
            mod.OnLoad(harmony);
            if (harmony.Installations != 1 || !harmony.OwnerReadyAtInstall)
            {
                throw new InvalidOperationException(
                    "Mod load must install once, after setting the probe owner.");
            }
            if (CycleTrim.Patches.FetchPickupCandidatePatch.Installations != 0)
                throw new InvalidOperationException("Fetch installation must wait for loaded-mod discovery.");
            mod.OnAllModsLoaded(harmony, Array.Empty<KMod.Mod>());
            if (harmony.AllModsLoadedNotifications != 1
                || CycleTrim.Patches.FetchPickupCandidatePatch.Installations != 1
                || !CycleTrim.Patches.FetchPickupCandidatePatch.BaseReadyAtInstall)
                throw new InvalidOperationException("Fetch installation must follow the base loaded-mod callback.");
            Console.WriteLine("PASS CycleTrim installs patches once with the probe owner ready");
        }
    }
}

// Mirrors the installed ONI 744825 UserMod2.OnLoad contract. The linked
// production ModInfo, rather than a copied initialization policy, is exercised.
namespace KMod
{
    public sealed class Mod {
        public string staticID { get; set; }
    }

    public class UserMod2
    {
        public Mod mod;

        public virtual void OnLoad(HarmonyLib.Harmony harmony)
        {
            harmony.PatchAll();
        }

        public virtual void OnAllModsLoaded(HarmonyLib.Harmony harmony,
            System.Collections.Generic.IReadOnlyList<Mod> mods)
        {
            harmony.AllModsLoadedNotifications++;
        }
    }
}

namespace HarmonyLib
{
    public sealed class Harmony
    {
        internal static Action BeforePatchAll;
        public Harmony(string id) { Id = id; }
        public string Id { get; }
        public int Installations { get; private set; }
        public bool OwnerReadyAtInstall { get; private set; }
        internal int AllModsLoadedNotifications;

        public void PatchAll()
        {
            BeforePatchAll?.Invoke();
            Installations++;
            OwnerReadyAtInstall = CycleTrim.Patches.PerformanceProbePatch.Owner == Id;
        }

        public HarmonyClassProcessor CreateClassProcessor(Type type)
        {
            return new HarmonyClassProcessor(this, type);
        }

        public MethodInfo Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
        {
            return original as MethodInfo;
        }
    }

    public sealed class HarmonyClassProcessor
    {
        public HarmonyClassProcessor(Harmony instance, Type type) { }
        public System.Collections.Generic.List<MethodInfo> Patch() => new System.Collections.Generic.List<MethodInfo>();
    }

    public sealed class HarmonyMethod
    {
        public HarmonyMethod() { }
        public HarmonyMethod(MethodInfo method) { this.method = method; }
        public HarmonyMethod(Type type, string name) { }
        public MethodInfo method;
    }
}

namespace CycleTrim.Patches
{
    internal static class PerformanceProbePatch
    {
        internal static string Owner;
        internal static void SetHarmonyId(string id) { Owner = id; }
    }

    internal static class FetchLayoutExperiment
    {
        internal static void StartIfRequested() { }
    }

    internal static class FrameRateUnlockPatch
    {
        internal static void Configure(int targetFrameRate) { }
        internal static void Apply(string reason) { }
    }

    internal static class FetchPickupCandidatePatch
    {
        internal static int Installations;
        internal static bool BaseReadyAtInstall;

        internal static void InstallAfterAllModsLoaded(HarmonyLib.Harmony harmony)
        {
            Installations++;
            BaseReadyAtInstall = harmony.AllModsLoadedNotifications == 1;
        }
    }
}
