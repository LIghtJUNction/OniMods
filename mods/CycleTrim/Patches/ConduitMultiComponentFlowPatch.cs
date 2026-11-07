using System.Collections.Generic;
using HarmonyLib;
using Klei;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 解除 ONI 原版管道“一节只能一种液体/气体”的限制。
    /// 允许管道内多组分共存流动，消除杂液卡管，并按真实质量加权混合。
    /// </summary>
    public static class ConduitMultiComponentFlowPatch
    {
        public struct ComponentEntry
        {
            public SimHashes Element;
            public float Mass;
            public float Temperature;
        }

        private static readonly Dictionary<long, List<ComponentEntry>> PipeMixtures =
            new Dictionary<long, List<ComponentEntry>>();

        private static long GetKey(ConduitFlow flow, int cell)
        {
            return ((long)flow.GetHashCode() << 32) | (uint)cell;
        }

        [HarmonyPatch(typeof(ConduitFlow), nameof(ConduitFlow.AddElement))]
        public static class AddElementPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                ConduitFlow __instance,
                int cell_idx,
                SimHashes element,
                float mass,
                float temperature,
                byte disease_idx,
                int disease_count,
                ref float __result)
            {
                if (!Grid.IsValidCell(cell_idx) || mass <= 0f)
                {
                    __result = 0f;
                    return false;
                }

                var conduit = __instance.GetConduit(cell_idx);
                if (conduit.idx == -1)
                {
                    __result = 0f;
                    return false;
                }

                ConduitFlow.ConduitContents contents = conduit.GetContents(__instance);
                float maxMass = Traverse.Create(__instance).Field<float>("MaxMass").Value;
                if (maxMass <= 0f) maxMass = 10f;

                float capacity = maxMass - contents.mass;
                float allowed = Mathf.Min(mass, capacity);
                if (allowed <= 0f)
                {
                    __result = 0f;
                    return false;
                }

                // 如果原本是真空，直接写入主组分
                if (contents.element == SimHashes.Vacuum || contents.mass <= 0f)
                {
                    contents.element = element;
                    contents.temperature = temperature;
                    contents.diseaseIdx = disease_idx;
                    contents.diseaseCount = disease_count;
                    contents.AddMass(allowed);
                    contents.ConsolidateMass();
                    __instance.SetContents(cell_idx, contents);

                    long key = GetKey(__instance, cell_idx);
                    PipeMixtures[key] = new List<ComponentEntry>
                    {
                        new ComponentEntry { Element = element, Mass = allowed, Temperature = temperature }
                    };

                    __result = allowed;
                    return false;
                }

                // 原版会在此处因为 contents.element != element 直接 return 0f 卡死！
                // 我们在此处激进允许混合：计算真实混合温度与加权病菌
                float totalMass = contents.mass + allowed;
                float finalTemp = (contents.temperature * contents.mass + temperature * allowed) / totalMass;

                // 记录组分
                long mixtureKey = GetKey(__instance, cell_idx);
                if (!PipeMixtures.TryGetValue(mixtureKey, out var list))
                {
                    list = new List<ComponentEntry>
                    {
                        new ComponentEntry { Element = contents.element, Mass = contents.mass, Temperature = contents.temperature }
                    };
                    PipeMixtures[mixtureKey] = list;
                }

                int foundIdx = list.FindIndex(e => e.Element == element);
                if (foundIdx >= 0)
                {
                    var existing = list[foundIdx];
                    existing.Mass += allowed;
                    existing.Temperature = finalTemp;
                    list[foundIdx] = existing;
                }
                else
                {
                    list.Add(new ComponentEntry { Element = element, Mass = allowed, Temperature = temperature });
                }

                // 主元素设为占比最大的组分，保证原版渲染与兼容性
                SimHashes dominantElement = element;
                float maxComponentMass = 0f;
                foreach (var comp in list)
                {
                    if (comp.Mass > maxComponentMass)
                    {
                        maxComponentMass = comp.Mass;
                        dominantElement = comp.Element;
                    }
                }

                contents.element = dominantElement;
                contents.temperature = finalTemp;
                contents.AddMass(allowed);
                contents.ConsolidateMass();

                if (disease_count > 0)
                {
                    int addedDisease = (int)((allowed / mass) * disease_count);
                    var diseaseInfo = SimUtil.CalculateFinalDiseaseInfo(disease_idx, addedDisease, contents.diseaseIdx, contents.diseaseCount);
                    contents.diseaseIdx = diseaseInfo.idx;
                    contents.diseaseCount = diseaseInfo.count;
                }

                __instance.SetContents(cell_idx, contents);
                __result = allowed;
                return false; // 拦截原版逻辑
            }
        }
    }
}
