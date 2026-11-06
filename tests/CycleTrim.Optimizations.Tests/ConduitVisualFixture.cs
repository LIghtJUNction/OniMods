using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

// An independent host model of mesh publication, invalidation and draw calls.
// The payload is a small integer calculation, not ONI geometry or simulation.
public sealed class ConduitFlowVisualizer
{
    private ConduitFlow flowManager;
    private bool showContents;
    private double animTime;
    private int layer;
    private int highlightedCell = -1;
    private Color32 highlightColour = new Color32(51, 51, 51, 51);
    private Tuning tuning;
    private Game.ConduitVisInfo visInfo;
    private ConduitFlowMesh movingBallMesh;
    private ConduitFlowMesh staticBallMesh;
    private static Vector2 GRID_OFFSET = new Vector2(0.5f, 0.5f);

    public int VisibleCount = 1;
    public int BuildCalls;
    public int EmptyBuildCalls;
    public int AudioResets;
    public int AudioCalls;
    public int AudioObservedVersion = -1;
    public int ThermalChanges;
    public int WorkIterations;
    public bool FailBeforeDraw;
    public bool FailAfterFirstDraw;
    public bool FailAfterBothDraws;
    public Action BeforeBuild;
    public Action AfterFirstDraw;
    public Action AfterBuild;

    public double AnimationTime { get { return animTime; } }
    public Mesh MovingMesh { get { return movingBallMesh.Mesh; } }
    public Mesh StaticMesh { get { return staticBallMesh.Mesh; } }
    public Material MovingMaterial { get { return movingBallMesh.Material; } }
    public Material StaticMaterial { get { return staticBallMesh.Material; } }
    public Tuning Settings { get { return tuning; } }
    public Game.ConduitVisInfo VisualInfo { get { return visInfo; } }

    public ConduitFlowVisualizer(ConduitFlow flow)
    {
        flowManager = flow;
        tuning = new Tuning
        {
            renderMesh = true, size = 1f, spriteCount = 4f, framesPerSecond = 12f,
            backgroundTexture = new Texture2D(), foregroundTexture = new Texture2D()
        };
        visInfo = new Game.ConduitVisInfo();
        movingBallMesh = new ConduitFlowMesh();
        staticBallMesh = new ConduitFlowMesh();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Render(float z, int render_layer, float lerp_percent, bool trigger_audio = false)
    {
        animTime += Time.deltaTime;
        if (trigger_audio)
        {
            AudioResets++;
            AudioObservedVersion = -1;
        }
        if (tuning.renderMesh)
            RenderMesh(z, render_layer, lerp_percent, trigger_audio);
        if (trigger_audio)
            AudioCalls++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RenderMesh(float z, int render_layer, float lerp_percent, bool trigger_audio)
    {
        BuildCalls++;
        BeforeBuild?.Invoke();
        var area = GridVisibleArea.GetVisibleArea();
        var context = new RenderMeshContext(this, lerp_percent, area.Min, area.Max);
        if (VisibleCount == 0)
        {
            EmptyBuildCalls++;
            return;
        }
        RenderMeshBatchJob.Instance.Reset(context);
        if (FailBeforeDraw)
            throw new InvalidOperationException("synthetic build failed before drawing");
        var payload = unchecked(flowManager.Content + flowManager.SimulationVersion * 31
            + flowManager.TopologyVersion * 397 + highlightedCell + ThermalChanges
            + (showContents ? 7 : 0) + highlightColour.r);
        for (var index = 0; index < WorkIterations; index++)
            payload = unchecked(payload * 1664525 + index + 1013904223);
        movingBallMesh.Mesh.Content = payload;
        staticBallMesh.Mesh.Content = ~payload;
        movingBallMesh.Material.SpriteTime = animTime;
        staticBallMesh.Material.SpriteTime = 0d;
        if (trigger_audio)
            AudioObservedVersion = flowManager.SimulationVersion;
        movingBallMesh.End(z, layer);
        ObserveFirstDraw();
        staticBallMesh.End(z, layer);
        ObserveBuildEnd();
        RenderMeshBatchJob.Instance.Reset(RenderMeshContext.EmptyContext);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ObserveFirstDraw()
    {
        AfterFirstDraw?.Invoke();
        if (FailAfterFirstDraw)
            throw new InvalidOperationException("synthetic build failed after its first draw");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ObserveBuildEnd()
    {
        if (FailAfterBothDraws)
            throw new InvalidOperationException("synthetic build failed before completion");
        AfterBuild?.Invoke();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ColourizePipeContents(bool show_contents, bool move_to_overlay_layer)
    {
        showContents = show_contents;
        layer = show_contents && move_to_overlay_layer ? 7 : 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void AddThermalConductivity(int cell, float conductivity)
    {
        ThermalChanges++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RemoveThermalConductivity(int cell, float conductivity)
    {
        ThermalChanges++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetHighlightedCell(int cell) { highlightedCell = cell; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void FreeResources()
    {
        movingBallMesh.Cleanup();
        staticBallMesh.Cleanup();
    }

    public sealed class Tuning
    {
        public bool renderMesh;
        public float size;
        public float spriteCount;
        public float framesPerSecond;
        public Texture2D backgroundTexture;
        public Texture2D foregroundTexture;
    }

    private sealed class ConduitFlowMesh
    {
        private Mesh mesh = new Mesh();
        private Material material = new Material();
        internal Mesh Mesh { get { return mesh; } }
        internal Material Material { get { return material; } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void End(float z, int layer)
        {
            mesh.Uploads++;
            Graphics.DrawMesh(mesh, new Vector3(GRID_OFFSET.x, GRID_OFFSET.y, z - 0.1f),
                Quaternion.identity, material, layer);
        }

        public void Cleanup()
        {
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            mesh = null;
            material = null;
        }
    }

    private struct RenderMeshContext
    {
        public static RenderMeshContext EmptyContext = default(RenderMeshContext);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public RenderMeshContext(ConduitFlowVisualizer outer, float lerp_percent,
            Vector2I min, Vector2I max)
        {
            // This fixture does not copy the game's pooled visible-pipe scan.
        }
    }

    private sealed class RenderMeshBatchJob
    {
        public static RenderMeshBatchJob Instance { get; } = new RenderMeshBatchJob();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Reset(RenderMeshContext context) { }
    }
}

public sealed class ConduitFlow
{
    private float lastUpdateTime = float.NegativeInfinity;
    public int Content;
    public int SimulationVersion;
    public int TopologyVersion;
    public int SimCalls;
    public int RebuildCalls;
    public Action DuringSimulation;
    public Action DuringRebuild;
    public bool FailSimulation;
    public bool FailRebuild;

    public float LastUpdateTime { get { return lastUpdateTime; } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Sim200ms(float dt)
    {
        SimCalls++;
        if (dt <= 0f)
            return;
        lastUpdateTime = Time.time;
        SimulationVersion++;
        DuringSimulation?.Invoke();
        if (FailSimulation)
            throw new InvalidOperationException("synthetic simulation failed");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RebuildConnections(IEnumerable<int> root_nodes)
    {
        RebuildCalls++;
        TopologyVersion++;
        DuringRebuild?.Invoke();
        if (FailRebuild)
            throw new InvalidOperationException("synthetic topology rebuild failed");
    }

    public void RebuildForTest() { RebuildConnections(new int[0]); }
    public void SetContentsForTest(int value) { Content = value; }
    public void SetLastUpdateTimeForTest(float value) { lastUpdateTime = value; }
}

public sealed partial class Game
{
    public int PrefabInitCalls;
    public int CleanupCalls;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnPrefabInit() { PrefabInitCalls++; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnCleanUp() { CleanupCalls++; }

    public void InitializeForTest() { OnPrefabInit(); }
    public void CleanupForTest() { OnCleanUp(); }

    public sealed class ConduitVisInfo
    {
        public Color32 tint = new Color32(1, 2, 3, 4);
        public Color32 insulatedTint = new Color32(5, 6, 7, 8);
        public Color32 radiantTint = new Color32(9, 10, 11, 12);
        public string overlayTintName = "overlay";
        public string overlayInsulatedTintName = "insulated";
        public string overlayRadiantTintName = "radiant";
        public Vector2 overlayMassScaleRange = new Vector2(1f, 1000f);
        public Vector2 overlayMassScaleValues = new Vector2(0.1f, 1f);
    }
}

public struct Vector2I
{
    public int x, y;
    public Vector2I(int x, int y) { this.x = x; this.y = y; }
}

public struct GridArea
{
    public Vector2I Min, Max;
    public GridArea(Vector2I min, Vector2I max) { Min = min; Max = max; }
}

public static class GridVisibleArea
{
    public static GridArea Area = new GridArea(new Vector2I(0, 0), new Vector2I(31, 31));
    public static int Reads;
    public static Action BeforeRead;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static GridArea GetVisibleArea()
    {
        Reads++;
        BeforeRead?.Invoke();
        return Area;
    }
}

public sealed class ClusterManager
{
    public static ClusterManager Instance { get; set; } = new ClusterManager();
    public int activeWorldId
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get;
        [MethodImpl(MethodImplOptions.NoInlining)] set;
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if (!ReferenceEquals(value, null))
                value.Destroyed = true;
        }

        public static bool operator ==(Object left, Object right)
        {
            var leftNull = ReferenceEquals(left, null) || left.Destroyed;
            var rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
    }

    public sealed class Mesh : Object
    {
        public int Content;
        public int Uploads;
    }

    public sealed class Material : Object
    {
        public double SpriteTime;
    }

    public sealed class Texture2D : Object { }

    public static class Time
    {
        public static int frameCount;
        public static double unscaledTimeAsDouble;
        public static float deltaTime = 1f / 60f;
        public static float time;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity { get { return new Quaternion { w = 1f }; } }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }
    }

    public static class Graphics
    {
        public struct DrawCall
        {
            public Mesh Mesh;
            public Material Material;
            public Vector3 Position;
            public Quaternion Rotation;
            public int Layer;
            public int Content;
        }

        public static readonly List<DrawCall> Draws = new List<DrawCall>();
        public static int DrawAttempts;
        public static int ThrowOnDrawCall;
        public static Action<Mesh> BeforeDraw;
        public static Action<DrawCall> AfterDraw;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation,
            Material material, int layer)
        {
            DrawAttempts++;
            BeforeDraw?.Invoke(mesh);
            if (DrawAttempts == ThrowOnDrawCall)
                throw new InvalidOperationException("synthetic draw failure");
            if (mesh == null || material == null)
                throw new InvalidOperationException("drawing a missing or released resource");
            var draw = new DrawCall
            {
                Mesh = mesh, Material = material, Position = position, Rotation = rotation,
                Layer = layer, Content = mesh.Content
            };
            Draws.Add(draw);
            AfterDraw?.Invoke(draw);
        }

        public static void ResetForTest()
        {
            Draws.Clear();
            DrawAttempts = 0;
            ThrowOnDrawCall = 0;
            BeforeDraw = null;
            AfterDraw = null;
        }
    }
}
