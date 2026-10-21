#!/usr/bin/env python3
"""Exercise an explicitly launched probe build; never launches ONI by itself."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import urllib.parse

REPO = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--protocol-client-assembly", type=Path,
                        help="Use an isolated OniOnline.Tests.dll build for protocol participants")
    parser.add_argument("--generate-colony", action="store_true")
    parser.add_argument("--steam-lobby", action="store_true")
    parser.add_argument("--world-sync", action="store_true", help="Verify real native publisher to read-only .NET observer")
    parser.add_argument("--star-map", action="store_true", help="Verify native star map and printing-pod engine entry")
    parser.add_argument("--screenshots", action="store_true", help="Explicit graphical acceptance only; disabled by default")
    parser.add_argument("--fixed-window", action="store_true", help="Keep the startup window dimensions; resized framebuffer captures require separate validation")
    parser.add_argument("--management-ui", action="store_true", help="Open real task and team windows in the joined test colony")
    parser.add_argument("--save-identity", action="store_true", help="Create a fresh native test checkpoint and record persistent planet identity")
    parser.add_argument("--native-work", action="store_true", help="Designate safe test-only native dig orders and verify provenance")
    parser.add_argument("--native-work-revocation", action="store_true",
                        help="Revoke a live native dig grant, reject its old confirmation after regrant, then restore only with a fresh request")
    parser.add_argument("--native-remote-work", action="store_true",
                        help="Drive native dig preview/confirm, reconnect replay, revocation and fresh regrant through an independent TCP protocol peer")
    parser.add_argument("--native-remote-work-completion", action="store_true",
                        help="After the network scenario, use at most thirty paused one-second steps to verify actual digging cleanup and save a fresh checkpoint")
    parser.add_argument("--native-task-ui", action="store_true", help="Verify actual graphical map input events in the native network-work fixture")
    parser.add_argument("--native-chat-ui", action="store_true", help="Verify long real chat messages, retained reading anchor and latest-message UI handlers in the paused graphical host")
    parser.add_argument("--native-transfer-preflight", action="store_true", help="Save a fresh private active checkpoint, then read-only inspect an original starter and native 256x384 host capacity; eligibility is reported separately")
    parser.add_argument("--native-dupe-archive", action="store_true", help="After native work finishes, export one existing starter and proxy without importing or changing it")
    parser.add_argument("--native-dupe", action="store_true", help="Issue a native MoveTo to a real starter dupe in a safe inspected corridor")
    parser.add_argument("--keep-running", action="store_true", help="Leave this owned game paused for another test batch")
    parser.add_argument("--resume-from-characters", action="store_true",
                        help="Continue this probe's own already-generated, paused starter selection")
    parser.add_argument("--resume-frontend", action="store_true", help="Continue this probe's existing native new-game screens")
    args = parser.parse_args()
    if args.native_work_revocation and not (args.native_work and args.generate_colony):
        parser.error("--native-work-revocation requires --native-work and --generate-colony")
    if args.native_remote_work and (not args.generate_colony or args.native_work or args.save_identity):
        parser.error("--native-remote-work requires --generate-colony and a separate run from --native-work or --save-identity")
    if args.native_remote_work_completion and not args.native_remote_work:
        parser.error("--native-remote-work-completion requires --native-remote-work")
    if (args.native_task_ui or args.native_dupe_archive) and not args.native_remote_work_completion:
        parser.error("--native-task-ui and --native-dupe-archive require --native-remote-work-completion")
    if args.native_chat_ui and not args.generate_colony:
        parser.error("--native-chat-ui requires --generate-colony and an explicitly graphical host")
    if args.native_transfer_preflight and not args.generate_colony:
        parser.error("--native-transfer-preflight requires this run's generated disposable colony; existing private checkpoint inspection uses the standalone probe commands")
    if args.native_transfer_preflight and args.native_remote_work and not args.native_remote_work_completion:
        parser.error("--native-transfer-preflight after network work requires --native-remote-work-completion so its paused checkpoint save is allowed")
    if args.protocol_client_assembly and not args.protocol_client_assembly.is_file():
        parser.error("--protocol-client-assembly must identify an existing test executable DLL")
    if (args.native_work or args.native_dupe) and not args.generate_colony:
        parser.error("native task fixtures require this run's generated disposable colony")
    output = args.output.resolve()
    status_path = output / "online-status.json"
    checks = []
    peers = []
    logs = []
    sequence = 0
    remote_work_prepared = False

    def status(max_age=10):
        if (output / "failure.json").exists():
            raise RuntimeError("Native game error recorded; inspect failure.json")
        if not status_path.exists():
            return {}
        try:
            value = json.loads(status_path.read_text())
        except json.JSONDecodeError:
            return {}
        if time.time() - status_path.stat().st_mtime > max_age:
            raise RuntimeError("Probe status is stale")
        if value.get("commandError"):
            raise RuntimeError(value["commandError"])
        return value

    def wait(predicate, description, timeout=20, max_age=10):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            value = status(max_age)
            if predicate(value):
                checks.append(description)
                print("PASS", description, flush=True)
                return value
            time.sleep(0.2)
        raise RuntimeError("Timeout: " + description)

    def command(action, value="", wait_ack=True):
        nonlocal sequence
        sequence += 1
        temporary = output / "command.tmp"
        temporary.write_text(json.dumps({"sequence": sequence, "action": action, "value": value}, ensure_ascii=False))
        temporary.replace(output / "command.json")
        if not wait_ack:
            return None
        transition = action in {"start", "survival", "online", "native_new", "generate", "embark", "save_test"}
        return wait(lambda s: s.get("command", 0) >= sequence, "probe command " + action,
                    timeout=120 if transition else 20, max_age=120 if transition else 10)

    def screenshot():
        if not args.screenshots:
            return
        if status().get("batchMode") or status().get("graphicsDevice") == "Null":
            raise RuntimeError("Screenshots require an explicitly graphical game")
        command("screenshot")
        path = output / ("screenshot-" + str(sequence) + ".png")
        wait(lambda _: path.exists() and path.stat().st_size > 10000, "game screenshot " + path.name)

    continuing = args.resume_from_characters or args.resume_frontend
    initial = wait(lambda s: s.get("joined") and not s.get("loaded") if continuing else s.get("mainMenuButton"),
                   "existing test room ready" if continuing else "main menu entry loaded", 180)
    sequence = initial["command"]
    if initial.get("dependencyError"):
        raise RuntimeError(initial["dependencyError"])
    receipt = {"status": "running", "build": initial.get("gameVersion"),
               "cycleTrimVersion": initial.get("cycleTrimVersion"), "checks": checks,
               "participants": {"completeGameInstances": 1, "peakProtocolClientProcesses": 4 if args.native_remote_work else 3,
                                "completeNativeGuestGames": 0},
               "scope": "One real Steam-launched ONI host plus " + ("four" if args.native_remote_work else "three") +
                        " same-host TCP protocol client processes at peak. No complete native guest game, two-device or Steam P2P acceptance."}
    try:
        if not continuing:
            command("host", "27142")
            command("show")
            current = wait(lambda s: s.get("joined") and s.get("popup") and s.get("players") == 1,
                           "real ONI creates LAN room and opens UI")
        else:
            current = initial
        address = urllib.parse.urlsplit(current["roomCode"])
        executable = args.protocol_client_assembly.resolve() if args.protocol_client_assembly else REPO / "tests/OniOnline.Tests/bin/Release/net10.0/OniOnline.Tests.dll"
        for name in ("PlayerA", "PlayerB", "PlayerC"):
            log = (output / (name + ".log")).open("w")
            logs.append(log)
            peer_args = ["dotnet", str(executable), "--peer", address.hostname,
                str(address.port), name, initial["gameVersion"], initial["cycleTrimVersion"], "", "900" if args.native_remote_work else "600"]
            if args.world_sync:
                peer_args.append(str(output / (name + "-observation.json")))
            peers.append(subprocess.Popen(peer_args,
                stdout=log, stderr=subprocess.STDOUT))
        wait(lambda s: s.get("players") == 4 and s.get("chatLines", 0) >= 3,
             "three external clients authenticate and chat with Unity host")
        command("chat", "中文聊天 <b>保持原样</b> 🚀")
        wait(lambda s: "保持原样" in s.get("chat", ""), "game UI sends Chinese chat")
        screenshot()
        if not args.resume_from_characters:
            if not args.resume_frontend:
                command("start")
            wait(lambda s: s.get("screen") in ("ModeSelectScreen", "ClusterCategorySelectionScreen"),
                 "host start enters native new-game flow")
            if status().get("screen") == "ModeSelectScreen":
                command("survival")
            wait(lambda s: s.get("onlineCategory"), "Online category with artwork exists")
            screenshot()

            command("online")
            wait(lambda s: s.get("popup"), "Online opens the public-system browser before native initialization")
            command("native_new")
            wait(lambda s: s.get("screen") == "ColonyDestinationSelectScreen", "new colony uses native Spaced Out destination selection")
            wait(lambda s: s.get("players") == 4, "room connection survives frontend transitions")

        if args.generate_colony:
            if not args.resume_from_characters:
                command("generate")
                wait(lambda s: s.get("screen") == "MinionSelectScreen", "native starter selection ready", 300, max_age=120)
            command("embark")
            wait(lambda s: s.get("loaded") and s.get("paused"), "native colony starts paused", 120)
            wait(lambda s: s.get("players") == 4, "all chat clients survive colony load")
            command("chat", "进入殖民地后仍可聊天")
            wait(lambda s: "进入殖民地后仍可聊天" in s.get("chat", ""), "chat works after colony load")
            screenshot()

            if args.native_chat_ui:
                if status().get("batchMode") or status().get("graphicsDevice") == "Null":
                    raise RuntimeError("--native-chat-ui requires an explicitly graphical game; no fallback native guest or screenshots were enabled")
                command("show")
                # Use the real UI SendChat path; the probe never constructs ChatLine records.
                for index in range(10):
                    paragraph = ("这是一条用于真实界面排版的长消息。阅读旧消息时，新消息不应该把画面拉到底部。" * 5 if index % 2 == 0 else
                                 "A long English message wraps inside the actual chat viewport. Reading older messages should retain the same row when new messages arrive. " * 2)
                    text = f"[chat-layout-{index}] <b>literal text</b> " + paragraph
                    command("chat", text)
                    wait(lambda s, text=text: text in s.get("chat", ""), "real long chat message " + str(index))
                    time.sleep(0.55)  # RoomSession's real five-per-two-second local rate limit still applies.

                chat_ui_path = output / "chat-ui-receipt.json"

                def chat_ui_receipt():
                    if not chat_ui_path.exists():
                        return {}
                    value = json.loads(chat_ui_path.read_text())
                    if value.get("phase") == "failed":
                        raise RuntimeError("Native chat UI failed: " + value.get("failure", "unknown"))
                    return value

                command("chat_ui_anchor")
                wait(lambda _: chat_ui_receipt().get("phase") == "anchored_waiting_message",
                     "real ScrollRect handler establishes a real-message reading anchor")
                command("chat", "[chat-layout-appended] 新消息到达，保留旧消息的阅读位置。 Keep the actual row anchored; <i>literal markup</i>.")
                wait(lambda _: chat_ui_receipt().get("phase") == "anchor_preserved",
                     "new real chat preserves viewport anchor within two logical pixels and shows latest button")
                screenshot()
                command("chat_ui_latest")
                wait(lambda _: chat_ui_receipt().get("passed") and chat_ui_receipt().get("phase") == "complete",
                     "actual latest Button handler follows bottom within twelve pixels with visible literal-text controls")
                receipt["nativeChatUi"] = chat_ui_receipt()
                screenshot()
                command("hide")

            if args.star_map:
                command("starmap")
                wait(lambda s: s.get("starMap") and s.get("starSystemPanel") and not s.get("sharedPause"),
                     "native star map hosts system controls with independent local time")
                screenshot()
                if args.screenshots and not args.fixed_window:
                    resized = command("resize", "1280x720")
                    wait(lambda s: s.get("screenWidth") == 1280 and s.get("screenHeight") == 720 and
                        s.get("frameCount", 0) >= resized.get("frameCount", 0) + 3, "native resize and layout frames settle")
                    screenshot()
                    resized = command("resize", "1858x1372")
                    wait(lambda s: s.get("screenWidth") == 1858 and s.get("screenHeight") == 1372 and
                        s.get("frameCount", 0) >= resized.get("frameCount", 0) + 3, "large native window layout settles")

                command("select_pod")
                wait(lambda s: s.get("printingEngineButton"), "native printing pod offers Planetary Engine entry")
                command("engine_entry")
                wait(lambda s: s.get("starMap") and s.get("engineStory"), "printing pod entry opens the story on the native star map")
                screenshot()
                command("native_step")
                wait(lambda s: s.get("localStepFinished") and s.get("localStepProgress", 0) > 0,
                     "native colony advances independently while three observation-only peers are connected")

            if args.native_dupe:
                command("dupe_native_fixture")
                current = wait(lambda s: s.get("nativeDupe", {}).get("orderQueued") and
                    s.get("nativeDupe", {}).get("replaySuppressed") and s.get("nativeDupe", {}).get("nativeExecuteCalls") == 1,
                    "native MoveTo accepted once for an inspected real starter duplicant")
                command("native_step")
                wait(lambda s: s.get("localStepFinished"), "guarded native movement step finishes paused")
                command("dupe_native_inspect")
                current = wait(lambda s: s.get("nativeDupe", {}).get("movedObserved"), "real duplicant moves through native navigation")
                receipt["nativeDupe"] = current["nativeDupe"]

            if args.native_work:
                command("work_native_fixture")
                current = wait(lambda s: s.get("nativeWork", {}).get("confirmationAccepted") and
                    s.get("nativeWork", {}).get("replaySuppressed") and s.get("nativeWork", {}).get("managedPresent") and
                    s.get("nativeWork", {}).get("unmanagedPresent"), "native dig authority executes once and marks only its own order")
                receipt["nativeWork"] = current["nativeWork"]
                if args.native_work_revocation:
                    command("work_native_revoke")
                    current = wait(lambda s: (s.get("nativeWork") or {}).get("revocationPhase") == "revoked_paused" and
                        s["nativeWork"].get("managedRemovalObserved") and not s["nativeWork"].get("managedPresent") and
                        s["nativeWork"].get("grantRevoked") and s["nativeWork"].get("ordinaryOrderPreserved") and
                        s["nativeWork"].get("revocationTimeUnchanged") and s["nativeWork"].get("revocationObservedFrames", 0) >= 1 and
                        s.get("paused"), "revoking permission removes only its pending native order across paused Unity frames")
                    phases = {"revoked": current["nativeWork"]}
                    receipt["nativeWorkRevocation"] = phases
                    command("work_native_regrant_replay")
                    current = wait(lambda s: (s.get("nativeWork") or {}).get("revocationPhase") == "old_replay_rejected_paused" and
                        s["nativeWork"].get("regrantConfirmed") and s["nativeWork"].get("grantTokenChanged") and
                        s["nativeWork"].get("oldReplayRejected") and s["nativeWork"].get("oldReplayReason") == "work_not_authorized" and
                        s["nativeWork"].get("oldReplayLeftManagedAbsent") and not s["nativeWork"].get("managedPresent") and
                        s["nativeWork"].get("ordinaryOrderPreserved") and s["nativeWork"].get("revocationTimeUnchanged") and s.get("paused"),
                        "regrant does not revive the old confirmation or recreate its physical order")
                    phases["oldReplayRejected"] = current["nativeWork"]
                    command("work_native_restore_fresh")
                    current = wait(lambda s: (s.get("nativeWork") or {}).get("revocationFinished") and
                        s["nativeWork"].get("revocationPhase") == "fresh_order_restored_paused" and
                        s["nativeWork"].get("freshPreviewAccepted") and s["nativeWork"].get("freshConfirmationAccepted") and
                        s["nativeWork"].get("freshRequestRestored") and s["nativeWork"].get("managedPresent") and
                        s["nativeWork"].get("managedMarked") and s["nativeWork"].get("managedPriority") == 7 and
                        s["nativeWork"].get("managedRequestId") != s["nativeWork"].get("originalManagedRequestId") and
                        s["nativeWork"].get("freshConfirmedRevision", 0) > s["nativeWork"].get("originalConfirmedRevision", 0) and
                        s["nativeWork"].get("ordinaryOrderPreserved") and s["nativeWork"].get("revocationTimeUnchanged") and
                        not s["nativeWork"].get("revocationFrozen") and s.get("paused"),
                        "only a fresh two-phase request restores the inspected native order without advancing simulation")
                    phases["freshOrderRestored"] = current["nativeWork"]
                    receipt["nativeWork"] = current["nativeWork"]
            if args.native_remote_work:
                wait(lambda s: s.get("planetAdmitted"), "native host planet is admitted before network work authority testing")
                command("native_step")
                wait(lambda s: s.get("localStepFinished") and s.get("paused") and s.get("localStepProgress", 0) > 0,
                    "one bounded preparation step initializes real starter navigation before the paused network fixture")
                command("network_work_prepare")
                current = wait(lambda s: (s.get("nativeRemoteWork") or {}).get("phase") == "ready_for_protocol_peer" and
                    s["nativeRemoteWork"].get("ordinaryPreserved") and not s["nativeRemoteWork"].get("managedPresent"),
                    "native host prepares an exact inspected cell with an unrelated ordinary control order")
                fixture = current["nativeRemoteWork"]
                remote_work_prepared = True
                peer_directory = output / "native-work-peer"
                peer_log = (output / "native-work-peer.log").open("w")
                logs.append(peer_log)
                work_peer = subprocess.Popen(["dotnet", str(executable), "--native-work-peer", address.hostname,
                    str(address.port), initial["gameVersion"], initial["cycleTrimVersion"], str(fixture["managedX"]),
                    str(fixture["managedY"]), fixture["epoch"], str(peer_directory), "240"], stdout=peer_log, stderr=subprocess.STDOUT)
                peers.append(work_peer)
                peer_sequence = 0

                def peer_status():
                    peer_receipt = peer_directory / "receipt.json"
                    if not peer_receipt.exists():
                        if work_peer.poll() is not None:
                            raise RuntimeError("Native-work protocol peer exited before writing its receipt")
                        return {}
                    value = json.loads(peer_receipt.read_text())
                    if value.get("error"):
                        raise RuntimeError("Native-work protocol peer: " + value["error"])
                    return value

                def peer_command(action):
                    nonlocal peer_sequence
                    peer_sequence += 1
                    temporary = peer_directory / "command.tmp"
                    temporary.write_text(json.dumps({"sequence": peer_sequence, "action": action}))
                    temporary.replace(peer_directory / "command.json")

                wait(lambda _: peer_status().get("stage") == "connected", "independent TCP work protocol peer authenticates with real native host", 45)
                identity = peer_status()["localProtocolIdentity"]
                peer_command("unauthorized")
                wait(lambda _: peer_status().get("stage") == "unauthorized_rejected", "native authority rejects an actual unauthorized network request")
                command("network_work_inspect")
                ungranted = wait(lambda s: not s["nativeRemoteWork"].get("managedPresent") and
                    s["nativeRemoteWork"].get("nativeCreatedOrders") == 0 and s["nativeRemoteWork"].get("confirmedPlans") == 0,
                    "unauthorized network request produces no native physical order")
                command("network_work_grant", identity)
                wait(lambda _: peer_status().get("receivedGrantPermissions", 0) & 2, "protocol peer receives its actual authority task grant")
                peer_command("dig")
                current = wait(lambda s: (s.get("nativeRemoteWork") or {}).get("nativeCreatedOrders") == 1 and
                    s["nativeRemoteWork"].get("nativeReconciliations") == 1 and
                    s["nativeRemoteWork"].get("managedMarked") and s["nativeRemoteWork"].get("managedPriority") == 7 and
                    s["nativeRemoteWork"].get("managedRequestId") == peer_status().get("confirmationRequestId") and
                    peer_status().get("withheldConfirmationReceipts", 0) > 0,
                    "network preview and confirmation create one real native order while acknowledgement is withheld", 45)
                phases = {"unauthorizedRejected": ungranted["nativeRemoteWork"], "createdBeforeReconnect": current["nativeRemoteWork"]}
                receipt["nativeRemoteWork"] = phases
                original_object = current["nativeRemoteWork"]["managedObjectId"]
                original_revision = current["nativeRemoteWork"]["confirmedRevision"]
                peer_command("disconnect_replay")
                wait(lambda _: peer_status().get("stage") == "replay_confirmed", "TCP reconnect resumes identity and replays the same confirmation", 60)
                command("network_work_inspect")
                current = wait(lambda s: s["nativeRemoteWork"].get("nativeCreatedOrders") == 1 and
                    s["nativeRemoteWork"].get("nativeReconciliations") == 1 and
                    s["nativeRemoteWork"].get("managedObjectId") == original_object and
                    s["nativeRemoteWork"].get("confirmedRevision") == original_revision,
                    "reconnect replay preserves the exact native object and canonical revision")
                phases["afterReconnectReplay"] = current["nativeRemoteWork"]
                command("network_work_revoke")
                current = wait(lambda s: not s["nativeRemoteWork"].get("managedPresent") and
                    s["nativeRemoteWork"].get("confirmedPlans") == 0 and s["nativeRemoteWork"].get("ordinaryPreserved") and
                    s["nativeRemoteWork"].get("nativeRemovalRequests") == 1 and peer_status().get("receivedGrantPermissions") == 0,
                    "real host revocation removes only the remote actor's native order across Unity frames")
                phases["revoked"] = current["nativeRemoteWork"]
                command("network_work_regrant")
                wait(lambda _: peer_status().get("receivedGrantPermissions", 0) & 2, "protocol peer receives the fresh regrant token")
                peer_command("replay_old")
                wait(lambda _: peer_status().get("stage") == "old_request_rejected", "old grant confirmation is rejected over the real TCP session")
                command("network_work_inspect")
                current = wait(lambda s: not s["nativeRemoteWork"].get("managedPresent") and
                    s["nativeRemoteWork"].get("nativeCreatedOrders") == 1 and s["nativeRemoteWork"].get("confirmedRevision") == original_revision,
                    "old network request does not recreate a physical order after regrant")
                phases["oldRequestRejected"] = current["nativeRemoteWork"]
                peer_command("fresh")
                wait(lambda _: peer_status().get("stage") == "fresh_complete", "fresh network preview and confirmation are accepted")
                current = wait(lambda s: s["nativeRemoteWork"].get("nativeCreatedOrders") == 2 and
                    s["nativeRemoteWork"].get("nativeReconciliations") == 3 and
                    s["nativeRemoteWork"].get("managedMarked") and s["nativeRemoteWork"].get("managedObjectId") != original_object and
                    s["nativeRemoteWork"].get("managedRequestId") == peer_status().get("confirmationRequestId") and
                    s["nativeRemoteWork"].get("ordinaryPreserved") and s["nativeRemoteWork"].get("gameTimeUnchanged") and s.get("paused"),
                    "only the fresh network request restores one native order while paused")
                phases["freshRestored"] = current["nativeRemoteWork"]
                if args.native_task_ui:
                    before_ui = command("task_ui_open")
                    ui_receipt = output / "task-ui-receipt.json"
                    wait(lambda _: ui_receipt.exists(), "real task map input acceptance receipt written", 40)
                    ui = json.loads(ui_receipt.read_text())
                    if not ui.get("passed"):
                        raise RuntimeError("Native task UI acceptance failed")
                    wait(lambda s: s.get("frameCount", 0) >= before_ui.get("frameCount", 0) + 3,
                         "native task UI layout and overlay frames settle")
                    receipt["nativeTaskUi"] = ui
                    screenshot()
                    command("task_ui_close")
                if args.native_remote_work_completion:
                    command("network_work_completion_begin")
                    for _ in range(30):
                        command("native_step")
                        wait(lambda s: s.get("localStepFinished") and s.get("paused"), "bounded native digging step ends paused")
                        command("network_work_completion_inspect")
                        current = status()
                        if current["nativeRemoteWork"].get("completionFinished"):
                            break
                    else:
                        raise RuntimeError("Native dig did not complete with full cleanup within thirty bounded steps; no completion pass")
                    phases["naturalCompletion"] = current["nativeRemoteWork"]
                    command("network_work_revoke")
                    command("network_work_regrant")
                    command("network_work_completion_inspect")
                    current = wait(lambda s: s["nativeRemoteWork"].get("completionFinished") and
                        not s["nativeRemoteWork"].get("managedPresent") and s["nativeRemoteWork"].get("nativeCreatedOrders") == 2,
                        "grant changes do not revive a naturally completed native task")
                    command("save_test")
                    saved = wait(lambda s: bool(s["nativeRemoteWork"].get("completionSavePath")), "naturally completed task cleanup saved in a fresh native checkpoint", 90)
                    phases["completionCheckpoint"] = {key: saved.get(key) for key in ("planetId", "homeSystemId", "testSavePath", "modSha256")}
                    phases["afterCompletionGrantChanges"] = saved["nativeRemoteWork"]
                peer_command("leave")
                wait(lambda _: peer_status().get("passed") and peer_status().get("stage") == "complete", "work protocol peer departs with acknowledged leave")
                if work_peer.wait(timeout=10) != 0:
                    raise RuntimeError("Native-work protocol peer exited unsuccessfully")
                phases["protocolPeer"] = peer_status()
                command("network_work_finish")
                current = wait(lambda s: s["nativeRemoteWork"].get("complete") and s.get("paused"), "native network-work scenario completes paused")
                phases["finalHost"] = current["nativeRemoteWork"]
                if args.native_dupe_archive:
                    command("archive_selected_starter")
                    manifests = sorted(output.glob("native-dupe-archive-*/manifest.json"))
                    if len(manifests) != 1:
                        raise RuntimeError("Exactly one fresh native starter archive is required")
                    manifest = json.loads(manifests[0].read_text())
                    if not manifest.get("PhysicalFingerprintUnchanged") or manifest.get("ImportReady"):
                        raise RuntimeError("Native export changed its source or claimed unverified import readiness")
                    receipt["nativeDupeArchive"] = manifest
                remote_work_prepared = False
            if args.save_identity or args.native_work:
                before = status()
                if not before.get("planetId") or not before.get("homeSystemId"):
                    raise RuntimeError("Native colony has no persistent online identity")
                command("save_test")
                saved = wait(lambda s: bool(s.get("testSavePath")), "fresh native test checkpoint written without overwriting a save", 90)
                receipt["identityCheckpoint"] = {key: saved.get(key) for key in
                    ("planetId", "homeSystemId", "homeName", "testSavePath", "modSha256", "modAssemblyPath")}

            if args.native_transfer_preflight:
                command("save_test", "activate_preflight")
                wait(lambda s: bool(s.get("testSavePath")), "explicit fresh private checkpoint activated for read-only transfer preflight", 90)
                command("native_transfer_preflight")
                transfer = json.loads((output / "native-transfer-preflight.json").read_text())
                if not transfer.get("preflightExecuted") or not transfer.get("preflightCheckPassed") or not transfer.get("nativeFingerprintUnchanged"):
                    raise RuntimeError("Read-only native source preflight execution/fingerprint failed")
                if transfer.get("importReady") or transfer.get("migrationAcceptancePassed"):
                    raise RuntimeError("Read-only source preflight incorrectly claimed migration acceptance")
                receipt["nativeTransferPreflight"] = transfer
                checks.append("native source inspection preserves original starters, amounts, assignments, time, grid and private save bytes; eligibility reported separately")
                command("native_world_capacity_preflight")
                capacity = json.loads((output / "native-world-capacity-preflight.json").read_text())
                if not capacity.get("preflightExecuted") or not capacity.get("preflightCheckPassed") or not capacity.get("nativeFingerprintUnchanged"):
                    raise RuntimeError("Read-only native capacity preflight execution/fingerprint failed")
                if capacity.get("importReady") or capacity.get("migrationAcceptancePassed"):
                    raise RuntimeError("Read-only capacity preflight incorrectly claimed migration acceptance")
                receipt["nativeWorldCapacityPreflight"] = capacity
                checks.append("native 256x384 target capacity and real empty axial hex inspected without allocating or loading a world; availability reported separately")

            if args.world_sync:
                command("share", "true")
                wait(lambda s: s.get("localSharedCells", 0) > 0 and s.get("paused") and not s.get("sharedPause"),
                     "native explored cells captured with local pause and no shared-clock controller")
                def observers_ready(_):
                    receipts = [output / (name + "-observation.json") for name in ("PlayerA", "PlayerB", "PlayerC")]
                    return all(p.exists() and json.loads(p.read_text()).get("cells", 0) > 0 for p in receipts)
                wait(observers_ready, "all three external observers assemble real authorized colony data", 90)
                receipt["nativeWorldObservers"] = [json.loads((output / (name + "-observation.json")).read_text())
                                                   for name in ("PlayerA", "PlayerB", "PlayerC")]
                command("worlds")
                screenshot()
                command("share", "false")
                wait(lambda s: s.get("localSharedCells", 0) == 0, "native sharing can be revoked")

        if args.star_map:
            command("leave")
            command("starmap")
            command("map_host")
            wait(lambda s: s.get("joined") and s.get("started") and s.get("starMap") and not s.get("popup"),
                 "native star-map controls name and open a running system without a room-creation modal")
            screenshot()

        receipt["lanFinal"] = status()
        if args.management_ui:
            if not status().get("loaded"):
                raise RuntimeError("Management UI acceptance requires a loaded test colony")
            command("work_plans")
            wait(lambda s: s.get("workPlanWindow"), "native task-layer window opens")
            screenshot()
            command("team")
            wait(lambda s: s.get("teamWindow") and not s.get("workPlanWindow"), "native team window replaces task window")
            screenshot()
        if args.steam_lobby:
            command("leave")
            command("host_steam")
            wait(lambda s: s.get("joined") and s.get("roomCode", "").startswith("oni://steam/"),
                 "native Steam lobby creation", 35)
            receipt["steamLobby"] = status()["roomCode"]
            command("search")
            wait(lambda s: s.get("ownPublicRoom"), "Steam public listing discovers the real hosted room", 35)
            receipt["steamP2P"] = "Not tested: requires another Steam account/device"
        receipt["status"] = "passed"
        receipt["final"] = status()
        if not args.keep_running:
            command("quit", wait_ack=False)
    except Exception as error:
        if remote_work_prepared:
            try:
                command("network_work_freeze", str(error), wait_ack=False)
            except Exception as freeze_error:
                receipt["nativeRemoteWorkFreezeDeliveryError"] = str(freeze_error)
        receipt["status"] = "failed"
        receipt["error"] = str(error)
        raise
    finally:
        for peer in peers:
            if peer.poll() is None:
                peer.terminate()
            try:
                peer.wait(timeout=5)
            except subprocess.TimeoutExpired:
                peer.kill()
        for log in logs:
            log.close()
        (output / "smoke-receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    main()
