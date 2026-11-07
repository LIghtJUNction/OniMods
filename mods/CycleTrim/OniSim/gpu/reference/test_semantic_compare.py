import unittest

from semantic_compare import compare


def snapshot(element, mass, temperature, disease=255, disease_count=0):
    return {
        "frames": 1,
        "arrays": {
            "element": [element],
            "mass": [mass],
            "temperature": [temperature],
            "disease": [disease],
            "disease_count": [disease_count],
            "flow_texture": [0],
            "liquid_texture": [0],
            "liquid_data_texture": [0],
            "material_texture": [0],
            "exposed_texture": [0],
        },
        "events": {"substance": [], "liquid": []},
    }


class SemanticCompareTests(unittest.TestCase):
    def test_exact_snapshots_have_zero_budget_delta(self):
        item = snapshot(2, 0x3F800000, 0x43FA0000, 0, 10)
        report = compare(item, item, [{"shc": 1}, {"shc": 1}, {"shc": 1.005}])
        self.assertEqual(report["relative_deltas"]["total_mass"], 0.0)
        self.assertEqual(report["relative_deltas"]["total_disease_count"], 0.0)
        self.assertEqual(report["changed_cells"]["mass"], 0)
        self.assertEqual(report["decision"], "UNASSESSED")

    def test_reports_material_and_temperature_deltas_without_auto_pass(self):
        reference = snapshot(2, 0x3F800000, 0x43FA0000)
        candidate = snapshot(2, 0x40000000, 0x44160000)
        report = compare(reference, candidate, [{"shc": 1}, {"shc": 1}, {"shc": 1.005}])
        self.assertGreater(report["relative_deltas"]["total_mass"], 0.0)
        self.assertEqual(report["changed_cells"]["temperature"], 1)
        self.assertEqual(report["decision"], "UNASSESSED")


if __name__ == "__main__":
    unittest.main()
