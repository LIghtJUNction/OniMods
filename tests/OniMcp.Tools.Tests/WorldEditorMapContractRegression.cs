using System;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static void RunMapContractRegressions()
        {
            var definitions = Assets.BuildingDefs.ToArray();
            var symbols = UniqueCharMap.ToArray();
            int assertions = 0;
            Action<bool, string> check = (condition, message) =>
            {
                if (!condition)
                    throw new InvalidOperationException("Map contract: " + message);
                assertions++;
            };

            try
            {
                Assets.BuildingDefs.Clear();
                Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "MeshTile", Name = "Mesh" });
                Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "Tile", Name = "Floor" });
                Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "OtherTile", Name = "Floor" });
                check(TryResolveBuildPrefabFromToken("Tile:5", 'T', out string prefab) && prefab == "Tile",
                    "an exact prefab must win over an earlier substring candidate");
                check(TryResolveBuildPrefabFromToken("tile:5", 't', out prefab) && prefab == "Tile",
                    "exact prefab IDs remain case-insensitive");
                check(!TryResolveBuildPrefabFromToken("Floor:5", 'F', out prefab),
                    "duplicate display names must not select the first building");
                check(!TryResolveBuildPrefabFromToken("TileTypo:5", 'T', out prefab),
                    "an unknown full name must not fall back to its first glyph");
                Assets.BuildingDefs.Reverse();
                check(TryResolveBuildPrefabFromToken("Tile:5", 'T', out prefab) && prefab == "Tile",
                    "resolution must not depend on asset enumeration order");

                UniqueCharMap.Clear();
                UniqueCharMap.Add("TestElement", '材');
                check(ParseBuildToken("Tile:7#TestElement@(14,9)", out char symbol, out int? priority, out string material)
                    && symbol == 'T' && priority == 7 && material == "TestElement",
                    "a full material ID must survive an annotated token round trip");
                check(ParseBuildToken("Tile:7#材", out symbol, out priority, out material)
                    && material == "TestElement", "known material glyphs remain supported");
                check(ParseBuildToken("Tile:7@(14,-9)", out symbol, out priority, out material)
                    && priority == 7 && material == null, "valid signed coordinates are annotations only");

                foreach (string invalid in new[]
                {
                    "Tile:0", "Tile:10", "Tile:-1", "Tile:2147483648", "Tile:bad", "Tile:",
                    "Tile:5#", "Tile:5#TestElement#Other", "Tile:5#TestElement:7",
                    "Tile:5@(1,2)junk", "Tile:5@(bad,2)", "Tile:5@(1,2)@(3,4)"
                })
                {
                    check(!ParseBuildToken(invalid, out symbol, out priority, out material),
                        "malformed token must be rejected: " + invalid);
                }
                check(!ParsePriority("dig:10").HasValue, "invalid priorities must not be silently clamped");
                check(NormalizeMapCompareToken("Tile@(1,2)junk") == "Tile@(1,2)junk",
                    "normalization must not discard an arbitrary suffix");
                check(SearchTokenMatches("Tile@(1,2)", "Tile"), "omitted coordinate annotations remain supported");
                check(!SearchTokenMatches("Tile", "*"), "the disconnected-line glyph is not a wildcard");
                check(!SearchTokenMatches("Tile", "?"), "unknown data is not a wildcard");
                check(SearchTokenMatches("*", "*") && SearchTokenMatches("?", "?"),
                    "map glyphs match literally");
                check(SearchTokenMatches("Tile", ".*"), "explicit wildcard syntax remains supported");
                Console.WriteLine("PASS: " + assertions + " map token contract assertions");
            }
            finally
            {
                Assets.BuildingDefs.Clear();
                Assets.BuildingDefs.AddRange(definitions);
                UniqueCharMap.Clear();
                foreach (var pair in symbols)
                    UniqueCharMap.Add(pair.Key, pair.Value);
            }
        }
    }
}
