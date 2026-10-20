# OniSim：CycleTrim 的 Rust 仿真内核与 C# 兼容层

OniSim 是 CycleTrim 的原生仿真核心，依据本机官方 SimDLL 接口与可观测行为实现 Rust 版本。
它已纳入 CycleTrim 源码与构建工程；游戏接管、性能和 Mod 兼容性仍在验收，当前不能当作已经验证的替代内核。

## 当前结构

```text
官方 C# / Mod 调用面 → 官方 C ABI → Rust 网格、命令与物理计算
                                      ↓
                         GameData 网格、事件、组件和贴图输出
```

- 直接使用官方消息布局，没有额外消息封包协议或 C# 全图镜像。
- 源码已包含气液、热传导、相变、疾病、辐射、质量组件和热量组件。
- 原生存档、世界初始化、挖掘和质量命令、贴图输出正在统一接线。
- 帧命令在封帧时移交所有权，按官方各阶段顺序处理。
- 调度正在恢复官方 Step 批次、双 GameData、ready 屏障与异步工作线程。
  细节见 [FRAME-SYNC.md](FRAME-SYNC.md)。
- C# 独立调用层提供长度边界、错误检查、会话生命周期和借用指针视图。
  CycleTrim 的 PLib Mod 设置默认启用 OniSim，可在游戏 Mod 设置页关闭，重启后生效；
  `CYCLETRIM_ONISIM_ENABLE=0` 也可在启动前强制关闭。真实游戏接管仍未验证。
- 疾病发射器的菱形遍历经过 Lean 4.19 证明和 65,040 个差分场景验证；该组件基准在记录的场景中快 13.894 倍，不代表整局性能提升。详见 [lean-lab/README.md](lean-lab/README.md)。

以上是实现进度，不是运行结果。`gameReady=false`、`runtimeValidated=false`
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

Linux x64，需要 Rust 和 .NET 10 SDK；独立兼容 DLL 目标为 netstandard2.0。

```sh
rtk cargo build --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --release
rtk dotnet build mods/CycleTrim/OniSim/managed/OniSim.Host/OniSim.Host.csproj -c Release
```

编译通过只证明源码和 ABI 边界可构建，不证明游戏行为、存档、DLC 或其他 Mod 兼容。
CycleTrim 在支持的 Linux x64 环境默认尝试使用 OniSim；可在 PLib Mod 设置页关闭并重启，或设置
`CYCLETRIM_ONISIM_ENABLE=0` 在启动前强制关闭。在完成隔离存档的真实游戏验收前，
不要把它当作已验证的原版替代品。

`development-build.json` 与 `first-run.json` 记录旧源码快照；退役的 OSB1 协议保存在 [history/](history/)。这些都是历史材料，不能代表当前源码。
