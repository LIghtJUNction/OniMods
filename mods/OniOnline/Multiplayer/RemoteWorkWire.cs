using System;
using System.IO;

namespace OniOnline.Multiplayer
{
    internal static class RemoteWorkWire
    {
        private const uint Magic = 0x52494e4f;
        internal const int MaxCells = 64;
        internal const int MaxBytes = 4096;
        private static readonly CommandWireText Text = new CommandWireText("work_text");

        internal static bool IsMessage(byte[] data)
        { return data != null && data.Length >= 6 && BitConverter.ToUInt32(data, 0) == Magic; }

        internal static byte[] Encode(RemoteWorkMessage value)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, CommandWireText.Encoding))
            {
                byte version = (byte)(value.Request != null && value.Request.IsCancellation ? 6 :
                    (value.Request != null && value.Request.Build != null || value.Plans != null && Array.Exists(value.Plans, p => p.Build != null)) ? 5 : 4);
                w.Write(Magic); w.Write(version); w.Write((byte)value.Kind); Text.Write(w, value.TargetId);
                if (value.Kind == RemoteWorkMessageKind.Grant)
                {
                    var g = value.Grant;
                    Text.Write(w, g.PlanetId); Text.Write(w, g.AuthorityEpoch); Text.Write(w, g.ActorId); Text.Write(w, g.Token);
                    w.Write(g.Generation); w.Write(g.Revision); w.Write((byte)g.Permissions);
                }
                else if (value.Kind == RemoteWorkMessageKind.Request)
                {
                    var r = value.Request;
                    Text.Write(w, r.RequestId); Text.Write(w, r.PlanetId); Text.Write(w, r.AuthorityEpoch); Text.Write(w, r.GrantToken);
                    w.Write((byte)r.Kind); w.Write((byte)r.Layer); w.Write(r.DryRun); Text.Write(w, r.PreviewToken ?? ""); w.Write(r.Priority);
                    if (r.Cells == null || r.Cells.Length < 1 || r.Cells.Length > MaxCells) throw new InvalidDataException("work_cell_count");
                    w.Write(r.Cells.Length);
                    foreach (var cell in r.Cells) { w.Write(cell.X); w.Write(cell.Y); }
                    if (version >= 5) WriteBuild(w, r.Build);
                    if (version >= 6) { Text.Write(w, r.CancelTargetRequestId); w.Write(r.CancelTargetRevision); }
                }
                else if (value.Kind == RemoteWorkMessageKind.Receipt)
                {
                    var r = value.Receipt;
                    Text.Write(w, r.RequestId); w.Write(r.Accepted); w.Write(r.DryRun); Text.Write(w, r.Reason);
                    w.Write(r.Count); Text.Write(w, r.PreviewToken ?? "");
                }
                else if (value.Kind == RemoteWorkMessageKind.QueryPlans || value.Kind == RemoteWorkMessageKind.Plans)
                {
                    Text.Write(w, value.PlanetId); w.Write(value.Merged); Text.Write(w, value.PlanActorId ?? "");
                    if (value.Kind == RemoteWorkMessageKind.Plans)
                    {
                        Text.Write(w, value.AuthorityEpoch); w.Write(value.AuthorityBlocked); Text.Write(w, value.AuthorityError ?? "");
                        w.Write(value.Generation); w.Write(value.GrantRevision); w.Write(value.Revision); w.Write(value.Offset); w.Write(value.Total);
                        if (value.Plans == null || value.Plans.Length > 8) throw new InvalidDataException("work_plan_count");
                        w.Write(value.Plans.Length);
                        foreach (var p in value.Plans)
                        {
                            Text.Write(w, p.ActorId); Text.Write(w, p.RequestId); w.Write(p.Cell.X); w.Write(p.Cell.Y);
                            w.Write((byte)p.Layer); w.Write((byte)p.Kind); w.Write(p.Priority); w.Write(p.ConfirmedRevision); w.Write(p.Historical); w.Write(p.HistoricalActive);
                            if (version >= 5) WriteBuild(w, p.Build);
                        }
                    }
                }
                else throw new InvalidDataException("work_kind");
                if (stream.Length > MaxBytes) throw new InvalidDataException("work_packet_size");
                return stream.ToArray();
            }
        }

        internal static RemoteWorkMessage Decode(byte[] data)
        {
            if (!IsMessage(data) || data.Length > MaxBytes) throw new InvalidDataException("work_packet_size");
            using (var stream = new MemoryStream(data, false))
            using (var r = new BinaryReader(stream, CommandWireText.Encoding))
            {
                r.ReadUInt32(); byte version = r.ReadByte(); if (version != 4 && version != 5 && version != 6) throw new InvalidDataException("work_version");
                var m = new RemoteWorkMessage { Kind = (RemoteWorkMessageKind)r.ReadByte(), TargetId = Text.Read(r) };
                if (version == 6 && m.Kind != RemoteWorkMessageKind.Request) throw new InvalidDataException("work_version");
                if (m.Kind == RemoteWorkMessageKind.Grant)
                {
                    m.Grant = new RemoteWorkGrant { PlanetId = Text.Read(r), AuthorityEpoch = Text.Read(r), ActorId = Text.Read(r),
                        Token = Text.Read(r), Generation = r.ReadInt64(), Revision = r.ReadInt64(), Permissions = (RemoteWorkPermission)r.ReadByte() };
                    if (m.Grant.Generation < 1 || m.Grant.Revision < 0 ||
                        (m.Grant.Revision == 0 && (m.Grant.Permissions != RemoteWorkPermission.None || m.Grant.Token != "")) || ((byte)m.Grant.Permissions & ~15) != 0) throw new InvalidDataException("work_grant");
                }
                else if (m.Kind == RemoteWorkMessageKind.Request)
                {
                    m.Request = new RemoteWorkRequest { RequestId = Text.Read(r), PlanetId = Text.Read(r), AuthorityEpoch = Text.Read(r),
                        GrantToken = Text.Read(r), Kind = (RemoteWorkKind)r.ReadByte(), Layer = (RemoteTaskLayer)r.ReadByte(), DryRun = r.ReadBoolean(),
                        PreviewToken = Text.Read(r), Priority = r.ReadInt32() };
                    int count = r.ReadInt32();
                    if (count < 1 || count > MaxCells || stream.Length - stream.Position < count * 8L) throw new InvalidDataException("work_cell_count");
                    m.Request.Cells = new WorkCell[count];
                    for (int i = 0; i < count; i++) m.Request.Cells[i] = new WorkCell(r.ReadInt32(), r.ReadInt32());
                    if (version >= 5) m.Request.Build = ReadBuild(r);
                    if (version >= 6) { m.Request.CancelTargetRequestId = Text.Read(r); m.Request.CancelTargetRevision = r.ReadInt64(); }
                }
                else if (m.Kind == RemoteWorkMessageKind.Receipt)
                    m.Receipt = new RemoteWorkReceipt { RequestId = Text.Read(r), Accepted = r.ReadBoolean(), DryRun = r.ReadBoolean(),
                        Reason = Text.Read(r), Count = r.ReadInt32(), PreviewToken = Text.Read(r) };
                else if (m.Kind == RemoteWorkMessageKind.QueryPlans || m.Kind == RemoteWorkMessageKind.Plans)
                {
                    m.PlanetId = Text.Read(r); m.Merged = r.ReadBoolean(); m.PlanActorId = Text.Read(r);
                    if (m.Kind == RemoteWorkMessageKind.Plans)
                    {
                        m.AuthorityEpoch = Text.Read(r); m.AuthorityBlocked = r.ReadBoolean(); m.AuthorityError = Text.Read(r);
                        m.Generation = r.ReadInt64(); m.GrantRevision = r.ReadInt64(); m.Revision = r.ReadInt64(); m.Offset = r.ReadInt32(); m.Total = r.ReadInt32();
                        int count = r.ReadInt32();
                        if (m.Generation < 1 || m.GrantRevision < 0 || count < 0 || count > 8 || m.Offset < 0 || m.Total < 0 || m.Total > RemoteWorkAuthority.MaxPlans || m.Offset + count > m.Total)
                            throw new InvalidDataException("work_plan_count");
                        m.Plans = new WorkTaskIntent[count];
                        for (int i = 0; i < count; i++)
                        {
                            m.Plans[i] = new WorkTaskIntent { PlanetId = m.PlanetId, ActorId = Text.Read(r), RequestId = Text.Read(r), Cell = new WorkCell(r.ReadInt32(), r.ReadInt32()),
                                Layer = (RemoteTaskLayer)r.ReadByte(), Kind = (RemoteWorkKind)r.ReadByte(), Priority = r.ReadInt32(), ConfirmedRevision = r.ReadInt64(), Historical = r.ReadBoolean(), HistoricalActive = r.ReadBoolean() };
                            if (version >= 5) m.Plans[i].Build = ReadBuild(r);
                            if (!WorkPlanGeometry.ValidShape(m.Plans[i].Kind, m.Plans[i].Layer, m.Plans[i].Build)) throw new InvalidDataException("work_plan_shape");
                        }
                    }
                }
                else throw new InvalidDataException("work_kind");
                if (stream.Position != stream.Length) throw new InvalidDataException("work_trailing_data");
                return m;
            }
        }

        internal static void WriteBuild(BinaryWriter w, WorkBuildSpec build)
        {
            w.Write(build != null); if (build == null) return;
            if (!WorkPlanGeometry.ValidBuild(build)) throw new InvalidDataException("work_build_spec");
            Text.Write(w, build.PrefabId); w.Write(build.Orientation); Text.Write(w, build.FacadeId);
            w.Write(build.MaterialTags.Length); foreach (string material in build.MaterialTags) Text.Write(w, material);
        }
        internal static WorkBuildSpec ReadBuild(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            var build = new WorkBuildSpec { PrefabId = Text.Read(r), Orientation = r.ReadInt32(), FacadeId = Text.Read(r) };
            int count = r.ReadInt32(); if (count != 1) throw new InvalidDataException("work_build_material_count");
            build.MaterialTags = new[] { Text.Read(r) };
            if (!WorkPlanGeometry.ValidBuild(build)) throw new InvalidDataException("work_build_spec");
            return build;
        }
    }
}
