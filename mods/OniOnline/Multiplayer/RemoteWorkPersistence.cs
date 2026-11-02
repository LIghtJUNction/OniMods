using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal interface IRemoteWorkPersistence
    {
        void Attach(RemoteWorkAuthority authority);
        RemoteWorkSavedPlanet Restore(WorkPlanet planet, string ownerPeerId);
        void Publish(WorkPlanet planet, string ownerPeerId);
    }
    internal sealed class RemoteWorkSavedDocument
    {
        internal string PersistentOwnerId;
        internal List<RemoteWorkSavedPlanet> Planets = new List<RemoteWorkSavedPlanet>();
    }
    internal sealed class RemoteWorkSavedPlanet
    {
        internal string PlanetId;
        internal int NativeWorldId;
        internal string OwnerPeerId;
        internal bool Blocked;
        internal WorkTaskIntent[] Plans = Array.Empty<WorkTaskIntent>();
        internal RemoteWorkSavedOrder[] Orders = Array.Empty<RemoteWorkSavedOrder>();
    }
    internal sealed class RemoteWorkSavedOrder
    {
        internal string Token;
        internal string OriginEpoch;
        internal WorkTaskIntent Winner;
        internal int NativeObjectId;
    }

    // No network grants or replay receipts cross the native-save boundary.
    internal static class RemoteWorkPersistence
    {
        internal const int MaxBytes = 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static string Encode(RemoteWorkSavedDocument document)
        {
            Validate(document);
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Utf8))
            {
                byte version = (byte)(document.Planets.Any(p => p.Plans.Any(plan => plan.Build != null)) ? 2 : 1);
                w.Write(0x4a574e4f); w.Write(version); Text(w, document.PersistentOwnerId); w.Write(document.Planets.Count);
                foreach (var planet in document.Planets)
                {
                    Text(w, planet.PlanetId); w.Write(planet.NativeWorldId); Text(w, planet.OwnerPeerId); w.Write(planet.Blocked);
                    w.Write(planet.Plans.Length); foreach (var plan in planet.Plans) Plan(w, plan, version);
                    w.Write(planet.Orders.Length);
                    foreach (var order in planet.Orders) { Text(w, order.Token); Text(w, order.OriginEpoch); Plan(w, order.Winner, version); if (version >= 2) w.Write(order.NativeObjectId); }
                }
                if (stream.Length > MaxBytes) throw new InvalidDataException("work_journal_size");
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        internal static RemoteWorkSavedDocument Decode(string payload)
        {
            if (payload == null || payload.Length > (MaxBytes + 2L) / 3 * 4) throw new InvalidDataException("work_journal_size");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(payload); } catch (FormatException) { throw new InvalidDataException("work_journal_encoding"); }
            if (bytes.Length > MaxBytes) throw new InvalidDataException("work_journal_size");
            using (var stream = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(stream, Utf8))
            {
                if (r.ReadInt32() != 0x4a574e4f) throw new InvalidDataException("work_journal_version");
                byte version = r.ReadByte(); if (version != 1 && version != 2) throw new InvalidDataException("work_journal_version");
                var doc = new RemoteWorkSavedDocument { PersistentOwnerId = Text(r) };
                int planets = Count(r, 32);
                for (int p = 0; p < planets; p++)
                {
                    var planet = new RemoteWorkSavedPlanet { PlanetId = Text(r), NativeWorldId = r.ReadInt32(), OwnerPeerId = Text(r), Blocked = r.ReadBoolean() };
                    planet.Plans = new WorkTaskIntent[Count(r, RemoteWorkAuthority.MaxPlans)];
                    for (int i = 0; i < planet.Plans.Length; i++) planet.Plans[i] = Plan(r, planet.PlanetId, version);
                    planet.Orders = new RemoteWorkSavedOrder[Count(r, RemoteWorkAuthority.MaxPlans)];
                    for (int i = 0; i < planet.Orders.Length; i++) planet.Orders[i] = new RemoteWorkSavedOrder { Token = Text(r), OriginEpoch = Text(r), Winner = Plan(r, planet.PlanetId, version), NativeObjectId = version >= 2 ? r.ReadInt32() : 0 };
                    doc.Planets.Add(planet);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("work_journal_trailing");
                Validate(doc); return doc;
            }
        }
        internal static void Validate(RemoteWorkSavedDocument doc)
        {
            Guid id;
            if (doc == null || !Guid.TryParseExact(doc.PersistentOwnerId, "N", out id) || doc.Planets == null || doc.Planets.Count > 32 ||
                doc.Planets.Select(p => p.PlanetId).Distinct().Count() != doc.Planets.Count) throw new InvalidDataException("work_journal_owner");
            int total = 0;
            var tokens = new HashSet<string>();
            foreach (var planet in doc.Planets)
            {
                if (!Guid.TryParseExact(planet.PlanetId, "N", out id) || planet.NativeWorldId < 0 || string.IsNullOrEmpty(planet.OwnerPeerId) ||
                    planet.OwnerPeerId.Length > 128 || planet.Plans == null || planet.Orders == null || planet.Plans.Length > RemoteWorkAuthority.MaxPlans || planet.Orders.Length > RemoteWorkAuthority.MaxPlans)
                    throw new InvalidDataException("work_journal_planet");
                total += planet.Plans.Length + planet.Orders.Length;
                if (total > 4096 || planet.Plans.Select(p => p.ActorId + ":" + p.Key).Distinct().Count() != planet.Plans.Length ||
                    planet.Orders.Select(o => o.Winner.Key).Distinct().Count() != planet.Orders.Length) throw new InvalidDataException("work_journal_count");
                foreach (var plan in planet.Plans) ValidatePlan(plan, planet.PlanetId);
                foreach (var order in planet.Orders)
                {
                    ValidatePlan(order.Winner, planet.PlanetId);
                    if (!Guid.TryParseExact(order.Token, "N", out id) || !Guid.TryParseExact(order.OriginEpoch, "N", out id) || !tokens.Add(order.Token) ||
                        !planet.Plans.Any(p => SameIntent(p, order.Winner)) || (order.Winner.Kind == RemoteWorkKind.Build && order.NativeObjectId < 1)) throw new InvalidDataException("work_journal_order");
                }
            }
        }
        internal static bool SameIntent(WorkTaskIntent a, WorkTaskIntent b)
        { return a.PlanetId == b.PlanetId && a.ActorId == b.ActorId && a.RequestId == b.RequestId && a.Key == b.Key && a.Priority == b.Priority && a.Kind == b.Kind && a.ConfirmedRevision == b.ConfirmedRevision && WorkPlanGeometry.SameBuild(a.Build, b.Build); }
        internal static void RemoveCell(RemoteWorkSavedPlanet planet, string key)
        {
            WorkTaskIntent removed = planet.Plans.FirstOrDefault(p => p.Key == key);
            if (removed != null) { RemovePlan(planet, removed); return; }
            planet.Orders = planet.Orders.Where(p => p.Winner.Key != key).ToArray();
        }
        internal static void RemovePlan(RemoteWorkSavedPlanet planet, WorkTaskIntent removed)
        {
            planet.Plans = planet.Plans.Where(p => !WorkPlanGeometry.Overlaps(p, removed)).ToArray();
            planet.Orders = planet.Orders.Where(p => !WorkPlanGeometry.Overlaps(p.Winner, removed)).ToArray();
        }
        // A missing marker is not proof that a saved blueprint still exists. In particular,
        // a closed execution gate must leave physical blueprints outside remote authority
        // without allowing them to block otherwise usable Dig reconciliation.
        internal static RemoteWorkSavedPlanet FilterBuildRecovery(RemoteWorkSavedPlanet saved, bool enabled, RemoteWorkSavedOrder[] verified)
        {
            if (saved == null) return null;
            return new RemoteWorkSavedPlanet { PlanetId = saved.PlanetId, NativeWorldId = saved.NativeWorldId, OwnerPeerId = saved.OwnerPeerId, Blocked = saved.Blocked,
                Plans = saved.Plans.Where(p => enabled || p.Kind != RemoteWorkKind.Build).Select(p => p.Copy()).ToArray(),
                Orders = saved.Orders.Where(o => o.Winner.Kind != RemoteWorkKind.Build || enabled && verified != null && verified.Any(v =>
                    v.Token == o.Token && v.OriginEpoch == o.OriginEpoch && v.NativeObjectId == o.NativeObjectId && SameIntent(v.Winner, o.Winner)))
                    .Select(o => new RemoteWorkSavedOrder { Token = o.Token, OriginEpoch = o.OriginEpoch, NativeObjectId = o.NativeObjectId, Winner = o.Winner.Copy() }).ToArray() };
        }
        internal static WorkTaskIntent[] RestorePlans(RemoteWorkSavedPlanet saved, string newOwnerPeerId)
        {
            return saved.Plans.Select(p =>
            {
                var copy = p.Copy();
                copy.Historical = true;
                copy.HistoricalActive = !saved.Blocked && saved.Orders.Any(o => SameIntent(o.Winner, p));
                // Only exact pending winners survive. Covered history cannot silently become work.
                if (copy.ActorId == saved.OwnerPeerId) copy.ActorId = newOwnerPeerId;
                return copy;
            }).ToArray();
        }
        private static void ValidatePlan(WorkTaskIntent p, string planet)
        {
            Guid id;
            if (p == null || p.PlanetId != planet || string.IsNullOrEmpty(p.ActorId) || p.ActorId.Length > 128 || !Guid.TryParseExact(p.RequestId, "N", out id) ||
                p.Cell.X < 0 || p.Cell.Y < 0 || p.Cell.X > 8192 || p.Cell.Y > 8192 || p.Priority < 1 || p.Priority > 9 || p.ConfirmedRevision < 1 ||
                !WorkPlanGeometry.ValidShape(p.Kind, p.Layer, p.Build) || WorkPlanGeometry.Footprint(p).Any(c => c.X > 8192 || c.Y > 8192)) throw new InvalidDataException("work_journal_plan");
        }
        private static void Plan(BinaryWriter w, WorkTaskIntent p, byte version)
        {
            Text(w, p.ActorId); Text(w, p.RequestId); w.Write(p.Cell.X); w.Write(p.Cell.Y); w.Write((byte)p.Layer); w.Write((byte)p.Kind);
            w.Write(p.Priority); w.Write(p.ConfirmedRevision); w.Write(p.Historical); w.Write(p.HistoricalActive);
            if (version >= 2) RemoteWorkWire.WriteBuild(w, p.Build);
        }
        private static WorkTaskIntent Plan(BinaryReader r, string planet, byte version)
        {
            var plan = new WorkTaskIntent { PlanetId = planet, ActorId = Text(r), RequestId = Text(r), Cell = new WorkCell(r.ReadInt32(), r.ReadInt32()),
                Layer = (RemoteTaskLayer)r.ReadByte(), Kind = (RemoteWorkKind)r.ReadByte(), Priority = r.ReadInt32(), ConfirmedRevision = r.ReadInt64(), Historical = r.ReadBoolean(), HistoricalActive = r.ReadBoolean() };
            if (version >= 2) plan.Build = RemoteWorkWire.ReadBuild(r);
            return plan;
        }
        private static int Count(BinaryReader r, int max) { int n = r.ReadInt32(); if (n < 0 || n > max) throw new InvalidDataException("work_journal_count"); return n; }
        private static void Text(BinaryWriter w, string text) { byte[] bytes = Utf8.GetBytes(text); if (bytes.Length > 384) throw new InvalidDataException("work_journal_text"); w.Write((ushort)bytes.Length); w.Write(bytes); }
        private static string Text(BinaryReader r) { int n = r.ReadUInt16(); if (n > 384 || r.BaseStream.Length - r.BaseStream.Position < n) throw new InvalidDataException("work_journal_text"); return Utf8.GetString(r.ReadBytes(n)); }
    }
}
