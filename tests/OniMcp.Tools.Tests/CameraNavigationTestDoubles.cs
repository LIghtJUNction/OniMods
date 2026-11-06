using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

// CameraController's native SetTargetPos/SnapTo keep a zoom target, whereas
// CameraGoTo replaces it with 10. A subsequent native frame applies that target.
// These doubles exercise production routing, not Unity rendering or timing.
internal sealed class CameraController
{
    internal static CameraController Instance;
    internal readonly Transform transform = new Transform();
    internal float targetOrthographicSize { get; private set; }
    internal Vector3 targetPos;
    internal float Speed;
    internal bool UnpauseOnTarget { get; set; }
    internal void SnapTo(Vector3 position, float zoom)
    {
        transform.Position = targetPos = position;
        targetOrthographicSize = Camera.main.orthographicSize = zoom;
    }
    internal void CameraGoTo(Vector3 position, float speed, bool sound)
    {
        SetTargetPos(position, 10f, sound);
        SetOverrideZoomSpeed(speed);
    }
    internal void SetTargetPos(Vector3 position, float zoom, bool sound)
    {
        targetPos = position;
        targetOrthographicSize = zoom;
        if (UnpauseOnTarget)
        {
            UnpauseOnTarget = false;
            SpeedControlScreen.Instance.Unpause(false);
        }
    }
    internal void SetOverrideZoomSpeed(float speed) { Speed = speed; }
    internal void AdvanceFrame()
    {
        transform.Position = targetPos;
        Camera.main.orthographicSize = targetOrthographicSize;
    }
}

internal sealed class ClusterManager
{
    internal static ClusterManager Instance { get; set; }
    internal int activeWorldId;
    internal readonly Dictionary<int, TestCameraWorld> Worlds = new Dictionary<int, TestCameraWorld>();
    internal TestCameraWorld GetWorld(int id) => Worlds.TryGetValue(id, out var world) ? world : id == 0 ? new TestCameraWorld
    {
        minimumBounds = new Vector3(0f, 0f, 0f), maximumBounds = new Vector3(300f, 500f, 0f)
    } : null;
    internal void SetActiveWorld(int id) { activeWorldId = id; }
}

internal sealed class TestCameraWorld
{
    internal WorldInventory worldInventory { get; set; }
    internal int id { get; set; }
    internal bool IsDiscovered { get; set; }
    internal bool IsDupeVisited { get; set; }
    internal bool IsModuleInterior { get; set; }
    internal Vector3 minimumBounds { get; set; }
    internal Vector3 maximumBounds { get; set; }
    internal void LookAtSurface() { }
    internal string GetProperName() => "fixture";
}

internal sealed class ManagementMenu
{
    internal static ManagementMenu Instance { get; set; }
    internal void CloseAll() { }
}

namespace UnityEngine
{
    internal sealed class Camera
    {
        internal static Camera main;
        internal float orthographicSize;
    }
    internal sealed class Transform
    {
        internal Vector3 Position;
        internal Vector3 GetPosition() => Position;
    }
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float value) => new Vector3(a.x * value, a.y * value, a.z * value);
    }
    internal static class Screen
    {
        internal static int width => 1228;
        internal static int height => 1516;
    }
}

namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static float? GetFloat(JObject args, string key) => args[key]?.Value<float?>();
    }
    public static partial class CameraTools
    {
        private static Dictionary<string, McpToolParameter> CameraControlParams() => new Dictionary<string, McpToolParameter>();
        public static McpTool SwitchView() => new McpTool();
        public static McpTool FocusCell() => new McpTool();
        public static McpTool FocusDupe() => new McpTool();
        public static McpTool TakeScreenshot() => new McpTool();
        public static McpTool TakeCoordinateScreenshot() => new McpTool();
    }
}
