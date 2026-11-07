using System;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Network;
using OniOnline.Sdk;

internal static class FrameworkPrimitiveTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(!SessionCapabilities.Core.Contains(SessionCapabilities.StrictAdmissionV1) &&
            !SessionCapabilities.Core.Contains(SessionCapabilities.SharedSimulationV1) &&
            !SessionCapabilities.Core.Contains(SessionCapabilities.SharedClockV1) &&
            !SessionCapabilities.Core.Contains(SessionCapabilities.PresenceV1),
            "chat keeps build, DLC and shared-simulation contracts out of the default session");

        var same = Endpoint("1", "744825", new[] { "EXPANSION1_ID" }, new[] { Mod("OniOnline", "0.5.0", "OniOnline") });
        var match = StrictAdmission.Evaluate(same, same, "guest");
        check(match.Accepted && match.Summary == "The games match." && match.Problems.Length == 0,
            "identical gameplay, build, DLC and mods pass strict admission");

        var otherBuild = Endpoint("1", "700000", new[] { "EXPANSION1_ID" }, new[] { Mod("OniOnline", "0.5.0", "OniOnline") });
        var rejected = StrictAdmission.Evaluate(same, otherBuild, "guest");
        check(!rejected.Accepted && rejected.Summary.Contains("game build differs") && rejected.Summary.StartsWith("Cannot play together:"),
            "strict admission names a build mismatch");

        string[] many = Enumerable.Range(1, 11).Select(i => "DLC" + i.ToString()).ToArray();
        var crowded = StrictAdmission.Evaluate(Endpoint("1", "744825", many, new StrictMod[0]),
            Endpoint("1", "744825", new string[0], new StrictMod[0]), "guest");
        check(!crowded.Accepted && crowded.Problems.Length == 10 && crowded.Omitted == 1 && crowded.Summary.Contains("and 1 more"),
            "strict admission lists at most ten differences");

        string[] negotiated = SessionCapabilities.Core;
        check(StrictAdmission.Gate(negotiated, same, otherBuild, "guest") == null,
            "without the strict contract a build mismatch does not block the session");
        string[] strict = negotiated.Concat(new[] { SessionCapabilities.StrictAdmissionV1 }).ToArray();
        check(StrictAdmission.Gate(strict, same, otherBuild, "guest").Contains("game build differs") &&
            StrictAdmission.Gate(strict, null, otherBuild, "guest") == "strict_admission_missing",
            "the strict contract returns the readable difference list and refuses a missing inventory");

        check(!SessionCapabilities.UsesSharedClock(new[] { SessionCapabilities.SharedClockV1 }) &&
            SessionCapabilities.UsesSharedClock(new[] { SessionCapabilities.SharedSimulationV1, SessionCapabilities.SharedClockV1 }) &&
            SessionCapabilities.UsesPresence(new[] { SessionCapabilities.PresenceV1 }),
            "shared pause requires a shared simulation, while cursors only require presence");

        var admission = new SnapshotAdmission();
        admission.Hold(0);
        admission.Offer(0, 4, new byte[] { 9 }, "finger", 100);
        check(admission.Pump(5) && admission.StallReports == 1 && admission.Status == SnapshotAdmissionStatus.Offered,
            "a snapshot with no new bytes records a stall");
        admission.NoteProgress(6, 100);
        check(!admission.Pump(12) && admission.StallReports == 1, "a fully sent snapshot does not keep logging stalls");
        check(!admission.ReportLoaded(13, 3, new byte[] { 9 }, "finger") && admission.Status == SnapshotAdmissionStatus.Offered,
            "a report for an older snapshot id does not complete the current offer");
        check(!admission.ReportLoaded(14, 4, new byte[] { 8 }, "finger") && admission.Failure == "checksum_mismatch",
            "a checksum mismatch fails the admission");

        admission = new SnapshotAdmission();
        admission.Hold(0);
        admission.Offer(10, 4, new byte[] { 9 }, "finger", 10);
        check(admission.Pump(100) && admission.Failure == "client_load_timeout",
            "a client that never reports the fingerprint times out");
        admission = new SnapshotAdmission();
        admission.Hold(0);
        check(admission.Pump(180) && admission.Failure == "host_load_timeout",
            "a hold that never produces a loaded snapshot times out");
        admission = new SnapshotAdmission();
        admission.Hold(0);
        admission.Offer(1, 7, new byte[] { 1, 2 }, "abc", 3);
        admission.SetGeneration(2, 2);
        check(admission.Status == SnapshotAdmissionStatus.Idle && !admission.ReportLoaded(3, 7, new byte[] { 1, 2 }, "abc"),
            "an admission generation change discards the outstanding snapshot");
        admission.Hold(4);
        admission.Offer(4, 7, new byte[] { 1, 2 }, "abc", 3);
        check(admission.ReportLoaded(5, 7, new byte[] { 1, 2 }, "abc") && admission.Status == SnapshotAdmissionStatus.Loaded,
            "matching checksum and fingerprint complete the snapshot");

        var board = new ContentDigestBoard();
        board.Publish(1, new ulong[] { 1, 2, 3, 4 });
        int[] repair;
        check(board.Observe(1, new ulong[] { 1, 2, 3, 4 }, out repair) == DigestDecision.Match,
            "equal chunk hashes match");
        check(board.Observe(1, new ulong[] { 1, 9, 3, 4 }, out repair) == DigestDecision.Repair && repair.SequenceEqual(new[] { 1 }),
            "a minority of differing chunks is repaired");
        check(board.Observe(1, new ulong[] { 9, 9, 9, 4 }, out repair) == DigestDecision.ResendSnapshot,
            "a majority of differing chunks asks for the snapshot again");
        check(board.Observe(2, new ulong[] { 1 }, out repair) == DigestDecision.Quarantine && board.Has(1),
            "an unknown snapshot is quarantined and the published hashes stay");

        var commands = new CommandInFlight();
        check(commands.TryAdmit("dig:1", 1, 0) && !commands.TryAdmit("dig:1", 1, 1),
            "the same command is not sent again before the authority answers");
        check(!commands.Acknowledge("dig:1", 2) && commands.Acknowledge("dig:1", 1) && commands.TryAdmit("dig:1", 1, 2),
            "only the matching generation acknowledgement releases the command");
        check(commands.Expire(2 + CommandInFlight.DefaultTimeoutSeconds, CommandInFlight.DefaultTimeoutSeconds) == 1 &&
            commands.TryAdmit("dig:1", 1, 100),
            "an unanswered command expires");
        commands.TryAdmit("build:2", 4, 100);
        commands.DropOtherGenerations(4);
        check(commands.Count == 1 && !commands.Acknowledge("dig:1", 1),
            "an admission generation change drops commands from the previous generation");

        var presence = new PresenceBoard();
        check(!presence.TryCursor(new PresenceCursor("a", 1, 2, 3)), "presence input is ignored until the contract is negotiated");
        presence.Enabled = true;
        check(presence.TryCursor(new PresenceCursor("a", 1, 2, 3)) && presence.TryCursor(new PresenceCursor("a", 1, 8, 9)) &&
            presence.CursorFor("a").X == 8, "a later cursor replaces the previous one for that player");
        check(presence.TryMark(new PresenceMark("a", 1, 4, 5, 1)) && !presence.TryMark(new PresenceMark("a", 1, 0, 0, 1)) &&
            presence.TryMark(new PresenceMark("a", 1, 6, 7, 2)) && presence.Marks.Length == 2,
            "map marks keep increasing sequences");
        presence.Drop("a");
        check(presence.CursorFor("a") == null && presence.Marks.Length == 0, "leaving removes that player's cursor and marks");

        var clock = new SharedClockPolicy();
        check(!clock.TryPause("a", true, 1), "shared pause does nothing until the gameplay enables it");
        clock.Enabled = true;
        check(clock.TryPause("a", true, 1) && clock.Paused && !clock.TryPause("b", false, 1) && clock.TryPause("b", false, 2) && !clock.Paused,
            "any participant can pause, and an older revision is ignored");
        check(clock.TrySpeed("a", 3, 3) && clock.Speed == 3 && !clock.TrySpeed("a", 0, 4) && clock.Speed == 3,
            "the newest speed revision wins and speeds stay inside 1..3");

        string root = TestSlotLayout.Root("/tmp/onionline-tests", 2);
        check(root.EndsWith("onionline-slot2") && TestSlotLayout.Saves(root).EndsWith(Path.Combine("onionline-slot2", "save_files")) &&
            TestSlotLayout.Preferences(root).EndsWith(Path.Combine("onionline-slot2", "playerprefs")),
            "a test slot keeps saves and preferences apart from the primary profile");
        bool rejectedSlot = false;
        try { TestSlotLayout.Root("/tmp/onionline-tests", 0); } catch (ArgumentOutOfRangeException) { rejectedSlot = true; }
        check(rejectedSlot, "slot zero is the primary profile and is not redirected");

        string scrubbed = SessionLogPrivacy.Scrub("player LIghtJUNction steam 76561199137573787\nCHAT hello secret\nbuild 744825\n",
            new[] { "LIghtJUNction" });
        check(scrubbed.Contains("[steam]") && scrubbed.Contains("[name]") && !scrubbed.Contains("76561199137573787") &&
            !scrubbed.Contains("hello secret") && scrubbed.Contains("build 744825"),
            "bug reports remove Steam IDs, player names and chat lines");

        string zip = Path.Combine(Path.GetTempPath(), "onionline-log-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            SessionLogPrivacy.WriteBundle(zip, new[] { new LogEntry("host.txt", "id 76561198878493534\nCHAT hidden\n") }, new string[0]);
            byte[] packed = File.ReadAllBytes(zip);
            string ascii = Encoding.ASCII.GetString(packed);
            check(packed.Length > 4 && packed[0] == 0x50 && packed[1] == 0x4B && ascii.Contains("[steam]") &&
                !ascii.Contains("76561198878493534") && !ascii.Contains("hidden"),
                "the report bundle stores the scrubbed log");
        }
        finally { if (File.Exists(zip)) File.Delete(zip); }

        // --- DeterministicNetId Tests ---
        long buildingId1 = DeterministicNetId.ForBuilding(0, 1024, "OxygenDiffuser", 1);
        long buildingId2 = DeterministicNetId.ForBuilding(0, 1024, "OxygenDiffuser", 1);
        long buildingIdDiffCell = DeterministicNetId.ForBuilding(0, 1025, "OxygenDiffuser", 1);
        long buildingIdDiffWorld = DeterministicNetId.ForBuilding(1, 1024, "OxygenDiffuser", 1);
        long buildingIdDiffPrefab = DeterministicNetId.ForBuilding(0, 1024, "ManualGenerator", 1);
        long buildingIdDiffLayer = DeterministicNetId.ForBuilding(0, 1024, "OxygenDiffuser", 2);

        check(buildingId1 == buildingId2 && buildingId1 > 0 && DeterministicNetId.IsStatic(buildingId1) && !DeterministicNetId.IsDynamic(buildingId1),
            "deterministic building net id produces identical static positive ids for identical inputs");
        check(buildingId1 != buildingIdDiffCell && buildingId1 != buildingIdDiffWorld && buildingId1 != buildingIdDiffPrefab && buildingId1 != buildingIdDiffLayer,
            "deterministic building net id differentiates world, cell, prefab, and layer");

        long cellId1 = DeterministicNetId.ForCell(0, 500, "dig");
        long cellId2 = DeterministicNetId.ForCell(0, 500, "mop");
        check(cellId1 != cellId2 && DeterministicNetId.IsStatic(cellId1), "cell net id differentiates task subcategories");

        long dynamicId = DeterministicNetId.ForDynamic(3, 42, 101);
        check(DeterministicNetId.IsDynamic(dynamicId) && !DeterministicNetId.IsStatic(dynamicId),
            "dynamic net id sets the high bit flag");
        check(DeterministicNetId.GetPeerId(dynamicId) == 3 &&
            DeterministicNetId.GetSequence(dynamicId) == 42 &&
            DeterministicNetId.GetTypeTag(dynamicId) == 101,
            "dynamic net id preserves peer id, sequence number, and type tag bit fields");

        // --- NetworkIdentityMap Tests ---
        var map = new NetworkIdentityMap<object>();
        var entityA = new object();
        var entityB = new object();
        check(map.TryRegister(1001, entityA) && !map.TryRegister(1001, entityB) && !map.TryRegister(1002, entityA),
            "identity map registers unique pairs and rejects duplicate keys or entities");

        object resolved;
        long resolvedId;
        check(map.TryGetEntity(1001, out resolved) && ReferenceEquals(resolved, entityA) &&
            map.TryGetId(entityA, out resolvedId) && resolvedId == 1001,
            "identity map resolves bidirectionally");

        check(map.Unregister(1001, now: 10.0, tombstoneSeconds: 5.0) && map.Count == 0 && map.TombstoneCount == 1,
            "unregistered entity leaves a temporary tombstone");
        check(!map.TryRegister(1001, entityB), "active tombstone prevents immediate id reuse");

        map.PurgeExpiredTombstones(now: 12.0);
        check(map.TombstoneCount == 1 && !map.TryRegister(1001, entityB), "unexpired tombstone stays active");

        map.PurgeExpiredTombstones(now: 16.0);
        check(map.TombstoneCount == 0 && map.TryRegister(1001, entityB), "expired tombstone purges and allows id reuse");

        // --- PresenceBoard Tool Drag & Ping Tests ---
        var drag = new ToolDragPresence("player_1", 0, "dig", ToolDragMode.Box, 10, 10, 15, 15, 7, "Sandstone", 0xFF0000FF);
        check(presence.TryToolDrag(drag) && presence.ToolDragFor("player_1") != null &&
            presence.ToolDragFor("player_1").ToolId == "dig" && presence.ToolDragFor("player_1").Priority == 7 &&
            presence.ToolDrags.Length == 1 && presence.ToolDrags[0].StartX == 10 &&
            presence.Cursors.Length == 0,
            "presence board retains active tool drag state for player and exposes collections");

        presence.ClearToolDrag("player_1");
        check(presence.ToolDragFor("player_1") == null && presence.ToolDrags.Length == 0, "clear tool drag removes the player drag preview");

        var ping = new PlayerPing("player_1", 0, 12, 12, "alert", 100.5);
        check(presence.TryPing(ping) && presence.Pings.Length == 1 && presence.Pings[0].Category == "alert",
            "presence board retains and rotates pings");

        presence.Drop("player_1");
        check(presence.Pings.Length == 0 && presence.ToolDragFor("player_1") == null && presence.ToolDrags.Length == 0,
            "player leave removes active tool drags and pings");

        // --- AnimReconciler & AnimSyncSharder Tests ---
        var localSnapshot = new AnimSnapshot(501, 1234, 1, 1.0f, 0.50f, 10.0);
        var authMatching = new AnimSnapshot(501, 1234, 1, 1.0f, 0.52f, 10.0);
        var correctionMatch = AnimReconciler.Evaluate(localSnapshot, authMatching, float.NaN, -1f);
        check(correctionMatch.Action == AnimCorrectionAction.None,
            "animation within drift tolerance requires no correction even with invalid tolerance clamps");

        var authDiffAnim = new AnimSnapshot(501, 5678, 2, 1.0f, 0.10f, 10.0);
        var correctionDiff = AnimReconciler.Evaluate(localSnapshot, authDiffAnim);
        check(correctionDiff.Action == AnimCorrectionAction.Replay && correctionDiff.TargetAnimHash == 5678,
            "differing animation forces replay with authoritative anim hash");

        // Small drift (0.35s drift > 0.15s tolerance, <= 0.6s max nudge) -> NudgeSpeed
        var authSmallDrift = new AnimSnapshot(501, 1234, 1, 1.0f, 0.15f, 10.0);
        var correctionNudge = AnimReconciler.Evaluate(localSnapshot, authSmallDrift);
        check(correctionNudge.Action == AnimCorrectionAction.NudgeSpeed && correctionNudge.RecommendedSpeed < 1.0f,
            "small positive drift nudges local animation playback speed down to converge");

        // Large drift (> 0.6s) -> SnapTime
        var authLargeDrift = new AnimSnapshot(501, 1234, 1, 1.0f, 2.50f, 10.0);
        var correctionSnap = AnimReconciler.Evaluate(localSnapshot, authLargeDrift);
        check(correctionSnap.Action == AnimCorrectionAction.SnapTime && Math.Abs(correctionSnap.TargetElapsedTime - 2.50f) < 0.001f,
            "large drift forces immediate snap to authoritative time");

        int shardA = AnimSyncSharder.GetShard(100, 5);
        int shardB = AnimSyncSharder.GetShard(101, 5);
        check(shardA >= 0 && shardA < 5 && shardB >= 0 && shardB < 5, "animation sharder bounds shards between 0 and N-1");
        check(AnimSyncSharder.IsEntityInShard(100, shardA, 5), "animation sharder accurately classifies target shard");
    }

    private static StrictEndpoint Endpoint(string gameplay, string build, string[] dlc, StrictMod[] mods)
    { return new StrictEndpoint(gameplay, build, dlc, mods); }

    private static StrictMod Mod(string id, string version, string title)
    { return new StrictMod(id, version, title); }
}
