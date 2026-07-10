---
name: oni-mcp-quick-demo
description: 当用户要求快速演示、展示 ONI MCP 能力、功能介绍、capability showcase、试玩 MCP 工具、看看 MCP 能做什么，或说出"演示一下""快速展示""showcase"时使用。执行一条预定义的 10 步快速演示流程，覆盖虚拟文件、视图切换、复制人管理、日程、消毒、截图、工具流程发现、实时世界分析、建造挖掘预检和规划功能。目标是快速、低确认、直观地向用户展示 MCP 工具链能力。
---

# ONI MCP 快速能力演示

## 执行原则

- **快速执行**：不要逐条询问确认，按顺序直接调用工具
- **边执行边解说**：每完成一步，用一句话向用户说明刚才演示了什么
- **错误继续**：单步失败不阻断后续步骤，记录失败原因并继续演示
- **批量读取**：独立的读取调用合并为 `server_control domain=batch action=call_many` 以减少往返
- **默认不破坏存档**：写入/执行能力优先用 `dryRun`、`apply=false` 或只读预览；只有用户明确要求真实演示时才改名、改日程、挖掘或建造。
- **工具发现**：示例工具名可能随服务端演进；执行前若当前工具列表没有该工具，用 `server_control domain=catalog action=search query=<action>` 确认 schema。

## 10 步演示流程

按顺序执行。每步完成后简要汇报，然后立即进入下一步。

### Step 1 — 虚拟文件入口

```
world_editor command=ls path=/active/
```

**解说**：已通过虚拟文件入口列出当前存档结构。每次调用的 taskDescription 会自动显示在玩家鼠标附近。

### Step 2 — 视图切换

```
navigation_control action=switch_view view=temperature screenshot=true
navigation_control action=switch_view view=oxygen screenshot=true
navigation_control action=switch_view view=none
```

**解说**：已快速切换温度视图、氧气视图并截图，最后切回普通视图。

### Step 3 — 复制人管理/重命名预览

先批量读取：

```
server_control domain=batch action=call_many responseMode=summary items=[
  { t: colony_control, a: { domain: read, action: dupes } },
  { t: colony_control, a: { domain: management, kind: schedule, action: list } }
]
```

演示批量重命名预览，不直接改名：

```
dupes_control domain=command action=auto_rename style=cn_job apply=false
```

**解说**：已读取复制人列表，并预览按岗位自动命名的结果；没有修改存档。

### Step 4 — 日程安排

使用 Step 3 中读取的 `colony_control domain=management kind=schedule action=list` 结果，演示日程修改预检：

```
colony_control domain=management kind=schedule action=set_block schedule=<日程名> hour=0 group=Sleep dryRun=true
```

**解说**：已预检把第一个日程的第 1 时段设为睡眠，演示了日程编辑能力；没有应用修改。

如用户要求真实演示，再用 `colony_control domain=management kind=schedule action=optimize apply=false` 预览轮班制，确认后才 `apply=true`。

### Step 5 — 自动消毒策略能力

```
server_control domain=catalog action=search query="auto disinfect" detail=brief
```

**解说**：已展示可发现并调用全局自动消毒策略工具；默认演示不修改存档。如用户要求真实开局设置，再调用 `colony_control domain=diagnostic action=set_auto_disinfect disabled=true applyNow=true confirm=true`。

### Step 6 — 截图分析

```
navigation_control action=screenshot
```

**解说**：已捕获当前游戏画面，可结合视觉进行后续分析。

### Step 7 — 工具流程发现

```
server_control domain=catalog action=guide goal="build and power an electrolyzer" detail=brief
```

**解说**：已根据建造并供电电解器这一目标，获取推荐资源、工具链和安全执行流程。

### Step 8 — 实时世界元素分析

```
read_control domain=world action=element_summary
```

**解说**：已只读汇总当前世界的元素质量与温度，展示实时存档分析能力。

### Step 9 — 建造/挖掘预检

```
world_editor command=read path=/active/map/viewport.md
building_control domain=planning action=placement_candidates prefabId=Tile x1=<viewport.x1> y1=<viewport.y1> x2=<viewport.x2> y2=<viewport.y2> limit=3
building_control domain=planning action=preview prefabId=Tile material=auto x=<bestCandidate.x> y=<bestCandidate.y> dryRun=true
orders_control domain=area action=dig x1=<viewport.x1> y1=<viewport.y1> x2=<viewport.x1+2> y2=<viewport.y1+2> dryRun=true detail=true
```

**解说**：已读取虚拟地图，并分别预检铺砖与小范围挖掘；没有下达真实命令。

### Step 10 — 规划功能

```
game_control domain=ui uiDomain=edit_mark action=list limit=3
```

**解说**：已读取游戏内编辑标记/规划请求列表。玩家可在游戏内框选区域并输入计划，agent 会在此读取并转化为可执行方案。

## 收尾

10 步全部完成后，向用户总结：

1. 已展示的 10 项能力清单（每项一句话）
2. 哪些步骤成功/失败
3. 一句话引导用户下一步可以做什么（如"需要我实际执行建造或规划吗？"）

保持总结在 5 句话以内。
