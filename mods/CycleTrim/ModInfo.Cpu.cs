using HarmonyLib;

namespace CycleTrim
{
    public sealed partial class ModInfo
    {
        partial void InstallCpuWorkOptimizations(Harmony harmony)
        {
            bool expansion = Jlib.Runtime.NavigationGraphExpansion.Install(harmony);
            bool links = Jlib.Runtime.NavigationLinkParallelism.Install(harmony);
            UnityEngine.Debug.Log("[CycleTrim] Navigation work hooks: expansion=" + expansion + ", parallelLinks=" + links + ".");
            if (!expansion) UnityEngine.Debug.LogWarning(Jlib.Runtime.NavigationGraphExpansion.LastFailure);
            if (!links) UnityEngine.Debug.LogWarning(Jlib.Runtime.NavigationLinkParallelism.LastFailure);
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
