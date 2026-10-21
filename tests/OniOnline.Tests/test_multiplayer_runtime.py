"""Collector regressions only. These fixtures never establish ONI acceptance."""
import argparse
import copy
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("multiplayer_runtime", Path(__file__).with_name("multiplayer_runtime.py"))
runtime = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runtime)

NONCE = "1" * 32
EXPECTED = {"modSha256": "2" * 64, "gameVersion": "744825", "gameAssemblySha256": "3" * 64,
            "cycleTrimSha256": "4" * 64, "transport": "steam"}


def pair():
    timestamp = datetime.now(timezone.utc).isoformat()
    values = []
    ids = ["a" * 32, "b" * 32]
    for index in range(2):
        probe = {"version": 1, "publishedUtc": timestamp, "processStartedUtc": timestamp,
                 "runNonce": str(index + 5) * 32, "executable": "OxygenNotIncluded", "nonce": NONCE,
                 "steamInitialized": True, "steamLoggedOn": True, "steamSubscribed": True,
                 "steamAccountHash": str(index + 6) * 64, "deviceHash": str(index + 7) * 64,
                 "worldCells": 100, "armed": True, "saveUnchanged": True, "roomId": "room",
                 "localPlayerId": ids[index], "connectedPlayers": ids, "pendingChats": 0,
                 **EXPECTED}
        values.append({"multiplayer": probe, "pid": 100 + index, "command": 0, "frameCount": 100,
                       "gameVersion": EXPECTED["gameVersion"], "modSha256": EXPECTED["modSha256"],
                       "screen": "Game", "loaded": True, "joined": True, "sharedPause": False,
                       "paused": True, "gameTime": 100.0})
    messages = [{"text": f"[oni-multiplayer:{NONCE}:{slot}]:{s['multiplayer']['steamAccountHash']}",
                 "playerId": ids[index], "sequence": index + 1, "messageId": 1}
                for index, (slot, s) in enumerate(zip(("a", "b"), values))]
    for state in values:
        state["multiplayer"]["messages"] = copy.deepcopy(messages)
    return values


class CollectorValidation(unittest.TestCase):
    def test_valid_collector_fixture(self):
        states = pair()
        for state in states:
            runtime.validate_snapshot(state, time.time(), time.time())
        runtime.validate_pair(states, EXPECTED, NONCE)
        self.assertTrue(runtime.verify_messages(states, NONCE))

    def test_copied_probe_cannot_count_as_two_clients(self):
        states = pair()
        states[1]["multiplayer"]["runNonce"] = states[0]["multiplayer"]["runNonce"]
        with self.assertRaisesRegex(runtime.ValidationError, "same_probe_used_twice"):
            runtime.validate_pair(states, EXPECTED, NONCE)

    def test_different_probe_nonces_still_require_different_processes(self):
        states = pair()
        states[1]["pid"] = states[0]["pid"]
        states[1]["multiplayer"]["deviceHash"] = states[0]["multiplayer"]["deviceHash"]
        with self.assertRaisesRegex(runtime.ValidationError, "same_native_process"):
            runtime.validate_pair(states, EXPECTED, NONCE)

    def test_same_machine_is_not_two_device_steam_acceptance(self):
        states = pair()
        states[1]["multiplayer"]["deviceHash"] = states[0]["multiplayer"]["deviceHash"]
        with self.assertRaisesRegex(runtime.ValidationError, "two_distinct_devices"):
            runtime.validate_pair(states, EXPECTED, NONCE)

    def test_restart_cannot_reuse_an_earlier_process_receipt(self):
        states = pair()
        run_nonces = [s["multiplayer"]["runNonce"] for s in states]
        states[1]["multiplayer"]["runNonce"] = "f" * 32
        with self.assertRaisesRegex(runtime.ValidationError, "game_process_restarted"):
            runtime.validate_pair(states, EXPECTED, NONCE, run_nonces)

    def test_same_account_is_not_steam_multiplayer_acceptance(self):
        states = pair()
        states[1]["multiplayer"]["steamAccountHash"] = states[0]["multiplayer"]["steamAccountHash"]
        with self.assertRaisesRegex(runtime.ValidationError, "two_distinct_steam_accounts"):
            runtime.validate_pair(states, EXPECTED, NONCE)

    def test_fixture_mutations_fail_closed(self):
        mutations = (("gameAssemblySha256", "8" * 64), ("nonce", "9" * 32), ("worldCells", 0),
                     ("steamSubscribed", False), ("transport", "tcp"), ("roomId", "other"),
                     ("saveUnchanged", False), ("connectedPlayers", ["a" * 32]))
        for key, value in mutations:
            with self.subTest(key=key):
                states = pair()
                states[1]["multiplayer"][key] = value
                with self.assertRaises(runtime.ValidationError):
                    runtime.validate_pair(states, EXPECTED, NONCE)

    def test_stale_and_future_status_are_rejected(self):
        state = pair()[0]
        for offset in (-20, 20):
            with self.subTest(offset=offset), self.assertRaises(runtime.ValidationError):
                runtime.validate_snapshot(state, time.time(), time.time() + offset)
        state["multiplayer"]["publishedUtc"] = "2001-01-01T00:00:00"
        with self.assertRaisesRegex(runtime.ValidationError, "timezone"):
            runtime.validate_snapshot(state, time.time(), time.time())

    def test_protocol_process_cannot_claim_native_game(self):
        state = pair()[0]
        state["multiplayer"]["executable"] = "dotnet"
        with self.assertRaisesRegex(runtime.ValidationError, "complete_oni_process"):
            runtime.validate_snapshot(state, time.time(), time.time())

    def test_either_unpaused_game_is_rejected(self):
        for index in range(2):
            with self.subTest(index=index):
                states = pair()
                states[index]["paused"] = False
                with self.assertRaisesRegex(runtime.ValidationError, "local_game_not_paused"):
                    runtime.validate_pair(states, EXPECTED, NONCE)

    def test_recent_frozen_status_cannot_complete_observation(self):
        states = pair()
        window = runtime.PublicationWindow(states, 0)
        self.assertFalse(window.observe(states, 1, 6))
        with self.assertRaisesRegex(runtime.ValidationError, "probe_publication_stalled"):
            window.observe(states, 6, 6)

    def test_both_games_must_keep_publishing_throughout_window(self):
        states = pair()
        window = runtime.PublicationWindow(states, 0)
        for second in range(1, 4):
            states[0]["frameCount"] += 60
            states[0]["multiplayer"]["publishedUtc"] = datetime.fromtimestamp(
                window.first[0][1] + second, timezone.utc).isoformat()
            if second < 3:
                self.assertFalse(window.observe(states, second, 6))
            else:
                with self.assertRaisesRegex(runtime.ValidationError, "probe_publication_stalled"):
                    window.observe(states, second, 6)

    def test_continuous_new_native_frames_complete_observation(self):
        states = pair()
        window = runtime.PublicationWindow(states, 0)
        for second in range(1, 7):
            for i, state in enumerate(states):
                state["frameCount"] += 60
                state["multiplayer"]["publishedUtc"] = datetime.fromtimestamp(
                    window.first[i][1] + second, timezone.utc).isoformat()
            self.assertEqual(window.observe(states, second, 6), second == 6)

    def test_controller_elapsed_time_cannot_replace_native_publications(self):
        states = pair()
        window = runtime.PublicationWindow(states, 0)
        for second in range(1, 7):
            for i, state in enumerate(states):
                state["frameCount"] += 60
                state["multiplayer"]["publishedUtc"] = datetime.fromtimestamp(
                    window.first[i][1] + second / 2, timezone.utc).isoformat()
            self.assertFalse(window.observe(states, second, 6))

    def test_late_new_file_does_not_hide_an_observation_gap(self):
        states = pair()
        window = runtime.PublicationWindow(states, 0)
        for i, state in enumerate(states):
            state["frameCount"] += 360
            state["multiplayer"]["publishedUtc"] = datetime.fromtimestamp(
                window.first[i][1] + 6, timezone.utc).isoformat()
        with self.assertRaisesRegex(runtime.ValidationError, "probe_publication_stalled"):
            window.observe(states, 6, 6)

    def test_publication_frame_and_timestamp_must_advance_together(self):
        for frame_delta, stamp_delta in ((-1, 1), (1, -1), (0, 1), (1, 0)):
            with self.subTest(frame_delta=frame_delta, stamp_delta=stamp_delta):
                states = pair()
                window = runtime.PublicationWindow(states, 0)
                states[0]["frameCount"] += frame_delta
                states[0]["multiplayer"]["publishedUtc"] = datetime.fromtimestamp(
                    window.first[0][1] + stamp_delta, timezone.utc).isoformat()
                with self.assertRaises(runtime.ValidationError):
                    window.observe(states, 1, 6)

    def test_missing_chat_is_pending_but_duplicate_is_failure(self):
        states = pair()
        states[1]["multiplayer"]["messages"].pop()
        self.assertFalse(runtime.verify_messages(states, NONCE))
        states = pair()
        states[1]["multiplayer"]["messages"].append(states[1]["multiplayer"]["messages"][0].copy())
        with self.assertRaisesRegex(runtime.ValidationError, "duplicate_chat"):
            runtime.verify_messages(states, NONCE)

    def test_chat_author_and_sequence_cannot_be_forged(self):
        for key, value in (("playerId", "f" * 32), ("sequence", 40), ("messageId", 50)):
            with self.subTest(key=key):
                states = pair()
                states[1]["multiplayer"]["messages"][0][key] = value
                with self.assertRaises(runtime.ValidationError):
                    runtime.verify_messages(states, NONCE)

    def test_disposable_copy_is_fingerprinted_without_writing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            save = root / "multiplayer-disposable/test.sav"
            save.parent.mkdir()
            save.write_bytes(b"collector fixture, not a game save")
            before = save.stat()
            digest = hashlib.sha256(save.read_bytes()).hexdigest()
            proof = runtime.disposable_save(root, "multiplayer-disposable/test.sav", digest)
            self.assertEqual(proof["saveWriteUnixMilliseconds"], before.st_mtime_ns // 1_000_000)
            self.assertEqual(save.stat().st_mtime_ns, before.st_mtime_ns)
            for name in ("../test.sav", "multiplayer-disposable/../test.sav", "multiplayer-disposable//test.sav"):
                with self.assertRaises(runtime.ValidationError):
                    runtime.disposable_save(root, name, digest)
            (save.parent / "linked.sav").symlink_to(save)
            with self.assertRaisesRegex(runtime.ValidationError, "symlinked"):
                runtime.disposable_save(root, "multiplayer-disposable/linked.sav", digest)
            os.link(save, save.parent / "hardlink.sav")
            with self.assertRaisesRegex(runtime.ValidationError, "hardlinked"):
                runtime.disposable_save(root, "multiplayer-disposable/hardlink.sav", digest)

    def test_missing_runtime_writes_blocked_zero_clients(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = argparse.Namespace(first=root / "first", second=root / "second", output=root / "result.json", timeout=10,
                mod_sha256=EXPECTED["modSha256"], game_version=EXPECTED["gameVersion"],
                game_assembly_sha256=EXPECTED["gameAssemblySha256"], cycletrim_sha256=EXPECTED["cycleTrimSha256"], transport="steam")
            result = runtime.run(args)
            self.assertEqual(result["status"], "blocked")
            self.assertEqual(result["completeGameInstances"], 0)
            self.assertEqual(result["protocolClientProcesses"], 0)
            self.assertEqual(json.loads(args.output.read_text())["status"], "blocked")

    def test_interrupt_keeps_a_failure_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = argparse.Namespace(first=root / "first", second=root / "second", output=root / "result.json", timeout=10,
                mod_sha256=EXPECTED["modSha256"], game_version=EXPECTED["gameVersion"],
                game_assembly_sha256=EXPECTED["gameAssemblySha256"], cycletrim_sha256=EXPECTED["cycleTrimSha256"], transport="steam")
            with patch.object(runtime.Participant, "read", side_effect=KeyboardInterrupt):
                result = runtime.run(args)
            self.assertEqual(result["reason"], "collector_interrupted")
            self.assertEqual(result["completeGameInstances"], 0)
            self.assertNotEqual(result["status"], "passed")

    def test_receipt_cannot_overwrite_live_probe_state(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "first"
            first.mkdir()
            path = first / "online-status.json"
            path.write_text("preserve")
            args = argparse.Namespace(first=first, second=root / "second", output=path)
            with self.assertRaisesRegex(runtime.ValidationError, "receipt_must_be_json_outside"):
                runtime.run(args)
            self.assertEqual(path.read_text(), "preserve")


if __name__ == "__main__":
    unittest.main()
