using PeterHan.PLib.Options;

namespace CycleTrim
{
    [ConfigFile("CycleTrimOptions.json", true)]
    public sealed class OniSimOptions
    {
        [Option(
            "Use OniSim simulation core / 使用 OniSim 仿真核心",
            "EXPERIMENTAL: allow OniSim only with CYCLETRIM_ONISIM_ENABLE=1 set before launch. Use disposable saves. Restart required. / 实验性功能：只有启动前设置 CYCLETRIM_ONISIM_ENABLE=1 才允许启用 OniSim。仅使用测试存档，修改后需重启。",
            "Simulation / 仿真")]
        [RestartRequired]
        public bool EnableOniSim { get; set; } = false;
    }
}
