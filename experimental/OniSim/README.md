# OniSim：独立 Rust 核心与 C# 兼容层

依据本机官方 SimDLL 接口和行为重写的实验项目，仍在开发。
**尚未完成游戏接管、整库性能测试和 Mod 兼容性验收，不能作为可用内核发布。**

2026-10-20 的后续实验已对疾病发射器的空间搜索实施局部优化：用无分配的
曼哈顿行区间替换全图标记和搜索队列。Lean 4.19.0 已检查 34 个相关定理；
原版与改进版源函数的独立测试、计时和证明范围见
[算法实验](lean-lab/README.md)。这里的组件实验不代表整库或
游戏验收；完整原生工程仍有交接快照留下的整合编译错误。

后续的[质量、温度与气体实验](transport-lab/README.md)增加了有序入队去重、
单遍热接触缓存和无菌气体分支，包含 27 个 Lean 定理和源函数差分测试。
它仍是候选：部分随机隔热及小额消耗场景有性能退化，不能无条件推广。
本轮使用 Rust 1.90.0，基线和候选均报告相同的 37 个原生整合错误；没有游戏验收。

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
  默认不启用的游戏加载入口正在开发；尚未验证它接管了任何游戏调用。

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
rtk cargo build --manifest-path experimental/OniSim/native/Cargo.toml --release
rtk dotnet build experimental/OniSim/managed/OniSim.Host/OniSim.Host.csproj -c Release
```

04:02 交接快照暂停时尚未运行模拟库、Host、测试、基准或游戏。本轮按用户的新
要求增加了疾病发射器的 Lean 证明和独立组件实验；完整核心与游戏验收仍待完成。
没有替换安装目录中的官方库。

`development-build.json` 是先前存储阶段的构建记录，不能代表当前源码。
`first-run.json` 是已退役 OSB1 原型的历史运行记录，也不适用于当前核心。
旧协议仅保存在 [history/OSB1.md](history/OSB1.md)。
