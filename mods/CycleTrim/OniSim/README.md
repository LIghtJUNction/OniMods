# OniSim：CycleTrim 的 Rust 仿真内核与 C# 兼容层

OniSim 是 CycleTrim 的原生仿真核心，依据本机官方 SimDLL 接口与可观测行为实现 Rust 版本。
它已纳入 CycleTrim 源码与构建工程；游戏接管、性能和 Mod 兼容性仍在验收，当前不能当作已经验证的替代内核。

当前内核仍是一格一物，气体排挤在 `native/src/gas.rs` 的 `displace_gas`。下个版本要实验的是另一套格子：固定组分质量、界面通量、无排挤搜索，并把该核放到 GPU 上。那不是本内核已经完成的替换，说明见 [下个版本：实验替换一格一物](../docs/next-cell-representation.md)。

当前源代码快照已接入 inline thermal command queue，47 项 Release Rust 测试通过。该结果只证明对应源码的单元测试；
不代表整局 FPS、RSS 或单/四线程加速。四线程整体性能门禁仍未通过；当前快照没有对应的真实游戏验收，旧二进制的运行回执不能替代。
详见 [PROGRESS.md](PROGRESS.md)。

质量搜索的 FIFO 去重及早期热／气体实验的整合范围，见
[Transport 整合记录](transport-lab/INTEGRATION-20261106.md)。历史实验与当前生产回归分别验证，
原始计时和退化结果保留在实验目录。

## 当前结构

```text
官方 C# / Mod 调用面 → 官方 C ABI → Rust 网格、命令与物理计算
                                      ↓
                         GameData 网格、事件、组件和贴图输出
```

- 直接使用官方消息布局，没有额外消息封包协议或 C# 全图镜像。
- 源码已包含气液、热传导、相变、疾病、辐射、质量组件和热量组件。
- 2026-11-07 静态路由审计在源码中找到全部 62 个官方消息 ID：59 个进入生命周期、世界、地块或组件处理器；`SetVisibleCells`、`ToggleProfiler`、`RadiationSickness` 这 3 个按固定版本官方派发注册表缺席证据作为 no-op。ID 被识别不等于行为已通过差分验证。
- `Elements_CreateInteractions` 已解析并保存规则，但 `do_gas_interaction` / `do_gas_interaction_pair` 目前没有固定步进调用点。对固定版本官方控制流的静态检查也没有找到旧三参数 helper 的入边；是否需要把它接入主循环尚未证实，不能仅凭函数存在就猜测调用时机。
- 原生存档、世界初始化、挖掘和质量命令、CPU 贴图输出已接线，行为对照继续扩展。
- 帧命令在封帧时移交所有权，按官方各阶段顺序处理。
- 调度已接入 Step 批次、双 GameData、ready 屏障与异步工作线程，完整调度等价性仍待验收。
  细节见 [FRAME-SYNC.md](FRAME-SYNC.md)。
- C# 独立调用层提供长度边界、错误检查、会话生命周期和借用指针视图。
  CycleTrim 的 PLib 设置使用新的 `EnableOniSimExperimental` 字段显式 opt-in，默认关闭；
  旧版 `EnableOniSim` 配置键不再读取，避免历史默认开启值在升级后继续接管。修改后需要重启。
  `CYCLETRIM_ONISIM_ENABLE=0` 可在启动前强制关闭。真实游戏完整兼容性尚未验收。
- 疾病发射范围和疾病生长后处理已接入最多 8 线程的常驻线程池，小任务保留串行；设置 `CYCLETRIM_ONISIM_WORKERS=1` 可关闭内核并行。验收与复现见 [PARALLEL-VALIDATION.md](PARALLEL-VALIDATION.md)。
- 疾病发射器的菱形遍历经过 Lean 4.19 证明和 65,040 个差分场景验证；该组件基准在记录的场景中快 13.894 倍，不代表整局性能提升。详见 [lean-lab/README.md](lean-lab/README.md)。

本轮实际运行结果与边界见 [RUNTIME-VALIDATION.md](RUNTIME-VALIDATION.md)。`gameReady=false`、`runtimeValidated=false`
保留到对应验收完成。支持某个消息编号也不代表其所有行为已验证。

## 来源边界

- 只依据本机官方 `libSimDLL.so` 的静态分析和 `Assembly-CSharp.dll`
  的 P/Invoke、序列化、结构布局与调用事实，实现独立 Rust 代码。
- 沿用 `leanrea` 方法；官方二进制与反编译实现只留在临时目录。
- 用户禁止读取社区源码之前，早期调查助手看过社区接口文件；该助手
  不参与实现。实现助手未继承那段上下文，只接收官方接口事实。
- 无社区 Rust SimDLL 源码、二进制或运行时依赖；未宣称完成独立来源审计。

## 按需阅读

| 内容 | 入口 |
| --- | --- |
| FFI 与指针生命周期 | [PROTOCOL.md](PROTOCOL.md) |
| 62 个官方消息及输入输出布局 | [OFFICIAL-MESSAGES.md](OFFICIAL-MESSAGES.md) |
| 帧批次、可见区域、组件输出时机 | [FRAME-SYNC.md](FRAME-SYNC.md) |
| 物理阶段与并行约束 | [SIMULATION.md](SIMULATION.md) |
| 原生存档格式 | [SAVE-FORMAT.md](SAVE-FORMAT.md) |
| C 调用声明 | [oni_sim.h](native/include/oni_sim.h) |
| 官方二进制指纹和 16 个导出 | [official-abi.json](official-abi.json) |

## 构建与验收边界

Linux x64，Rust 工程固定为 1.99.1 / Edition 2024，需要 .NET 10 SDK；独立兼容 DLL 目标为 netstandard2.0。

```sh
rtk proxy cargo +1.99.1 build --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --release
rtk dotnet build mods/CycleTrim/OniSim/managed/OniSim.Host/OniSim.Host.csproj -c Release
```

编译通过只证明源码和 ABI 边界可构建，不证明游戏行为、存档、DLC 或其他 Mod 兼容。
CycleTrim 在支持的 Linux x64 环境默认保留官方 SimDLL，不接管游戏物理管线。只有在 PLib 设置页
显式开启 `EnableOniSimExperimental` 并重启后才会尝试映射；`CYCLETRIM_ONISIM_ENABLE=0`
可在启动前强制关闭。在完成多场景、长时与原版对照验收前，不要把它当作已验证的原版替代品。

`development-build.json` 与 `first-run.json` 记录旧源码快照；退役的 OSB1 协议保存在 [history/](history/)。这些都是历史材料，不能代表当前源码。

## Rust 工具链与依赖

`rust-toolchain.toml` 固定 Rust 1.99.1，原生库与 Lean 对照工程均使用
2024 Edition、Cargo resolver 3，并禁止 unsafe 函数体内的隐式 unsafe 操作。
在 OniSim 目录下运行 Cargo 会自动使用该工具链；从仓库根目录运行时使用
`cargo +1.99.1`。CycleTrim 打包目标的 Cargo 工作目录也已对齐。

2026-10-21 使用新版 Cargo 联网更新两个锁文件的解析结果，均为 0 项升级：
Rayon 1.12.0 及其间接依赖已经是最新的 Rust 1.99 兼容版本。
迁移与验证记录见 `conformance/results/linux-744825-rust2024.json`；
内存专项结果见 [PERFORMANCE.md](PERFORMANCE.md)。新候选仍需独立游戏验收。
