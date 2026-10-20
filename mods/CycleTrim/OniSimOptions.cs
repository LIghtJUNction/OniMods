using PeterHan.PLib.Options;

namespace CycleTrim
{
    [ConfigFile("CycleTrimOptions.json", true)]
    public sealed class OniSimOptions
    {
        [Option(
            "Use OniSim simulation core / 使用 OniSim 仿真核心",
            "Use CycleTrim's native simulation core for the game's SimDLL imports. Changes require restarting ONI. / 使用 CycleTrim 原生仿真核心替代游戏的 SimDLL；修改后需要重启游戏。",
            "Simulation / 仿真")]
        [RestartRequired]
        public bool EnableOniSim { get; set; } = true;
    }
}
