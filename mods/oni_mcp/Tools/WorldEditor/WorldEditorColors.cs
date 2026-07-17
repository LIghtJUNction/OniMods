using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string GetBuildingColor(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#ecc94b";
            string clean = name.Replace("Complete", "").Replace("Preview", "").Replace("UnderConstruction", "").Trim();
            if (clean.Contains("Tile") || clean.Contains("Ladder") || clean.Contains("Door"))
                return "#718096";
            if (clean.Contains("Generator") || clean.Contains("Battery") || clean.Contains("Wire") || clean.Contains("Transformer"))
                return "#dd6b20";
            if (clean.Contains("Diffuser") || clean.Contains("Deoxidizer") || clean.Contains("Filter") || clean.Contains("Scrubber") || clean.Contains("Vent"))
                return "#319795";
            if (clean.Contains("Pump") || clean.Contains("Liquid") || clean.Contains("Pipe") || clean.Contains("Purifier"))
                return "#3182ce";
            if (clean.Contains("Planter") || clean.Contains("Farm") || clean.Contains("Ration") || clean.Contains("Cooker") || clean.Contains("Microbe"))
                return "#38a169";
            if (clean.Contains("Research") || clean.Contains("Station") || clean.Contains("Computer"))
                return "#805ad5";
            int hash = 0;
            foreach (char c in clean)
                hash = c + (hash << 6) + (hash << 16) - hash;
            int hue = Math.Abs(hash) % 360;
            return HueToRgbString(hue, 0.65f, 0.55f);
        }

        private static string HueToRgbString(float h, float s, float l)
        {
            float r, g, b;
            if (s == 0f)
            {
                r = g = b = l;
            }
            else
            {
                float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
                float p = 2f * l - q;
                r = HueToRgb(p, q, h / 360f + 1f / 3f);
                g = HueToRgb(p, q, h / 360f);
                b = HueToRgb(p, q, h / 360f - 1f / 3f);
            }
            return string.Format("#{0:x2}{1:x2}{2:x2}",
                Math.Min(255, (int)(r * 255f)),
                Math.Min(255, (int)(g * 255f)),
                Math.Min(255, (int)(b * 255f)));
        }

        private static float HueToRgb(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        private static string GetOverlayViewName(HashedString mode)
        {
            if (mode == OverlayModes.None.ID) return "默认视图 (Default)";
            if (mode == OverlayModes.Temperature.ID) return "温度视图 (Temperature)";
            if (mode == OverlayModes.Oxygen.ID) return "氧气视图 (Oxygen)";
            if (mode == OverlayModes.Power.ID) return "电力视图 (Power)";
            if (mode == OverlayModes.LiquidConduits.ID) return "液体管道视图 (Liquid Pipes)";
            if (mode == OverlayModes.GasConduits.ID) return "气体管道视图 (Gas Pipes)";
            if (mode == OverlayModes.Light.ID) return "光照视图 (Light)";
            return "自定义视图 (" + mode.ToString() + ")";
        }

        private static string GetTemperatureColor(float c)
        {
            if (c < -260f) return "#90cdf4"; // 绝对零度
            if (c < -18f) return "#00b5d8";  // 寒冷
            if (c < 0f) return "#4299e1";    // 冰冷
            if (c < 20f) return "#48bb78";   // 温和
            if (c < 35f) return "#ecc94b";   // 温暖
            if (c < 100f) return "#ed8936";  // 炎热
            if (c < 1000f) return "#e57373"; // 灼热
            return "#e53e3e";                // 熔融
        }

        private static string GetHtmlCellColor(int cell, Element elem, GameObject go, HashedString activeMode, float temp, string bldName)
        {
            if (elem == null) return "#1a202c";

            if (activeMode == OverlayModes.Temperature.ID)
            {
                return GetTemperatureColor(temp - 273.15f);
            }
            else if (activeMode == OverlayModes.Power.ID)
            {
                bool isWire = Grid.Objects[cell, (int)ObjectLayer.Wire] != null;
                bool isPowerBld = go != null && (go.GetComponent<EnergyConsumer>() != null || go.GetComponent<EnergyGenerator>() != null || go.GetComponent<Battery>() != null);
                return isPowerBld ? "#dd6b20" : (isWire ? "#ecc94b" : "#2d3748");
            }
            else if (activeMode == OverlayModes.Oxygen.ID)
            {
                if (elem.IsSolid) return "#2d3748"; // solid block grey
                if (elem.IsGas)
                {
                    float mass = Grid.Mass[cell];
                    string gId = elem.id.ToString();
                    bool isBreathable = gId == "Oxygen" || gId == "ContaminatedOxygen" || gId == "PollutedOxygen";
                    if (isBreathable)
                    {
                        if (mass >= 0.6f) return "#48bb78"; // 易 (Green)
                        else if (mass >= 0.1f) return "#ecc94b"; // 可 (Yellow)
                        else return "#ed8936"; // 难 (Orange)
                    }
                }
                return "#e53e3e"; // 不 (Red)
            }
            else if (activeMode == OverlayModes.LiquidConduits.ID)
            {
                bool hasPipe = Grid.Objects[cell, (int)ObjectLayer.LiquidConduit] != null;
                return hasPipe ? "#3182ce" : "#2d3748";
            }
            else if (activeMode == OverlayModes.GasConduits.ID)
            {
                bool hasPipe = Grid.Objects[cell, (int)ObjectLayer.GasConduit] != null;
                return hasPipe ? "#38a169" : "#2d3748";
            }
            else if (activeMode == OverlayModes.Light.ID)
            {
                if (elem.IsSolid) return "#2d3748"; // solid block grey
                int lux = Grid.LightIntensity[cell];
                if (lux >= 72500) return "#fffbeb"; // 晒 (Sunburn)
                if (lux >= 1000) return "#fef08a";  // 明 (Bright)
                if (lux >= 200) return "#fde047";   // 普 (Normal)
                if (lux > 0) return "#ca8a04";      // 弱 (Weak/Dim)
                return "#111827";                   // 暗 (Dark)
            }

            // Default view
            if (!string.IsNullOrEmpty(bldName))
            {
                return GetBuildingColor(bldName);
            }
            return GetElementColor(elem);
        }

        private static string GetPowerInfo(GameObject go)
        {
            if (go == null) return string.Empty;

            var generator = go.GetComponent<Generator>();
            if (generator != null)
            {
                var op = go.GetComponent<Operational>();
                bool isActive = op != null && op.IsOperational;
                float currentGen = isActive ? generator.WattageRating : 0f;
                return $"[发电设备] 当前发电: {currentGen:F0}W / 额定: {generator.WattageRating:F0}W";
            }

            var consumer = go.GetComponent<EnergyConsumer>();
            if (consumer != null)
            {
                return $"[用电设备] 当前耗电: {consumer.WattsUsed:F0}W / 额定: {consumer.WattsNeededWhenActive:F0}W";
            }

            var battery = go.GetComponent<Battery>();
            if (battery != null)
            {
                return $"[蓄电设备] 电量: {battery.JoulesAvailable:F0}J / 容量: {battery.Capacity:F0}J";
            }

            return string.Empty;
        }

    }
}
