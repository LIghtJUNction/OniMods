#!/usr/bin/env python3
"""Accept two existing Steam-launched ONI probes, never simulated participants.

Build with OnlineRuntimeProbe=true. On each machine use a fresh probe directory,
copy an approved disposable save to PROBE/multiplayer-disposable/test.sav, and
load that copy in the already Steam-launched game. Join the same two-player room
and pause both games. Mount/sync the two directories for this controller. Supply
the exact built DLL hashes, game version, and each copied save's SHA-256.

The test only sends two unique production chat messages while both games stay
paused. It never resumes simulation or invokes a save. Independent time steps and
automatic-save behavior are outside this test's scope.
Receipts are private local evidence: publish a reviewed summary, not game files.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import sys
import time


class ValidationError(RuntimeError):
    pass


def need(condition, reason):
    if not condition:
        raise ValidationError(reason)


def hex_value(value, length):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{%d}" % length, value) is not None


def utc_seconds(value):
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        need(parsed.tzinfo is not None, "probe_timestamp_has_no_timezone")
        return parsed.timestamp()
    except (AttributeError, TypeError, ValueError) as error:
        raise ValidationError("invalid_probe_timestamp") from error


def validate_snapshot(value, now, modified, max_age=10):
    need(isinstance(value, dict) and isinstance(value.get("multiplayer"), dict), "multiplayer_probe_required")
    probe = value["multiplayer"]
    need(probe.get("version") == 1, "unsupported_multiplayer_probe")
    need(-2 <= now - modified <= max_age and -2 <= now - utc_seconds(probe.get("publishedUtc")) <= max_age,
         "stale_or_future_probe_status")
    need(hex_value(probe.get("runNonce"), 32), "process_nonce_required")
    need(type(value.get("pid")) is int and value["pid"] > 0, "real_process_identity_required")
    need(type(value.get("frameCount")) is int and value["frameCount"] > 0 and
         type(value.get("command")) is int and value["command"] >= 0, "native_frame_and_command_required")
    need(utc_seconds(probe.get("processStartedUtc")) <= utc_seconds(probe["publishedUtc"]), "invalid_process_start")
    need(probe.get("executable") == "OxygenNotIncluded", "complete_oni_process_required")
    need(not value.get("dependencyError") and not value.get("commandError") and not probe.get("failure"),
         "native_probe_reported_failure")
    return probe


def validate_pair(states, expected, nonce=None, run_nonces=None):
    probes = [s["multiplayer"] for s in states]
    need(probes[0]["runNonce"] != probes[1]["runNonce"], "same_probe_used_twice")
    for index, (state, probe) in enumerate(zip(states, probes)):
        need(state.get("gameVersion") == expected["gameVersion"] and state.get("modSha256") == expected["modSha256"] and
             probe.get("gameAssemblySha256") == expected["gameAssemblySha256"] and
             probe.get("cycleTrimSha256") == expected["cycleTrimSha256"], "exact_build_mismatch")
        need(state.get("loaded") is True and state.get("screen") == "Game" and probe.get("worldCells", 0) > 0,
             "loaded_native_world_required")
        need(state.get("joined") is True and state.get("sharedPause") is False, "joined_independent_room_required")
        need(not state.get("error"), "runtime_reported_error")
        need(state.get("paused") is True, "local_game_not_paused")
        need(probe.get("steamInitialized") is True and probe.get("steamSubscribed") is True,
             "authorized_steam_session_required")
        need(probe.get("transport") == expected["transport"], "actual_transport_mismatch")
        need(isinstance(state.get("gameTime"), (int, float)) and math.isfinite(state["gameTime"]), "finite_native_time_required")
        if expected["transport"] == "steam":
            need(probe.get("steamLoggedOn") is True, "steam_online_required")
        if run_nonces is not None:
            need(probe["runNonce"] == run_nonces[index], "game_process_restarted")
        if nonce is not None:
            need(probe.get("nonce") == nonce and (probe.get("armed") is True or probe.get("finished") is True),
                 "live_challenge_not_acknowledged")
            need(probe.get("saveUnchanged") is True and hex_value(probe.get("steamAccountHash"), 64) and
                 hex_value(probe.get("deviceHash"), 64), "live_save_or_account_proof_missing")
    ids = [p.get("localPlayerId") for p in probes]
    need(all(hex_value(i, 32) for i in ids) and ids[0] != ids[1], "distinct_room_players_required")
    need(probes[0].get("roomId") and probes[0]["roomId"] == probes[1].get("roomId"), "different_rooms")
    need(all(sorted(p.get("connectedPlayers", [])) == sorted(ids) for p in probes), "exactly_two_reciprocal_players_required")
    if nonce is not None:
        same_process = states[0]["pid"] == states[1]["pid"] and probes[0]["deviceHash"] == probes[1]["deviceHash"]
        need(not same_process, "same_native_process_used_twice")
        if expected["transport"] == "steam":
            need(probes[0]["steamAccountHash"] != probes[1]["steamAccountHash"], "two_distinct_steam_accounts_required")
            need(probes[0]["deviceHash"] != probes[1]["deviceHash"], "two_distinct_devices_required")


def verify_messages(states, nonce):
    expected = {f"[oni-multiplayer:{nonce}:{slot}]:{state['multiplayer']['steamAccountHash']}":
                state["multiplayer"]["localPlayerId"] for slot, state in zip(("a", "b"), states)}
    received = []
    for state in states:
        probe = state["multiplayer"]
        messages = probe.get("messages", [])
        need(isinstance(messages, list), "chat_proof_list_required")
        need(all(m.get("text") in expected for m in messages), "unexpected_challenge_chat")
        records = {}
        for text, sender in expected.items():
            matches = [m for m in messages if m.get("text") == text]
            need(len(matches) <= 1, "duplicate_chat_delivery")
            if not matches:
                continue
            message = matches[0]
            need(message.get("playerId") == sender and type(message.get("sequence")) is int and message["sequence"] > 0 and
                 type(message.get("messageId")) is int and message["messageId"] > 0, "chat_sender_or_sequence_mismatch")
            records[text] = (sender, message["sequence"], message["messageId"])
        received.append(records)
    complete = all(len(r) == 2 for r in received) and all(s["multiplayer"].get("pendingChats") == 0 for s in states)
    if complete:
        need(received[0] == received[1], "chat_receipts_disagree")
    return complete


class PublicationWindow:
    """Measure new native publications, not repeated reads of a recent file."""
    def __init__(self, states, now):
        self.started = now
        self.first = [(s["frameCount"], utc_seconds(s["multiplayer"]["publishedUtc"])) for s in states]
        self.previous = self.first[:]
        self.last_advance = [now, now]

    def observe(self, states, now, seconds):
        current = [(s["frameCount"], utc_seconds(s["multiplayer"]["publishedUtc"])) for s in states]
        for i, (frame, stamp) in enumerate(current):
            old_frame, old_stamp = self.previous[i]
            need(now - self.last_advance[i] <= 2, "probe_publication_stalled")
            need(frame >= old_frame and stamp >= old_stamp, "probe_publication_moved_backwards")
            if frame != old_frame or stamp != old_stamp:
                need(frame > old_frame and stamp > old_stamp, "probe_frame_timestamp_disagree")
                self.last_advance[i] = now
        self.previous = current
        return now - self.started >= seconds and all(frame > first[0] and stamp - first[1] >= seconds
                    for (frame, stamp), first in zip(current, self.first))


def disposable_save(directory, relative, expected_hash):
    path = PurePosixPath(relative)
    need(relative.startswith("multiplayer-disposable/") and "\\" not in relative and
         all(p not in ("", ".", "..") for p in relative.split("/")) and path.suffix.lower() == ".sav",
         "disposable_save_path_required")
    target = directory / relative
    need(target.is_file() and not any(p.is_symlink() for p in [target, *target.parents]), "disposable_copy_missing_or_symlinked")
    info = target.stat()
    need(info.st_nlink == 1, "hardlinked_save_copy_refused")
    with target.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    need(digest == expected_hash and info.st_size > 0, "disposable_copy_hash_mismatch")
    need(target.stat().st_mtime_ns == info.st_mtime_ns, "disposable_copy_changed_during_read")
    return {"saveRelativePath": relative, "saveSha256": digest, "saveBytes": info.st_size,
            "saveWriteUnixMilliseconds": info.st_mtime_ns // 1_000_000}


class Participant:
    def __init__(self, directory, slot, timeout):
        self.directory, self.slot, self.timeout = directory, slot, timeout
        self.sequence = 0
        self.run_nonce = None
        self.nonce = None

    def read(self):
        try:
            with (self.directory / "online-status.json").open("rb") as stream:
                info = os.fstat(stream.fileno())
                need(info.st_size <= 1024 * 1024, "oversized_probe_status")
                value = json.load(stream)
        except (OSError, ValueError) as error:
            raise ValidationError("participant_" + self.slot + "_status_unavailable") from error
        validate_snapshot(value, time.time(), info.st_mtime)
        return value

    def command(self, action, **fields):
        current = self.read()
        previous_frame = current["frameCount"]
        self.sequence = max(self.sequence, current["command"]) + 1
        request = {"runNonce": self.run_nonce, "nonce": self.nonce, **fields}
        command = {"sequence": self.sequence, "action": "multiplayer_" + action,
                   "value": json.dumps(request, separators=(",", ":"))}
        temporary = self.directory / ("multiplayer-command-" + secrets.token_hex(8) + ".tmp")
        try:
            temporary.write_text(json.dumps(command), encoding="utf-8")
            temporary.replace(self.directory / "command.json")
        finally:
            temporary.unlink(missing_ok=True)
        deadline = time.monotonic() + self.timeout
        while time.monotonic() < deadline:
            current = self.read()
            need(current["multiplayer"]["runNonce"] == self.run_nonce, "game_process_restarted")
            need(current["command"] <= self.sequence, "another_controller_changed_commands")
            if current["command"] == self.sequence:
                need(current["frameCount"] > previous_frame, "command_without_new_native_frame")
                return current
            time.sleep(0.1)
        raise ValidationError("participant_" + self.slot + "_command_timeout")


def evidence(state):
    probe = state["multiplayer"]
    names = ("publishedUtc", "runNonce", "processStartedUtc", "deviceHash", "steamAccountHash", "roomId", "localPlayerId", "transport",
             "gameAssemblySha256", "cycleTrimSha256", "saveSha256", "saveWriteUnixMilliseconds", "saveBytes",
             "worldId", "worldCells", "messages", "finished")
    return {"pid": state["pid"], "gameVersion": state["gameVersion"], "modSha256": state["modSha256"],
            "gameTime": state["gameTime"], "paused": state["paused"], "frameCount": state["frameCount"],
            "command": state["command"], **{name: probe.get(name) for name in names}}


def run(args):
    output = args.output.resolve()
    need(output.suffix.lower() == ".json" and all(p.resolve() != output and p.resolve() not in output.parents
         for p in (args.first, args.second)), "receipt_must_be_json_outside_probe_directories")
    nonce = secrets.token_hex(16)
    expected = {"modSha256": args.mod_sha256, "gameVersion": args.game_version,
                "gameAssemblySha256": args.game_assembly_sha256, "cycleTrimSha256": args.cycletrim_sha256,
                "transport": args.transport}
    participants = [Participant(args.first, "a", args.timeout), Participant(args.second, "b", args.timeout)]
    with Path(__file__).open("rb") as source:
        collector_hash = hashlib.file_digest(source, "sha256").hexdigest()
    receipt = {"schema": 1, "status": "blocked", "nonce": nonce, "startedUtc": datetime.now(timezone.utc).isoformat(),
               "collectorSha256": collector_hash,
               "completeGameInstances": 0, "protocolClientProcesses": 0, "checks": [], "expected": expected,
               "scope": "Requested acceptance: two complete ONI clients, bidirectional real chat while both stay paused.",
               "notTested": ["independent time progression", "automatic saves", "disconnect recovery", "process restart",
                             "save/load", "planet/duplicant migration", "host replacement"]}
    armed = []
    verified = False
    try:
        states = [p.read() for p in participants]
        validate_pair(states, expected)
        run_nonces = [s["multiplayer"]["runNonce"] for s in states]
        for participant, state, path, digest in zip(participants, states,
                (args.first_save, args.second_save), (args.first_save_sha256, args.second_save_sha256)):
            participant.run_nonce = state["multiplayer"]["runNonce"]
            participant.nonce = nonce
            permit = disposable_save(participant.directory, path, digest)
            armed.append(participant)
            participant.command("arm", slot=participant.slot, **expected, **permit)
        states = [p.read() for p in participants]
        validate_pair(states, expected, nonce, run_nonces)
        verified = True
        receipt["completeGameInstances"] = 2
        receipt["distinctDevices"] = states[0]["multiplayer"]["deviceHash"] != states[1]["multiplayer"]["deviceHash"]
        receipt["distinctSteamAccounts"] = states[0]["multiplayer"]["steamAccountHash"] != states[1]["multiplayer"]["steamAccountHash"]
        receipt["checks"].append("Both native processes answered this run's fresh challenge with exact build, world, room, save and account evidence.")
        paused_times = [s["gameTime"] for s in states]
        receipt["pausedNativeTimes"] = paused_times
        for participant in participants:
            participant.command("chat")
        deadline = time.monotonic() + args.timeout
        publication_window = None
        while time.monotonic() < deadline:
            states = [p.read() for p in participants]
            validate_pair(states, expected, nonce, run_nonces)
            need([s["gameTime"] for s in states] == paused_times, "native_time_changed_during_paused_chat")
            if verify_messages(states, nonce):
                if publication_window is None:
                    publication_window = PublicationWindow(states, time.monotonic())
                if publication_window.observe(states, time.monotonic(), args.settle_seconds):
                    break
            else:
                publication_window = None
            time.sleep(0.1)
        else:
            raise ValidationError("chat_delivery_or_settlement_timeout")
        receipt["checks"].append("Each client received each unique production chat once, with matching sender/sequence and no pending delivery.")
        receipt["chatObservationSeconds"] = args.settle_seconds
        receipt["chatPublicationWindow"] = {"first": publication_window.first, "last": publication_window.previous,
                                            "maximumPublicationGapSeconds": 2}
        for participant in participants:
            participant.command("finish")
        armed.clear()
        states = [p.read() for p in participants]
        validate_pair(states, expected, nonce, run_nonces)
        need([s["gameTime"] for s in states] == paused_times, "native_time_changed_during_paused_chat")
        need(all(s["multiplayer"].get("finished") is True for s in states) and verify_messages(states, nonce), "final_receipt_not_complete")
        receipt["participants"] = [evidence(s) for s in states]
        receipt["status"] = "passed"
    except (ValidationError, OSError) as error:
        receipt["status"] = "failed" if verified else "blocked"
        receipt["reason"] = str(error) if isinstance(error, ValidationError) else "collector_file_io_failed"
    except KeyboardInterrupt:
        receipt["status"] = "failed" if verified else "blocked"
        receipt["reason"] = "collector_interrupted"
    finally:
        cleanup = []
        for participant in armed:
            try:
                participant.command("finish")
                cleanup.append({"participant": participant.slot, "pausedAndFinished": True})
            except (ValidationError, OSError):
                cleanup.append({"participant": participant.slot, "pausedAndFinished": False,
                                "fallback": "This collector never resumes either game; manual inspection required."})
        receipt["cleanup"] = cleanup
        receipt["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        args.output.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.output.with_name(args.output.name + "." + secrets.token_hex(8) + ".tmp")
        temporary.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        temporary.replace(args.output)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for name in ("first", "second", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    for name in ("mod", "game-assembly", "cycletrim", "first-save", "second-save"):
        parser.add_argument("--" + name + "-sha256", required=True)
    parser.add_argument("--game-version", required=True)
    parser.add_argument("--transport", choices=("steam", "tcp"), default="steam")
    parser.add_argument("--first-save", default="multiplayer-disposable/test.sav")
    parser.add_argument("--second-save", default="multiplayer-disposable/test.sav")
    parser.add_argument("--timeout", type=float, default=25)
    parser.add_argument("--settle-seconds", type=float, default=6)
    args = parser.parse_args()
    for name in ("mod_sha256", "game_assembly_sha256", "cycletrim_sha256", "first_save_sha256", "second_save_sha256"):
        if not hex_value(getattr(args, name), 64):
            parser.error("SHA-256 values must be 64 lowercase hexadecimal characters")
    if not math.isfinite(args.timeout) or not 10 <= args.timeout <= 30 or not math.isfinite(args.settle_seconds) or not 3 <= args.settle_seconds <= 10:
        parser.error("Use a 10-30 second timeout and a 3-10 second chat observation window")
    output = args.output.resolve()
    if output.suffix.lower() != ".json" or any(p.resolve() == output or p.resolve() in output.parents for p in (args.first, args.second)):
        parser.error("Write the JSON receipt outside both probe directories")
    result = run(args)
    print(result["status"] + ": " + result.get("reason", "two complete ONI clients passed the stated scope"))
    return 0 if result["status"] == "passed" else 2


if __name__ == "__main__":
    sys.exit(main())
