using System.Runtime.CompilerServices;

// Synthetic animation receiver: observes only the elapsed time delivered by
// the production prefix, without loading Unity or implementing ONI animation.
public sealed class KBatchedAnimController : UnityEngine.Object
{
    public int InstanceId;
    public bool isMovable;
    public bool IsMoving;
    public bool Visible = true;
    public float Elapsed;
    public int Updates;

    public int GetInstanceID() { return InstanceId; }
    public bool IsVisible() { return Visible; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateAnim(float dt)
    {
        Elapsed += dt;
        Updates++;
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
