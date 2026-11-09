#!/usr/bin/env python3
"""Exercise FrameChain parsing and attribution invariants without game binaries."""

import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ANALYZER = Path(__file__).with_name("analyze_cycletrim_frame_chain.py")
SPEC = importlib.util.spec_from_file_location("frame_chain_analyzer", ANALYZER)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def window():
    # Two complete intervals, 10 and 30 ms. Nested parents total more than
    # the frame, while their disjoint self times cover exactly 26 of 40 ms.
    timings = {
        "Game.Update": (2, 22, 4, 18),
        "Game.SimEveryTick": (2, 18, 2, 16),
        "Game.StepTheSim": (2, 16, 16, 16),
        "Global.LateUpdate": (2, 4, 4, 4),
    }
    phases = []
    for name in MODULE.EXPECTED_PHASES:
        calls, inclusive, own, maximum = timings.get(name, (0, 0, 0, 0))
        phases.append({"name": name, "resolved": True, "calls": calls,
                       "inclusiveTicks": inclusive, "selfTicks": own, "maxTicks": maximum})
    return {"schema": 1, "frameAnchor": "Game.Update", "capture": 1, "sequence": 1,
            "firstFrame": 10, "endFrame": 12, "frames": 2, "frequency": 1000,
            "totalTicks": 40, "coveredTicks": 26, "unattributedTicks": 14,
            "droppedScopes": 0, "p50Ticks": 10, "p95Ticks": 30, "p99Ticks": 30,
            "maxTicks": 30, "gcCollectionsDelta": [1, 0, 0], "phases": phases}


def log_line(report):
    return "[12:00:00] " + MODULE.MARKER + " " + json.dumps(report)


class FrameChainTests(unittest.TestCase):
    def assert_rejected(self, report, text):
        errors = MODULE.validate(report)
        self.assertTrue(any(text in error for error in errors), errors)
        with self.assertRaises(ValueError):
            MODULE.summarize([report])

    def test_nested_self_coverage_and_call_maximum(self):
        sample = window()
        self.assertEqual(MODULE.validate(sample), [])
        result = MODULE.summarize([sample])
        self.assertEqual(result["coveredMs"], 26)
        self.assertEqual(result["unattributedPercent"], 35)
        self.assertEqual(result["phases"][0]["name"], "Game.StepTheSim")
        self.assertEqual(result["phases"][0]["selfMsPerFrame"], 8)
        self.assertEqual(result["phases"][0]["maxCallMs"], 16)
        self.assertEqual(result["maxFrameMs"], 30)
        self.assertGreater(sum(p["inclusiveMs"] for p in result["phases"]), result["elapsedMs"])
        self.assertTrue(result["instrumentationComplete"])
        self.assertNotIn("p95Ms", result)
        self.assertTrue(all("p95Ms" not in p for p in result["phases"]))
        self.assertIn("not presented FPS", MODULE.summary_text(result))

    def test_weighted_cadence_and_window_percentiles(self):
        second = window()
        second.update(sequence=2, firstFrame=12, endFrame=15, frames=3,
                      totalTicks=120, unattributedTicks=94,
                      p50Ticks=40, p95Ticks=40, p99Ticks=40, maxTicks=40)
        result = MODULE.summarize([window(), second])
        self.assertEqual(result["frames"], 5)
        self.assertEqual(result["meanFrameMs"], 32)
        self.assertEqual(result["cadenceHz"], 31.25)
        self.assertEqual([w["p95Ms"] for w in result["windows"]], [30, 40])
        self.assertEqual(result["gcCollectionsDelta"], [2, 0, 0])
        self.assertEqual(result["gaps"], [])

    def test_captures_frequencies_resets_and_int32_wrap(self):
        wrapped = window()
        wrapped.update(firstFrame=MODULE.INT32_MAX, endFrame=-MODULE.INT32_MAX)
        self.assertEqual(MODULE.validate(wrapped), [])
        later = window()
        later.update(sequence=4, firstFrame=500, endFrame=502)
        result = MODULE.summarize([window(), later])
        self.assertEqual(len(result["gaps"]), 1)
        self.assertEqual(result["elapsedMs"], 80)
        later.update(capture=2, sequence=1, frequency=2000)
        result = MODULE.summarize([window(), later])
        self.assertEqual(result["captures"], [1, 2])
        self.assertEqual(result["meanFrameMs"], 15)
        self.assertAlmostEqual(result["cadenceHz"], 4 / 0.06)
        self.assertEqual(result["gaps"], [])

    def test_unknown_and_missing_targets_are_visible(self):
        sample = window()
        sample["futureMetadata"] = {"note": "forward-compatible"}
        sample["phases"].append({"name": "Extension.Stage", "resolved": True, "calls": 1,
                                  "inclusiveTicks": 1, "selfTicks": 1, "maxTicks": 1})
        sample.update(coveredTicks=27, unattributedTicks=13)
        result = MODULE.summarize([sample])
        self.assertEqual(result["unknownPhases"], ["Extension.Stage"])
        self.assertEqual(result["coveredMs"], 27)
        sample["droppedScopes"] = 1
        self.assertFalse(MODULE.summarize([sample])["instrumentationComplete"])
        sample["droppedScopes"] = 0
        sample["phases"] = [p for p in sample["phases"] if p["name"] != "KScreenManager.Update"]
        next(p for p in sample["phases"] if p["name"] == "CameraController.Update")["resolved"] = False
        sample["droppedScopes"] = 2
        result = MODULE.summarize([sample])
        self.assertFalse(result["instrumentationComplete"])
        self.assertEqual(result["missingTargets"], ["KScreenManager.Update"])
        self.assertEqual(result["unresolvedTargets"], ["CameraController.Update"])
        self.assertIn("INCOMPLETE", MODULE.summary_text(result))

    def test_invalid_window_arithmetic(self):
        changes = (
            ({"coveredTicks": 27}, "coveredTicks + unattributedTicks"),
            ({"coveredTicks": 25, "unattributedTicks": 15}, "sum(selfTicks)"),
            ({"endFrame": 13}, "int32 wrap"),
            ({"p50Ticks": 31}, "percentiles"),
            ({"p95Ticks": 31}, "percentiles"),
            ({"maxTicks": 100}, "inconsistent"),
            ({"totalTicks": 100, "unattributedTicks": 74}, "inconsistent"),
            ({"frameAnchor": "Camera.Render"}, "frameAnchor"),
            ({"schema": 2}, "schema"),
            ({"gcCollectionsDelta": [0, 0]}, "gcCollectionsDelta"),
        )
        for changeset, error in changes:
            with self.subTest(changes=changeset):
                sample = window()
                sample.update(changeset)
                self.assert_rejected(sample, error)

    def test_invalid_phase_arithmetic_and_structure(self):
        for changes, error in (
            ({"selfTicks": 23}, "selfTicks exceeds"),
            ({"inclusiveTicks": 27}, "inclusiveTicks exceeds coveredTicks"),
            ({"maxTicks": 23}, "call maxTicks exceeds"),
            ({"calls": 0}, "zero-call"),
            ({"maxTicks": 1}, "calls * call maxTicks"),
            ({"resolved": False}, "unresolved"),
            ({"resolved": "false"}, "resolved"),
            ({"name": []}, "name"),
        ):
            with self.subTest(changes=changes):
                sample = window()
                sample["phases"][0].update(changes)
                self.assert_rejected(sample, error)
        sample = window()
        sample["phases"].append(copy.deepcopy(sample["phases"][0]))
        self.assert_rejected(sample, "duplicate phase")
        for field in ("name", "resolved", "calls", "inclusiveTicks", "selfTicks", "maxTicks"):
            sample = window()
            del sample["phases"][0][field]
            self.assert_rejected(sample, field)
        sample = window()
        sample["phases"][0] = None
        self.assert_rejected(sample, "must be an object")

    def test_integer_fields_reject_boolean_fraction_and_overflow(self):
        sample = window()
        fields = [key for key, value in sample.items() if type(value) is int]
        for field in fields:
            for value in (True, False, 1.0, None, "1", 10 ** 100):
                with self.subTest(field=field, value=value):
                    broken = window()
                    broken[field] = value
                    self.assert_rejected(broken, field)
        for field in ("calls", "inclusiveTicks", "selfTicks", "maxTicks"):
            for value in (True, 1.0, None, -1, 10 ** 100):
                broken = window()
                broken["phases"][0][field] = value
                self.assert_rejected(broken, field)
        for value in ([True, 0, 0], [-1, 0, 0], [1.0, 0, 0], [0, 0, 0, 0], None):
            broken = window()
            broken["gcCollectionsDelta"] = value
            self.assert_rejected(broken, "gcCollectionsDelta")
        for field in ("capture", "sequence", "frames", "frequency", "totalTicks"):
            broken = window()
            broken[field] = 0
            self.assert_rejected(broken, field)

    def test_series_rejects_duplicates_reverse_captures_and_frequency_drift(self):
        with self.assertRaisesRegex(ValueError, "sequence must increase"):
            MODULE.summarize([window(), window()])
        second = window()
        second.update(sequence=2, frequency=2000)
        with self.assertRaisesRegex(ValueError, "frequency changed"):
            MODULE.summarize([window(), second])
        second.update(capture=2)
        with self.assertRaisesRegex(ValueError, "capture decreased"):
            MODULE.summarize([second, window()])

    def test_log_parser_rejects_malformed_json_and_duplicate_keys(self):
        text = "unrelated line\n" + MODULE.MARKER + " unresolved target: Example\n" + log_line(window())
        self.assertEqual(MODULE.reports(text), [window()])
        for payload in ("{", "[]", "null", '{"schema":1,"schema":1}',
                        '{"value":NaN}', '{"value":Infinity}', '{"value":-Infinity}'):
            with self.subTest(payload=payload):
                with self.assertRaises(ValueError):
                    MODULE.reports(MODULE.MARKER + " " + payload)
        with self.assertRaisesRegex(ValueError, "no CycleTrim FrameChain"):
            MODULE.reports("unrelated log")
        with self.assertRaisesRegex(ValueError, "at least one"):
            MODULE.summarize([])

    def test_cli_json_summary_and_fail_closed_output(self):
        with tempfile.TemporaryDirectory(prefix="cycletrim-frame-chain-") as directory:
            log = Path(directory) / "Player.log"
            output = Path(directory) / "report.json"
            log.write_text(log_line(window()), encoding="utf-8")
            command = [sys.executable, str(ANALYZER), str(log)]
            result = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout)["frames"], 2)
            self.assertIn("max single call", result.stderr)
            result = subprocess.run(command + ["--output", str(output)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(output.read_text())["coveredMs"], 26)
            self.assertEqual(result.stdout, "")
            result = subprocess.run(command + ["--output", str(log)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn("must not overwrite", result.stderr)
            output.unlink()
            log.write_text(MODULE.MARKER + " {broken", encoding="utf-8")
            result = subprocess.run(command + ["--output", str(output)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
