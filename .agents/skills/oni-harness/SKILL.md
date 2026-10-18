---
name: oni-harness
description: 使用 OniHarness 测试缺氧 Mod，选择不启动游戏的原生物理用例，或通过 Steam 启动真实游戏、减少渲染并可选观看 AI 的同一份文本地图。用于无头测试、后台游戏验证、渲染开销比较和文本观看。
---

# OniHarness 无头测试

先读取子项目中的[配套技能](../../../tools/OniHarness/.agents/skills/oni-harness/SKILL.md)，
按其中流程选择原生物理测试、C# 用例或明确启动游戏的验证流程。

子模块未初始化时，从 OniMods 根目录运行：

```sh
rtk git submodule update --init tools/OniHarness
```

技能里的命令从 `tools/OniHarness` 运行。需要完整游戏行为时，读取子项目的
`docs/runtime.md`，通过 Steam 启动真实 ONI，保留原生模拟并减少绘制。
文本观看开关和游戏渲染设置分开；它展示 AI 记录的原始地图，不读取截图。
游玩仍遵守 ONI 控制技能的暂停、观察、行动、短步运行和验证规则。

物理实验的图像来自数值状态回放，
不是游戏画面，也不代表已实现复制人、建筑生命周期或完整殖民地游玩。
保持原生模拟、独立 .NET 宿主、静态元数据和游戏内运行的证据边界。
