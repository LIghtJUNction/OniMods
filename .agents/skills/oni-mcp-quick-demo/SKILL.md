---
name: oni-mcp-quick-demo
description: 当用户要求快速演示、展示 ONI MCP 能力、功能介绍、capability showcase、试玩 MCP 工具、看看 MCP 能做什么，或说出"演示一下""快速展示""showcase"时使用。执行一条预定义的 10 步快速演示流程，覆盖指针交互、视图切换、复制人管理、日程、消毒、截图、游戏百科、机制查询、建造挖掘意图和规划功能。目标是快速、低确认、直观地向用户展示 MCP 工具链能力。
---

# ONI MCP 快速能力演示

## 执行原则

- **快速执行**：不要逐条询问确认，按顺序直接调用工具
- **边执行边解说**：每完成一步，用一句话向用户说明刚才演示了什么
- **错误继续**：单步失败不阻断后续步骤，记录失败原因并继续演示
- **批量读取**：独立的读取调用合并为 `tools_call_many` 以减少往返
- **默认不破坏存档**：写入/执行能力优先用 `dryRun`、`apply=false` 或只读预览；只有用户明确要求真实演示时才改名、改日程、挖掘或建造。
- **工具发现**：示例工具名可能随服务端演进；执行前若当前工具列表没有该工具，用 `tools_search query=<action>` 确认 schema。

## 10 步演示流程

按顺序执行。每步完成后简要汇报，然后立即进入下一步。

### Step 1 — Agent 指针亮相

```
agent_pointer_jump code=home displayText="ONI MCP 演示开始：先展示只读、预览和 dry-run 能力。"
```

**解说**：Agent 指针已在游戏中出现并显示气泡。

### Step 2 — 视图切换

```
camera_switch_view view=temperature screenshot=true
camera_switch_view view=oxygen screenshot=true
camera_switch_view view=none
```

**解说**：已快速切换温度视图、氧气视图并截图，最后切回普通视图。

### Step 3 — 复制人管理/重命名预览

先批量读取：

```
tools_call_many responseMode=summary items=[
  { t: dupes_list, a: {} },
  { t: schedule_list, a: {} }
]
```

演示批量重命名预览，不直接改名：

```
dupes_auto_rename style=cn_job apply=false
```

**解说**：已读取复制人列表，并预览按岗位自动命名的结果；没有修改存档。

### Step 4 — 日程安排

使用 Step 3 中读取的 `schedule_list` 结果，演示日程修改预检：

```
schedule_set_block schedule=<日程名> hour=0 group=Sleep dryRun=true
```

**解说**：已预检把第一个日程的第 1 时段设为睡眠，演示了日程编辑能力；没有应用修改。

如用户要求真实演示，再用 `schedule_optimize apply=false` 预览轮班制，确认后才 `apply=true`。

### Step 5 — 自动消毒策略能力

```
tools_search query="auto disinfect" detail=brief
```

**解说**：已展示可发现并调用全局自动消毒策略工具；默认演示不修改存档。如用户要求真实开局设置，再调用 `colony_auto_disinfect_set disabled=true applyNow=true confirm=true`。

### Step 6 — 截图分析

```
game_screenshot
```

**解说**：已捕获当前游戏画面，可结合视觉进行后续分析。

### Step 7 — 游戏内 Wiki 查询

```
database_query query="Electrolyzer" includeContent=true limit=5
```

**解说**：已查询游戏内置百科关于电解器的信息。

### Step 8 — 游戏机制理解

```
database_query query="oxygen generation oxygen production" includeContent=true limit=5
```

**解说**：已向 MCP 服务器查询制氧机制与经验公式，展示了游戏内知识库能力。

### Step 9 — 建造/挖掘（Agent 指针）

```
agent_pointer_jump code=mouse
agent_pointer_select_tool tool=dig
agent_pointer_left_click dryRun=true displayText="演示：预检挖掘此格"
agent_pointer_select_tool tool=build prefabId=Tile material=auto
agent_pointer_left_click dryRun=true displayText="演示：预检在此铺砖"
```

**解说**：Agent 指针已跳转到鼠标位置，分别预检了挖掘和铺砖；没有下达真实命令。

### Step 10 — 规划功能

```
edit_mark_request_list limit=3
```

**解说**：已读取游戏内编辑标记/规划请求列表。玩家可在游戏内框选区域并输入计划，agent 会在此读取并转化为可执行方案。

## 收尾

10 步全部完成后，向用户总结：

1. 已展示的 10 项能力清单（每项一句话）
2. 哪些步骤成功/失败
3. 一句话引导用户下一步可以做什么（如"需要我实际执行建造或规划吗？"）

保持总结在 5 句话以内。
