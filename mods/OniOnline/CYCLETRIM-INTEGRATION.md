# CycleTrim 接入调查

2026-10-20 对当前工作区做只读源码检查。CycleTrim/OniSim 存在其他任务的未提交改动，本轮没有修改它们，也没有执行或验证这些实现。以下结论仅限列出的源码边界，不代表全面 API 审计。

检查时仓库 HEAD 为 `0ee3001e891ec96f696e1e71fbf9bfdf9e5cfaf1`，它不包含整个当前脏工作区。通过 `rtk proxy python` 读取文件并计算 SHA-256，记录被检查文件的内容指纹：

```text
d66958c4203ed2f36f33f5165f08972e167f3c9a3254d1073c59c8a463c8ed31  native/include/oni_sim.h
029a292391441b73b630199acabd3cc64cc275b4c182cdd3e9ac1881c563bc52  managed/OniSim.Compatibility/OniSimSession.Frames.cs
464bc273212f89fb78debd0ac9ace3c0bfe0f35ae9b0d4013b545ce4d3f223b8  managed/OniSim.Compatibility/GridImage.cs
3470fc7fa6ad892300b86a47d13a53fbba4c29ab558ba6b6bc9b5df0588593e5  managed/OniSim.Compatibility/OniSimSession.Save.cs
```

以上路径相对于 `mods/CycleTrim/OniSim/`，仅用于后续识别源码漂移，不代表运行结果。

## 已读到的起点

| 源码 | 接口或类型 | 可以确定 | 尚不能确定 |
| --- | --- | --- | --- |
| `../CycleTrim/OniSim/native/include/oni_sim.h` | `ONISIM_GetCapabilities` | 有原生能力查询入口 | 是否有 Online 所需的世界级能力 |
| 同上 | `ONISIM_CopyGridImage` | 提供拥有独立缓冲区的原生网格映像复制 | 完整殖民地存档、区域增量、托管恢复 |
| `../CycleTrim/OniSim/managed/OniSim.Compatibility/OniSimSession.Frames.cs` | `NewFrame` / `ActiveRegion` | 输入区域坐标、时间、日照和辐射 | 未选世界的物理与 Unity 侧逻辑是否完全不运行 |
| 同上 | `Prepare` | 接收逐格可见性字节并发布网格视图 | 按接收玩家过滤网络数据、隐藏信息不外泄 |
| `../CycleTrim/OniSim/managed/OniSim.Compatibility/GridImage.cs` | `GridImage` | 网格尺寸、种子、逐格序列化数据 | 建筑、任务、小人状态和所有引用的恢复 |
| `../CycleTrim/OniSim/managed/OniSim.Compatibility/OniSimSession.Save.cs` | `Save` / `Load` | 原生 SIMSAVE 数据存取边界 | 跨电脑的完整游戏检查点、动态接管和全局时间一致性 |

原生返回指针有借用期，外部读取不能与变更重叠。联网时不能直接把原生指针交给异步发送队列；需要在明确同步点复制到自有数据。此处只是依据头文件的接口约束，未实现网络发送。

## 第一项需要证明的能力

选择两个实际 DLC 世界，只允许本机负责的世界推进，并证明另一个世界的气液热、小人 AI、任务、建筑和管网均不重复演算。仅从原生活动区域里移除一块区域不算完成，必须检查 Unity 侧逻辑。

先在可丢弃测试存档中通过 Steam 启动 ONI 验证，再通过真实双端局域网检查一端计算、一端显示。构建、接口存在、宿主测试和实际游戏运行分别记录，不能互相替代。

通过之后才依次验证：

1. 全世界数据的完整恢复，以及只给普通观察者发送授权数据。
2. 已提交公共时间步上的复制和可信托管。
3. 掉线、托管继续、原主人同步返回后交还计算权。
4. 火箭内部世界和落地小人的跨世界交接。

本调查未新增 CycleTrim 接口，也未执行这些模拟隔离和迁移验收。第一阶段已另行构建 OniOnline 通信 DLL，但房间与聊天不依赖完整模拟隔离能力，不构成上述接口的验收。本文件用于选定后续实际实验，不作为已完成接口的清单。
