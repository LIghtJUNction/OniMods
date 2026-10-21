using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    [Serializable]
    internal sealed class WorldAdmissionWorldRectangle
    {
        public int Id, X, Y, Width, Height;
        public string Name;
        public bool IsModuleInterior;
    }

    [Serializable]
    internal sealed class WorldAdmissionPreflightReport
    {
        // CapacityAvailable permits further investigation only. It is not migration authorization.
        public bool CapacityAvailable, FitsNativeGrid, HexAvailable, CandidateClear, AssemblyMatched;
        public bool ImportReady = false;
        public bool Loaded, Paused, Stable;
        public int Build, GridWidth, GridHeight, HexQ, HexR, RequestedWidth, RequestedHeight;
        public int CandidateX = -1, CandidateY = -1, CandidateWidth, CandidateHeight, CandidateWorldId = -1;
        public int RemainingWorldIds, CheckedCells, CheckedBoundaryCells;
        public string AssemblySha256 = "", AssemblyModuleId = "";
        public double SimulationTime;
        public WorldAdmissionWorldRectangle[] Worlds = new WorldAdmissionWorldRectangle[0];
        public string[] BlockingReasons = new string[0];
        public string[] RequiredMetadata = {
            "original_world_configuration_and_dimensions", "world_traits_story_traits_seasons",
            "sunlight_radiation_boundary_hidden_height", "native_sim_cells_disease_backwall",
            "biomes_world_detail_polygons_with_rebased_coordinates", "objects_cell_world_instance_reference_mapping",
            "native_inventory_navigation_and_cluster_registration", "save_reload_and_failure_rollback_contract"
        };
    }

    // Construct and call on the Unity main thread while a fully loaded colony is paused.
    // This class only reads existing runtime state. BestFit calculates coordinates but does not allocate.
    internal sealed class NativeWorldAdmissionPreflight
    {
        private const string ExpectedAssemblySha256 = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private const int ExpectedBuild = 744825, MaxPlanetCells = 262144;
        private readonly int owningThread = Thread.CurrentThread.ManagedThreadId;

        internal WorldAdmissionPreflightReport Inspect(int width, int height, int hexQ, int hexR)
        {
            var report = new WorldAdmissionPreflightReport { RequestedWidth = width, RequestedHeight = height,
                HexQ = hexQ, HexR = hexR, CandidateWidth = width, CandidateHeight = height };
            var failures = new List<string>();
            if (Thread.CurrentThread.ManagedThreadId != owningThread)
            { failures.Add("world_capacity_requires_unity_thread"); Finish(report, failures); return report; }
            try { Read(report, failures); }
            catch (Exception error)
            {
                // Fail closed without leaking exception paths or attempting repairs.
                failures.Add("world_capacity_native_read_failed:" + error.GetType().Name);
            }
            Finish(report, failures); return report;
        }

        private static void Finish(WorldAdmissionPreflightReport report, List<string> failures)
        {
            report.BlockingReasons = failures.ToArray();
            report.CapacityAvailable = failures.Count == 0 && report.AssemblyMatched && report.Loaded && report.Paused && report.Stable &&
                report.FitsNativeGrid && report.HexAvailable && report.CandidateClear && report.CandidateWorldId >= 0;
        }

        private static void Read(WorldAdmissionPreflightReport report, List<string> failures)
        {
            FieldInfo build = typeof(KleiVersion).GetField("ChangeList", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            report.Build = build == null || !build.IsLiteral ? -1 : Convert.ToInt32(build.GetRawConstantValue());
            Assembly assembly = typeof(Game).Assembly;
            report.AssemblyModuleId = assembly.ManifestModule.ModuleVersionId.ToString("N");
            using (var file = new FileStream(assembly.Location, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = new SHA256CryptoServiceProvider())
                report.AssemblySha256 = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            report.AssemblyMatched = report.Build == ExpectedBuild && report.AssemblySha256 == ExpectedAssemblySha256;
            if (!report.AssemblyMatched) failures.Add("world_capacity_native_assembly_mismatch");

            Game game = Game.Instance;
            ClusterManager manager = ClusterManager.Instance;
            ClusterGrid cluster = ClusterGrid.Instance;
            report.Loaded = game != null && game.baseAlreadyCreated && !game.IsLoading() && manager != null && cluster != null && GameClock.Instance != null;
            report.Paused = SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused;
            if (!report.Loaded) failures.Add("world_capacity_game_not_ready");
            if (!report.Paused) failures.Add("world_capacity_game_not_paused");
            if (!report.AssemblyMatched || !report.Loaded || !report.Paused) return;
            report.SimulationTime = GameClock.Instance.GetTime();
            report.GridWidth = Grid.WidthInCells; report.GridHeight = Grid.HeightInCells;
            long total = (long)report.GridWidth * report.GridHeight;
            var indices = Grid.WorldIdx; var elements = Grid.Element; var layers = Grid.ObjectLayers;
            if (report.GridWidth < 1 || report.GridHeight < 1 || total != Grid.CellCount || indices == null || elements == null ||
                total > indices.Length || total > elements.Length || layers == null || layers.Length != 45)
            { failures.Add("world_capacity_grid_contract_invalid"); return; }

            var worlds = manager.WorldContainers;
            if (worlds == null || worlds.Count == 0 || worlds.Count > 255)
            { failures.Add("world_capacity_world_list_invalid"); return; }
            var snapshot = worlds.ToArray();
            var rectangles = new List<WorldAdmissionWorldRectangle>();
            var ids = new HashSet<int>();
            foreach (WorldContainer world in snapshot)
            {
                if (world == null) { failures.Add("world_capacity_null_world"); continue; }
                var rect = new WorldAdmissionWorldRectangle { Id = world.id, X = world.WorldOffset.x, Y = world.WorldOffset.y,
                    Width = world.WorldSize.x, Height = world.WorldSize.y, IsModuleInterior = world.IsModuleInterior,
                    Name = world.GetProperName() ?? "" };
                rectangles.Add(rect);
                if (rect.Id < 0 || rect.Id >= 255 || !ids.Add(rect.Id)) failures.Add("world_capacity_world_id_invalid");
                if (!Fits(rect.X, rect.Y, rect.Width, rect.Height, report.GridWidth, report.GridHeight))
                    failures.Add("world_capacity_world_rectangle_invalid");
            }
            report.Worlds = rectangles.ToArray();
            for (int i = 0; i < rectangles.Count; i++)
                for (int j = i + 1; j < rectangles.Count; j++)
                    if (Overlaps(rectangles[i], rectangles[j])) failures.Add("world_capacity_existing_worlds_overlap");
            for (int id = 0; id < 255; id++)
                if (!ids.Contains(id)) { report.RemainingWorldIds++; if (report.CandidateWorldId < 0) report.CandidateWorldId = id; }
            if (report.CandidateWorldId < 0) failures.Add("world_capacity_native_ids_exhausted");

            var hex = new AxialI(report.HexQ, report.HexR);
            List<ClusterGridEntity> occupants = null;
            report.HexAvailable = cluster.IsValidCell(hex) && cluster.cellContents.TryGetValue(hex, out occupants) && occupants != null && occupants.Count == 0;
            if (!report.HexAvailable) failures.Add("world_capacity_native_hex_unavailable");
            if (report.RequestedWidth < 1 || report.RequestedHeight < 1 ||
                (long)report.RequestedWidth * report.RequestedHeight > MaxPlanetCells)
                failures.Add("world_capacity_requested_dimensions_invalid");
            else if (report.RequestedWidth > report.GridWidth || report.RequestedHeight > report.GridHeight)
                failures.Add("world_capacity_requested_dimensions_exceed_grid");
            if (failures.Count > 0) return;

            Vector2I offset;
            Vector2I required = BestFit.GetGridOffset(worlds, new Vector2I(report.RequestedWidth, report.RequestedHeight), out offset);
            report.CandidateX = offset.x; report.CandidateY = offset.y;
            report.FitsNativeGrid = required.x <= report.GridWidth && required.y <= report.GridHeight &&
                Fits(offset.x, offset.y, report.RequestedWidth, report.RequestedHeight, report.GridWidth, report.GridHeight);
            if (!report.FitsNativeGrid) { failures.Add("world_capacity_no_existing_grid_rectangle"); return; }

            // Validate the candidate plus the in-bounds one-cell ring, independently of BestFit's packing result.
            int minX = Math.Max(0, offset.x - 1), minY = Math.Max(0, offset.y - 1);
            int maxX = Math.Min(report.GridWidth - 1, offset.x + report.RequestedWidth);
            int maxY = Math.Min(report.GridHeight - 1, offset.y + report.RequestedHeight);
            foreach (var rect in rectangles)
                if (RectanglesOverlap(minX, minY, maxX - minX + 1, maxY - minY + 1, rect.X, rect.Y, rect.Width, rect.Height))
                { failures.Add("world_capacity_candidate_boundary_overlaps_world"); return; }
            var occupiedLayers = new List<Dictionary<int, GameObject>>();
            foreach (var layer in layers)
            {
                if (layer == null) { failures.Add("world_capacity_object_layer_unavailable"); return; }
                if (layer.Count != 0) occupiedLayers.Add(layer);
            }
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    report.CheckedCells++;
                    if (x < offset.x || y < offset.y || x >= offset.x + report.RequestedWidth || y >= offset.y + report.RequestedHeight)
                        report.CheckedBoundaryCells++;
                    if (!Grid.IsValidCell(cell) || indices[cell] != byte.MaxValue)
                    { failures.Add("world_capacity_candidate_world_index_not_free"); return; }
                    foreach (var layer in occupiedLayers)
                        if (layer.ContainsKey(cell))
                        { failures.Add("world_capacity_candidate_object_entry_present"); return; }
                    Element element = elements[cell];
                    if (element == null || (element.id != SimHashes.Vacuum && element.id != SimHashes.Void))
                    { failures.Add("world_capacity_candidate_element_not_empty"); return; }
                    float mass = Grid.Mass[cell];
                    if (float.IsNaN(mass) || float.IsInfinity(mass) || mass != 0f || Grid.DiseaseCount[cell] != 0 || BackwallManager.HasBackwall(cell))
                    { failures.Add("world_capacity_candidate_material_or_backwall_present"); return; }
                }
            report.CandidateClear = true;
            List<ClusterGridEntity> finalOccupants;
            report.Stable = cluster.cellContents.TryGetValue(hex, out finalOccupants) && ReferenceEquals(finalOccupants, occupants) && finalOccupants.Count == 0 &&
                ReferenceEquals(Game.Instance, game) && !game.IsLoading() &&
                ReferenceEquals(ClusterManager.Instance, manager) && ReferenceEquals(ClusterGrid.Instance, cluster) &&
                SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused && GameClock.Instance.GetTime() == report.SimulationTime &&
                Grid.WidthInCells == report.GridWidth && Grid.HeightInCells == report.GridHeight &&
                ReferenceEquals(Grid.WorldIdx, indices) && ReferenceEquals(Grid.Element, elements) && ReferenceEquals(Grid.ObjectLayers, layers) &&
                worlds.Count == snapshot.Length;
            if (report.Stable)
                for (int i = 0; i < snapshot.Length; i++)
                {
                    WorldContainer world = worlds[i]; var rect = report.Worlds[i];
                    if (!ReferenceEquals(world, snapshot[i]) || world.id != rect.Id || world.WorldOffset.x != rect.X || world.WorldOffset.y != rect.Y ||
                        world.WorldSize.x != rect.Width || world.WorldSize.y != rect.Height)
                    { report.Stable = false; break; }
                }
            if (!report.Stable) failures.Add("world_capacity_observation_changed");
        }

        private static bool Fits(int x, int y, int width, int height, int outerWidth, int outerHeight)
        { return x >= 0 && y >= 0 && width > 0 && height > 0 && (long)x + width <= outerWidth && (long)y + height <= outerHeight; }
        private static bool Overlaps(WorldAdmissionWorldRectangle a, WorldAdmissionWorldRectangle b)
        { return RectanglesOverlap(a.X, a.Y, a.Width, a.Height, b.X, b.Y, b.Width, b.Height); }
        private static bool RectanglesOverlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        { return (long)ax < (long)bx + bw && (long)bx < (long)ax + aw && (long)ay < (long)by + bh && (long)by < (long)ay + ah; }
    }
}
