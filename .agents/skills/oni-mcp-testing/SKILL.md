---
name: oni-mcp-testing
description: 当用户要求测试、验证或审计 ONI MCP 服务器功能、连接状态、工具可用性，或评估 MCP 操作体验和易用性时使用。指导 agent 执行系统化的功能测试流程，覆盖连接、查询、地图、建造、指针、订单等核心能力，并生成测试报告。
---

# ONI MCP 功能测试

## 何时使用

- 用户说「测试 MCP」、「看看 MCP 好不好用」、「验证连接」
- 用户要求审计 ONI MCP 服务器状态、工具覆盖或操作体验
- 新环境/新存档首次连接后，快速验证 MCP 是否正常工作
- 排查 MCP 工具调用失败、数据异常或行为不符合预期

## 测试原则

- **只读优先**：前两个阶段全部使用只读工具，不修改游戏状态
- **dryRun 先行**：所有写/执行操作必须先 dryRun，确认通过再执行
- **最小侵入**：实际建造/挖掘测试选择影响最小的位置和规模
- **重点倾斜**：适当精简基础工具测试，重点考核「地图易于理解性」和「规划难度」

## 测试流程

### 阶段 1: 连接与基础状态（只读）

验证服务器连接和游戏基本状态：

```
server_status              → 服务器加载状态、端口、工具数量
game_time                  → 游戏时间、周期、暂停状态
colony_state_snapshot      → 殖民地完整快照（替代 6 个独立调用）
```

检查项：
- [ ] `server_status.loaded` 为 true
- [ ] `toolCount` > 0（正常应数百个）
- [ ] `game_time` 返回有效周期和时间
- [ ] `colony_state_snapshot` 返回复制人、食物、建筑、研究、告警

### 阶段 2: 关键信息查询（只读，精简版）

并行测试对规划决策最关键的信息工具，跳过非核心查询：

```
power_summary includeDetails=true   → 重点检测未接入电路的设备
dupes_status_check                  → 复制人导航可达性、被困风险
buildings_search_defs query=厕所     → 中文查询 + 材料可用性
```

检查项：
- [ ] `power_summary` 发现未连接设备（如 circuitId=-1）为加分项
- [ ] `dupes_status_check` 无被困风险，导航样本合理
- [ ] `buildings_search_defs` 中文关键词可用，返回 `placement.anchor` 规则

### 阶段 3: 地图易于理解性（只读，重点）

这是测试核心。重点验证地图输出是否真正「可读、可规划、可决策」：

```
world_text_map
  view=base, includeBuildings=true, includeItems=true, format=markdown
  → 测试连续区段地图的可读性、图例完整性、对象标注精度

world_text_map
  view=temperature, format=markdown
  → 测试温度视图是否能快速识别 hot/mild/cold 区域

world_area_snapshot
  preset=utilities, overlays=[power,gas_conduits,liquid_conduits,logic]
  compact=true
  → 测试多 overlay 信息密度、紧凑模式实用性、冲突诊断输出
```

检查项（地图可读性）：
- [ ] 连续区段式地图比像素网格更易读，行列坐标清晰
- [ ] 图例覆盖 `bld`/`dup`/`itm`/`tile`/`sol`/`bp`/`bp_a` 等全部标记
- [ ] 对象列表含 `anchor`/`footprint`/`size`，多格建筑边界明确
- [ ] 温度视图按区段标注 `hot`/`mild`/`cold`，无大面积混淆
- [ ] `compact=true` 下对象表省略空字段，信息密度高但不丢失关键数据

检查项（诊断价值）：
- [ ] 自动检测 `unsupported` 建筑（缺失支撑格坐标列出）
- [ ] 自动检测 `overlap` 冲突（列出具体冲突对象，如 tile@x,y 或 building:Door@x,y）
- [ ] 电力 overlay 正确区分 `w`(电线) / `p`(电力设备) / `.`(空)
- [ ] 逻辑 overlay 为空时简洁输出，不冗余

### 阶段 4: 规划难度（dryRun + 分析，重点）

测试 MCP 是否能有效降低殖民地规划的认知负担：

```
layout_candidates purpose=barracks limit=5
  → 评分、分类、需挖掘/铺砖量、危险格、可达性

layout_candidates purpose=bathroom limit=5
  → 不同用途的候选推荐是否差异化

build_preview prefabId=Outhouse x=... y=... dryRun=true
  → OnFloor 支撑检测（missingSupportCells）

build_preview prefabId=Door x=... y=... dryRun=true
  → 多格建筑阻碍检测（solid_cell + 已有建筑）

build_preview prefabId=Tile x=... y=... dryRun=true
  → Tile 规则与空位检测
```

检查项（候选规划）：
- [ ] `layout_candidates` 返回 `score` 和 `scoreExplanation`，扣分逻辑透明
- [ ] 候选分类（`open_ready`/`mixed_platform` 等）能帮助快速决策
- [ ] `requiredDig`/`requiredTiles`/`existingSupportCells` 量化改造工作量
- [ ] `hazardCells` 标注液体/危险元素坐标，避免盲目挖掘

检查项（建造预检复杂度）：
- [ ] `build_preview` 对 OnFloor 建筑返回 `missingSupportCells` 具体坐标
- [ ] 对被阻挡位置返回多类阻碍：`solid_cell` / `building` / `blueprint`
- [ ] 多格建筑 footprint 与 anchor 规则一致（lower-left cell）
- [ ] `material=auto` 自动选择当前星球可用材料

### 阶段 5: 批量与区域规划（dryRun / 轻量执行）

测试大范围规划的基础设施：

```
area_define x1=... y1=... x2=... y2=... label=plan_zone
area_blocks blockWidth=20 blockHeight=20 label=plan_blocks
area_merge areaIds=[blk1,blk2]
  → 区域定义、切块、合并的连贯性

build_area prefabId=Tile anchors=[[x1,y1],[x2,y2],[x3,y3]] dryRun=true
  → 批量 anchor 预检，验证整批原子性（全过或全拒）

orders_dig_area areaId=... dryRun=true detail=true
orders_sweep_area areaId=... dryRun=true detail=true
  → 区域级订单预览，检测风险与 previewToken 复用
```

检查项：
- [ ] `area_define` / `area_blocks` / `area_merge` 生成的 areaId 可在后续工具复用
- [ ] `build_area` 多 anchor 遇冲突时 `committed=false`，不残留部分蓝图
- [ ] `orders_dig_area` 返回目标元素、质量、温度、风险列表
- [ ] `previewToken` 支持复用，避免重复扫描

### 阶段 6: 指针与相机控制（精简版）

合并为少量调用，验证可用即可：

```
agent_pointer_get
agent_pointer_aim_cell x=... y=...
agent_pointer_select_tool tool=build prefabId=Ladder material=auto
agent_pointer_jump_point_set code=p1
agent_pointer_say message="MCP 规划测试运行中"
```

检查项：
- [ ] 指针定位、工具切换、跳转点、气泡消息正常

### 阶段 7: 批量调用与游戏控制（可选）

```
tools_call_many responseMode=summary
  items:
    - { t: agent_pointer_get, a: {} }
    - { t: buildings_search_defs, a: { query: 梯子, limit: 3 } }
```

检查项：
- [ ] 批量调用成功，`requireAllValid=true` 正确工作

### 阶段 8: 实际建造验证（可选）

如果前 7 阶段全部通过，可选执行一次小规模建造验证：

```
build_preview prefabId=Ladder x=... y=... dryRun=true
agent_pointer_aim_cell x=... y=...
agent_pointer_select_tool tool=build prefabId=Ladder material=auto
agent_pointer_hold_left direction=down length=3 confirm=true
world_text_map x1=... y1=... x2=... y2=...
  → 验证拖拽建造后地图是否正确显示 bp 标记
```

规则：
- 只建造影响极小的建筑（如梯子）
- 优先使用 `build_preview` 已验证通过的位置
- 建造后必须验证地图 `bp` / `bp_a` 标记
- 如果用户未明确要求，可以跳过此阶段

## 测试报告模板

每轮测试后返回简洁报告，重点评价「地图易于理解性」和「规划难度」：

```
## ONI MCP 测试报告

### 连接状态
| 项目 | 结果 |
|------|------|
| 服务器加载 | ✅/❌ |
| 工具数量 | N 个 |
| 游戏状态 | 第 X 周期，暂停/运行 |

### 功能测试结果
| 类别 | 状态 | 备注 |
|------|------|------|
| 基础查询 | ✅/❌ | |
| 地图可读性 | ✅/❌ | |
| 地图诊断价值 | ✅/❌ | unsupported/overlap/overlay |
| 规划候选质量 | ✅/❌ | layout_candidates 评分与分类 |
| 建造预检复杂度 | ✅/❌ | 多格/支撑/阻碍检测 |
| 批量与区域 | ✅/❌ | 原子性、areaId 复用 |
| 指针/相机 | ✅/❌ | |
| 实际建造 | ✅/❌/跳过 | |

### 发现的问题
- （如有）

### 总体评价
| 维度 | 评分 | 说明 |
|------|------|------|
| 易用性 | ⭐x/5 | |
| 安全性 | ⭐x/5 | |
| 地图可读性 | ⭐x/5 | 连续区段、图例、overlay 清晰度 |
| 规划难度 | ⭐x/5 | 候选评分、预检诊断、批量原子性 |
```

## 常见问题排查

| 现象 | 可能原因 | 排查步骤 |
|------|---------|---------|
| `server_status.loaded=false` | 游戏未运行或模组未加载 | 确认游戏已启动，检查模组是否启用 |
| 工具调用返回 `not found` | 工具名拼写错误或不在核心列表 | 使用 `tools_search` 查找正确工具名 |
| `build_preview` 坐标偏移 | anchor 是 lower-left cell，非中心 | 核对 `buildings_search_defs` 的 placement.anchor 说明 |
| `world_text_map` 与 `build_area` 坐标不一致 | 不同工具可能使用不同坐标基准 | 以 `build_area` 返回的 `actualAnchor` 和 `placementCheck` 为准 |
| `colony_state_snapshot` 告警未更新 | 建筑只是蓝图，未完工 | 蓝图不等于完工建筑；等复制人建造后重新读取 |
| 中文查询无结果 | 查询词不在本地化数据中 | 尝试英文关键词或 `category` 过滤 |
| `orders_dig_area` 风险提示不全 | 只检测明显风险，不保证全面 | 结合 `world_text_map` 人工确认液体/高温/真空相邻 |
| `layout_candidates` 评分偏低 | 区域含大量危险格或需大量挖掘 | 检查 `hazardCells` 和 `requiredDig`，更换 purpose 或扩大区域 |

## 快速测试路径（压缩版）

如果时间有限，只测核心链路：

```
server_status
game_time
colony_state_snapshot profile=brief
world_text_map x1=... y1=... x2=... y2=... format=markdown
layout_candidates purpose=barracks limit=3
build_preview prefabId=Outhouse x=... y=... dryRun=true
```

6 个调用即可验证连接、查询、地图可读性、规划候选、建筑预检五大核心能力。
