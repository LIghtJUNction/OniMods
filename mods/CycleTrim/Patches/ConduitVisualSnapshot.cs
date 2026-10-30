using System;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    // Only interpolation and sprite phase may be sampled at a lower rate.
    // All other inputs to the normal (non-overlay) display are compared here.
    internal struct ConduitVisualSnapshot
    {
        private long generation;
        private int world, width, height, minX, minY, maxX, maxY;
        private int layer, highlightedCell;
        private float size, sprites, framesPerSecond;
        private Vector2 massRange, massValues, gridOffset;
        private Color32 tint, insulatedTint, radiantTint, highlightColour;
        private ConduitFlow flow;
        private ConduitFlowVisualizer.Tuning tuning;
        private Game.ConduitVisInfo style;
        private Texture2D background, foreground;
        private object movingHolder, staticHolder;
        private Mesh movingMesh, staticMesh;
        private Material movingMaterial, staticMaterial;

        internal static bool TryCapture(ConduitFlowVisualizer instance, long generation,
            out ConduitVisualSnapshot snapshot)
        {
            snapshot = default(ConduitVisualSnapshot);
            var tuning = Access.Tuning(instance);
            var style = Access.Style(instance);
            var moving = Access.Moving(instance);
            var stationary = Access.Stationary(instance);
            if (tuning == null || style == null || moving == null || stationary == null
                || !tuning.renderMesh || Access.Flow(instance) == null)
                return false;

            var movingMesh = Access.Mesh(moving);
            var staticMesh = Access.Mesh(stationary);
            var movingMaterial = Access.Material(moving);
            var staticMaterial = Access.Material(stationary);
            if (movingMesh == null || staticMesh == null
                || movingMaterial == null || staticMaterial == null)
                return false;

            // Use the same live camera query as the original. The controller's
            // cached CurrentArea can precede a camera/world change this frame.
            var area = GridVisibleArea.GetVisibleArea();
            var cluster = ClusterManager.Instance;
            snapshot.generation = generation;
            snapshot.world = cluster == null ? -1 : cluster.activeWorldId;
            snapshot.width = Grid.WidthInCells;
            snapshot.height = Grid.HeightInCells;
            snapshot.minX = Math.Max(0, area.Min.x - 1);
            snapshot.minY = Math.Max(0, area.Min.y - 1);
            snapshot.maxX = Math.Min(Grid.WidthInCells - 1, area.Max.x + 1);
            snapshot.maxY = Math.Min(Grid.HeightInCells - 1, area.Max.y + 1);
            snapshot.layer = Access.Layer(instance);
            snapshot.highlightedCell = Access.HighlightedCell(instance);
            snapshot.highlightColour = Access.HighlightColour(instance);
            snapshot.flow = Access.Flow(instance);
            snapshot.tuning = tuning;
            snapshot.style = style;
            snapshot.size = tuning.size;
            snapshot.sprites = tuning.spriteCount;
            snapshot.framesPerSecond = tuning.framesPerSecond;
            snapshot.background = tuning.backgroundTexture;
            snapshot.foreground = tuning.foregroundTexture;
            snapshot.tint = style.tint;
            snapshot.insulatedTint = style.insulatedTint;
            snapshot.radiantTint = style.radiantTint;
            snapshot.massRange = style.overlayMassScaleRange;
            snapshot.massValues = style.overlayMassScaleValues;
            snapshot.gridOffset = Access.GridOffset();
            snapshot.movingHolder = moving;
            snapshot.staticHolder = stationary;
            snapshot.movingMesh = movingMesh;
            snapshot.staticMesh = staticMesh;
            snapshot.movingMaterial = movingMaterial;
            snapshot.staticMaterial = staticMaterial;
            return true;
        }

        internal bool SameInputs(in ConduitVisualSnapshot other)
        {
            return generation == other.generation && world == other.world
                && width == other.width && height == other.height
                && minX == other.minX && minY == other.minY
                && maxX == other.maxX && maxY == other.maxY
                && layer == other.layer && highlightedCell == other.highlightedCell
                && ReferenceEquals(flow, other.flow) && ReferenceEquals(tuning, other.tuning)
                && ReferenceEquals(style, other.style)
                && size == other.size && sprites == other.sprites
                && framesPerSecond == other.framesPerSecond
                && ReferenceEquals(background, other.background)
                && ReferenceEquals(foreground, other.foreground)
                && Same(tint, other.tint) && Same(insulatedTint, other.insulatedTint)
                && Same(radiantTint, other.radiantTint) && Same(highlightColour, other.highlightColour)
                && Same(massRange, other.massRange) && Same(massValues, other.massValues)
                && Same(gridOffset, other.gridOffset)
                && ReferenceEquals(movingHolder, other.movingHolder)
                && ReferenceEquals(staticHolder, other.staticHolder)
                && ReferenceEquals(movingMesh, other.movingMesh)
                && ReferenceEquals(staticMesh, other.staticMesh)
                && ReferenceEquals(movingMaterial, other.movingMaterial)
                && ReferenceEquals(staticMaterial, other.staticMaterial);
        }

        internal void Draw(float z)
        {
            var position = new Vector3(gridOffset.x, gridOffset.y, z - 0.1f);
            Graphics.DrawMesh(movingMesh, position, Quaternion.identity, movingMaterial, layer);
            Graphics.DrawMesh(staticMesh, position, Quaternion.identity, staticMaterial, layer);
        }

        private static bool Same(Vector2 left, Vector2 right)
        {
            return left.x == right.x && left.y == right.y;
        }

        private static bool Same(Color32 left, Color32 right)
        {
            return left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;
        }

        // Build accessors once, after the game has created its visualizers.
        // No FieldInfo.GetValue, boxing or argument arrays occur in hot calls.
        internal static class Access
        {
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, bool> ShowContents;
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, int> Layer, HighlightedCell;
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, Color32> HighlightColour;
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, ConduitFlow> Flow;
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, ConduitFlowVisualizer.Tuning> Tuning;
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, Game.ConduitVisInfo> Style;
            internal static AccessTools.FieldRef<ConduitFlowVisualizer, object> Moving, Stationary;
            internal static AccessTools.FieldRef<object, Mesh> Mesh;
            internal static AccessTools.FieldRef<object, Material> Material;
            internal static AccessTools.FieldRef<Vector2> GridOffset;

            internal static void Initialize(Type meshType)
            {
                ShowContents = AccessTools.FieldRefAccess<ConduitFlowVisualizer, bool>("showContents");
                Layer = AccessTools.FieldRefAccess<ConduitFlowVisualizer, int>("layer");
                HighlightedCell = AccessTools.FieldRefAccess<ConduitFlowVisualizer, int>("highlightedCell");
                HighlightColour = AccessTools.FieldRefAccess<ConduitFlowVisualizer, Color32>("highlightColour");
                Flow = AccessTools.FieldRefAccess<ConduitFlowVisualizer, ConduitFlow>("flowManager");
                Tuning = AccessTools.FieldRefAccess<ConduitFlowVisualizer, ConduitFlowVisualizer.Tuning>("tuning");
                Style = AccessTools.FieldRefAccess<ConduitFlowVisualizer, Game.ConduitVisInfo>("visInfo");
                Moving = AccessTools.FieldRefAccess<ConduitFlowVisualizer, object>("movingBallMesh");
                Stationary = AccessTools.FieldRefAccess<ConduitFlowVisualizer, object>("staticBallMesh");
                Mesh = AccessTools.FieldRefAccess<object, Mesh>(AccessTools.Field(meshType, "mesh"));
                Material = AccessTools.FieldRefAccess<object, Material>(AccessTools.Field(meshType, "material"));
                GridOffset = AccessTools.StaticFieldRefAccess<Vector2>(
                    AccessTools.Field(typeof(ConduitFlowVisualizer), "GRID_OFFSET"));
            }
        }
    }
}
