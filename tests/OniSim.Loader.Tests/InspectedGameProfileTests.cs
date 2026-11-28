using OniSim.CycleTrim;

internal static class InspectedGameProfileTests
{
    internal static void Run()
    {
        var previous = InspectedGameProfile.ForLinuxBuild(744825);
        var current = InspectedGameProfile.ForLinuxBuild(763796);
        var updated = InspectedGameProfile.ForLinuxBuild(764961);
        Require(previous.GameBuild == 744825 && current.GameBuild == 763796 && updated.GameBuild == 764961,
            "Exact inspected builds must select their own profiles");
        Require(previous.AssemblySha256 != current.AssemblySha256 &&
            previous.MonoSha256 != current.MonoSha256,
            "The U60 profile must retain its distinct managed and runtime identities");
        previous.ValidateRuntimeDigests(previous.MonoSha256, InspectedGameProfile.SimulatorSha256);
        current.ValidateRuntimeDigests(current.MonoSha256, InspectedGameProfile.SimulatorSha256);
        Require(updated.AssemblySha256 != current.AssemblySha256 && updated.MonoSha256 == current.MonoSha256,
            "Build 764961 must pin its own assembly and the shared U60 Mono runtime");
        updated.ValidateRuntimeDigests(current.MonoSha256, InspectedGameProfile.SimulatorSha256);

        Reject(() => InspectedGameProfile.ForLinuxBuild(763797), "Unknown later build");
        Reject(() => InspectedGameProfile.ForLinuxBuild(0), "Missing build identity");
        Reject(() => previous.ValidateRuntimeDigests(current.MonoSha256,
            InspectedGameProfile.SimulatorSha256), "U59 assembly with U60 Mono");
        Reject(() => current.ValidateRuntimeDigests(previous.MonoSha256,
            InspectedGameProfile.SimulatorSha256), "U60 assembly with U59 Mono");
        Reject(() => updated.ValidateRuntimeDigests(previous.MonoSha256,
            InspectedGameProfile.SimulatorSha256), "764961 assembly with U59 Mono");
        Reject(() => current.ValidateRuntimeDigests(current.MonoSha256, new string('0', 64)),
            "Uninspected official simulator");
        Reject(() => current.ValidateRuntimeDigests(null, InspectedGameProfile.SimulatorSha256),
            "Missing Mono identity");
        Console.WriteLine("Inspected Linux profiles: exact runtime pairs pass; unknown builds, mixed runtimes and changed simulator rejected");
    }

    private static void Reject(Action action, string description)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, description + " was accepted");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
