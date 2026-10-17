#!/usr/bin/env python3
"""Source-only eligibility regression for OniMcp #318 and #324.

This checks production wiring, not C# execution or ONI runtime behavior.
Automatically included by scripts/check_mods.py.
"""

from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1] / "mods/OniMcp/Tools/Impl/Facility"


def method(source: str, name: str) -> str:
    # The inspected methods have block bodies. Scan nested scopes.
    source = re.sub(r"//[^\n]*|/\*.*?\*/", "", source, flags=re.S)
    match = re.search(r"\b" + re.escape(name) + r"\s*\([^)]*\)\s*\{", source)
    if match is None:
        raise AssertionError("Method not found: " + name)
    start = match.end() - 1
    depth = 0
    for pos in range(start, len(source)):
        if source[pos] == "{":
            depth += 1
        elif source[pos] == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:pos]
    raise AssertionError("Unclosed method: " + name)


def compact(value: str) -> str:
    return re.sub(r"\s+", "", value)


class FacilityEligibility(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lore = (ROOT / "FacilityLoreTelepadTools.cs").read_text(encoding="utf-8")
        cls.genetics = (ROOT / "RemoteWorkAndGeneticsTools.cs").read_text(encoding="utf-8")
        cls.info = (ROOT / "StoryFacilityInfoHelpers.cs").read_text(encoding="utf-8")

    def test_lore_has_native_target_guards(self):
        rule = compact(method(self.lore, "IsValidLoreBearer"))
        self.assertIn("lore!=null&&!lore.hideLore", rule)
        self.assertIn("lore.useDefaultLore||!string.IsNullOrWhiteSpace(lore.poiOverrideLoreUnlockId)", rule)

    def test_lore_list_and_press_share_guard(self):
        listing = compact(method(self.lore, "ListLoreBearers"))
        pressing = compact(method(self.lore, "PressLoreBearer"))
        self.assertIn("IsValidLoreBearer(go)", listing)
        self.assertIn("FindObjectTarget(args,IsValidLoreBearer)", pressing)
        self.assertLess(pressing.index("FindObjectTarget("), pressing.index("lore.OnSidescreenButtonPressed()"))

    def test_force_cannot_override_hidden_lore_target_rule(self):
        pressing = compact(method(self.lore, "PressLoreBearer"))
        self.assertLess(pressing.index("FindObjectTarget(args,IsValidLoreBearer)"), pressing.index('GetBool(args,"force",false)'))
        self.assertIn("if(!force&&!lore.SidescreenButtonInteractable())", pressing)
        self.assertNotIn("force", method(self.lore, "IsValidLoreBearer"))

    def test_genetic_options_fail_closed_before_discovery_init(self):
        info = compact(method(self.info, "GetGeneticSeedOptions"))
        self.assertIn("PlantSubSpeciesCatalog.Instance==null||DiscoveredResources.Instance==null", info)
        self.assertIn("DiscoveredResources.Instance.IsDiscovered(seed)", info)
        self.assertIn("subspecies.Count<=1", info)

    def test_seed_write_requires_current_list_option(self):
        setter = compact(method(self.genetics, "SetGeneticAnalysisSeed"))
        match = "GetGeneticSeedOptions(station).Exists(option=>newTag((string)option[\"seedId\"])==seed)"
        self.assertIn(match, setter)
        self.assertLess(setter.index(match), setter.index("station.SetSeedForbidden("))
        self.assertLess(setter.index("ResolveSeedTag(args)"), setter.index(match))

    def test_genetic_read_and_write_use_same_options(self):
        reader = compact(method(self.info, "GeneticStationInfo"))
        setter = compact(method(self.genetics, "SetGeneticAnalysisSeed"))
        self.assertIn('result["options"]=GetGeneticSeedOptions(station)', reader)
        self.assertIn("GetGeneticSeedOptions(station)", setter)


if __name__ == "__main__":
    unittest.main()
