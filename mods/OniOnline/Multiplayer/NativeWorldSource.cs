using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
using OniOnline.Network;
using OniOnline.Runtime;

namespace OniOnline.Multiplayer
{
    /// <summary>
    /// Copies the locally simulated world's explored cells. Call only on the owning Unity thread.
    /// Remote views consume these owned values; this source never creates or changes game objects.
    /// </summary>
    internal sealed class NativeWorldSource : IWorldSource
    {
        internal const int MaxWorldCells = 262144;
        private const int MaxNameLength = 64;
        private readonly int owningThread = Thread.CurrentThread.ManagedThreadId;
        private Game loadedGame;
        private WorldContainer loadedWorld;
        private string epoch;
        private bool hasSharingState;
        private bool lastShared;
        private readonly Func<int> selectedWorld;

        internal NativeWorldSource(Func<int> selectedWorld = null) { this.selectedWorld = selectedWorld; }

        internal string UnavailableReason
        { get { return NativeAdapterAvailability.Supports(SessionCapabilities.NativeObservationV1) ? null : "native_adapter_unavailable"; } }

        public bool IsAvailable
        {
            get
            {
                WorldContainer world;
                return UnavailableReason == null && TryGetWorld(out world);
            }
        }

        public double SimulationTime
        {
            get
            {
                WorldContainer world;
                return TryGetWorld(out world) ? GameClock.Instance.GetTime() : 0d;
            }
        }

        public string Epoch
        {
            get
            {
                WorldContainer world;
                return TryGetWorld(out world) ? epoch : null;
            }
        }

        public int NativeWorldId
        {
            get
            {
                WorldContainer world;
                return TryGetWorld(out world) ? world.id : -1;
            }
        }

        public WorldSnapshot Capture(string ownerId, long sequence, bool shared, bool includeCells = true)
        {
            RequireOwningThread();
            if (!ValidOwnerId(ownerId))
                throw new ArgumentException("invalid_player_id", "ownerId");
            if (sequence < 1) throw new ArgumentOutOfRangeException("sequence");

            WorldContainer world;
            if (!TryGetWorld(out world)) throw new InvalidOperationException("world_not_ready");
            if (hasSharingState && lastShared != shared) epoch = Guid.NewGuid().ToString("N");
            hasSharingState = true;
            lastShared = shared;
            int width = world.WorldSize.x;
            int height = world.WorldSize.y;
            var descriptor = new WorldDescriptor
            {
                Id = ownerId + ":" + world.id.ToString(CultureInfo.InvariantCulture),
                OwnerId = ownerId,
                Name = DisplayName(world.GetProperName()),
                Epoch = epoch,
                ControlRevision = 1,
                NativeWorldId = world.id,
                Width = width,
                Height = height,
                Shared = shared,
                SimulationTime = GameClock.Instance.GetTime()
            };
            var snapshot = new WorldSnapshot
            {
                World = descriptor,
                Sequence = sequence,
                Cells = new WorldCell[0],
                Dupes = new WorldDupe[0]
            };
            if (!shared || !includeCells) return snapshot;

            int offsetX = world.WorldOffset.x;
            int offsetY = world.WorldOffset.y;
            var cells = new List<WorldCell>(Math.Min(width * height, 8192));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int cell = Grid.XYToCell(offsetX + x, offsetY + y);
                    // Grid.IsVisible also honours the local debug/render fog toggle. It is
                    // deliberately not an authorization check: hidden material is never read here.
                    if (Grid.WorldIdx[cell] != world.id || Grid.Visible[cell] == 0) continue;
                    Element element = Grid.Element[cell];
                    if (element == null || element.substance == null) continue;
                    float mass = Grid.Mass[cell];
                    float temperature = Grid.Temperature[cell];
                    if (!Finite(mass) || !Finite(temperature) || mass < 0f || temperature < 0f) continue;
                    Color32 color = element.substance.uiColour;
                    cells.Add(new WorldCell
                    {
                        Index = y * width + x,
                        ElementHash = (int)element.id,
                        Mass = mass,
                        Temperature = temperature,
                        Color = ((uint)color.r << 24) | ((uint)color.g << 16) |
                            ((uint)color.b << 8) | color.a
                    });
                }
            }

            var dupes = new List<WorldDupe>();
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                int cell = Grid.PosToCell(dupe);
                if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id || Grid.Visible[cell] == 0)
                    continue;
                int x = Grid.CellColumn(cell) - offsetX;
                int y = Grid.CellRow(cell) - offsetY;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                int index = y * width + x;
                if (!ContainsIndex(cells, index)) continue;
                KPrefabID identity = dupe.GetComponent<KPrefabID>();
                if (identity == null || identity.InstanceID < 0) continue;
                if (dupes.Count == 128) throw new InvalidOperationException("world_too_many_duplicants");
                dupes.Add(new WorldDupe
                {
                    Id = identity.InstanceID,
                    Index = index,
                    Name = DisplayName(dupe.GetProperName())
                });
            }
            dupes.Sort((a, b) => a.Id.CompareTo(b.Id));
            snapshot.Cells = cells.ToArray();
            snapshot.Dupes = dupes.ToArray();
            return snapshot;
        }

        private bool TryGetWorld(out WorldContainer world)
        {
            NativeAdapterAvailability.Require(SessionCapabilities.NativeObservationV1);
            RequireOwningThread();
            world = null;
            Game game = Game.Instance;
            if (game == null || !game.baseAlreadyCreated || game.IsLoading() ||
                GameClock.Instance == null || ClusterManager.Instance == null)
                return Unavailable();

            int selected = selectedWorld == null ? ClusterManager.Instance.activeWorldId : selectedWorld();
            if (selected < 0) return Unavailable();
            world = ClusterManager.Instance.GetWorld(selected);
            if (world == null || world.id < 0 || world.id > byte.MaxValue ||
                Grid.WidthInCells <= 0 || Grid.HeightInCells <= 0 ||
                Grid.Visible == null || Grid.WorldIdx == null || Grid.Element == null)
                return Unavailable();

            int width = world.WorldSize.x;
            int height = world.WorldSize.y;
            int offsetX = world.WorldOffset.x;
            int offsetY = world.WorldOffset.y;
            long count = (long)width * height;
            long gridCount = (long)Grid.WidthInCells * Grid.HeightInCells;
            if (width <= 0 || height <= 0 || count > MaxWorldCells || offsetX < 0 || offsetY < 0 ||
                (long)offsetX + width > Grid.WidthInCells || (long)offsetY + height > Grid.HeightInCells ||
                gridCount > Grid.Visible.Length || gridCount > Grid.WorldIdx.Length ||
                gridCount > Grid.Element.Length || !Finite(GameClock.Instance.GetTime()))
                return Unavailable();

            if (!ReferenceEquals(loadedGame, game) || !ReferenceEquals(loadedWorld, world))
            {
                loadedGame = game;
                loadedWorld = world;
                epoch = Guid.NewGuid().ToString("N");
                hasSharingState = false;
            }
            return true;
        }

        private bool Unavailable()
        {
            // A transient loading/publication gap is not a new world incarnation.
            // The next available Game/WorldContainer reference decides whether to renew it.
            return false;
        }

        private void RequireOwningThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != owningThread)
                throw new InvalidOperationException("world_source_requires_unity_thread");
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool ValidOwnerId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
            foreach (char item in value)
                if (!(item >= 'a' && item <= 'z') && !(item >= 'A' && item <= 'Z') &&
                    !(item >= '0' && item <= '9') && item != '-' && item != '_' && item != '.')
                    return false;
            return true;
        }

        private static bool ContainsIndex(List<WorldCell> cells, int index)
        {
            int low = 0;
            int high = cells.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int found = cells[middle].Index;
                if (found == index) return true;
                if (found < index) low = middle + 1;
                else high = middle - 1;
            }
            return false;
        }

        private static string DisplayName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var result = new StringBuilder(Math.Min(value.Length, MaxNameLength));
            for (int i = 0; i < value.Length && result.Length < MaxNameLength; i++)
            {
                char item = value[i];
                if (char.IsControl(item)) continue;
                if (char.IsHighSurrogate(item))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]) ||
                        result.Length + 2 > MaxNameLength) continue;
                    result.Append(item).Append(value[++i]);
                }
                else if (!char.IsLowSurrogate(item)) result.Append(item);
            }
            return result.ToString();
        }
    }
}
