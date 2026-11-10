using System;
using System.Runtime.CompilerServices;

// Signature doubles for the opt-in timing hooks. Their durations and call order
// are synthetic; they do not implement ONI or measure its gameplay performance.
public enum SimMessageHashes { PrepareGameData = 1078620451, Other = 1 }
public static class Sim
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IntPtr HandleMessage(SimMessageHashes message, int length, byte[] data)
    { return IntPtr.Zero; }
}
public sealed partial class Game
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UnsafeSim200ms(float dt)
    {
        Sim.HandleMessage(SimMessageHashes.PrepareGameData, 0, null);
        Sim.HandleMessage(SimMessageHashes.Other, 0, null);
        new JobManager().Run(new FrameChainWork());
        new JobManager().Run(new FetchManager.UpdateOffsetTables());
        new JobManager().Run(new FetchManager.UpdatePickupWorkItem());
        new JobManager().Run(new ChoreProvider.Work());
        new JobManager().Run(new PropertyTexturesJob());
        new JobManager().Run(new SimDebugViewJob());
    }
}
public interface IWorkItemCollection { void Run(); }
public sealed class FrameChainWork : IWorkItemCollection
{
    public void Run() { new StateMachineUpdater().AdvanceOneSimSubTick(); }
}
public sealed class JobManager
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run(IWorkItemCollection work) { work.Run(); }
}
public sealed class Global
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void LateUpdate() { }
}
public sealed class App
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void LateUpdate() { }
}
public sealed class StateMachineUpdater
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void AdvanceOneSimSubTick() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Render(float dt) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RenderEveryTick(float dt) { }
}
public sealed class KAnimBatchManager
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateDirty(int frame) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Render() { }
}
public sealed class Pathfinding
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateNavGrids(bool serial) { }
}
public sealed class AsyncPathProber
{
    public sealed class Manager
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void TickFrame() { }
    }
}
public sealed class KScreenManager
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { }
}
public sealed partial class CameraController
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { }
}
public sealed class GroundRenderer
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void Render(Vector2I min, Vector2I max, bool force) { }
}
public static class KFMOD
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void RenderEveryTick(float dt) { }
}

public sealed class FetchManager
{
    public sealed class UpdateOffsetTables : IWorkItemCollection { public void Run() { } }
    public sealed class UpdatePickupWorkItem : IWorkItemCollection { public void Run() { } }
}
public sealed class ChoreProvider
{
    public sealed class Work : IWorkItemCollection { public void Run() { } }
}

public sealed class PropertyTexturesJob : IWorkItemCollection { public void Run() { } }
public sealed class SimDebugViewJob : IWorkItemCollection { public void Run() { } }
