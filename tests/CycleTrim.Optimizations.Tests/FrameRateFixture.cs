using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// These small host methods expose the production patch signatures and record
// their inputs. They do not reproduce ONI's frame loop or simulation.
public sealed partial class Game
{
    private float simDt;
    public int FrameUpdateCalls;
    public int FrameLateUpdateCalls;
    public int StepTheSimCalls;
    public int SimEveryTickCalls;
    public float LastTickDt;
    public float LastSimulationBacklog;
    public Action FrameUpdateAction;

    public void SetSimulationBacklogForTest(float value) { simDt = value; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnSpawn() { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Update()
    {
        FrameUpdateCalls++;
        FrameUpdateAction?.Invoke();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LateUpdate() { FrameLateUpdateCalls++; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void StepTheSim() { StepTheSimCalls++; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SimEveryTick(float dt)
    {
        SimEveryTickCalls++;
        LastTickDt = dt;
        LastSimulationBacklog = simDt;
    }
}

public sealed class MainMenu
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnSpawn() { }
}

public sealed class GraphicsOptionsScreen
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ApplySettings() { }
}

namespace UnityEngine
{
    public static class QualitySettings
    {
        public static int vSyncCount = 1;
    }

    public static class Application
    {
        public static int targetFrameRate = 60;
    }

    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();

        public static void Log(object value) { Messages.Add(Convert.ToString(value)); }
        public static void LogWarning(object value) { Log(value); }
    }
}
