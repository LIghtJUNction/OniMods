using HarmonyLib;

namespace CycleTrim
{
    public sealed partial class ModInfo
    {
        partial void InstallCpuWorkOptimizations(Harmony harmony)
        {
            if (Core.CycleTrimPatchOptions.OptimizePickupCandidates)
            {
                Jlib.Runtime.CpuWorkOptimizations.Install(harmony);
                UnityEngine.Debug.Log("[CycleTrim] CPU work hooks: fetch="
                    + Jlib.Runtime.CpuWorkOptimizations.FetchInstalled + ", navigation="
                    + Jlib.Runtime.CpuWorkOptimizations.NavigationInstalled + ".");
            }
        }
    }
}
