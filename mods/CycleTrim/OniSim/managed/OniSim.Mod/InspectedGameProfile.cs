using System;

namespace OniSim.CycleTrim
{
    // Each Linux profile binds one inspected game/runtime pair. Do not combine
    // independently accepted assembly and Mono digests across game releases.
    internal sealed class InspectedGameProfile
    {
        internal static readonly InspectedGameProfile LinuxU59 = new InspectedGameProfile(
            744825,
            "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1",
            "60bf98b369d3a48cd93fab678dc8c6d37f61adea00f942a59e418d1d9db6bf44");
        internal static readonly InspectedGameProfile LinuxU60 = new InspectedGameProfile(
            763796,
            "801207a0e194ad11892c86af754ad4d3218d89de3a079c2179e24723947135d4",
            "ef78998d272456345b025b41c71bd3fd294da19d963e68f1a1e00c907eacb20b");
        internal static readonly InspectedGameProfile LinuxU60Build764961 = new InspectedGameProfile(
            764961,
            "51aafd6c2857e997daaf154ea0ac0f4db9b4520fb8872bc92333f9086e933d9a",
            LinuxU60.MonoSha256);

        // The shipped Linux simulator is byte-identical across these inspected builds.
        internal const string SimulatorSha256 =
            "85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d";

        internal int GameBuild { get; private set; }
        internal string AssemblySha256 { get; private set; }
        internal string MonoSha256 { get; private set; }

        private InspectedGameProfile(int build, string assemblySha256, string monoSha256)
        {
            GameBuild = build;
            AssemblySha256 = assemblySha256;
            MonoSha256 = monoSha256;
        }

        internal static InspectedGameProfile ForLinuxBuild(int build)
        {
            if (build == LinuxU59.GameBuild) return LinuxU59;
            if (build == LinuxU60.GameBuild) return LinuxU60;
            if (build == LinuxU60Build764961.GameBuild) return LinuxU60Build764961;
            throw new InvalidOperationException("No inspected Linux OniSim profile exists for game build " + build + ".");
        }

        internal void ValidateRuntimeDigests(string monoSha256, string simulatorSha256)
        {
            if (!string.Equals(monoSha256, MonoSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("Unity Mono runtime does not match the inspected game build " + GameBuild + ".");
            if (!string.Equals(simulatorSha256, SimulatorSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The official simulator does not match the inspected game build " + GameBuild + ".");
        }
    }
}
