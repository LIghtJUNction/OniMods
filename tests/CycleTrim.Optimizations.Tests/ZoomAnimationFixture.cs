using System.Runtime.CompilerServices;

// Synthetic animation receiver: observes delivered time and the original
// visibility gate. It does not load Unity or implement the animation system.
// The pinned 744825 UpdateAnim processes transforms before its hidden gate;
// hidden OffscreenUpdate controllers advance time, and forceRebuild bypasses
// that gate. These distinctions must survive the production prefix.
public sealed class KBatchedAnimController : UnityEngine.Object
{
    public int InstanceId;
    public bool isMovable;
    public bool IsMoving;
    public bool Visible = true;
    public bool OffscreenUpdates;
    public bool ForceRebuild;
    public bool TransformDirty;
    public float Elapsed;
    public int Updates;
    public int Rebuilds;
    public int TransformUpdates;

    public int GetInstanceID() { return InstanceId; }
    public bool IsVisible() { return Visible; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateAnim(float dt)
    {
        Updates++;
        if (TransformDirty)
        {
            TransformDirty = false;
            TransformUpdates++;
        }
        if (!Visible && !ForceRebuild)
        {
            if (OffscreenUpdates)
                Elapsed += dt;
            return;
        }
        Elapsed += dt;
        if (ForceRebuild)
        {
            ForceRebuild = false;
            Rebuilds++;
        }
    }
}

public sealed class CameraController
{
    public static CameraController Instance = new CameraController();
    public float OrthographicSize;
}

public static partial class OverlayModes
{
    public sealed class TileMode { public void Update() { } }
    public sealed class ConduitMode { public void Update() { } }
}
