# OniMods 架构与核心实现原理

本文档系统介绍仓库内两大核心 Mod 的底层实现机制、设计取舍与运行原理：
- **`OniMcp`**：面向大模型（LLM）的智能体交互协议与虚拟化世界系统（AI 交互与控制层）
- **`CycleTrim`**：针对底层物理与行为模拟热点的轻量级性能优化 Mod（底层算力与稳定性保障）

---

## 目录

- [一、 双 Mod 协同体系](#一-双-mod-协同体系)
- [二、 OniMcp 原理：世界文本化与大模型交互](#二-onimcp-原理世界文本化与大模型交互)
  - [1. 为什么需要世界文本化？](#1-为什么需要世界文本化)
  - [2. 语义行程编码 (Semantic Runs)](#2-语义行程编码-semantic-runs)
  - [3. 单一坐标权威与消除歧义 (Disambiguation)](#3-单一坐标权威与消除歧义-disambiguation)
  - [4. 动态活动图例 (Active Legend Filtering)](#4-动态活动图例-active-legend-filtering)
  - [5. 空间 AABB 快速初筛裁剪](#5-空间-aabb-快速初筛裁剪)
  - [6. 虚拟文件系统与代码编辑隐喻 (World Editor)](#6-虚拟文件系统与代码编辑隐喻-world-editor)
  - [7. 严格安全控制环路 (Preflight & Safety Guard)](#7-严格安全控制环路-preflight--safety-guard)
- [三、 CycleTrim 原理：底层模拟热点削峰](#三-cycletrim-原理底层模拟热点削峰)
  - [1. 缺氧模拟的性能瓶颈分析](#1-缺氧模拟的性能瓶颈分析)
  - [2. 忙碌复制人任务感应节流 (Busy Duplicant Chore Throttle)](#2-忙碌复制人任务感应节流-busy-duplicant-chore-throttle)
  - [3. 条件弱引用状态表与反射短路 (ConditionalWeakTable)](#3-条件弱引用状态表与反射短路-conditionalweaktable)
  - [4. 寻路探针队列配额与削峰 (AsyncPathProber Quota)](#4-寻路探针队列配额与削峰-asyncpathprober-quota)
  - [5. 静态/沉睡生物脑休眠 (Stationary Critter Sleep)](#5-静态沉睡生物脑休眠-stationary-critter-sleep)
- [四、 架构对比与设计取舍](#四-架构对比与设计取舍)

---

## 一、 双 Mod 协同体系

在《缺氧》中实现长周期大模型自主代理（Autonomous Play）面临两大根本难题：
1. **交互瓶颈**：原版游戏信息极度碎片化，大模型直接读图代价高昂且缺乏精细坐标，直接倾倒全量网格 JSON 会瞬间爆满上下文。
2. **算力瓶颈**：游戏进行到大后期，全星体实体、动物与寻路逻辑导致单核 CPU 严重过载，造成卡顿掉帧，进而导致外部 AI 调用的时钟超时与失序。

`OniMcp` 与 `CycleTrim` 形成了互补的协同闭环：

```
┌────────────────────────────────────────────────────────┐
│                      外部 AI / 客户端                  │
└───────────────────────────┬────────────────────────────┘
                            │  Model Context Protocol (HTTP)
                            ▼
┌────────────────────────────────────────────────────────┐
│                        OniMcp                          │
│  • 将 2D 物理世界压缩为极低 Token 的语义行程与虚拟文件  │
│  • 确保坐标无歧义，消除幻觉图例，提供代码级编辑接口   │
│  • 严格执行：暂停 -> 观察 -> 规划 -> 预检 -> 验证环路 │
└───────────────────────────┬────────────────────────────┘
                            │  操控游戏实体与订单
                            ▼
┌────────────────────────────────────────────────────────┐
│                   Oxygen Not Included                  │
│                     原生游戏物理引擎                   │
└───────────────────────────▲────────────────────────────┘
                            │  Harmony 运行时补丁
┌───────────────────────────┴────────────────────────────┐
│                       CycleTrim                        │
│  • 截断忙碌小人的无用感应计算，缓存 ChoreConsumer 引用 │
│  • 动态控制寻路探针配额，休眠静止生物行为树            │
│  • 为 AI 长期自主游玩提供平稳帧率与低延迟环境          │
└────────────────────────────────────────────────────────┘
```

---

## 二、 OniMcp 原理：世界文本化与大模型交互

### 1. 为什么需要世界文本化？
《缺氧》拥有复杂的网格系统（默认每个星体宽度数百格，每个格子包含实体、温度、气压、质量、病菌、铺设管线等）。如果直接输出网格 JSON，一个 40×40 的小房间需要 1600+ 个对象，占用数万 Token；如果输出 ASCII 像素画，既无法表达多层物理信息（如气体中的电线与管道），又充斥无意义空格。

OniMcp 的世界文本化将 2D 实体网格转换为**语义行与结构化切片**。

### 2. 语义行程编码 (Semantic Runs)
在视口扫描中，横向连续同质的单元格被压缩为一个“行程”（Run）：
- 格式定义：`x=X1..X2:`token``（单格为 `x=X:`token``）
- 示例：
  ```markdown
  | Y | RY | Runs |
  |---|---:|---|
  | `102` | `2` | x=14..18:`tile`; x=19:`bld_anchor`; x=20..22:`bld`; x=23..30:`oxy` |
  ```
- **成效**：
  - Token 开销由网格维度的 $O(W \times H)$ 降至语义连续区段的 $O(\text{Runs})$，平均节省 **70%~90%** 的 Token。
  - 行程内保留高层抽象符号（`bld_anchor` 建筑基准点、`bp` 施工蓝图、`tile` 砖块、`oxy` 氧气）。

### 3. 单一坐标权威与消除歧义 (Disambiguation)
早期文本化实现中包含 `x=14..18 rx=0..4` 等相对坐标字段，实践证明大模型经常发生“坐标系混淆”，将相对视口偏移 `rx` 当作绝对坐标下达挖掘指令，造成越界空挥或破坏重要设施。
- **全局权威坐标**：文本地图表格全面废除 run 内连续的 `rx=` 重复输出，仅在表头标注视口原点，正文 Run 统一使用绝对坐标。
- **稀疏模式去冗余**：稀疏运行表格（`sparseRuns`）剔除 `RX Range` 列，防止并列多列数值造成模型视觉注意力错位。

### 4. 动态活动图例 (Active Legend Filtering)
传统静态图例无论当前区域有何元素，均倾倒全量 14~20 行定义（包含氢气、高危氯气、极低温气体等）。
- **动态追踪**：扫描网格单元时通过 `observedTokens` 收集实际出现的 Token。
- **按需渲染**：图例表格仅输出当前视野中真实存在的标记，彻底杜绝模型因看到图例条目而臆测当前区域有毒气或特殊物料的幻觉（Hallucination）。

### 5. 空间 AABB 快速初筛裁剪
在生成建筑与蓝图覆盖层时，建筑的地基支撑（`MissingSupportCells`）与空间重叠（`FootprintObstructions`）计算开销昂贵。
- **包围盒剪枝**：在对星体全量建筑调用分析算法前，先执行视口 AABB 快速相交初筛（`objX < minX || objX > maxX || objY < minY || objY > maxY`），跳过 90% 以上与当前视口无关的建筑。

### 6. 虚拟文件系统与代码编辑隐喻 (World Editor)
OniMcp 的 `world_editor` 将整个世界状态映射为一组虚拟 Markdown 文件：
- `/active/infrastructure/power.md`：电力连通性拓扑
- `/active/infrastructure/liquid_conduits.md`：水管网络
- `/active/buildings/instances/...`：具体建筑的参数与运行配置
- `/active/map/cell_X_Y.md`：单格跨层快照
大模型无需记忆海量专属 API，而是通过最擅长的代码重构能力（`read` 配合 `SEARCH/REPLACE` 块）提出规划与管线布线修改。

### 7. 严格安全控制环路 (Preflight & Safety Guard)
为防止 AI 执行破坏性操作，OniMcp 强制执行防作弊与安全预检：
1. **硬编码安全策略**：禁止沙盒强制放置、瞬间建造与实体无成本生成。
2. **前置预检 (Dry-Run)**：建筑规划与指令下达优先使用 `dryRun=true` 验证地基、材料存量与路径可达性。
3. **闭环节奏**：AI 操控遵守 `pause (暂停) -> observe (观测) -> plan (规划) -> dryRun (预检) -> execute (执行) -> resume (短恢复) -> pause (再次暂停) -> verify (验证)` 节拍。

---

## 三、 CycleTrim 原理：底层模拟热点削峰

### 1. 缺氧模拟的性能瓶颈分析
《缺氧》模拟每帧都需要更新大量系统。大后期殖民地性能急剧劣化的主要热点包括：
1. **Duplicant Chore Sensors**：无论复制人是否在执行任务，传感器系统每 tick 都在重新评估全图搬运池。
2. **Component Lookup 开销**：C# 托管层与 Unity 原生层之间的 `GetComponent` 密集查询。
3. **寻路风暴 (NavGrid Churn)**：几十只小动物同时计算不可达目标导致的异步寻路队列暴涨。

### 2. 忙碌复制人任务感应节流 (Busy Duplicant Chore Throttle)
- **核心观察**：当复制人正处于长周期确定性任务中（如长途跋涉搬运、持续建造、持续挖掘），绝大多数情况不需要在下一 tick 强行打断并重新分配相同的常规任务。
- **行为保持**：一旦小人变成空闲（Idle）、任务被打断、或优先级发生主动改变，节流立即失效，无缝恢复原版排程敏感度。

### 3. 条件弱引用状态表与反射短路 (ConditionalWeakTable)
- **痛点**：在传感器补丁中每次调用 `sensor.GetComponent<ChoreConsumer>()` 会在高频调用下累积严重的 CPU 耗时。
- **实现机制**：
  ```csharp
  // 维护传感器与状态对象的弱引用绑定，不阻碍垃圾回收
  private static readonly ConditionalWeakTable<PickupableSensor, State> SensorStates =
      new ConditionalWeakTable<PickupableSensor, State>();
  ```
  在 `State` 中缓存解析好的 `ChoreConsumer` 引用，使后续数十万次的高频检查完全避开反射与原生组件查询，纯粹在托管内存完成。

### 4. 寻路探针队列配额与削峰 (AsyncPathProber Quota)
- 异步寻路探针在高密度基地下容易积压成千上万个工单。
- CycleTrim 对后台 Worker 分发实施滑动窗口配额，按在途排队数量做自适应背压削峰，防止突发大范围不可达搜索抢占主渲染线程。

### 5. 静态/沉睡生物脑休眠 (Stationary Critter Sleep)
- 当小动物处于被束缚、在捕获点等待搬运或夜间休眠时，其空间坐标处于静态锁定。
- 补丁将其高频的地面导航探测与行为树刷新挂起，直至受到外部物理扰动或状态变更事件唤醒，**单项热点耗时下降 90% 以上**。

---

## 四、 架构对比与设计取舍

| 核心维度 | OniMcp | CycleTrim |
|---|---|---|
| **层次** | 应用层 / 外部通信协议 | 核心模拟层 / 引擎补丁 |
| **工作介质** | HTTP、JSON-RPC、Markdown 文本 | C# IL 字节码、Harmony 运行时 Patch |
| **优化目标** | **降低上下文 Token 消耗，消除 LLM 幻觉与歧义** | **降低 CPU 占用，消除掉帧与单帧卡顿（Stutter）** |
| **安全准则** | 零作弊、沙盒隔离、必须经过 Preflight 验证 | 严格兼容原版关键行为，仅在确定性无影响路径下优化 |
| **验证体系** | 静态契约检查、无头 MCP 协议套件、离线格式断言 | 合成微基准、ILSpy 反编译断言、目标 DLL 契约验证 |

---

## 五、 现代浏览器自动化驱动的高效控制范式 (Browser-Inspired & CodeMode)

为了解决传统基于 LLM 的游戏控制中“频繁乒乓轮询、盲目休眠、进度缓慢、Token 开销巨大”的痛点，OniMcp 深度借鉴了现代浏览器自动化框架（**Playwright**、**Chrome DevTools Protocol**）的核心设计模式：

### 1. 目标驱动条件推进 (`step_until`，对标 Playwright `waitForCondition` 与游戏自动化硬件报警)

事实读取（`step_facts`）、原子条件与 `all` / `any` 组合（`until`）、有时限推进并暂停（`step_until`）分别负责观察、表达和执行。具体暂停策略交给 Agent；例如全员空闲只是可选条件。完整语义与示例见 [可组合推进条件](composable-game-waits.md)。
- **淘汰盲目休眠**：严禁无目的的 `resume -> sleep(10s) -> pause`（复制人跑路需要十几秒，固定休眠导致 LLM 频繁空转轮询）。
- **事件驱动快进**：通过 `game_control domain=speed action=step_until` 指定终止条件（`condition: "orders_complete" | "dupe_idle" | "cycle_end" | "print" | "research" | "alarm" | "alarm:<name>" | "notify:<keyword>" | "notify:good" | "notify:bad"` 等），游戏以 **3x 快进倍速** 持续模拟。
- **全方位原生通知拦截（HUD Notifications Hook）**：全面接管缺氧游戏内的 `NotificationScreen` 与 `NotificationManager`。不仅支持打印舱就绪（`condition: "print"`）与科技研究完毕（`condition: "research"`）这类里程碑瞬时唤醒，还支持按通知类型（`notify:good` / `notify:bad` / `notify:message` / `notify:event`）或按关键字（如管道堵塞 `notify:pipe`、建筑故障 `notify:broken`、饥饿 `notify:starv`）进行模糊拦截。
- **自动化告警硬件联动（Hardware Alarm Hook）**：充分利用缺氧强大的自动化电路（传感器 + 自动化通知器 `LogicAlarm`）。Agent 只需建造自动化通知器并命名（如 `WaterFull` 或 `TempNormal`），即可直接运行 `step_until condition="WaterFull"`。信号线变绿瞬间，主线程毫秒级捕获通知器事件并自动暂停返回，达到对标 Webhook / 硬件中断级别的超高效率。
- **原生威胁自动拦截**：无论当前设置何种条件，只要出现复制人窒息、致命伤、受困红警或 `NotificationType.DuplicantThreatening`，引擎立即强制刹车暂停并返回威胁告警，杜绝挂机致死。
- **自动暂停与截断**：主线程在 Unity `Update()` 每帧监听目标达成或异常告警，一旦达成立即触发安全暂停，避免挂机空跑或灾难蔓延。

### 2. 增量感知与执行报告 (`ExecutionDelta`，对标 DOM Mutation Diff)
- **拒绝全量 Dump**：推进结束后不重新序列化数千 Token 的完整地图，而是直接返回仅百余 Token 的精炼差异摘要（包含新增 HUD 通知、告警、空闲复制人与工单状态）：
  ```json
  {
    "stopReason": "notification:Research Complete: Basic Farming",
    "condition": "research",
    "elapsedGameSeconds": 48.5,
    "elapsedWallSeconds": 13.2,
    "startCycle": 10,
    "endCycle": 10,
    "remainingTargetOrders": 0,
    "idleDuplicants": [],
    "alerts": [],
    "alarms": [],
    "notifications": [
      { "title": "Research Complete: Basic Farming", "type": "Good", "isNew": true, "isAlarm": false, "notifier": "ResearchCenter" }
    ],
    "newNotificationCount": 1,
    "immigrantsAvailable": false,
    "isPaused": true
  }
  ```
- LLM 凭借增量即可确认工单达成，节省 **85%+ 上下文开销**。

### 3. 原生 CodeMode 流程支持 (`domain=code` / `domain=program`)
- **单回合闭环控制**：Agent 无需在聊天对话中反复来回发起单个工具调用，可直接以代码/流程形式将“工单下达 -> 目标快进 -> 结果返回”组合为原子脚本：
  ```json
  {
    "domain": "code",
    "action": "execute",
    "code": [
      { "tool": "orders_control", "args": { "domain": "dig", "action": "line", "x1": 20, "y1": 20, "x2": 25, "y2": 20 } },
      { "tool": "game_control", "args": { "domain": "speed", "action": "step_until", "condition": "orders_complete", "speed": 3, "maxGameSeconds": 60 }, "saveAs": "delta" },
      { "return": "$delta" }
    ]
  }
  ```
- 该架构实现了单回合内游戏时间推进数十秒、工单全量交付的极高自主游玩效率。

