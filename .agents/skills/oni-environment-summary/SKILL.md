---
name: oni-mcp-environment-summary
description: 当用户要求快速总结 ONI 附近环境/资源、开局区域概览、周围资源、附近危险、扩张方向，或问“我周围有什么”时使用。指导 agent 使用低 token 的公开只读工具，如 world_editor 虚拟文件 /active/map/viewport.md 和 /active/colony/status.oni，并返回简洁、可执行的环境/资源摘要，不执行游戏动作。
---

# ONI MCP 环境摘要

## 环境线索如何影响下一步

用户还要求扩张建议或资源利用方向时，按 [阶段判断](../oni-mcp-colony-strategy/references/stages.md) 区分当前生存缺口和未来模块资源。发现泉、硫、石油或火山只代表候选资源，不代表研究、材料、通行和安全开发前置已齐全。需要模块建议才读取相应参考并附原文链接，纯环境摘要不加载整个知识库。

## 通信须知

MCP 请求必须使用 IPv4 地址 `http://127.0.0.1:8788/mcp/`（避免 `localhost` 解析为 IPv6 `::1` 导致超时）。直接 curl 时需携带 `Mcp-Session-Id` header 及协议版本 `2025-11-25`。

## 目的

快速总结当前视角、选区、打印舱附近，或用户提供坐标周围的情况。这个技能只读：不要挖掘、建造、清扫、收获或修改设置。

## 快速路径

用最少读取回答问题，统一使用公开 7 工具及 `world_editor`：

```
world_editor command=read path=/active/colony/status.oni profile=minimal
world_editor command=read path=/active/map/viewport.md compact=true syncView=false focusCamera=false
```

如果存在玩家编辑标记请求，或用户说“框选区域/标记区域/玩家规划”，先读取：

```
game_control domain=ui uiDomain=edit_mark action=list limit=5
```

## 何时补充细节

只有快速路径不够时才通过对应虚拟文件添加：

- 库存水平：`world_editor command=read path=/active/resources/inventory.oni`
- 食物储备：`world_editor command=read path=/active/resources/food.oni`
- 电网总览：`world_editor command=read path=/active/infrastructure/power.md`
- 房间划分：`world_editor command=read path=/active/infrastructure/rooms.oni`
- 复制人状态：`world_editor command=read path=/active/dupes/index.oni`
- 温度覆盖：`world_editor command=read path=/active/map/viewport.md view=temperature compact=true syncView=false focusCamera=false`

除非用户要求视觉确认，否则避免截图。

## 地图阅读规则

先读视口地图或 `world_editor command=read path=/active/map/viewport.md`：

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

- 公开 7 工具只读操作（`world_editor command=read`）
- `game_control domain=ui uiDomain=edit_mark action=list`
- `world_editor command=read path=/active/map/viewport.md`
- 虚拟文件库存/食物/电力/房间读取

不允许：

- 任何 `orders_control`、`building_control`、`game_control` 写入或执行动作
- 任何其他会改变游戏状态的配置/写入/执行工具

如果用户在摘要后要求动作，切换到规划/建造控制技能，并在执行前 dry-run。
