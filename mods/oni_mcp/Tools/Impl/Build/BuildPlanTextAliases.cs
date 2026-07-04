using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, string> PlanElementAliases()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["氧气"] = "Oxygen",
                ["污染氧"] = "ContaminatedOxygen",
                ["污氧"] = "ContaminatedOxygen",
                ["二氧化碳"] = "CarbonDioxide",
                ["氢气"] = "Hydrogen",
                ["氯气"] = "ChlorineGas",
                ["真空"] = "Vacuum",
                ["泥土"] = "Dirt",
                ["藻类"] = "Algae",
                ["粉砂岩"] = "SiltStone",
                ["砂岩"] = "SandStone",
                ["沙岩"] = "SandStone",
                ["沉积岩"] = "SedimentaryRock",
                ["花岗岩"] = "Granite",
                ["火成岩"] = "IgneousRock",
                ["黑曜石"] = "Obsidian",
                ["铜矿"] = "Cuprite",
                ["金汞齐"] = "GoldAmalgam",
                ["铁矿"] = "IronOre",
                ["铝矿"] = "AluminumOre",
                ["钨锰铁矿"] = "Wolframite",
                ["沙子"] = "Sand",
                ["沙"] = "Sand",
                ["粘土"] = "Clay",
                ["煤"] = "Carbon",
                ["煤炭"] = "Carbon",
                ["水"] = "Water",
                ["污染水"] = "DirtyWater",
                ["污水"] = "DirtyWater"
            };
        }


        private static Dictionary<string, string> PlanMaterialAliases()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["砂岩"] = "SandStone",
                ["沙岩"] = "SandStone",
                ["sandstone"] = "SandStone",
                ["粉砂岩"] = "SiltStone",
                ["siltstone"] = "SiltStone",
                ["沉积岩"] = "SedimentaryRock",
                ["sedimentaryrock"] = "SedimentaryRock",
                ["sedimentary rock"] = "SedimentaryRock",
                ["花岗岩"] = "Granite",
                ["granite"] = "Granite",
                ["火成岩"] = "IgneousRock",
                ["igneousrock"] = "IgneousRock",
                ["igneous rock"] = "IgneousRock",
                ["黑曜石"] = "Obsidian",
                ["obsidian"] = "Obsidian",
                ["铜矿"] = "Cuprite",
                ["copperore"] = "Cuprite",
                ["copper ore"] = "Cuprite",
                ["金汞齐"] = "GoldAmalgam",
                ["goldamalgam"] = "GoldAmalgam",
                ["gold amalgam"] = "GoldAmalgam",
                ["铁矿"] = "IronOre",
                ["ironore"] = "IronOre",
                ["iron ore"] = "IronOre",
                ["铝矿"] = "AluminumOre",
                ["aluminumore"] = "AluminumOre",
                ["aluminum ore"] = "AluminumOre",
                ["钨锰铁矿"] = "Wolframite",
                ["wolframite"] = "Wolframite",
                ["钢"] = "Steel",
                ["steel"] = "Steel",
                ["精炼金属"] = "RefinedMetal",
                ["refinedmetal"] = "RefinedMetal",
                ["refined metal"] = "RefinedMetal",
                ["泥土"] = "Dirt",
                ["dirt"] = "Dirt",
                ["粘土"] = "Clay",
                ["clay"] = "Clay",
                ["藻类"] = "Algae",
                ["algae"] = "Algae"
            };
        }

        private static Dictionary<string, string> PlanBuildingAliases()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tile"] = "Tile",
                ["tiles"] = "Tile",
                ["砖"] = "Tile",
                ["砖块"] = "Tile",
                ["地砖"] = "Tile",
                ["普通砖"] = "Tile",
                ["梯子"] = "Ladder",
                ["电线"] = "Wire",
                ["导线"] = "Wire",
                ["电缆"] = "Wire",
                ["水管"] = "LiquidConduit",
                ["液管"] = "LiquidConduit",
                ["气管"] = "GasConduit",
                ["运输轨道"] = "SolidConduit",
                ["信号线"] = "LogicWire",
                ["网格砖"] = "MeshTile",
                ["透气砖"] = "GasPermeableMembrane",
                ["门"] = "ManualPressureDoor",
                ["床"] = "Bed",
                ["厕所"] = "Outhouse",
                ["洗手盆"] = "WashBasin",
                ["储存箱"] = "StorageLocker",
                ["存储箱"] = "StorageLocker",
                ["藻类制氧机"] = "MineralDeoxidizer",
                ["制氧机"] = "MineralDeoxidizer",
                ["手动发电机"] = "ManualGenerator",
                ["人力发电机"] = "ManualGenerator",
                ["电池"] = "Battery",
                ["小型电池"] = "Battery",
                ["冰箱"] = "Refrigerator",
                ["小型冰箱"] = "Refrigerator"
            };
        }
    }
}
