---
name: oni-harness
description: 使用 OniHarness 测试缺氧 Mod、任意形状物理地图与建筑模拟、连通器流体平衡、Mod 安装测试及遥测数据采集，或通过 Steam 启动无窗口真实游戏。
---

# OniHarness 无头测试与物理仿真技能

先读取子项目中的[配套技能](../../../tools/OniHarness/.agents/skills/oni-harness/SKILL.md)，
所有测试命令均在 `tools/OniHarness` 根目录下执行。

子模块未初始化时，从 OniMods 根目录运行：

```sh
rtk git submodule update --init tools/OniHarness
```

## 核心测试能力

### 1. 连通器与任意几何流体模拟测试
支持测试连通器原理，验证液面高度和资源变化。
```sh
cd tools/OniHarness
python3 scripts/u_tube_experiment.py
# 自动生成 artifacts/u-tube-experiment/index.html 与 communicating_vessels_data.csv
```
或者运行声明式场景测试：
```sh
cd tools/OniHarness
dotnet run --project src/OniHarness.Cli -c Release --no-build -- \
  test examples/scenarios/u-tube-communicating-vessels.json
```

### 2. Mod 局部迭代

优先运行相关局部用例并复用构建产物。以下命令在 `tools/OniHarness` 下执行。

```sh
rtk proxy python3 scripts/test_mod.py --native-project ../../mods/CycleTrim/OniSim/native --suite liquid-unit
rtk proxy python3 scripts/test_mod.py --native-project ../../mods/CycleTrim/OniSim/native --changed-file src/liquid_transport.rs
rtk proxy python3 scripts/test_mod.py --mod /path/to/MyMod.dll --assembly /path/to/MyMod.Tests.dll --filter RelevantCase --skip-build
rtk proxy python3 scripts/verify.py --only local
```

原生入口使用 Cargo 增量构建，分别报告构建与用例执行时间。测试组由项目的 `oniharness.json` 定义。`test_mod.py --headless` 当前明确报告未实现实机执行。用户要求仅局部测试时持续保持游戏关闭。

### 3. 任意形状与建筑地图
- 支持 `polygonRegions`（多边形区域）、`pointsRegions`（离散点集）、`asciiMap` & `legend`（字符画二维网格矩阵）。
- 支持 `buildings`（`Tile`、`InsulatedTile`、`MeshTile`、`ManualPressureDoor` 等建筑物理映射）。

### 4. 数据采集与时序遥测
通过 `SimulationDataCollector` 实时采集：
- 各测点探针的质量（Mass）与温度（Temperature）
- 各列自由液面高度（Column Liquid Level）
- 各监控区域液体总质量（Region Mass）
- 一键导出为结构化 CSV 与 JSON 文件。

---

需要完整游戏行为时，读取子项目的 `docs/runtime.md`，通过 Steam 启动真实 ONI，保留原生模拟。
默认无头启动必须验证 batch mode、Null 图形设备、零相机绘制；失败不能改为图形启动。
保持原生模拟、独立 .NET 宿主、静态元数据和游戏内运行的证据边界。
