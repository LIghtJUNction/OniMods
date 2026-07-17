using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static char TemperatureSymbol(string elemId, float tempC)
        {
            if (elemId == "Vacuum") return '.';
            if (tempC < -260f) return '零';
            if (tempC < -18f) return '寒';
            if (tempC < 0f) return '冰';
            if (tempC < 20f) return '和';
            if (tempC < 35f) return '暖';
            if (tempC < 100f) return '炎';
            if (tempC < 1000f) return '灼';
            return '熔';
        }

        private static char OxygenSymbol(int cell)
        {
            if (!Grid.IsValidCell(cell)) return '?';
            if (Grid.Element[cell].IsSolid) return '■';
            if (!Grid.Element[cell].IsGas) return '液';
            float mass = Grid.Mass[cell];
            string id = Grid.Element[cell].id.ToString();
            bool breathable = id == "Oxygen" || id == "ContaminatedOxygen" || id == "PollutedOxygen";
            if (!breathable) return '不';
            if (mass >= 0.6f) return '易';
            if (mass >= 0.1f) return '可';
            return '难';
        }

        private static char LightSymbol(int cell)
        {
            if (!Grid.IsValidCell(cell)) return '?';
            if (Grid.Element[cell].IsSolid) return '■';
            int lux = Grid.LightIntensity[cell];
            if (lux >= 72500) return '晒';
            if (lux >= 1000) return '明';
            if (lux >= 200) return '普';
            if (lux > 0) return '弱';
            return '暗';
        }

        private static char DecorSymbol(int cell)
        {
            if (!Grid.IsValidCell(cell) || Grid.Element[cell].IsSolid) return '.';
            float decor = GameUtil.GetDecorAtCell(cell);
            if (decor >= 50f) return '美';
            if (decor > 0f) return '好';
            if (decor == 0f) return '平';
            if (decor > -50f) return '差';
            return '丑';
        }

        private static char DiseaseSymbol(int cell)
        {
            if (!Grid.IsValidCell(cell) || Grid.DiseaseCount[cell] <= 0) return '.';
            int count = Grid.DiseaseCount[cell];
            if (count < 100) return '微';
            if (count < 10000) return '菌';
            return '疫';
        }

        private static char RadiationSymbol(int cell)
        {
            if (!Grid.IsValidCell(cell) || Grid.Element[cell].IsSolid) return '.';
            float rads = Grid.Radiation[cell];
            if (rads <= 0) return '.';
            if (rads < 100) return '低';
            if (rads < 1000) return '辐';
            return '危';
        }

        private static char MaterialSymbol(string elemId, string elemName)
        {
            return elemId == "Vacuum" ? '.' : GetUniqueChar(elemId, elemName);
        }

        private static char CropSymbol(GameObject go)
        {
            if (go == null) return '.';
            var wilt = go.GetComponent<WiltCondition>();
            if (wilt != null && wilt.IsWilting()) return '枯';
            var harvest = go.GetComponent<HarvestDesignatable>();
            if (harvest != null) return harvest.CanBeHarvested() ? '收' : '植';
            return '.';
        }

        private static char RoomSymbol(int cell)
        {
            if (!Grid.IsValidCell(cell) || Grid.Element[cell].IsSolid) return '.';
            try
            {
                var prober = Game.Instance != null ? Game.Instance.roomProber : null;
                var cavity = prober != null ? prober.GetCavityForCell(cell) : null;
                var room = cavity != null ? cavity.room : null;
                if (room == null || room.roomType == null) return '.';
                return GetUniqueChar(room.roomType.Id, room.roomType.Name);
            }
            catch { return '?'; }
        }

        private static string SymbolLegend(HashedString mode, char symbol)
        {
            if (IsConnectionGlyph(symbol)) return ConnectionLegend(symbol);
            if (symbol == '.') return "空/无该视图内容";
            if (mode == OverlayModes.Light.ID) return "光照等级";
            if (mode == OverlayModes.Decor.ID) return "装饰度等级";
            if (mode == OverlayModes.Disease.ID) return "病菌数量等级";
            if (mode == OverlayModes.Radiation.ID) return "辐射等级";
            if (mode == OverlayModes.Crop.ID || mode == OverlayModes.Harvest.ID) return "作物状态";
            if (mode == OverlayModes.Rooms.ID) return "房间类型";
            return "Cell symbol";
        }
    }
}
