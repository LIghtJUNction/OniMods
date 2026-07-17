using System;
using System.Reflection;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class SmartReservoirSignalPatch
    {
        [HarmonyPatch]
        private static class OnSpawnPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(SmartReservoir), "OnSpawn")
                    ?? throw new InvalidOperationException("CycleTrim could not find SmartReservoir.OnSpawn.");
            }

            private static void Postfix(bool ___activated, LogicPorts ___logicPorts)
            {
                if (___logicPorts == null)
                {
                    return;
                }

                ___logicPorts.SendSignal(SmartReservoir.PORT_ID, ___activated ? 1 : 0);
            }
        }

        [HarmonyPatch]
        private static class UpdateLogicCircuitPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(SmartReservoir),
                        "UpdateLogicCircuit",
                        new[] { typeof(object) })
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find SmartReservoir.UpdateLogicCircuit(object).");
            }

            private static bool Prefix(
                SmartReservoir __instance,
                ref bool ___activated,
                int ___activateValue,
                int ___deactivateValue,
                LogicPorts ___logicPorts)
            {
                var percent = __instance.PercentFull * 100f;
                var wasActivated = ___activated;
                if (wasActivated)
                {
                    if (percent >= ___deactivateValue)
                    {
                        ___activated = false;
                    }
                }
                else if (percent <= ___activateValue)
                {
                    ___activated = true;
                }

                if (___activated != wasActivated)
                {
                    ___logicPorts.SendSignal(
                        SmartReservoir.PORT_ID,
                        ___activated ? 1 : 0);
                }

                return false;
            }
        }
    }
}
