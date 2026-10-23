using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using OniOnline.Network;
using OniOnline.Runtime;

namespace OniOnline.Multiplayer
{
    /// <summary>
    /// A read-only, viewport-bounded sampler. Create one on the Unity thread per observer;
    /// its numeric cache is private to that observer and is discarded across authority changes.
    /// This does not use the legacy whole-world snapshot path.
    /// </summary>
    internal sealed class NativeWorldInterestSource
    {
        internal const int DuplicantKind = 1;
        internal const int AnimalKind = 2;
        private readonly int owningThread = Thread.CurrentThread.ManagedThreadId;
        private string epoch;
        private long subscriptionRevision = -1, authorizationRevision = -1, lastTick = -1;
        private Game loadedGame;
        private WorldContainer loadedWorld;
        private WorldInterestRegion region;
        private Dictionary<int, NumericSample> numeric = new Dictionary<int, NumericSample>();

        private struct NumericSample
        {
            internal float Temperature, Mass;
            internal long TemperatureTick, MassTick;
            internal WorldInterestChannels Fields;
        }

        // Authorization is expressed in planet-local row-major cell indices, matching the wire model.
        // It is checked before any material or numerical property of that cell is inspected.
        internal WorldInterestFrame Capture(WorkPlanet planet, WorldInterestSubscription subscription,
            long tick, long authRevision, Func<int, bool> authorized)
        {
            return NativeAdapterAvailability.Read(SessionCapabilities.NativeObservationV1,
                () => CaptureNative(planet, subscription, tick, authRevision, authorized));
        }

        private WorldInterestFrame CaptureNative(WorkPlanet planet, WorldInterestSubscription subscription,
            long tick, long authRevision, Func<int, bool> authorized)
        {
            if (Thread.CurrentThread.ManagedThreadId != owningThread)
                throw new InvalidOperationException("world_interest_requires_unity_thread");
            if (planet == null || string.IsNullOrEmpty(planet.AuthorityEpoch) || planet.AuthorityEpoch.Length > 64 ||
                planet.NativeWorldId < 0 || planet.NativeWorldId > byte.MaxValue || subscription == null ||
                tick < 0 || authRevision < 0 || authorized == null)
                throw new ArgumentException("invalid_native_world_interest");

            Game game = Game.Instance;
            WorldContainer world = game == null || !game.baseAlreadyCreated || game.IsLoading() || ClusterManager.Instance == null
                ? null : ClusterManager.Instance.GetWorld(planet.NativeWorldId);
            if (world == null || world.id != planet.NativeWorldId || world.IsModuleInterior)
                throw new InvalidOperationException("world_not_ready");
            int width = world.WorldSize.x, height = world.WorldSize.y;
            int offsetX = world.WorldOffset.x, offsetY = world.WorldOffset.y;
            long gridCount = (long)Grid.WidthInCells * Grid.HeightInCells;
            if (width < 1 || height < 1 || (long)width * height > NativeWorldSource.MaxWorldCells ||
                offsetX < 0 || offsetY < 0 || (long)offsetX + width > Grid.WidthInCells ||
                (long)offsetY + height > Grid.HeightInCells || Grid.Visible == null || Grid.WorldIdx == null ||
                gridCount > Grid.Visible.Length || gridCount > Grid.WorldIdx.Length)
                throw new InvalidOperationException("world_grid_not_ready");
            WorldInterestSubscription sub = subscription.ValidatedCopy(width, height);

            bool replacement = loadedGame != game || loadedWorld != world;
            if (replacement && epoch == planet.AuthorityEpoch && loadedWorld != null)
                throw new InvalidOperationException("world_interest_authority_epoch_not_renewed");
            if (replacement || epoch != planet.AuthorityEpoch || subscriptionRevision != sub.Revision ||
                authorizationRevision != authRevision || region.X != sub.Region.X || region.Y != sub.Region.Y ||
                region.Width != sub.Region.Width || region.Height != sub.Region.Height)
            {
                numeric.Clear(); lastTick = -1;
            }
            if (tick < lastTick) throw new ArgumentException("regressing_native_interest_tick");
            loadedGame = game; loadedWorld = world; epoch = planet.AuthorityEpoch;
            subscriptionRevision = sub.Revision; authorizationRevision = authRevision; lastTick = tick; region = sub.Region;

            // Fast motion does not inspect empty viewport cells or invalidate slower numerical samples.
            // Only actual, authorized entity locations need exploration records for the publisher.
            if (sub.Channels == WorldInterestChannels.Entities && sub.HoverIndex == -1)
            {
                var occupiedCells = new List<WorldInterestCell>();
                var motion = CaptureEntities(world, width, height, sub.Region, authorized,
                    new HashSet<int>(), occupiedCells);
                return new WorldInterestFrame { Epoch = epoch, Tick = tick, AuthorizationRevision = authRevision,
                    Width = width, Height = height, Cells = occupiedCells.ToArray(), Entities = motion.ToArray() };
            }

            var cells = new List<WorldInterestCell>(sub.Region.Width * sub.Region.Height);
            var allowedCells = new HashSet<int>();
            var nextNumeric = new Dictionary<int, NumericSample>();
            for (int y = sub.Region.Y; y < sub.Region.Y + sub.Region.Height; y++)
            {
                for (int x = sub.Region.X; x < sub.Region.X + sub.Region.Width; x++)
                {
                    int localIndex = y * width + x;
                    int cell = Grid.XYToCell(offsetX + x, offsetY + y);
                    // Visible[] is persistent exploration; Grid.IsVisible also admits debug/render fog overrides.
                    if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id || Grid.Visible[cell] == 0 ||
                        !authorized(localIndex)) continue;

                    var fields = sub.Channels & (WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass);
                    bool hover = localIndex == sub.HoverIndex;
                    if (hover) fields |= WorldInterestChannels.Temperature | WorldInterestChannels.Mass;
                    var value = new WorldInterestCell { Index = localIndex, Explored = true, Fields = fields };
                    if ((fields & WorldInterestChannels.Visual) != 0)
                    {
                        if (Grid.Element == null || cell >= Grid.Element.Length) continue;
                        Element element = Grid.Element[cell];
                        if (element == null || element.substance == null) continue;
                        Color32 color = element.substance.uiColour;
                        value.ElementHash = (int)element.id;
                        value.Color = ((uint)color.r << 24) | ((uint)color.g << 16) | ((uint)color.b << 8) | color.a;
                    }

                    NumericSample sample;
                    numeric.TryGetValue(localIndex, out sample);
                    if ((fields & WorldInterestChannels.Temperature) != 0)
                    {
                        if (hover || (sample.Fields & WorldInterestChannels.Temperature) == 0 ||
                            tick - sample.TemperatureTick >= sub.NumericIntervalTicks)
                        {
                            float temperature = Grid.Temperature[cell];
                            if (!FiniteNonnegative(temperature)) continue;
                            sample.Temperature = temperature; sample.TemperatureTick = tick;
                            sample.Fields |= WorldInterestChannels.Temperature;
                        }
                        value.Temperature = sample.Temperature;
                    }
                    if ((fields & WorldInterestChannels.Mass) != 0)
                    {
                        if (hover || (sample.Fields & WorldInterestChannels.Mass) == 0 ||
                            tick - sample.MassTick >= sub.NumericIntervalTicks)
                        {
                            float mass = Grid.Mass[cell];
                            if (!FiniteNonnegative(mass)) continue;
                            sample.Mass = mass; sample.MassTick = tick;
                            sample.Fields |= WorldInterestChannels.Mass;
                        }
                        value.Mass = sample.Mass;
                    }
                    if (sample.Fields != WorldInterestChannels.None) nextNumeric.Add(localIndex, sample);
                    cells.Add(value); allowedCells.Add(localIndex);
                }
            }
            numeric = nextNumeric;

            var entities = (sub.Channels & WorldInterestChannels.Entities) != 0
                ? CaptureEntities(world, width, height, sub.Region, authorized, allowedCells, null)
                : new List<WorldInterestEntity>();
            return new WorldInterestFrame { Epoch = epoch, Tick = tick, AuthorizationRevision = authRevision,
                Width = width, Height = height, Cells = cells.ToArray(), Entities = entities.ToArray() };
        }

        private static List<WorldInterestEntity> CaptureEntities(WorldContainer world, int width, int height,
            WorldInterestRegion region, Func<int, bool> authorized, HashSet<int> allowedCells,
            List<WorldInterestCell> occupiedCells)
        {
            var entities = new List<WorldInterestEntity>();
            var seen = new HashSet<int>();
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
                AddEntity(dupe, DuplicantKind, world, width, height, region, authorized, allowedCells, seen, entities, occupiedCells);
            foreach (Brain brain in Components.Brains.Items)
            {
                // CreatureBrain also serves some robots. The Creature tag limits this lane to animals.
                if (brain == null || !(brain is CreatureBrain)) continue;
                KPrefabID identity = brain.GetComponent<KPrefabID>();
                if (identity == null || !identity.HasTag(GameTags.Creature)) continue;
                AddEntity(brain, AnimalKind, world, width, height, region, authorized, allowedCells, seen, entities, occupiedCells);
            }
            entities.Sort((a, b) => a.Id.CompareTo(b.Id));
            return entities;
        }

        private static void AddEntity(KMonoBehaviour entity, int kind, WorldContainer world, int width, int height,
            WorldInterestRegion region, Func<int, bool> authorized, HashSet<int> allowedCells, HashSet<int> seen,
            List<WorldInterestEntity> result, List<WorldInterestCell> occupiedCells)
        {
            if (entity == null) return;
            int cell = Grid.PosToCell(entity);
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id) return;
            int localX = Grid.CellColumn(cell) - world.WorldOffset.x;
            int localY = Grid.CellRow(cell) - world.WorldOffset.y;
            if (localX < 0 || localY < 0 || localX >= width || localY >= height) return;
            int index = localY * width + localX;
            if (occupiedCells == null)
            { if (!allowedCells.Contains(index)) return; }
            else if (!region.Contains(index, width) || Grid.Visible[cell] == 0 || !authorized(index)) return;
            KPrefabID identity = entity.GetComponent<KPrefabID>();
            if (identity == null || identity.InstanceID < 0 || !seen.Add(identity.InstanceID)) return;
            Vector3 position = entity.transform.GetPosition();
            float x = position.x - world.WorldOffset.x, y = position.y - world.WorldOffset.y;
            if (!FiniteNonnegative(x) || !FiniteNonnegative(y) || x >= width || y >= height ||
                (int)x + (int)y * width != index) return;
            if (result.Count >= WorldInterestPublisher.MaxVisibleEntities)
                throw new InvalidOperationException("world_interest_too_many_visible_entities");
            result.Add(new WorldInterestEntity { Id = identity.InstanceID, CellIndex = index, Kind = kind, X = x, Y = y });
            if (occupiedCells != null && allowedCells.Add(index))
                occupiedCells.Add(new WorldInterestCell { Index = index, Explored = true, Fields = WorldInterestChannels.None });
        }

        private static bool FiniteNonnegative(float value)
        { return value >= 0f && !float.IsInfinity(value); }
    }
}
