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

        [Option("物流候选优化 / Pickup candidates",
            "复用拾取候选的路径成本；关闭后使用原版候选选择。需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool OptimizePickupCandidates { get; set; } = true;

        [Option("忙碌复制人刷新优化 / Busy duplicants",
            "减少忙碌复制人的重复差事刷新，空闲和明确优先更新仍立即处理。关闭后使用原版刷新。需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool ThrottleBusyDuplicants { get; set; } = true;

        [Option("静止小动物寻路优化 / Stationary critters",
            "减少静止小动物的重复寻路检查，移动时不降频。关闭后使用原版寻路检查。需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool ThrottleStationaryCritters { get; set; } = true;

        [Option("异步寻路去重 / Async path probes",
            "合并普通小动物的重复寻路工作并限制排队；复制人和不兼容的寻路能力仍走原版。需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool OptimizeAsyncPathProbes { get; set; } = true;

        [Option("小动物思考刷新优化 / Creature brains",
            "限制普通小动物重复思考的频率，紧急优先更新和复制人不受影响。关闭后使用原版调度。需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool LimitCreatureBrainUpdates { get; set; } = true;

        [Option("存档类型信息缓存 / Save type metadata",
            "复用组件类型的固定标记和名称，减少存档时重复反射；存档内容和写入顺序保持原样。需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool OptimizeSaveMetadata { get; set; } = true;

        [Option("电力视图查询复用 / Power overlay queries",
            "在单次电力视图刷新内复用同一电路的功率查询。电路重复少时可能增加开销，默认关闭；需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool OptimizePowerOverlay { get; set; }

        [Option("输送轨道路径查找 / Conveyor path lookup",
            "在单次输送轨道路径构建中复用重复查找的索引。小路径保留直接搜索；默认关闭，需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool OptimizeConveyorPathLookup { get; set; }

        [Option("动画重复更新优化 / Animation symbol updates",
            "动画覆盖数据逐位相同时保留版本号，减少重复的纹理数据复制。默认关闭，需要重启游戏。",
            "优化 / Optimizations")]
        [RestartRequired]
        public bool OptimizeAnimationSymbols { get; set; }

        [Option("性能诊断日志 / Performance diagnostics",
            "向 Player.log 输出耗时和内存统计，用于反馈问题。统计本身有额外开销，平时保持关闭。需要重启游戏。",
            "诊断 / Diagnostics")]
        [RestartRequired]
        public bool EnablePerformanceDiagnostics { get; set; }
    }
}
