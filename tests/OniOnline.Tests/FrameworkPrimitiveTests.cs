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
        check(commands.TryAdmit("dig:1", 1, 0) && !commands.TryAdmit("dig:1", 1, 1) &&
            !commands.TryAdmit("dig:2", 1, double.NaN) && !commands.TryAdmit("dig:3", 1, -1.0),
            "the same command is not sent again before the authority answers and invalid timestamps are rejected");
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
        clock.Reset();
        check(!clock.Paused && clock.Speed == 1 && clock.Revision == 0 && clock.ActorId == null,
            "reset clears clock policy state");

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
        check(map.IsTombstoned(1001) && map.IsTombstoned(1001, 12.0) && !map.IsTombstoned(1001, 16.0),
            "is tombstoned identifies active and expired tombstones");
        check(!map.TryRegister(1001, entityB), "active tombstone prevents immediate id reuse");

        map.PurgeExpiredTombstones(now: 12.0);
        check(map.TombstoneCount == 1 && !map.TryRegister(1001, entityB), "unexpired tombstone stays active");

        map.PurgeExpiredTombstones(now: 16.0);
        check(map.TombstoneCount == 0 && !map.IsTombstoned(1001) && map.TryRegister(1001, entityB), "expired tombstone purges and allows id reuse");

        var entityC = new object();
        var entityD = new object();
        check(map.TryRegister(7, entityC) && map.Unregister(7) &&
            map.TombstoneCount == 1 && map.IsTombstoned(7) && map.IsTombstoned(7, 0.0) &&
            !map.IsTombstoned(7, NetworkIdentityMap<object>.DefaultTombstoneSeconds) &&
            !map.TryRegister(7, entityD) && !map.TryRegister(7, entityD, now: 5.0),
            "default unregister leaves a 10-second anti-ABA tombstone blocking immediate reuse");
        check(map.TryRegister(7, entityD, now: NetworkIdentityMap<object>.DefaultTombstoneSeconds) &&
            map.TombstoneCount == 0 && map.Count == 2,
            "timestamped TryRegister automatically purges an expired tombstone without waiting for PurgeExpiredTombstones");
        check(map.Unregister(7, now: 20.0) && map.TombstoneCount == 1, "unregister at t=20 records tombstone");
        map.Clear(preserveTombstones: true);
        check(map.Count == 0 && map.TombstoneCount == 1 && !map.TryRegister(7, entityC),
            "Clear(preserveTombstones: true) drops active entities while preserving anti-ABA tombstones");
        map.Clear();
        check(map.Count == 0 && map.TombstoneCount == 0, "default Clear resets both entities and tombstones");
        check(!map.Unregister(1001, double.NaN, 5.0) && !map.Unregister(1001, 10.0, double.PositiveInfinity) &&
            map.UnregisterEntity(entityB, 20.0, 5.0) && map.IsTombstoned(1001, 22.0),
            "identity map rejects non-finite timestamps and supports unregistering by entity instance");

        // --- PresenceBoard Tool Drag & Ping Tests ---
        var drag = new ToolDragPresence("player_1", 0, "dig", ToolDragMode.Box, 10, 10, 15, 15, 7, "Sandstone", 0xFF0000FF);
        check(presence.TryToolDrag(drag) && presence.ToolDragFor("player_1") != null &&
            presence.ToolDragFor("player_1").ToolId == "dig" && presence.ToolDragFor("player_1").Priority == 7 &&
            presence.ToolDrags.Length == 1 && presence.ToolDrags[0].StartX == 10 &&
            presence.Cursors.Length == 0,
            "presence board retains active tool drag state for player and exposes collections");
        bool invalidDragRejected = false;
        try { new ToolDragPresence("player_1", 0, "dig", (ToolDragMode)99, 0, 0, 1, 1); } catch (ArgumentOutOfRangeException) { invalidDragRejected = true; }
        bool invalidPingRejected = false;
        try { new PlayerPing("player_1", 0, 1, 1, "   ", 0); } catch (ArgumentException) { invalidPingRejected = true; }
        bool nanPingRejected = false;
        try { new PlayerPing("player_1", 0, 1, 1, "alert", double.NaN); } catch (ArgumentOutOfRangeException) { nanPingRejected = true; }
        check(invalidDragRejected && invalidPingRejected && nanPingRejected,
            "tool drag and player ping validate mode, non-whitespace category, and finite timestamp");

        presence.ClearToolDrag("player_1");
        check(presence.ToolDragFor("player_1") == null && presence.ToolDrags.Length == 0, "clear tool drag removes the player drag preview");

        var ping = new PlayerPing("player_1", 0, 12, 12, "alert", 100.5);
        check(presence.TryPing(ping) && presence.Pings.Length == 1 && presence.Pings[0].Category == "alert",
            "presence board retains and rotates pings");

        presence.Clear();
        check(presence.Pings.Length == 0 && presence.ToolDrags.Length == 0 && presence.Marks.Length == 0 && presence.Cursors.Length == 0,
            "presence board clear removes all active cursors, drags, marks, and pings");

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

        var authDiffMode = new AnimSnapshot(501, 1234, 2, 1.0f, 0.50f, 10.0);
        var correctionDiffMode = AnimReconciler.Evaluate(localSnapshot, authDiffMode);
        check(correctionDiffMode.Action == AnimCorrectionAction.Replay && correctionDiffMode.TargetPlayMode == 2,
            "differing play mode forces replay with authoritative mode");

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

        var authInfiniteElapsed = new AnimSnapshot(501, 1234, 1, float.PositiveInfinity, float.PositiveInfinity, 10.0);
        var correctionInf = AnimReconciler.Evaluate(localSnapshot, authInfiniteElapsed);
        var authInfiniteReplay = new AnimSnapshot(501, 9999, 2, float.NegativeInfinity, float.PositiveInfinity, 10.0);
        var correctionInfReplay = AnimReconciler.Evaluate(localSnapshot, authInfiniteReplay);
        check(!float.IsInfinity(correctionInf.TargetElapsedTime) && !float.IsNaN(correctionInf.TargetElapsedTime) &&
            correctionInf.TargetElapsedTime == 0f && correctionInf.RecommendedSpeed == 1.0f &&
            !float.IsInfinity(correctionInfReplay.TargetElapsedTime) && correctionInfReplay.TargetElapsedTime == 0f,
            "infinite elapsed time and speed are sanitized to finite non-negative bounds");

        int shardA = AnimSyncSharder.GetShard(100, 5);
        int shardB = AnimSyncSharder.GetShard(101, 5);
        check(shardA >= 0 && shardA < 5 && shardB >= 0 && shardB < 5, "animation sharder bounds shards between 0 and N-1");
        check(AnimSyncSharder.IsEntityInShard(100, shardA, 5), "animation sharder accurately classifies target shard");

        // --- CellBatchCodec Tests ---
        byte[] boxPayload = CellBatchCodec.EncodeBox(0, 10, 20, 30, 40, "DigTool", 7, "Sandstone");
        int bWorld, bStartX, bStartY, bEndX, bEndY, bPriority;
        string bTool, bMat;
        check(boxPayload != null && boxPayload.Length > 0 &&
            CellBatchCodec.TryDecodeBox(boxPayload, out bWorld, out bStartX, out bStartY, out bEndX, out bEndY, out bTool, out bPriority, out bMat) &&
            bWorld == 0 && bStartX == 10 && bStartY == 20 && bEndX == 30 && bEndY == 40 && bTool == "DigTool" && bPriority == 7 && bMat == "Sandstone",
            "cell batch codec serializes and deserializes rectangular box regions accurately");

        int[] sampleCells = new[] { 100, 101, 102, 150, 151, 200 };
        byte[] cellsPayload = CellBatchCodec.EncodeCells(1, sampleCells, "BuildTool", 9, "Cu");
        int cWorld, cPriority;
        int[] cCells;
        string cTool, cMat;
        check(CellBatchCodec.TryDecodeCells(cellsPayload, out cWorld, out cCells, out cTool, out cPriority, out cMat) &&
            cWorld == 1 && cPriority == 9 && cTool == "BuildTool" && cMat == "Cu" && cCells.SequenceEqual(sampleCells),
            "cell batch codec delta-encodes and reconstructs discrete cell lists accurately");

        check(!CellBatchCodec.TryDecodeBox(new byte[5], out bWorld, out bStartX, out bStartY, out bEndX, out bEndY, out bTool, out bPriority, out bMat) &&
            !CellBatchCodec.TryDecodeCells(new byte[5], out cWorld, out cCells, out cTool, out cPriority, out cMat),
            "cell batch codec rejects truncated or invalid payloads safely");

        int[] maxBatchCells = Enumerable.Range(1000, CellBatchCodec.MaximumCellsPerBatch).ToArray();
        byte[] maxBatchPayload = CellBatchCodec.EncodeCells(2, maxBatchCells, "DigTool", 6, "Granite");
        check(maxBatchPayload.Length <= CellBatchCodec.MaximumPayloadSize &&
            CellBatchCodec.TryDecodeCells(maxBatchPayload, out cWorld, out cCells, out cTool, out cPriority, out cMat) &&
            cWorld == 2 && cPriority == 6 && cTool == "DigTool" && cMat == "Granite" && cCells.SequenceEqual(maxBatchCells),
            "cell batch codec compresses 4096 contiguous cells within the 4096-byte payload limit using ZigZag VarInt deltas");

        byte[] trailingBox = boxPayload.Concat(new byte[] { 0xFF }).ToArray();
        byte[] trailingCells = cellsPayload.Concat(new byte[] { 0xFF }).ToArray();
        check(!CellBatchCodec.TryDecodeBox(trailingBox, out bWorld, out bStartX, out bStartY, out bEndX, out bEndY, out bTool, out bPriority, out bMat) &&
            !CellBatchCodec.TryDecodeCells(trailingCells, out cWorld, out cCells, out cTool, out cPriority, out cMat),
            "cell batch codec rejects payloads with trailing garbage bytes");

        bool negativeCellRejected = false;
        try { CellBatchCodec.EncodeCells(0, new[] { 10, -1 }, "DigTool"); } catch (ArgumentOutOfRangeException) { negativeCellRejected = true; }
        bool outOfRangeCoordRejected = false;
        try { CellBatchCodec.EncodeBox(0, 40000, 0, 10, 10, "DigTool"); } catch (ArgumentOutOfRangeException) { outOfRangeCoordRejected = true; }
        bool oversizedMaterialRejected = false;
        try { CellBatchCodec.EncodeBox(0, 0, 0, 10, 10, "DigTool", 5, new string('M', CellBatchCodec.MaximumMaterialTagLength + 1)); } catch (ArgumentException) { oversizedMaterialRejected = true; }
        check(negativeCellRejected && outOfRangeCoordRejected && oversizedMaterialRejected,
            "cell batch codec rejects negative cell indices, out-of-range Int16 box coordinates, and oversized material tags");

        int[] grid64x64 = Enumerable.Range(0, 64).SelectMany(y => Enumerable.Range(0, 64).Select(x => y * 256 + x)).ToArray();
        byte[] gridPayload = CellBatchCodec.EncodeCells(0, grid64x64, "mop", 6, "Water");
        int[] decodedGrid;
        check(gridPayload.Length <= CellBatchCodec.MaximumPayloadSize &&
            CellBatchCodec.TryDecodeCells(gridPayload, out cWorld, out decodedGrid, out cTool, out cPriority, out cMat) &&
            decodedGrid.SequenceEqual(grid64x64),
            "64x64 2D grid of 4096 cells compresses via RLE + ZigZag VarInt deltas and round-trips");

        byte[] spoofedStringLength = new byte[] { (byte)CellBatchType.Box, 0, 0, 0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0x80, 0x80, 0x80, 0x04, (byte)'a' };
        check(!CellBatchCodec.TryDecodeBox(spoofedStringLength, out bWorld, out bStartX, out bStartY, out bEndX, out bEndY, out bTool, out bPriority, out bMat),
            "codec rejects spoofed multi-megabyte string length prefix before allocating");

        // --- ResearchProgression Tests ---
        var research = new ResearchProgression();
        check(research.TrySelectTech("BasicFarming", 1, "host_player") && research.ActiveTechId == "BasicFarming" && research.CurrentRevision == 1,
            "research progression selects initial tech with revision");
        check(!research.TrySelectTech("AdvancedResearch", 1, "peer_2"),
            "research selection rejects equal or older revisions");
        check(research.TrySelectTech("AdvancedResearch", 2, "peer_2") && research.ActiveTechId == "AdvancedResearch" && research.CurrentRevision == 2,
            "higher revision overrides active research selection");

        check(research.TryEnqueueTech("PowerRegulation") && research.Queue.Length == 1 && research.Queue[0] == "PowerRegulation",
            "enqueue adds tech to progression queue");
        check(!research.TryEnqueueTech("PowerRegulation") && !research.TryEnqueueTech("AdvancedResearch"),
            "duplicate or currently active tech cannot be queued");

        float totalPoints;
        check(research.TryAddPoints("AdvancedResearch", "basic", 10.0f, out totalPoints) && totalPoints == 10.0f,
            "monotonic research point addition records points correctly");
        byte[] snapshotAt10 = research.ExportState();
        check(snapshotAt10[0] == ResearchProgression.SnapshotFormatVersion,
            "research snapshot starts with format version byte");
        check(research.TryAddPoints("AdvancedResearch", "basic", 5.0f, out totalPoints) && totalPoints == 15.0f,
            "additional research points accumulate monotonically");
        check(!research.TryImportState(snapshotAt10) && research.GetPoints("AdvancedResearch", "basic") == 15.0f,
            "same-revision stale snapshot with fewer points is rejected and cannot roll back progress");
        check(research.TryAddPoints("AdvancedResearch", "basic", 5.5f, out totalPoints) && totalPoints == 20.5f,
            "research points continue accumulating monotonically after stale import rejection");
        check(!research.TryAddPoints("AdvancedResearch", "basic", -1.0f, out totalPoints) &&
            !research.TryAddPoints("AdvancedResearch", "basic", float.NaN, out totalPoints),
            "invalid or negative research points are strictly rejected");

        var overflowCheck = new ResearchProgression();
        float maxTotal;
        check(overflowCheck.TryAddPoints("MaxTech", "basic", float.MaxValue, out maxTotal) && maxTotal == float.MaxValue &&
            !overflowCheck.TryAddPoints("MaxTech", "basic", float.MaxValue, out maxTotal) &&
            !float.IsInfinity(overflowCheck.GetPoints("MaxTech", "basic")) &&
            overflowCheck.GetPoints("MaxTech", "basic") == float.MaxValue,
            "research point addition rejects float overflow to infinity and preserves finite total");

        byte[] exportedState = research.ExportState();
        check(exportedState != null && exportedState.Length > 0 && exportedState.SequenceEqual(research.ExportState()),
            "research progression exports deterministic compact binary snapshot");

        byte[] badVersionState = (byte[])exportedState.Clone();
        badVersionState[0] = 99;
        check(!research.TryImportState(badVersionState), "snapshot with unsupported format version is rejected");

        var replicaResearch = new ResearchProgression();
        check(replicaResearch.TryImportState(exportedState) &&
            replicaResearch.ActiveTechId == "AdvancedResearch" &&
            replicaResearch.CurrentRevision == 2 &&
            replicaResearch.Queue.SequenceEqual(new[] { "PowerRegulation" }) &&
            replicaResearch.GetPoints("AdvancedResearch", "basic") == 20.5f,
            "replica research successfully imports and restores authoritative progression snapshot");

        byte[] trailingResearch = exportedState.Concat(new byte[] { 0x00 }).ToArray();
        check(!replicaResearch.TryImportState(trailingResearch) && replicaResearch.CurrentRevision == 2,
            "research snapshot import fails closed on trailing garbage bytes without mutating state");

        using (var badMs = new MemoryStream())
        using (var badWriter = new BinaryWriter(badMs, Encoding.UTF8))
        {
            badWriter.Write(ResearchProgression.SnapshotFormatVersion);
            badWriter.Write(10L);
            CellBatchCodec.WriteBoundedString(badMs, "Invalid;Tech");
            CellBatchCodec.WriteBoundedString(badMs, "host_player");
            badWriter.Write((ushort)0);
            badWriter.Write((ushort)0);
            badWriter.Write((ushort)0);
            check(!replicaResearch.TryImportState(badMs.ToArray()) && replicaResearch.ActiveTechId == "AdvancedResearch",
                "snapshot with invalid active tech token is rejected without mutating existing state");
        }

        check(research.TryUnlock("AdvancedResearch") && research.IsUnlocked("AdvancedResearch") &&
            research.ActiveTechId == string.Empty && !research.TryEnqueueTech("AdvancedResearch") &&
            !research.TrySelectTech("AdvancedResearch", 3, "peer_2"),
            "unlocking a tech marks it completed, clears from active/queue, and prevents re-selecting");

        // --- SessionCompatibility Helpers ---
        var capsWithNew = new[] { SessionCapabilities.CellBatchV1, SessionCapabilities.ResearchSyncV1 };
        check(SessionCapabilities.UsesCellBatch(capsWithNew) && SessionCapabilities.UsesResearchSync(capsWithNew),
            "session compatibility detects negotiated cell batch and research sync capabilities");
    }

    private static StrictEndpoint Endpoint(string gameplay, string build, string[] dlc, StrictMod[] mods)
    { return new StrictEndpoint(gameplay, build, dlc, mods); }

    private static StrictMod Mod(string id, string version, string title)
    { return new StrictMod(id, version, title); }
}
