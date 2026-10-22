using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace OniOnline.Core
{
    internal enum DupeOriginKind : byte { LegacyLocal = 1, NativeLocal = 2 }
    internal enum DupeAdmissionKind : byte { LegacyLocal = 1, NativeLocal = 2, Imported = 3 }
    internal sealed class DupeOriginRecord
    {
        internal string StableDupeId;
        internal string OriginalOwnerId;
        internal string OriginPlanetId;
        internal string OriginHomeSystemId;
        internal DupeOriginKind Kind;
        internal DupeOriginRecord Copy() { return (DupeOriginRecord)MemberwiseClone(); }
        internal bool SameAs(DupeOriginRecord other)
        { return other != null && StableDupeId == other.StableDupeId && OriginalOwnerId == other.OriginalOwnerId &&
            (OriginPlanetId ?? "") == (other.OriginPlanetId ?? "") && OriginHomeSystemId == other.OriginHomeSystemId && Kind == other.Kind; }
    }
    internal sealed class NativeDupeOriginBinding
    {
        internal string StableDupeId;
        internal int NativeInstanceId;
        internal int NativeWorldId;
        internal string CurrentPlanetId;
        internal DupeAdmissionKind AdmissionKind;
        internal string AdmissionTransferId;
        internal NativeDupeOriginBinding Copy() { return (NativeDupeOriginBinding)MemberwiseClone(); }
        internal bool SameAs(NativeDupeOriginBinding other)
        { return other != null && StableDupeId == other.StableDupeId && NativeInstanceId == other.NativeInstanceId &&
            NativeWorldId == other.NativeWorldId && CurrentPlanetId == other.CurrentPlanetId &&
            AdmissionKind == other.AdmissionKind && (AdmissionTransferId ?? "") == (other.AdmissionTransferId ?? ""); }
    }
    internal sealed class DupeOriginDocument
    {
        internal string HomeSystemId;
        internal DupeOriginRecord[] Origins = Array.Empty<DupeOriginRecord>();
        internal NativeDupeOriginBinding[] Bindings = Array.Empty<NativeDupeOriginBinding>();
        internal DupeOriginDocument Copy()
        { return new DupeOriginDocument { HomeSystemId = HomeSystemId, Origins = Origins.Select(o => o.Copy()).ToArray(), Bindings = Bindings.Select(b => b.Copy()).ToArray() }; }
    }
    internal sealed class VerifiedDupeImport
    {
        internal DupeOriginRecord Origin = null;
        internal NativeDupeOriginBinding Binding = null;
        internal NativeDupeOriginBinding ExpectedPreviousBinding = null;
    }

    // Save-level identity, never a Minion component. No native object creation or transport occurs here.
    internal sealed class DupeOriginLedger
    {
        private DupeOriginDocument document;
        private readonly int originalSchema;
        private readonly string originalPayload;
        internal bool Blocked { get; private set; }
        internal bool LegacyPending { get; private set; }
        internal string Error { get; private set; }
        internal int SchemaToSave { get { return Blocked ? originalSchema : LegacyPending ? 0 : 1; } }
        private DupeOriginLedger(int schema, string payload)
        { originalSchema = schema; originalPayload = payload; }
        internal static DupeOriginLedger Load(int schema, string payload, string homeSystemId)
        {
            var ledger = new DupeOriginLedger(schema, payload);
            try
            {
                if (!DupeOriginCodec.ValidId(homeSystemId)) throw new InvalidDataException("dupe_origin_home");
                if (schema == 0 && payload == null)
                { ledger.LegacyPending = true; ledger.document = new DupeOriginDocument { HomeSystemId = homeSystemId }; return ledger; }
                if (schema != 1 || string.IsNullOrEmpty(payload)) throw new InvalidDataException("dupe_origin_version_or_missing_payload");
                ledger.document = DupeOriginCodec.Decode(payload);
                if (ledger.document.HomeSystemId != homeSystemId) throw new InvalidDataException("dupe_origin_scope");
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException)
            { ledger.Blocked = true; ledger.Error = error.Message; }
            return ledger;
        }
        internal string CapturePayload()
        { return Blocked || LegacyPending ? originalPayload : DupeOriginCodec.Encode(document); }
        internal DupeOriginDocument CaptureDocument()
        { return Blocked || document == null ? null : document.Copy(); }
        internal bool TryBootstrapLegacyLocal(string ownerId, NativeDupeOriginBinding[] nativeCandidates, out string error)
        {
            return TryBootstrapLocal(ownerId, nativeCandidates, DupeAdmissionKind.LegacyLocal, DupeOriginKind.LegacyLocal, out error);
        }
        internal bool TryBootstrapVerifiedNewGame(string ownerId, NativeDupeOriginBinding[] nativeCandidates, out string error)
        {
            if (nativeCandidates == null || nativeCandidates.Length == 0)
            { error = "dupe_origin_newgame_empty_batch"; return false; }
            return TryBootstrapLocal(ownerId, nativeCandidates, DupeAdmissionKind.NativeLocal, DupeOriginKind.NativeLocal, out error);
        }
        private bool TryBootstrapLocal(string ownerId, NativeDupeOriginBinding[] nativeCandidates,
            DupeAdmissionKind admission, DupeOriginKind kind, out string error)
        {
            error = null;
            if (Blocked || !LegacyPending || !DupeOriginCodec.ValidId(ownerId) || nativeCandidates == null || nativeCandidates.Length > DupeOriginCodec.MaxRecords)
            { error = "dupe_origin_bootstrap_denied"; return false; }
            try
            {
                var next = new DupeOriginDocument { HomeSystemId = document.HomeSystemId };
                var origins = new List<DupeOriginRecord>(); var bindings = new List<NativeDupeOriginBinding>();
                foreach (var native in nativeCandidates)
                {
                    if (native == null || native.AdmissionKind != admission || !string.IsNullOrEmpty(native.AdmissionTransferId))
                        throw new InvalidDataException("dupe_origin_legacy_candidate");
                    string id = Guid.NewGuid().ToString("N");
                    var binding = native.Copy(); binding.StableDupeId = id; binding.AdmissionTransferId = "";
                    origins.Add(new DupeOriginRecord { StableDupeId = id, OriginalOwnerId = ownerId, Kind = kind,
                        OriginPlanetId = binding.CurrentPlanetId, OriginHomeSystemId = document.HomeSystemId }); bindings.Add(binding);
                }
                next.Origins = origins.ToArray(); next.Bindings = bindings.ToArray(); DupeOriginCodec.Validate(next);
                document = next; LegacyPending = false; return true;
            }
            catch (Exception failure) when (failure is InvalidDataException || failure is IOException || failure is ArgumentException)
            { error = failure.Message; return false; }
        }
        internal bool TryResolve(int nativeId, int worldId, string planetId, out DupeOriginRecord origin)
        {
            origin = null;
            if (Blocked || LegacyPending || document == null || worldId < 0 || worldId >= DupeOriginCodec.RetiredWorldId) return false;
            var binding = document.Bindings.FirstOrDefault(b => b.NativeInstanceId == nativeId && b.NativeWorldId == worldId && b.CurrentPlanetId == planetId);
            if (binding == null) return false;
            var known = document.Origins.FirstOrDefault(o => o.StableDupeId == binding.StableDupeId);
            if (known == null) return false;
            origin = known.Copy(); return true;
        }
        internal NativeDupeOriginBinding GetBinding(string stableDupeId)
        { return Blocked || LegacyPending ? null : document.Bindings.FirstOrDefault(b => b.StableDupeId == stableDupeId)?.Copy(); }
        // The native adapter must prove the completed recruitment and exact new object.
        // Commit its immutable origin and binding together; ordinary load never calls this.
        internal bool TryRegisterVerifiedNativeBirth(string ownerId, NativeDupeOriginBinding candidate,
            out DupeOriginRecord origin, out string error)
        {
            origin = null; error = null;
            if (Blocked || LegacyPending || !DupeOriginCodec.ValidId(ownerId) || candidate == null ||
                !string.IsNullOrEmpty(candidate.StableDupeId) || candidate.AdmissionKind != DupeAdmissionKind.NativeLocal ||
                !string.IsNullOrEmpty(candidate.AdmissionTransferId) || candidate.NativeInstanceId < 0 ||
                candidate.NativeWorldId < 0 || candidate.NativeWorldId >= DupeOriginCodec.RetiredWorldId || !DupeOriginCodec.ValidId(candidate.CurrentPlanetId) ||
                document.Bindings.Any(b => b.NativeInstanceId == candidate.NativeInstanceId))
            { error = "dupe_origin_birth_denied"; return false; }
            var binding = candidate.Copy(); binding.StableDupeId = Guid.NewGuid().ToString("N"); binding.AdmissionTransferId = "";
            var record = new DupeOriginRecord { StableDupeId = binding.StableDupeId, OriginalOwnerId = ownerId,
                OriginPlanetId = binding.CurrentPlanetId, OriginHomeSystemId = document.HomeSystemId, Kind = DupeOriginKind.NativeLocal };
            var next = document.Copy();
            next.Origins = next.Origins.Concat(new[] { record }).ToArray();
            next.Bindings = next.Bindings.Concat(new[] { binding }).ToArray();
            if (!TryCommit(next, out error)) return false;
            origin = record.Copy(); return true;
        }
        internal bool TryRebindVerifiedNative(NativeDupeOriginBinding expectedOld, NativeDupeOriginBinding replacement, out string error)
        {
            error = null;
            if (Blocked || LegacyPending || expectedOld == null || expectedOld.NativeWorldId == DupeOriginCodec.RetiredWorldId ||
                replacement == null || expectedOld.StableDupeId != replacement.StableDupeId ||
                expectedOld.AdmissionKind != replacement.AdmissionKind || expectedOld.AdmissionTransferId != replacement.AdmissionTransferId)
            { error = "dupe_origin_rebind_denied"; return false; }
            var current = document.Bindings.FirstOrDefault(b => b.StableDupeId == expectedOld.StableDupeId);
            if (current == null || !current.SameAs(expectedOld)) { error = "dupe_origin_stale_binding"; return false; }
            var next = document.Copy(); next.Bindings = next.Bindings.Where(b => b.StableDupeId != expectedOld.StableDupeId).Concat(new[] { replacement.Copy() }).ToArray();
            return TryCommit(next, out error);
        }
        // The native importer must separately verify its archive and exact post-load object mapping.
        // This method commits only provenance/bindings, atomically; it does not load an archive.
        internal bool TryCommitVerifiedImports(VerifiedDupeImport[] imports, out string error)
        {
            error = null;
            if (Blocked || LegacyPending || imports == null || imports.Length == 0 || imports.Length > DupeOriginCodec.MaxRecords)
            { error = "dupe_origin_import_denied"; return false; }
            var next = document.Copy(); var origins = next.Origins.ToDictionary(o => o.StableDupeId, StringComparer.Ordinal);
            var bindings = next.Bindings.ToDictionary(b => b.StableDupeId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in imports)
            {
                if (entry?.Origin == null || entry.Binding == null || !DupeOriginCodec.ValidId(entry.Origin.StableDupeId) ||
                    !DupeOriginCodec.ValidId(entry.Binding.StableDupeId) || entry.Binding.AdmissionKind != DupeAdmissionKind.Imported ||
                    !DupeOriginCodec.ValidId(entry.Binding.AdmissionTransferId) || entry.Origin.StableDupeId != entry.Binding.StableDupeId ||
                    !seen.Add(entry.Origin.StableDupeId)) { error = "dupe_origin_import_record"; return false; }
                DupeOriginRecord known;
                if (origins.TryGetValue(entry.Origin.StableDupeId, out known) && !known.SameAs(entry.Origin))
                { error = "dupe_origin_immutable_conflict"; return false; }
                NativeDupeOriginBinding prior;
                bool hasPrior = bindings.TryGetValue(entry.Origin.StableDupeId, out prior);
                if (hasPrior && prior.NativeWorldId == DupeOriginCodec.RetiredWorldId)
                { error = "dupe_origin_retired_binding"; return false; }
                if (hasPrior != (entry.ExpectedPreviousBinding != null) || (hasPrior && !prior.SameAs(entry.ExpectedPreviousBinding)))
                { error = "dupe_origin_stale_binding"; return false; }
                origins[entry.Origin.StableDupeId] = entry.Origin.Copy(); bindings[entry.Origin.StableDupeId] = entry.Binding.Copy();
            }
            next.Origins = origins.Values.ToArray(); next.Bindings = bindings.Values.ToArray(); return TryCommit(next, out error);
        }
        internal bool TryRemoveVerifiedBinding(NativeDupeOriginBinding expectedOld, out string error)
        {
            error = null;
            if (Blocked || LegacyPending || expectedOld == null || expectedOld.NativeWorldId == DupeOriginCodec.RetiredWorldId)
            { error = "dupe_origin_remove_denied"; return false; }
            var current = document.Bindings.FirstOrDefault(b => b.StableDupeId == expectedOld.StableDupeId);
            if (current == null || !current.SameAs(expectedOld)) { error = "dupe_origin_stale_binding"; return false; }
            var next = document.Copy(); next.Bindings = next.Bindings.Where(b => b.StableDupeId != expectedOld.StableDupeId).ToArray();
            return TryCommit(next, out error);
        }
        private bool TryCommit(DupeOriginDocument next, out string error)
        {
            error = null;
            try { DupeOriginCodec.Validate(next); document = next; return true; }
            catch (Exception failure) when (failure is InvalidDataException || failure is IOException || failure is ArgumentException)
            { error = failure.Message; return false; }
        }
    }

    internal static class DupeOriginCodec
    {
        internal const int MaxRecords = 16384;
        internal const int RetiredWorldId = 255;
        internal const int MaxBytes = 4 * 1024 * 1024;
        private const int Magic = 0x4f444e4f;
        internal static bool ValidId(string value)
        { Guid id; return value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out id) && id != Guid.Empty && id.ToString("N") == value; }
        internal static void Validate(DupeOriginDocument document)
        {
            if (document == null || !ValidId(document.HomeSystemId) || document.Origins == null || document.Bindings == null ||
                document.Origins.Length > MaxRecords || document.Bindings.Length > MaxRecords) throw new InvalidDataException("dupe_origin_document");
            var origins = new HashSet<string>(StringComparer.Ordinal);
            foreach (var origin in document.Origins)
                if (origin == null || !ValidId(origin.StableDupeId) || !ValidId(origin.OriginalOwnerId) || !ValidId(origin.OriginHomeSystemId) ||
                    (!ValidId(origin.OriginPlanetId) && !(origin.Kind == DupeOriginKind.LegacyLocal && string.IsNullOrEmpty(origin.OriginPlanetId))) ||
                    (origin.Kind != DupeOriginKind.LegacyLocal && origin.Kind != DupeOriginKind.NativeLocal) || !origins.Add(origin.StableDupeId))
                    throw new InvalidDataException("dupe_origin_record");
            var entities = new HashSet<string>(StringComparer.Ordinal); var nativeIds = new HashSet<int>();
            foreach (var binding in document.Bindings)
                if (binding == null || !origins.Contains(binding.StableDupeId) || !entities.Add(binding.StableDupeId) ||
                    binding.NativeInstanceId < 0 || !nativeIds.Add(binding.NativeInstanceId) || binding.NativeWorldId < 0 || binding.NativeWorldId > 255 ||
                    !ValidId(binding.CurrentPlanetId) || binding.AdmissionKind < DupeAdmissionKind.LegacyLocal || binding.AdmissionKind > DupeAdmissionKind.Imported ||
                    (binding.AdmissionKind == DupeAdmissionKind.Imported ? !ValidId(binding.AdmissionTransferId) : !string.IsNullOrEmpty(binding.AdmissionTransferId)))
                    throw new InvalidDataException("dupe_origin_binding");
        }
        internal static string Encode(DupeOriginDocument document)
        {
            Validate(document);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)1); Id(writer, document.HomeSystemId);
                writer.Write(document.Origins.Length); writer.Write(document.Bindings.Length);
                foreach (var origin in document.Origins.OrderBy(o => o.StableDupeId, StringComparer.Ordinal))
                { Id(writer, origin.StableDupeId); Id(writer, origin.OriginalOwnerId); Id(writer, origin.OriginPlanetId); Id(writer, origin.OriginHomeSystemId); writer.Write((byte)origin.Kind); }
                foreach (var binding in document.Bindings.OrderBy(b => b.StableDupeId, StringComparer.Ordinal))
                { Id(writer, binding.StableDupeId); writer.Write(binding.NativeInstanceId); writer.Write(binding.NativeWorldId); Id(writer, binding.CurrentPlanetId); writer.Write((byte)binding.AdmissionKind); Id(writer, binding.AdmissionTransferId); }
                writer.Flush(); byte[] body = stream.ToArray();
                if (body.Length > MaxBytes - 32) throw new InvalidDataException("dupe_origin_budget");
                byte[] result = new byte[body.Length + 32]; Buffer.BlockCopy(body, 0, result, 0, body.Length);
                using (var sha = SHA256.Create()) Buffer.BlockCopy(sha.ComputeHash(body), 0, result, body.Length, 32);
                return Convert.ToBase64String(result);
            }
        }
        internal static DupeOriginDocument Decode(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > ((MaxBytes + 2) / 3) * 4) throw new InvalidDataException("dupe_origin_budget");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(payload); } catch (FormatException error) { throw new InvalidDataException("dupe_origin_encoding", error); }
            if (bytes.Length < 62 || bytes.Length > MaxBytes || Convert.ToBase64String(bytes) != payload) throw new InvalidDataException("dupe_origin_encoding");
            using (var sha = SHA256.Create())
                if (!sha.ComputeHash(bytes, 0, bytes.Length - 32).SequenceEqual(bytes.Skip(bytes.Length - 32))) throw new InvalidDataException("dupe_origin_checksum");
            using (var stream = new MemoryStream(bytes, 0, bytes.Length - 32, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic || reader.ReadByte() != 1) throw new InvalidDataException("dupe_origin_version");
                var result = new DupeOriginDocument { HomeSystemId = Id(reader) };
                int count = reader.ReadInt32(), bindings = reader.ReadInt32();
                if (count < 0 || count > MaxRecords || bindings < 0 || bindings > MaxRecords || count > (stream.Length - stream.Position) / 37)
                    throw new InvalidDataException("dupe_origin_count");
                result.Origins = new DupeOriginRecord[count]; result.Bindings = new NativeDupeOriginBinding[bindings];
                for (int i = 0; i < count; i++) result.Origins[i] = new DupeOriginRecord { StableDupeId = Id(reader), OriginalOwnerId = Id(reader),
                    OriginPlanetId = Id(reader), OriginHomeSystemId = Id(reader), Kind = (DupeOriginKind)reader.ReadByte() };
                for (int i = 0; i < bindings; i++) result.Bindings[i] = new NativeDupeOriginBinding { StableDupeId = Id(reader), NativeInstanceId = reader.ReadInt32(),
                    NativeWorldId = reader.ReadInt32(), CurrentPlanetId = Id(reader), AdmissionKind = (DupeAdmissionKind)reader.ReadByte(), AdmissionTransferId = Id(reader) };
                if (stream.Position != stream.Length) throw new InvalidDataException("dupe_origin_trailing"); Validate(result); return result;
            }
        }
        private static void Id(BinaryWriter writer, string id)
        { bool present = !string.IsNullOrEmpty(id); writer.Write(present); if (present) writer.Write(Guid.ParseExact(id, "N").ToByteArray()); }
        private static string Id(BinaryReader reader)
        {
            byte present = reader.ReadByte(); if (present > 1) throw new InvalidDataException("dupe_origin_id"); if (present == 0) return "";
            byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); return new Guid(bytes).ToString("N");
        }
    }

    internal sealed class RegisteredOwnerClaim
    {
        internal string PeerId;
        internal string OwnerId;
        internal bool Connected;
    }
    // Cooperative, admitted claims only. This is not Steam account authentication.
    internal sealed class RegisteredRoomOwners
    {
        private RegisteredOwnerClaim[] claims = Array.Empty<RegisteredOwnerClaim>();
        private readonly Dictionary<string, string> lastPeer = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> retired = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> firstOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        internal void Update(RegisteredOwnerClaim[] registeredClaims)
        {
            claims = (registeredClaims ?? Array.Empty<RegisteredOwnerClaim>()).Where(c => c != null && DupeOriginCodec.ValidId(c.OwnerId) &&
                DupeOriginCodec.ValidId(c.PeerId)).Select(c => new RegisteredOwnerClaim { PeerId = c.PeerId, OwnerId = c.OwnerId, Connected = c.Connected }).ToArray();
            foreach (var claim in claims)
            {
                string prior;
                if (firstOwner.TryGetValue(claim.PeerId, out prior) && prior != claim.OwnerId) retired.Add(claim.PeerId);
                else firstOwner[claim.PeerId] = claim.OwnerId;
            }
            foreach (var group in claims.Where(c => c.Connected && !retired.Contains(c.PeerId)).GroupBy(c => c.OwnerId, StringComparer.Ordinal))
            {
                var peers = group.Select(c => c.PeerId).Distinct(StringComparer.Ordinal).ToArray();
                if (peers.Length != 1 || claims.Where(c => c.PeerId == peers[0]).Select(c => c.OwnerId).Distinct(StringComparer.Ordinal).Count() != 1) continue;
                string previous;
                if (lastPeer.TryGetValue(group.Key, out previous) && previous != peers[0]) retired.Add(previous);
                lastPeer[group.Key] = peers[0];
            }
        }
        internal bool TryResolveActor(string peerId, out string ownerId)
        {
            ownerId = null;
            if (peerId == null || retired.Contains(peerId)) return false;
            var owners = claims.Where(c => c.Connected && c.PeerId == peerId).Select(c => c.OwnerId).Distinct(StringComparer.Ordinal).ToArray();
            if (owners.Length != 1) return false;
            var peers = claims.Where(c => c.Connected && c.OwnerId == owners[0] && !retired.Contains(c.PeerId)).Select(c => c.PeerId).Distinct(StringComparer.Ordinal).ToArray();
            string chosen;
            if (peers.Length != 1 || peers[0] != peerId || !lastPeer.TryGetValue(owners[0], out chosen) || chosen != peerId) return false;
            ownerId = owners[0]; return true;
        }
        internal string ProjectionPeer(string ownerId)
        { string peer; return ownerId != null && lastPeer.TryGetValue(ownerId, out peer) && !retired.Contains(peer) ? peer : null; }
    }

    internal static class DupeOriginRetentionPolicy
    {
        internal static bool MayRemoveBinding(bool everConfirmed, bool exactNativeObjectDestroyed, bool matchingLiveNativeId)
        { return everConfirmed && exactNativeObjectDestroyed && !matchingLiveNativeId; }
    }

    internal static class DupeOwnershipPolicy
    {
        internal static bool CanControl(DupeOriginRecord origin, string actorOwnerId, string directGrantIssuerOwnerId, bool directGrant,
            bool explicitEntityDelegation)
        {
            if (origin == null || !DupeOriginCodec.ValidId(origin.StableDupeId) || !DupeOriginCodec.ValidId(origin.OriginalOwnerId) ||
                !DupeOriginCodec.ValidId(actorOwnerId)) return false;
            return actorOwnerId == origin.OriginalOwnerId || (directGrant && directGrantIssuerOwnerId == origin.OriginalOwnerId) || explicitEntityDelegation;
        }
    }
}
