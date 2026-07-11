---
name: oni-mcp-opening-setup
description: 当开始新的 Oxygen Not Included 殖民地，或用户要求开局设置/bootstrap 配置时使用。涵盖思考前暂停、错峰日程、按属性重命名复制人并由用户确认命名风格、禁用自动消毒、侦察起始星体，以及规划早期双侧扩张和打印舱实验室。
---

# ONI MCP 开局设置

## 触发

新游戏或早期周期设置使用本技能：

- 开局配置
- 开局 bootstrap
- 日程设置
- 按属性重命名复制人
- 初始星体概览
- 打印舱附近第一轮挖掘/地基/实验室计划

## 硬规则

分析或规划前先暂停游戏：

```
game_control domain=speed action=pause
```

读取状态、规划、提问和下达设置命令时保持暂停。只有计划完成且满足以下任一条件后才恢复：

- 用户明确要求继续
- 当前任务明确包含“设置后恢复”

如果用户要求思考、检查、规划或配置，先暂停。

## 必问用户问题

重命名复制人前，询问用户命名风格。独立的“批量重命名复制人”请求走重命名快路径，不执行完整开局快照、日程、消毒或环境侦察。

只简短询问一次。示例：

- `职业前缀`: `Dig-Ada`, `Build-Meep`
- `中文岗位`: `挖掘-艾达`, `建造-米普`
- `短标签`: `Digger`, `Builder`, `Cook`
- 用户自定义风格

用户回答前，不要执行 `dupes_control domain=command action=auto_rename apply=true`。允许预览：

```
dupes_control domain=command action=auto_rename style=<candidate> apply=false
```

## 工具调用纪律

- 同一个只读工具不要用同一参数连续调用两次。第二次前必须说明缺失了什么，并改用更合适的工具或停止询问用户。
- 不要用 `dupes_control domain=info action=status_check` 代替名单、属性或重命名工具；它只用于健康/可达性分诊。
- 如果当前工具列表里没有看到目标工具，先用 `server_control domain=catalog action=search query=<tool or action>` 或 `server_control domain=catalog action=manifest group=<group>` 发现 schema，再调用；不要猜旧工具名。
- 简单配置任务只读取完成该任务必需的信息。不要为了重命名读取地图、截图、食物、电力或环境。

## 开局流程

### 1. 暂停并快照

```
game_control domain=speed action=pause
colony_control domain=snapshot action=get profile=standard includeAtmosphere=false
colony_control domain=read action=worlds
navigation_control action=get_view
```

除非缺少细节，否则使用 `colony_control domain=snapshot action=get`，不要拆成单独的 status/dupe/food/research 调用。

### 2. 配置错峰日程

读取当前日程：

```
colony_control domain=management kind=schedule action=list
```

预览错峰班次：

```
colony_control domain=management kind=schedule action=optimize apply=false
```

只有当任务明确是设置/配置，或用户确认后才应用：

```
colony_control domain=management kind=schedule action=optimize apply=true prefix="AI轮班"
colony_control domain=management kind=schedule action=list
```

目标是早期开局错峰，减少厕所、床位和娱乐拥堵。除非用户要求具体数量，否则保持默认自动班次数。

### 3. 按属性重命名复制人

独立批量重命名请求使用快路径：

```
game_control domain=speed action=pause
# 如果用户还没选风格，先问一次并停止等待回答。
dupes_control domain=command action=auto_rename style=<user style> apply=false
# 用户确认预览后：
dupes_control domain=command action=auto_rename style=<user style> apply=true
colony_control domain=read action=dupes
```

默认不先调用 `colony_control domain=read action=dupes`、`dupes_control domain=info action=attributes` 或 `dupes_control domain=skill action=list`，因为 `dupes_control domain=command action=auto_rename apply=false` 会给出预览。只有这些情况才补读上下文：

- 用户要求解释每个岗位判断。
- 预览结果明显不合理或缺少复制人。
- 用户要求手动指定某些复制人的名字。

需要解释岗位或手动命名时，读取足够的复制人上下文：

```
colony_control domain=read action=dupes
dupes_control domain=info action=attributes
dupes_control domain=skill action=list
```

根据属性/兴趣推断岗位：

- 挖掘/建造：excavation、construction、strength
- 研究/操作：science、machinery
- 农业/畜牧/烹饪：agriculture、ranching、cuisine
- 供应/整理：athletics、strength、低专精

应用前先询问命名风格。然后使用：

```
dupes_control domain=command action=auto_rename style=<user style> apply=false
dupes_control domain=command action=auto_rename style=<user style> apply=true
```

或显式重命名：

```
# 先用 server_control domain=catalog action=search 确认可用工具名；当前常见名称是 dupes_control domain=command action=rename 或 rename_dupe。
dupes_control domain=command action=rename id=<dupeId> newName=<name>
```

用 `colony_control domain=read action=dupes` 验证。

### 4. 禁用自动消毒

开局不要下达大范围消毒命令。

使用全局策略工具，不要逐对象按用户菜单：

```
colony_control domain=diagnostic action=set_auto_disinfect disabled=true applyNow=true confirm=true
```

开局设置时绝不要遍历 `AutoDisinfectable` 对象并调用 `building_control domain=side_surface surface=user_menu action=batch`。目标是全局关闭自动消毒，让当前和新发现对象都保持关闭。

### 5. 侦察起始区域和星体概览

获取当前相机/打印舱区域地图上下文：

```
read_control domain=world action=area_snapshot preset=utilities encoding=plain includeScreenshot=false
```

如果区域太小或打印舱不可见，围绕观察到的起始坐标扩大到 40x30 到 60x40 格，并保持低于 `maxCells`。

为用户总结：

- active world / 星体类型
- 起始生态区信号
- 附近固体/液体/气体
- 直接危险
- 食物/植物/资源线索
- 可用扩张方向
- 早期氧气、水或温度是否紧急

坐标结论使用文本地图，不用截图。

### 6. 第一轮建造/扩张计划

默认开局建议：

- 从打印舱向左和向右扩张。
- 两侧排入小型保守挖掘矩形，避开液体和危险空腔。
- 精确地基、平台、砖块和梯子先读 `/active/map/viewport.md`（需要时 zoom 或读 `symbols/glyphs.md`），再把目标空格 token 改为 `建筑名:优先级`，可选加 `#材料字`。
- 保留打印舱附近区域作为早期实验室。
- 研究/电力设置必须先有地板/支撑，再放人力发电机、电池、研究站和连接电线。

第一波保持小规模。普通 Cycle 1 开局工作优先使用一次紧凑快照，然后用 `building_control` / `orders_control` 的语义动作执行。不要为了挖一小块材料或放短平台创建正式计划。

放置：

```
building_control domain=planning action=placement_candidates prefabId=<PrefabId> areaId=<area> limit=8
world_editor command=read path=/active/map/viewport.md
# edit the map markdown: prefer one SEARCH/REPLACE block replacing empty tokens with 建筑名:优先级[#材料字]
# preview: outer dryRun=true, confirm=false/omitted
# execute: a new edit with outer dryRun=false, confirm=true; then re-read the map
```

普通聚合工具拒绝 raw coordinates，不要直接传 `points`/`anchors`/`x`/`y`。`/active/ops/build.md` 只用于无 raw coordinates 的语义 plan/auto_connect；精确路线使用可编辑地图 token。多 block 仅在外层 `allowPartial=true` 时允许且不可事务回滚。

挖掘：

```
orders_control domain=area action=dig confirm=true ...
```

绝不要用 `orders_control domain=designation action=attack` 挖掘。

### 7. 快速路径与正式计划

简单低风险开局动作默认走快速路径：

```
server_control domain=batch action=call_many dryRun=true responseMode=summary requireAllValid=true stopOnError=true items=[...]
server_control domain=batch action=call_many dryRun=false responseMode=summary requireAllValid=true stopOnError=true items=[...]
```

适用于日程设置、全局自动消毒、小型安全挖掘矩形、短支撑/地板线，以及 dry-run 通过的第一间实验室/电力蓝图。

只有当开局计划范围大、多阶段、有风险、来自用户计划，或需要以后恢复时才用 `plan_harness`：

```
plan_harness_create objective="Opening setup and Cycle 1 expansion" riskTolerance=low requireVerification=true
plan_harness_record stage=plan summary="Opening setup plan" payload={...}
plan_harness_validate id=<planId>
```

如果执行了动作，用 `colony_control domain=snapshot action=get profile=brief` 和 `read_control domain=world action=area_snapshot preset=construction encoding=plain` 等可读地图验证。只有存在 harness 时才记录 harness 验证。

## 执行策略

当用户要求开局设置/配置时，本技能允许执行设置动作：

- `game_control domain=speed action=pause`
- `colony_control domain=management kind=schedule action=optimize apply=true`
- 用户回答命名风格后才允许 `dupes_control domain=command action=auto_rename apply=true`
- `colony_control domain=diagnostic action=set_auto_disinfect disabled=true applyNow=true confirm=true`
- `orders_control domain=area action=dig`
- `building_control domain=planning action=placement_candidates` 以及 `/active/map/viewport.md` token edit

建造放置前仍要检查 prefab、材料和支撑。

规划/配置完成且用户允许继续前，不要恢复游戏。

## 最终回复格式

保持简洁：

- 暂停状态
- 日程设置结果
- 重命名状态或待确认的命名风格问题
- 自动消毒结果
- 星体概览
- 扩张/实验室计划摘要
- 游戏是否保持暂停，或是否按明确请求恢复
