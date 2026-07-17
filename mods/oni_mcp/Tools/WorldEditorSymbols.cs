using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static readonly Dictionary<string, char> ResolvedGlyphById = BuildResolvedGlyphById();
        private static readonly Dictionary<string, char> UniqueCharMap = BuildUniqueCharMap();
        private static bool RuntimeDatabaseReady;
        private static bool RuntimeRoomGlyphsLoaded;
        private const string FallbackGlyphPool =
            "甲乙丙丁戊己庚辛壬癸子丑寅卯辰巳午未申酉戌亥乾坤艮兑坎离震巽"
            + "壹贰叁肆伍陆柒捌玖拾佰仟万亿兆京垣垒垛垠垣垦埠埴垌垡垢垲垧垭垯"
            + "垱垸埒埔埕埘埙埚埝埤埭堋堍堑堙堞堠堡堤堰塄塬塾墀墁境墅墉墒"
            + "墘墨墩墼壁壑壕壤壹处备复夏夔夙夤夷夸奁奂奎奏契奕奖套奚奠奢"
            + "奥奭妆妍妤妥妨妮妯妹姗姚姜姝姣姥姨姬姹姻姿威娄娅娆娇娈娜娟";

        private static Dictionary<string, char> BuildUniqueCharMap()
        {
            return new Dictionary<string, char>(ResolvedGlyphById, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, char> BuildResolvedGlyphById()
        {
            var result = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<char> { '人', '仿', '物' };

            foreach (var group in GeneratedGlyphEntries.GroupBy(GlyphGroupKey).OrderBy(GlyphGroupPriority))
            {
                char glyph = ChooseGlyph(group.First(), used);
                foreach (var entry in group)
                    result[entry.Id] = glyph;
                if (glyph != '?')
                    used.Add(glyph);
            }
            return result;
        }

        internal static void MarkRuntimeDatabaseReady()
        {
            RuntimeDatabaseReady = true;
            RuntimeRoomGlyphsLoaded = false;
        }

        private static void AddRuntimeRoomGlyphs(Dictionary<string, char> result, HashSet<char> used)
        {
            AddRuntimeRoomGlyphs(result, used, RuntimeRoomGlyphEntries());
        }

        private static void AddRuntimeRoomGlyphs(Dictionary<string, char> result, HashSet<char> used,
            IEnumerable<SymbolGlyphEntry> entries)
        {
            foreach (var entry in entries.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                if (result.ContainsKey(entry.Id))
                    continue;
                char glyph = ChooseGlyph(entry, used);
                result[entry.Id] = glyph;
                if (glyph != '?')
                    used.Add(glyph);
            }
        }

        private static void EnsureRuntimeRoomGlyphs()
        {
            if (RuntimeRoomGlyphsLoaded)
                return;
            var entries = RuntimeRoomGlyphEntries();
            if (entries.Count == 0)
                return;
            var used = new HashSet<char>(ResolvedGlyphById.Values.Where(glyph => glyph != '?'));
            AddRuntimeRoomGlyphs(ResolvedGlyphById, used, entries);
            foreach (var entry in entries)
                if (ResolvedGlyphById.TryGetValue(entry.Id, out char glyph))
                    UniqueCharMap[entry.Id] = glyph;
            RuntimeRoomGlyphsLoaded = true;
        }

        private static IReadOnlyList<SymbolGlyphEntry> RuntimeRoomGlyphEntries()
        {
            var entries = new List<SymbolGlyphEntry>();
            if (!RuntimeDatabaseReady)
                return entries;
            try
            {
                var roomTypes = Db.Get()?.RoomTypes?.resources;
                if (roomTypes == null)
                    return entries;
                foreach (var roomType in roomTypes)
                {
                    if (roomType == null || string.IsNullOrWhiteSpace(roomType.Id))
                        continue;
                    string name = string.IsNullOrWhiteSpace(roomType.Name) ? roomType.Id : roomType.Name;
                    string token = MapTokenPart(name);
                    entries.Add(new SymbolGlyphEntry
                    {
                        Kind = "Room",
                        Id = roomType.Id,
                        Name = name,
                        Glyph = string.IsNullOrEmpty(token) ? '?' : token[0]
                    });
                }
            }
            catch
            {
                // The database may be unavailable during early static initialization.
            }
            return entries;
        }

        private static string GlyphGroupKey(SymbolGlyphEntry entry)
        {
            string name = MapTokenPart(entry.Name);
            if (string.IsNullOrWhiteSpace(name))
                name = StripCompleteSuffix(entry.Id);
            return entry.Kind + ":" + name;
        }

        private static int GlyphGroupPriority(IGrouping<string, SymbolGlyphEntry> group)
        {
            var first = group.First();
            if (first.Id == "Minion" || first.Id == "BionicMinion" || first.Id == "Critter")
                return 0;
            if (string.Equals(first.Kind, "Element", StringComparison.OrdinalIgnoreCase))
                return 10;
            int common = CommonBuildingRank(StripCompleteSuffix(first.Id));
            if (common >= 0)
                return 20 + common;
            if (string.Equals(first.Kind, "Building", StringComparison.OrdinalIgnoreCase))
                return 100;
            return 200;
        }

        private static int CommonBuildingRank(string id)
        {
            string[] common =
            {
                "Tile", "Ladder", "Bed", "Outhouse", "WashBasin", "ResearchCenter",
                "ManualGenerator", "Battery", "MineralDeoxidizer", "StorageLocker",
                "Door", "Wire", "LiquidConduit", "GasConduit", "LogicWire", "SolidConduit"
            };
            for (int i = 0; i < common.Length; i++)
                if (string.Equals(common[i], id, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static char ChooseGlyph(SymbolGlyphEntry entry, HashSet<char> used)
        {
            foreach (char c in CandidateGlyphs(entry))
                if (IsSafeGlyph(c, used))
                    return c;
            return '?';
        }

        private static IEnumerable<char> CandidateGlyphs(SymbolGlyphEntry entry)
        {
            yield return entry.Glyph;
            foreach (char c in MapTokenPart(entry.Name))
                yield return c;
            foreach (char c in StripCompleteSuffix(entry.Id))
                yield return c;
            foreach (char c in FallbackGlyphPool)
                yield return c;
        }

        private static bool IsSafeGlyph(char c, HashSet<char> used)
        {
            if (char.IsWhiteSpace(c) || c == '\0')
                return false;
            if (c == '?' || c == '.' || c == '人' || c == '仿' || c == '物')
                return false;
            if (IsConnectionGlyph(c) || IsReservedOverlayGlyph(c) || used.Contains(c))
                return false;
            return true;
        }

        private static bool IsReservedOverlayGlyph(char c)
        {
            return c == '⌒' || c == '∥' || c == '⏚' || c == '⏧'
                || c == '⊗' || c == '⊙' || c == '⨂' || c == '⨀'
                || c == '⊂' || c == '⊃' || c == '∈' || c == '∋';
        }

        private static char GetUniqueChar(string id, string localName)
        {
            if (string.IsNullOrWhiteSpace(id))
                return '.';

            string cleanId = StripLinkTags(id).Trim();
            EnsureRuntimeRoomGlyphs();
            char glyph;
            if (ResolvedGlyphById.TryGetValue(cleanId, out glyph))
                return glyph;

            string baseId = StripCompleteSuffix(cleanId);
            return ResolvedGlyphById.TryGetValue(baseId, out glyph) ? glyph : '?';
        }

        private static SymbolGlyphEntry FindGlyphEntry(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            string cleanId = StripLinkTags(id).Trim();
            SymbolGlyphEntry entry;
            if (GeneratedGlyphById.TryGetValue(cleanId, out entry))
                return entry;

            string baseId = StripCompleteSuffix(cleanId);
            return GeneratedGlyphById.TryGetValue(baseId, out entry) ? entry : null;
        }

        private static string StripCompleteSuffix(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;
            return id.EndsWith("Complete", StringComparison.OrdinalIgnoreCase)
                ? id.Substring(0, id.Length - "Complete".Length)
                : id;
        }

        private static readonly string[] OverlayViews =
        {
            "oxygen", "power", "gas_conduits", "liquid_conduits", "solid_conveyor",
            "logic", "temperature", "heat_flow", "materials", "rooms", "priorities",
            "disease", "radiation", "decor", "light"
        };
    }
}
