using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Klei;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 解除 ONI 原版管道“一节只能一种液体/气体”的限制。
    /// 允许管道内多组分共存流动，消除杂液卡管，并动态计算百分比在悬停卡片和详情面板中显示。
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

        public static bool TryGetPipeMixture(ConduitFlow flow, int cell, out List<ComponentEntry> list)
        {
            long key = GetKey(flow, cell);
            if (PipeMixtures.TryGetValue(key, out list) && list != null && list.Count > 0)
            {
                return true;
            }

            // 如果无独立组分记录但有内容，构造默认单组分列表
            var contents = flow.GetContents(cell);
            if (contents.mass > 0f && contents.element != SimHashes.Vacuum)
            {
                list = new List<ComponentEntry>
                {
                    new ComponentEntry
                    {
                        Element = contents.element,
                        Mass = contents.mass,
                        Temperature = contents.temperature
                    }
                };
                return true;
            }

            list = null;
            return false;
        }

        public static string FormatPipeMixtureSuffix(ConduitFlow flow, int cell)
        {
            if (!TryGetPipeMixture(flow, cell, out var list) || list == null || list.Count <= 1)
                return string.Empty;

            float totalMass = 0f;
            foreach (var c in list) totalMass += c.Mass;
            if (totalMass <= 0f) return string.Empty;

            var sb = new StringBuilder(" [");
            bool first = true;
            foreach (var comp in list)
            {
                if (comp.Mass <= 0f) continue;
                if (!first) sb.Append(", ");
                first = false;

                Element elem = ElementLoader.FindElementByHash(comp.Element);
                string name = elem != null && !string.IsNullOrEmpty(elem.name) ? elem.name : comp.Element.ToString();
                int pct = (int)Math.Round((comp.Mass / totalMass) * 100f);
                sb.Append(name).Append(' ').Append(pct).Append('%');
            }
            sb.Append(']');
            return first ? string.Empty : sb.ToString();
        }

        [HarmonyPatch(typeof(ConduitFlow), nameof(ConduitFlow.CanMergeContents))]
        public static class CanMergeContentsPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ConduitFlow __instance, ConduitFlow.ConduitContents from, ConduitFlow.ConduitContents to, float massToMove, ref bool __result)
            {
                // 彻底打破管道不同元素无法合并的硬编码锁
                if (__instance.GetAmountAllowedForMerging(from, to, massToMove) <= 0f)
                {
                    __result = false;
                    return false;
                }
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(ConduitFlow), nameof(ConduitFlow.SetContents))]
        public static class SetContentsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(ConduitFlow __instance, int cell, ConduitFlow.ConduitContents contents)
            {
                if (contents.mass <= 0f || contents.element == SimHashes.Vacuum)
                {
                    PipeMixtures.Remove(GetKey(__instance, cell));
                }
            }
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

                // 真空格子直接写入主组分
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

                // 管道多组分真实混合
                float totalMass = contents.mass + allowed;
                float finalTemp = (contents.temperature * contents.mass + temperature * allowed) / totalMass;

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
                return false;
            }
        }

        /// <summary>
        /// 拦截管道状态项，在悬停信息卡与材料详情页面中动态展示各组分百分比。
        /// </summary>
        [HarmonyPatch(typeof(StatusItem), nameof(StatusItem.GetName), new Type[] { typeof(object) })]
        public static class StatusItemGetNamePatch
        {
            [HarmonyPostfix]
            public static void Postfix(StatusItem __instance, object data, ref string __result)
            {
                if (data == null || string.IsNullOrEmpty(__result))
                    return;

                if (data is Conduit conduit && __instance.Id == "Pipe")
                {
                    int cell = Grid.PosToCell(conduit);
                    var flow = conduit.GetFlowManager();
                    string suffix = FormatPipeMixtureSuffix(flow, cell);
                    if (!string.IsNullOrEmpty(suffix) && !__result.Contains(suffix))
                    {
                        __result += suffix;
                    }
                }
                else if (data is CellSelectionObject cellObj && __instance.Id == "ElementalMass")
                {
                    if (Jlib.Multicomponent.MulticomponentCellAccess.TryGetCell(cellObj.mouseCell, out var snapshot))
                    {
                        string suffix = Jlib.Multicomponent.MulticomponentHoverPatch.FormatCompositionSuffix(snapshot);
                        if (!string.IsNullOrEmpty(suffix) && !__result.Contains(suffix))
                        {
                            __result += suffix;
                        }
                    }
                }
            }
        }
    }
}
