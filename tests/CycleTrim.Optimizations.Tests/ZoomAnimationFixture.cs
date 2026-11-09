using System.Runtime.CompilerServices;

// Synthetic animation receiver: observes delivered time and the original
// visibility/suspension gates. It does not load Unity or implement the animation
// system. RestartFromEventManager models a queued-animation reset between calls.
// The pinned 744825 UpdateAnim processes transforms before its hidden gate;
// hidden OffscreenUpdate controllers advance time, and forceRebuild bypasses
// that gate. None of these operations can be deferred by skipping UpdateAnim.
public sealed class KBatchedAnimController : UnityEngine.Object
{
    public int InstanceId;
    public bool isMovable;
    public bool IsMoving;
    public bool Visible = true;
    public bool OffscreenUpdates;
    public bool ForceRebuild;
    public bool TransformDirty;
    public bool Stopped;
    public bool AnimationPaused;
    public bool Suspended;
    public float Elapsed;
    public float LastDeltaTime;
    public int Updates;
    public int Rebuilds;
    public int TransformUpdates;
    public int SuspendRequests;
    public int AnimationStarts;

    public int GetInstanceID() { return InstanceId; }
    public bool IsVisible() { return Visible; }

    // AnimEventManager can call TriggerStop independently of UpdateAnim. The
    // queued-animation path resets elapsed time and resumes updates.
    public void RestartFromEventManager(float timeOffset)
    {
        Elapsed = timeOffset;
        Stopped = AnimationPaused = Suspended = false;
        AnimationStarts++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateAnim(float dt)
    {
        Updates++;
        LastDeltaTime = dt;
        if (TransformDirty)
        {
            TransformDirty = false;
            TransformUpdates++;
        }
        if (!ForceRebuild && (Stopped || AnimationPaused))
        {
            Suspended = true;
            SuspendRequests++;
        }
        if (!Visible && !ForceRebuild)
        {
            if (OffscreenUpdates && !Stopped && !AnimationPaused)
                Elapsed += dt;
            return;
        }
        if (!Stopped && !AnimationPaused)
            Elapsed += dt;
        if (ForceRebuild)
        {
            ForceRebuild = false;
            Rebuilds++;
        }
    }
}

public sealed partial class CameraController
{
    public static CameraController Instance = new CameraController();
    public float OrthographicSize;
}

public static partial class OverlayModes
{
    public sealed class TileMode { public void Update() { } }
    public sealed class ConduitMode { public void Update() { } }
}
