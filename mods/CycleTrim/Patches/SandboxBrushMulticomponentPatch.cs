using System;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 沙盒刷子多组分混合补丁。
    /// 原版沙盒刷子默认无脑调用 ReplaceElement 抹除格内所有物质。
    /// 当玩家向已有流体（气体或液体）的格子刷入流体时，改用 AddRemoveSubstance 触发真实混合，
    /// 支持在沙盒中直观调试与验证一格多组分混合及分压。
    /// </summary>
    [HarmonyPatch(typeof(SandboxBrushTool), "OnPaintCell")]
    public static class SandboxBrushMulticomponentPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SandboxBrushTool __instance, int cell, int distFromOrigin)
        {
            if (!Grid.IsValidCell(cell))
                return true;

            var settings = Traverse.Create(__instance).Property("settings").GetValue<SandboxSettings>();
            if (settings == null)
                return true;

            Element newElem = ElementLoader.elements[settings.GetIntSetting("SandboxTools.SelectedElement")];
            if (newElem == null || newElem.id == SimHashes.Vacuum || newElem.IsSolid)
            {
                // 真空清格或固体方块刷涂，保留原版 ReplaceElement 逻辑
                return true;
            }

            Element currentElem = Grid.Element[cell];
            float currentMass = Grid.Mass[cell];

            // 若当前格已有气体或液体，且新刷入的也是气体或液体：执行多组分混合！
            if (currentElem != null && currentElem.id != SimHashes.Vacuum && !currentElem.IsSolid && currentMass > 0f)
            {
                byte diseaseIdx = Db.Get().Diseases.GetIndex(Db.Get().Diseases.Get("FoodPoisoning").id);
                Disease disease = Db.Get().Diseases.TryGet(settings.GetStringSetting("SandboxTools.SelectedDisease"));
                if (disease != null)
                {
                    diseaseIdx = Db.Get().Diseases.GetIndex(disease.id);
                }

                float mass = settings.GetFloatSetting("SandboxTools.Mass");
                float temp = settings.GetFloatSetting("SandbosTools.Temperature");
                int diseaseCount = settings.GetIntSetting("SandboxTools.DiseaseCount");

                SimMessages.AddRemoveSubstance(cell, newElem.id, CellEventLogger.Instance.ElementEmitted, mass, temp, diseaseIdx, diseaseCount);

                // 更新刷子高亮反馈
                var recentCells = Traverse.Create(__instance).Field<HashSet<int>>("recentlyAffectedCells").Value;
                var recentColors = Traverse.Create(__instance).Field<Dictionary<int, Color>>("recentAffectedCellColor").Value;
                if (recentCells != null) recentCells.Add(cell);
                if (recentColors != null) recentColors[cell] = newElem.substance.uiColour;

                return false; // 拦截原版暴力覆盖
            }

            return true;
        }
    }
}
