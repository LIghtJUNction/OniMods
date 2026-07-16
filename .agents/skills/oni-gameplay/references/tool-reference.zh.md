# ONI MCP 工具参考

在选择工具、编排多工具流程、配置参数、提示词与资源映射到聚合调用时，优先查阅此参考。

## 目录

- [发现能力](#发现能力)
- [读取、写入、执行](#读取写入执行)
- [标准流程](#标准流程)
- [参数规则](#参数规则)
- [批量与效率模式](#批量与效率模式)
- [工具分类](#工具分类)
- [提示词工作流](#提示词工作流)
- [资源 URI](#资源-uri)
- [快速参考](#快速参考)

## 发现能力

| 目标 | 调用 |
|---|---|
| 查看完整公开能力 | `server_control domain=catalog action=manifest` |
| 按意图筛选 | `server_control domain=catalog action=search query=<意图> detail=brief` |
| 获取解决方案链路 | `server_control domain=catalog action=guide goal=<目标>` |
| 检查覆盖范围 | `server_control domain=catalog action=coverage` |
| 审核安全元数据 | `server_control domain=catalog action=static_audit` |

`manifest` 暴露的是当前简化的公共聚合接口。将遗留细粒度命名视为兼容入口，而非首选。

## 读取、写入、执行

- **读取（Read）**：快照与状态查询，例如快照、复制人详情、资源、基础设施、地图。
- **写入（Write）**：日程、优先级、过滤器、阈值、名称、策略等配置。通常为中风险操作，通常需要确认。
- **执行（Execute）**：暂停/恢复、指定任务、拆除/建造/拍照等操作。涉及地图变更的危险操作需要确认。

`oni://...` 属于可缓存资源。工具调用是带参数的实时操作。提示词只描述链路，不能直接执行。

## 标准流程

### 殖民地健康检查

```text
colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
colony_control domain=snapshot action=get profile=brief        # 仅在 minimal 标记相关时使用
read_control domain=resources action=food                      # 需要食物细节时
read_control domain=infrastructure action=power_summary        # 发现电力问题时
read_control domain=world action=thermal_overheat_risk         # 发现过热风险时
read_control domain=infrastructure action=rooms                # 房间覆盖率相关时
```

### 简单低风险执行

1. 先读取目标状态。
2. 对有边界的语义调用或虚拟文件补丁做 dry-run。
3. 确认后执行。
4. 用对应的精确读取进行验证。

适用于短距离挖掘、拖地、清洁、变更设置或已人工复核的蓝图。

### 复杂空间计划

```text
game_control domain=speed action=pause
read_control domain=world action=area_snapshot preset=construction|utilities encoding=rle
read_control domain=world action=text_map profile=standard encoding=plain includeElements=true
# 仅用于可选的可视化确认
navigation_control action=switch_view view=<overlay> screenshot=true
# dry-run -> 执行 -> 再读取同一区域
```

精确编辑地图请使用显式地图表头；反复语义操作可用 `areaId`。

### 复制人管理

```text
colony_control domain=read action=dupes
dupes_control domain=info action=detail id=<id>
dupes_control domain=info action=needs id=<id>
dupes_control domain=priority action=list id=<id>
# 仅提交必需变更
dupes_control domain=priority action=list id=<id>   # 验证
```

在使用需要 `id` 的工具前，先从花名册解析到数值 ID。

### 配置批量更新

```text
server_control domain=batch action=call_many
  dryRun=true responseMode=summary requireAllValid=true stopOnError=true
  defaults={confirm:true}
  items=[
    {t:dupes_control,a:{domain:priority,action:set,id:1,choreGroup:Dig,priority:4}},
    {t:colony_control,a:{domain:management,kind:schedule,action:set_block,schedule:"Default",hour:3,group:Sleep}}
  ]
```

验证通过后，以 `dryRun=false` 再调用一次，并对每个受影响域做复核。

## 参数规则

### `worldId`

- 活跃世界可省略该参数。
- 小行星或火箭内舱请传递具体 ID。
- 仅当操作覆盖全部世界时使用 `-1`。

### `limit`、`detail` 与 `encoding`

- 保持 `limit` 收窄；改为分页或过滤，而非一次拉满。
- 决策阶段优先 `detail=brief|compact`，仅在精确诊断时使用 `full`。
- 针对可读性较高的精确地图阅读使用 `profile=standard encoding=plain`。
- 大范围只读扫描使用 `profile=scan encoding=rle`。
- 地图补丁编辑请用 `format=edit compact=false`。
- 批量一般用 `responseMode=summary`，重试循环可用 `errors`，精确子结果需要 `full`。

### 坐标与区域

- 纯只读地图检查可使用原始矩形；但仅限 typed operation 文件明确支持的语法。
- 普通聚合工具应使用语义查询、目标、计划或 `areaId`，而不是原始坐标。
- 坐标为左下原点世界格点。保留返回的绝对坐标，不要将 `rx/ry` 与 `x/y` 混淆。
- 用 `read_control domain=area action=define` 定义可复用区域；必要时合并相邻句柄，临时句柄复用后应忘记。

### 确认逻辑

- 读取类调用不需要确认。
- 中/高风险变更必须在执行调用上带 `confirm=true`。
- 先 dry-run，再在授权后执行，不要只为通过校验而提前加确认。

## 批量与效率模式

### 先读后写

先读精确的日程、策略、建筑、复制人或地图状态；在本地计算差异；只提交差异字段；再读回同一对象确认。

### 并行独立读取

```text
server_control domain=batch action=call_many responseMode=summary items=[
  {t:colony_control,a:{domain:read,action:status}},
  {t:colony_control,a:{domain:diagnostic,action:diagnostics}},
  {t:read_control,a:{domain:resources,action:food}},
  {t:read_control,a:{domain=infrastructure,action:power_summary}}
]
```

如果后续调用依赖前一条返回的 ID/句柄，则不要并批处理。

### 差分更新

先完整读取相关列表，离线计算变更，只打包变更后的字段并批次提交，再用一次读取复核。

### 缓存建议

对已加载运行时可缓存 catalog 结果；`dupes`、库存、日程、房间摘要与稳定状态可在局面不快速变化时短期缓存。不要缓存写入结果、格子级状态、波动警报、活动镜头状态或网格变化中的电力状态。每次写入或速度变更后应失效相关读取。

## 工具分类

| 分类 | 读取 | 配置/写入 | 执行 |
|---|---|---|---|
| 殖民地 | 快照、状态、诊断、警报、报告 | 诊断设置、管理 | 通知类操作 |
| 复制人 | 花名册；详情、属性、需求、状态、优先级、技能 | 优先级、技能、帽子、改名、可分配项 | 显式支持时可移动或强制动作 |
| 日程 | 日程列表管理 | 创建、设置区块、分配、优化 | — |
| 资源 | 库存、食物、物品检索、存储明细、膳食状态 | 图钉、存储过滤、膳食策略 | — |
| 建筑 | 列表/摘要、defs、材料、候选项、配置列表 | 预览、自动连接、启用/切换/复制、可视化配置 | 通过支持的规划流程进行标记建造 |
| 指令 | 优先级列表 | 设置优先级 | 挖掘、拖地、拖拭、消毒、取消、收获、拆除、捕捉、清空/切断管线 |
| 基础设施 | 电力总览/端口、房间、公用地图 | 支持的配置变更 | 通过 orders/building 工具设置 utility designations |
| 世界 | 世界、格点、元素、地图、过热风险、区域 | 定义/合并/忘记区域 | 通过 orders/build plans 进行地图变更 |
| 相机 | 查看视角 | — | 移动、聚焦、覆盖层、截图 |
| 游戏 | 时间、存档、DLC/状态读取 | 支持的状态/DLC 设置 | 暂停、恢复、加速、保存/加载/退出 |
| 服务 | manifest、search、guide、coverage、diagnostics | — | 聚合调用批量 |
| 虚拟文件 | pwd, ls, read, zoom, search, symbols | edit, blueprint | 已确认的 edit/batch |

## 提示词工作流

| 提示词 | 触发条件 | 建议链路 |
|---|---|---|
| `colony_triage` | 快速健康检查 | colony status → diagnostics → alerts → food |
| `next_cycle_plan` | 短周期计划 | colony summary → inventory → research → schedules → dupes |
| `inspect_area` | 空间分析 | world text map → targeted area snapshot/details |
| `dupe_care_review` | 复制人体检 | dupes → schedules → detail/needs/attributes |
| `power_audit` | 电力检查 | power summary → 需要时查看端口/配置 |
| `rooms_overview` | 房间覆盖 | rooms list → 按类型/大小过滤 |
| `thermal_audit` | 热风险 | overheat risk → 元素图或温度图细节 |

提示词定义调用链路；实际调用仍需显式触发工具/资源。

## 资源 URI

| URI | 对应工具 | 用途 |
|---|---|---|
| `oni://colony/status` | `colony_control domain=read action=status` | 基线状态 |
| `oni://colony/diagnostics` | `colony_control domain=diagnostic action=diagnostics` | 已诊断问题 |
| `oni://colony/alerts` | `colony_control domain=diagnostic action=alerts` | 当前警报 |
| `oni://colony/summary` | `colony_control domain=report action=summary` | 规划摘要 |
| `oni://resources/inventory` | `read_control domain=resources action=inventory` | 库存水平 |
| `oni://resources/food` | `read_control domain=resources action=food` | 食物与过期 |
| `oni://power/summary` | `read_control domain=infrastructure action=power_summary` | 电路健康 |
| `oni://rooms/list` | `read_control domain=infrastructure action=rooms` | 房间覆盖 |
| `oni://thermal/overheat-risk` | `read_control domain=world action=thermal_overheat_risk` | 过热风险排序 |
| `oni://world/elements` | `read_control domain=world action=element_summary` | 元素质量/温度 |
| `oni://world/text-map` | `read_control domain=world action=text_map` | 地形与覆盖层 |
| `oni://dupes` | `colony_control domain=read action=dupes` | 花名册 |
| `oni://schedules` | `colony_control domain=management kind=schedule action=list` | 日程 |
| `oni://research/status` | `colony_control domain=management kind=research action=status` | 研究状态 |
| `oni://tools/manifest` | `server_control domain=catalog action=manifest` | 公共工具清单 |
| `oni://tools/search` | `server_control domain=catalog action=search` | 筛选发现 |

部分模板可接受查询参数，例如 `oni://power/summary?worldId=2&includeDetails=true`。

## 快速参考

| 场景 | 首次调用 | 后续 | 验证 |
|---|---|---|---|
| 发生了什么？ | minimal snapshot | 对应 flagged domain 深入查询 | minimal/brief snapshot |
| 处理电力问题 | power summary | power ports 或配置/建造计划 | power summary |
| 建造物 | 搜索 defs / 材料 | 候选项 + 语义计划或精确地图补丁 | 复查地图/建筑 |
| 复制人管理 | 花名册 | detail/needs 后有界更新 | 花名册/详情 |
| 检查温度 | 热风险 | 元素图或温度图 | 同一风险区域 |
| 计划制定 | catalog guide | search + dry-run 工具 | 相关实时读取 |
| 批量配置 | 批处理 dry-run | 批处理执行 | 涉及域 |
| 查工具 | catalog search | guide 或 manifest | 安全相关场景使用 static audit |
| 区域操作 | define/read area | 有界语义操作 | 区域读取 |
| 相机导航 | get view | 仅需要时 move/focus | get view |
| 检查研究 | research status | research list | research status |
| 火箭 | rocket status | rocket detail | rocket status |
| 仓储 | storage list/detail | set filter | storage detail |
| 自动化 | list automation | 设置可用自动化入口 | list automation |

## 错误处理

| 错误 | 下一步 |
|---|---|
| 参数缺失/无效 | 先做 catalog search，并按当前 schema 处理 |
| 目标不存在 | 刷新花名册/建筑/对象列表 |
| 无可达目标 | 检查复制人状态与路径区域，未授权不要强行救援 |
| 需要确认 | 先 dry-run，确认授权后执行 |
| 超时 | 检查返回的任务状态，或缩小范围重试 |
| 沙盒/即时建造研究锁定 | 运行中的 DLL 很可能是旧版本，不要为了测试而加入研究；请告知需后续安全重载 |
