---
name: oni-mcp-dupe-status-check
description: 当用户要求检查 ONI 复制人状态、怀疑复制人卡住/被困/不可达、报告复制人在错误地点发呆或睡觉、要求救援分诊，或需要快速复制人健康/导航审计时使用。指导 agent 先用 world_editor command=read path=/active/dupes/index.oni，只检查被标记区域，除非用户明确授权，否则不要下达救援动作。
---

# ONI MCP 复制人状态检查

## 目的

快速识别可能被困、不可达、疲劳、窒息、饥饿，或无法正常执行工作的复制人。这个技能默认以只读为主。

## 通信须知

MCP 请求必须使用 IPv4 地址 `http://127.0.0.1:8788/mcp/`（避免 `localhost` 解析为 IPv6 `::1` 导致超时）。直接 curl 时需携带 `Mcp-Session-Id` header 及协议版本 `2025-11-25`。

## 快速路径

使用公开 7 工具及权威虚拟文件读取复制人状态：

```
world_editor command=read path=/active/dupes/index.oni
```

检查单个复制人详细信息：

```
world_editor command=read path=/active/dupes/<dupeName>.md
```

检查殖民地基线指标与警报（包含压力、饥饿、红警）：

```
world_editor command=read path=/active/colony/status.oni profile=minimal
```

除非虚拟文件读取缺少必要细节，否则不要从大范围地图扫描或全图截图开始。

## 需求字段缺失时

`needs.amounts` 为空对象，或索引中的需求值为 `null`，表示该读取没有提供数值，不表示需求为零或复制人安全。该情况可能出现在普通复制人或仿生复制人的接口结果中。需要判断健康风险时，按索引里的风险/状态标记和单个复制人详情作针对性核查；不要把空值改写成“无风险”，也不要重复读取相同参数。

## 边界和防循环

- 本技能只处理健康、路径、被困、任务阻塞和救援分诊；复制人重命名、属性分工、日程设置不使用本技能。
- 公开 7 工具核心不直接暴露 `dupes_control` 或 `colony_control`。需要名单与状态请统一使用 `world_editor command=read path=/active/dupes/index.oni`。
- 同一参数的只读请求最多调用一次。若无风险，直接报告“未发现风险”，不要追加大范围地图或截图。

## 后续读取

如果状态索引中显示有复制人处于危险状态（如卡住、受困、窒息）：

```
world_editor command=read path=/active/map/viewport.md compact=true syncView=false focusCamera=false
```

只有需要时才补充细节：

- `world_editor command=read path=/active/dupes/<name>.md`：查看属性/状态详情。
- `navigation_control action=focus_dupe id=<id>`：仅在用户需要视觉聚焦时使用。
- 截图只用于视觉确认，不用于坐标推理。

## 风险阅读

这些情况按紧急处理：

- `risk=critical`
- 原因包含 `no_reachable_nearby_cells`、`low_breath`、`invalid_current_cell`
- 复制人在液体中、极端温度中，或热量/体力很低

这些情况按规划警告处理：

- `target_unreachable`
- `very_few_reachable_nearby_cells`
- 当前工作异常、空闲/无工作，或远离基地睡觉

使用工具响应里的 `scanRect` 和 `nextRead`，不要自己发明大范围地图查询。

## 救援策略

默认只读。除非用户明确要求修复/救援/执行，否则不要调用：

- 任何挖掘、建造、移动复制人、恢复游戏或其他会改变游戏状态的写入/执行工具

救援动作前：

1. 如果用户要求思考或规划，先暂停。
2. 检查被标记的 `scanRect`。
3. 优先通过挖掘/建造支撑打开可达路径，不要盲目移动复制人。
4. 用 `building_control domain=planning action=search_defs/materials/placement_candidates/preview` 检查支撑方案；获得明确授权后才以单 anchor `build_area` dry-run，再 confirm 执行。
5. 动作完成后用 `world_editor command=read path=/active/dupes/index.oni` 验证状态恢复正常。

不要盲目将复制人传送到未知格子；优先通过常规挖掘和建造梯子打通路径。

## 输出格式

用简洁中文返回：

```
复制人状态:
风险:
疑似被困:
环境/路径:
建议:
```

说明复制人姓名、坐标、风险和一个下一步安全读取/动作。如果无人被标记，直接说明并避免额外工具调用。
