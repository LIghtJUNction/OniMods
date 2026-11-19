#!/usr/bin/env python3
"""Behavioral regressions for read-only, reproducible FrameChain window selection."""
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

import select_cycletrim_frame_windows as selector


def observation(second, frame, paused=False):
    captured = (datetime(2026, 1, 1, tzinfo=timezone.utc) + timedelta(seconds=second)).isoformat()
    return {"observedAt": captured, "observedMonotonicNs": int((second + 10) * 1e9),
            "status": {"schema": 1, "capturedAt": captured, "pid": 42, "unity": "2020.3",
                       "graphicsDevice": "OpenGLCore", "batchMode": False,
                       "headlessRequested": False, "headless": False, "mode": "normal",
                       "renderInterval": 1, "updateRate": 60, "targetFrameRate": -1,
                       "vSyncCount": 0, "runInBackground": True, "duplicants": 3,
                       "gameLoaded": True, "paused": paused, "nativePauseCount": 0,
                       "gameHasCrash": False, "gameError": None, "error": None,
                       "frameCount": frame, "simulationSeconds": float(second * 2)}}


def window(first=20, end=40, sequence=1, capture=1):
    frames = end - first
    phases = [{"name": name, "resolved": True, "calls": 0, "inclusiveTicks": 0,
               "selfTicks": 0, "maxTicks": 0} for name in selector.chain.EXPECTED_PHASES]
    phases[0].update(calls=frames, inclusiveTicks=frames * 5, selfTicks=frames * 5, maxTicks=5)
    return {"schema": 1, "frameAnchor": "Game.Update", "capture": capture, "sequence": sequence,
            "firstFrame": first, "endFrame": end, "frames": frames, "frequency": 1000,
            "totalTicks": frames * 10, "coveredTicks": frames * 5, "unattributedTicks": frames * 5,
            "droppedScopes": 0, "p50Ticks": 10, "p95Ticks": 10, "p99Ticks": 10,
            "maxTicks": 10, "gcCollectionsDelta": [0, 0, 0], "phases": phases,
            "environment": {"applicationIsFocused": True, "appHasFocus": True,
                            "sleepWhenOutOfFocus": True, "effectiveSleepWhenOutOfFocus": False,
                            "backgroundThrottleOverrideRequested": True,
                            "backgroundThrottleOverrideApplied": True,
                            "vSyncCount": 0, "targetFrameRate": -1, "observations": frames + 1,
                            "observedChanges": 0, "focusChanges": 0,
                            "settingsKnown": True, "stable": True}}


def log(*windows):
    return "\n".join(selector.chain.MARKER + " " + json.dumps(item) for item in windows)


def samples():
    return [observation(1, 10), observation(2, 60), observation(3, 110)]


class WindowSelectionTests(unittest.TestCase):
    def test_stable_windows_retain_real_analyzer_semantics(self):
        chosen, result = selector.select_windows(samples(), log(window(), window(70, 90, 2)))
        self.assertTrue(result["validWindowSelection"], result)
        self.assertEqual(len(chosen), 2)
        summary = selector.chain.summarize(chosen)
        self.assertEqual(summary["cadenceHz"], 100)
        self.assertNotIn("p95Ms", summary)
        self.assertFalse(result["comparisonBoundary"]["pairedRunChecked"])
        self.assertIn("not presented FPS", result["metric"])

    def test_same_frame_boundaries_are_not_temporal_brackets(self):
        chosen, result = selector.select_windows(samples(),
            log(window(10, 20), window(20, 40, 2), window(90, 110, 3)))
        self.assertTrue(result["validWindowSelection"], result)
        self.assertEqual([item["sequence"] for item in chosen], [2])
        self.assertEqual(len(result["excludedWindows"]), 2)

    def test_pause_transition_splits_segments(self):
        rows = [observation(1, 10), observation(2, 60), observation(3, 110, True),
                observation(4, 160), observation(5, 210)]
        chosen, result = selector.select_windows(rows,
            log(window(), window(70, 150, 2), window(170, 190, 3)))
        self.assertTrue(result["validWindowSelection"], result)
        self.assertEqual([item["sequence"] for item in chosen], [1, 3])

    def test_blocked_observer_invalidates_the_whole_trial(self):
        rows = [observation(1, 10), observation(2, 60), observation(6, 200),
                observation(7, 250), observation(8, 300)]
        chosen, result = selector.select_windows(rows, log(window(), window(260, 280, 2)))
        self.assertEqual(len(chosen), 2, "Retain other windows for diagnosis")
        self.assertFalse(result["validWindowSelection"])
        self.assertTrue(any("Observation gap" in item["reason"] for item in result["observationErrors"]))

    def test_reversed_observation_time_is_not_a_valid_rate(self):
        rows = samples()
        rows[1]["observedMonotonicNs"] = rows[0]["observedMonotonicNs"]
        _, result = selector.select_windows(rows, log(window()))
        self.assertFalse(result["validWindowSelection"])
        self.assertTrue(result["observationErrors"])

    def test_identity_must_be_complete_and_stable(self):
        for field in selector.IDENTITY_FIELDS:
            with self.subTest(missing=field):
                rows = samples()
                del rows[0]["status"][field]
                _, result = selector.select_windows(rows, log(window()))
                self.assertFalse(result["validWindowSelection"])
        rows = samples()
        rows[1]["status"]["graphicsDevice"] = "Null"
        _, result = selector.select_windows(rows, log(window()))
        self.assertFalse(result["validWindowSelection"])

    def test_error_after_a_good_window_is_not_filtered_away(self):
        rows = samples() + [observation(4, 160)]
        rows[-1]["status"]["gameError"] = "Sim failure"
        chosen, result = selector.select_windows(rows, log(window()))
        self.assertEqual(len(chosen), 1)
        self.assertFalse(result["validWindowSelection"])
        self.assertTrue(any("Runtime reports" in item["reason"] for item in result["observationErrors"]))

    def test_stale_or_invalid_clock_and_pause_holds_fail_closed(self):
        for key, value in (("nativePauseCount", None), ("nativePauseCount", 1),
                           ("simulationSeconds", True), ("simulationSeconds", float("nan")),
                           ("gameHasCrash", None), ("frameCount", True)):
            with self.subTest(field=key, value=value):
                rows = samples()
                rows[1]["status"][key] = value
                _, result = selector.select_windows(rows, log(window()))
                self.assertFalse(result["validWindowSelection"])
        rows = samples()
        rows[1]["observedAt"] = observation(20, 1)["observedAt"]
        _, result = selector.select_windows(rows, log(window()))
        self.assertFalse(result["validWindowSelection"])

    def test_unverified_or_changing_environment_is_rejected(self):
        cases = (("backgroundThrottleOverrideApplied", False), ("focusChanges", 1),
                 ("observedChanges", 1), ("settingsKnown", False),
                 ("effectiveSleepWhenOutOfFocus", True), ("vSyncCount", False),
                 ("targetFrameRate", -1.0), ("observations", 20), ("appHasFocus", None))
        for key, value in cases:
            with self.subTest(field=key, value=value):
                report = window()
                report["environment"][key] = value
                _, result = selector.select_windows(samples(), log(report))
                self.assertFalse(result["validWindowSelection"])
                self.assertTrue(result["windowErrors"])
        second = window(70, 90, 2)
        second["environment"].update(applicationIsFocused=False, appHasFocus=False)
        chosen, result = selector.select_windows(samples(), log(window(), second))
        self.assertEqual(len(chosen), 1)
        self.assertFalse(result["validWindowSelection"])

    def test_status_and_frame_environment_must_agree(self):
        rows = samples()
        for row in rows:
            row["status"]["targetFrameRate"] = 60
        _, result = selector.select_windows(rows, log(window()))
        self.assertFalse(result["validWindowSelection"])
        self.assertIn("disagrees", result["windowErrors"][0]["reason"])

    def test_duplicate_and_overlapping_windows_do_not_double_count(self):
        for reports in ((window(), window()), (window(), window(30, 50, 2))):
            with self.subTest(reports=reports[1]["sequence"]):
                chosen, result = selector.select_windows(samples(), log(*reports))
                self.assertEqual(chosen, [])
                self.assertFalse(result["validWindowSelection"])
                self.assertTrue(result["logErrors"])

    def test_game_instance_captures_cannot_be_combined(self):
        _, result = selector.select_windows(samples(), log(window(), window(70, 90, 1, 2)))
        self.assertFalse(result["validWindowSelection"])
        self.assertIn("multiple Game-instance", result["windowErrors"][-1]["reason"])

    def test_dropped_scopes_and_zero_progress_are_not_performance_evidence(self):
        report = window()
        report["droppedScopes"] = 1
        _, result = selector.select_windows(samples(), log(report))
        self.assertFalse(result["validWindowSelection"])
        rows = samples()
        for row in rows:
            row["status"]["simulationSeconds"] = 10
        _, result = selector.select_windows(rows, log(window()))
        self.assertFalse(result["validWindowSelection"])

    def test_resolution_notice_is_ignored_but_corrupt_json_is_not(self):
        notice = selector.chain.MARKER + " unresolved target: GroundRenderer.Render\n"
        _, result = selector.select_windows(samples(), notice + log(window()))
        self.assertTrue(result["validWindowSelection"], result)
        malformed = log(window()).replace('"schema": 1', '"schema": 1, "schema": 1')
        _, result = selector.select_windows(samples(), malformed)
        self.assertFalse(result["validWindowSelection"])
        self.assertTrue(result["logErrors"])
        rows = selector.read_samples(json.dumps(samples()[0]) + "\n{bad json\n" + json.dumps(samples()[2]))
        _, result = selector.select_windows(rows, log(window()))
        self.assertFalse(result["validWindowSelection"])
        self.assertTrue(any("Invalid sample JSON" in item["reason"] for item in result["observationErrors"]))

    def test_changed_observer_is_explicit(self):
        header = {"kind": selector.OBSERVER_KIND, "script": {"sha256": "a"}}
        changed = copy.deepcopy(header)
        changed["script"]["sha256"] = "b"
        _, result = selector.select_windows([header] + samples() + [changed], log(window()))
        self.assertFalse(result["validWindowSelection"])
        self.assertTrue(any("Observer configuration changed" in item["reason"] for item in result["observationErrors"]))

    def test_cli_records_provenance_without_claiming_paired_comparability(self):
        with tempfile.TemporaryDirectory(prefix="cycletrim-window-test-") as temporary:
            root = Path(temporary)
            sample_path, log_path = root / "samples.jsonl", root / "player.log"
            sample_path.write_text("\n".join(json.dumps(row) for row in samples()) + "\n")
            log_path.write_text(log(window()) + "\n")
            context = {"save": {"sha256": "a" * 64}, "binaries": {"CycleTrim.dll": "b" * 64},
                       "observer": {"runtime": "c" * 64}, "graphics": {"renderer": "fixture", "resolution": [1280, 720]}}
            context_path = root / "context.json"
            context_path.write_text(json.dumps(context))
            command = [sys.executable, str(Path(selector.__file__)), "select", "--samples", str(sample_path),
                       "--log", str(log_path), "--output", str(root / "selected.log"),
                       "--summary", str(root / "summary.json"), "--context", str(context_path)]
            process = subprocess.run(command, text=True, capture_output=True)
            self.assertEqual(process.returncode, 0, process.stderr + process.stdout)
            summary = json.loads((root / "summary.json").read_text())
            self.assertTrue(summary["validWindowSelection"])
            self.assertEqual(summary["declaredTrialContext"], context)
            self.assertEqual(summary["missingTrialContext"], [])
            self.assertFalse(summary["comparisonBoundary"]["pairedRunChecked"])
            self.assertEqual(summary["sources"]["samples"]["sha256"], hashlib.sha256(sample_path.read_bytes()).hexdigest())
            self.assertEqual(selector.chain.validate_series(selector.chain.reports((root / "selected.log").read_text())), [])
            before = (root / "selected.log").read_bytes()
            process = subprocess.run(command, text=True, capture_output=True)
            self.assertEqual(process.returncode, 1)
            self.assertEqual((root / "selected.log").read_bytes(), before)

    def test_watch_is_read_only_and_records_observer_identity(self):
        with tempfile.TemporaryDirectory(prefix="cycletrim-watch-test-") as temporary:
            root = Path(temporary)
            status = observation(1, 10)["status"]
            status["capturedAt"] = datetime.now(timezone.utc).isoformat()
            source = root / "status.json"
            source.write_text(json.dumps(status))
            original = source.read_bytes()
            args = SimpleNamespace(control=root, output=root / "samples.jsonl", seconds=1)
            with patch.object(selector.time, "monotonic", side_effect=[0, 0, 2]), patch.object(selector.time, "sleep"):
                selector.watch(args)
            rows = selector.read_samples(args.output.read_text())
            self.assertEqual(rows[0]["kind"], selector.OBSERVER_KIND)
            self.assertEqual(rows[0]["script"]["sha256"], hashlib.sha256(Path(selector.__file__).read_bytes()).hexdigest())
            self.assertEqual(rows[1]["status"], status)
            self.assertEqual(source.read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
