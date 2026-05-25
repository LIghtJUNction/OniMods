---
name: oni-mcp-environment-summary
description: 当用户要求快速总结 ONI 附近环境/资源、开局区域概览、周围资源、附近危险、扩张方向，或问“我周围有什么”时使用。指导 agent 使用低 token 的只读 MCP 工具，尤其是 world_area_snapshot，并返回简洁、可执行的环境/资源摘要，不执行游戏动作。
---

# ONI MCP 环境摘要

## 目的

快速总结当前视角、选区、打印舱附近，或用户提供坐标周围的情况。这个技能只读：不要挖掘、建造、清扫、收获或修改设置。

## 快速路径

用最少读取回答问题：

```
colony_state_snapshot profile=brief includeAtmosphere=false
camera_get_view
world_area_snapshot preset=utilities encoding=plain includeScreenshot=false
```

如果用户提供了 `areaId` 或坐标，把它们传给 `world_area_snapshot`。

如果存在玩家编辑标记请求，或用户说“框选区域/标记区域/玩家规划”，先读取：

```
edit_mark_request_list limit=5
world_area_snapshot areaId=<request.areaId> preset=utilities encoding=plain
```

## 何时补充细节

只有快速路径不够时才添加：

- `world_element_summary state=solid|liquid|gas`：查看世界级质量/温度上下文。
- `resources_inventory limit=30`：查看已知库存/碎片资源。
- `resources_food limit=20`：查看食物数量和千卡。
- `farming_harvestables_list x1/y1/x2/y2 readyOnly=false limit=80`：查看附近野生植物/食物。
- `world_text_map profile=standard encoding=plain`：当 `world_area_snapshot` 范围太小时做更大地形扫描。

除非用户要求视觉确认，否则避免截图。

## 地图阅读规则

先读 `maps.base` 或 `world_text_map` 的 base 视图：

- `sol`：天然固体砖；候选挖掘/资源
- `tile`：人工砖/地基
- `liq`：液体；如果有元素细节，说明水/污染水/盐水等
- `oxy/po2/co2/hyd`：氧气/污染氧/二氧化碳/氢气
- `bld`：建筑
- `dup`：复制人
- `itm`：散落物/碎片
- `unk`：未知/未揭示或世界外

再读 overlay：

- `maps.power`：电线和用电/发电设备
- `maps.gas_conduits`、`maps.liquid_conduits`：现有气管/液管路线
- `maps.logic`：自动化
- `maps.solid_conveyor`：运输轨道

使用文本地图坐标。不要从截图推断精确格子。

## 摘要格式

用这些标题返回简洁中文摘要：

```
周围环境摘要
地形/空间:
资源:
气体/液体:
可采集/食物:
危险/限制:
扩张方向:
建议下一步:
```

每个标题 1-3 条。只有坐标能帮助行动时才提坐标。

## 分诊启发

优先关注 ONI 前期真正重要的事项：

- 可呼吸空间和二氧化碳聚集
- 附近水源或麻烦液体
- 藻类/氧石/有机固体/金属矿可用性
- 野生食物/可收获物和种子机会
- 高温、真空、菌泥/污染氧、敌对小动物、不可达空腔
- 左/右/上/下扩张质量
- 打印舱附近空间是否能变成实验室/核心基地

不确定时，说明未知点并建议一次聚焦后续扫描，不要滥用大范围工具。

## 安全

允许：

- 只读工具
- `edit_mark_request_list`
- `world_area_snapshot`
- `world_text_map`
- 库存/农业读取工具

不允许：

- `orders_*`
- `agent_pointer_left_click`
- `agent_pointer_hold_left`
- `game_resume`
- 配置/写入/执行工具

如果用户在摘要后要求动作，切换到规划/建造控制技能，并在执行前 dry-run。
