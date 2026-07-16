# 世界编辑器协议

在执行 `world_editor` 的地图编辑、离屏取景、operation 文件、管理文件、plan、蓝图或 batch 操作前，请先阅读本参考。

## 目录

- [心智模型](#心智模型)
- [虚拟路径](#虚拟路径)
- [通用补丁协议](#通用补丁协议)
- [精确地图矩形协议](#精确地图矩形协议)
- [地图 token 编辑](#地图-token-编辑)
- [typed operation 文件](#typed-operation-文件)
- [管理、计划与蓝图文件](#管理计划与蓝图文件)
- [world-editor 批处理](#world-editor-批处理)
- [示例](#示例)
- [排错](#排错)

## 心智模型

`world_editor` 会把实时游戏状态暴露为一个虚拟目录。读取会返回当前状态；编辑会提交文本补丁，由预检流程转换为游戏操作。

```text
world_editor command=pwd|cd|ls|read|zoom|grep|symbols|search|edit|blueprint|batch
  path=<virtual path> content=<patch>? dryRun=? confirm=? allowPartial=?
```

也支持相机速度、视图/覆盖层与截图等转发操作。除非操作本身天然适合虚拟文件流程，否则优先使用聚合工具。

## 虚拟路径

| 路径 | 用途 | 编辑形态 |
|---|---|---|
| `/active/index.md` | 只读殖民地索引与可选初始状态 | 只读 |
| `/active/map/viewport.md` | 框选矩形中的地形、建筑与标记 | 地图 token |
| `/active/map/index.md` | 可编辑视口地图别名 | 地图 token |
| `/active/map/layers/layer_<yMin>_<yMax>.md` | 当前活动覆盖层渲染的高度切片 | 地图 token，连接符号除外 |
| `/active/map/symbols/glyphs.md` | token 图例 | 只读 |
| `/active/ops/tools.md` | 当前公开 typed operation 文件与工具 | 只读 |
| `/active/ops/orders.md` | Dig/mop/sweep/disinfect/harvest/cancel/deconstruct/attack/capture | 单条命令 |
| `/active/ops/build.md` | 语义化无坐标建造计划 | 单条命令 |
| `/active/ops/{game,colony,read,search,dupes,navigation,server,...}.md` | typed tool 调用 | 单条命令 |
| `/active/management/{schedule,priorities,dupes,food,skills,research}.md` | 面板快照与编辑命令 | 单条命令 |
| `/active/dupes/<name>.md` | 单个复制人详情 | 支持 `Name:` 等字段编辑 |
| `/active/buildings/plans.oni` | 建筑计划文本 | plan 补丁 |
| `/active/infrastructure/*.oni` | 公共管线连接计划 | plan 补丁 |
| `/active/infrastructure/*.md` | 基础设施地图视图 | 精确地图预检；连接符号编辑会拒绝 |

仅 `/active/` 可变更。其余存档槽为历史或未加载视图。

## 通用补丁协议

`content` 参数包含一个或多个标记块。为减少仓库差异误判，marker 行前置了缩进；解析器允许这种变体。

```text
  <<<<<<< SEARCH
<exact current text>
  =======
<replacement text>
  >>>>>>> REPLACE
```

执行门控：

- `dryRun=true`、缺失 `confirm` 或 `confirm=false`：仅预览。
- `dryRun=false confirm=true`：执行转换后的真实操作。

规则：

- 优先使用单块补丁。多个会变更的块请设置外层 `allowPartial=true`；游戏变更不能事务回滚。
- 所有补丁在变更前会预检。若失败，则中止（`phase=preflight`），除非使用了显式允许部分成功的模型。
- 补丁仅支持文本，禁止发送 `editCells` 或 `editLines` 这类坐标载荷。
- 执行后请再次读取。不要复用过期补丁。

## 精确地图矩形协议

地图行需包含三个显式 X 坐标表头和 `Y=<n>:` 行：

```text
百位X: ...
十位X: ...
个位X: ...
Y=166: ...
Y=165: ...
```

### 推荐显式模式

在 SEARCH 中包含 **全部三种** `百位X`、`十位X`、`个位X` 表头，并附带相关 Y 行。预检会从来源精确推导矩形，不依赖相机移动/聚焦/当前镜头；匹配前会内部强制 `format=edit compact=false`。

该“精确源坐标”行为适用于 `/active/map/viewport.md`、其别名 `/active/map/index.md`、`/active/map/layers/layer_<yMin>_<yMax>.md` 与基础设施 Markdown 地图。它不会让只读 `/active/index.md` 变为可写。

如果 SEARCH 试图携带任意 X 表头，但表头缺失、格式错误、不一致或其他非法时，预检会直接失败，不会回退为视口相对匹配。

只有完全不带 X 表头的补丁才可使用旧式视口相对行匹配；该模式仅适用于当前视口内单行且唯一的微小补丁。

### 离屏只读取景

不移动相机读取离屏矩形：

```text
world_editor command=zoom path=/active/map/viewport.md
  x1=81 y1=42 x2=85 y2=42
  syncView=false focusCamera=false format=edit compact=false
```

`zoom` 可不持久化。将返回的三行 X 表头和所需 Y 行直接复制到后续补丁。精确表头预检会在不依赖相机的前提下重读对应世界坐标。

不要为了让补丁可寻址而使用 `navigation_control focus_cell`。

### 展开行

为了便于编写补丁，请请求 `format=edit compact=false`。只读地图可使用 RLE（`粉x3`），但压缩行用于 token 级编辑容易出错。`REPLACE` 必须保留每行的精确格数。

## 地图 token 编辑

流程：

1. 用 `format=edit compact=false` 进行读取或离屏 zoom。
2. 将三行 X 表头与最小完整的 Y 行集复制到 SEARCH。
3. 在 REPLACE 中重复同样头部，仅修改目标格子。
4. 用 `dryRun=true` 预览。
5. 查看转换操作、材料、支撑、冲突与变更格数。
6. 若状态变更则重读；否则使用新调用执行 `dryRun=false confirm=true`。
7. 再读取同一矩形。

Token 形式：

- 建筑命令：`建筑名:优先级`，可选 `#材料字`，如 `梯子:7#粉`。
- 裸建筑名表示已存在对象，不是建造命令。
- 指令：`挖`、`拆`、`擦`、`扫`、`毒`、`杀`、`收`、`消`、`捕`，可附 `:优先级`。
- 地图层与基础设施 Markdown 上不接受连接符号编辑，因为 `auto_connect` 可能改写到验证快照外部的格子。要改 utility 请用显式 `/active/infrastructure/*.oni` plan 或 `/active/ops/build.md` 的 `auto_connect` 命令。
- SEARCH 通配 `?` 或 `*` 匹配单 token；`/regex/` 或 `~regex` 匹配单 token 的正则。REPLACE 中的 `?`、`*`、`.*` 保留原始 token。

多格建筑需在同一替换块内包含完整占格。包含支撑格和足够邻域以保证匹配唯一。

默认写入预算为 512 个变更格；`maxWriteCells`/`maxCells` 可配置但硬上限 2500。开启 `partial=true` 时，重读并仅对 `remainingCells` 生成新补丁。

## typed operation 文件

先读 `/active/ops/tools.md`，再读目标 operation 文件。每个文件会说明默认工具、schema 与示例注释。

规则：

- 每次编辑只提交一个可执行命令。
- 使用 `call tool=<name> key=value ...`；typed 文件可省略 `tool=`。
- 空 SEARCH 块表示新增一条命令。
- 外层 `task` 会继承并描述操作。
- 不要在 operation 文件里递归调用 `world_editor`。
- 不要使用隐藏的 `coordinate_control` 或 `/active/ops/coordinate.md`。
- 原始坐标仅在对应 typed 文件明确记录的语法中允许。

常见 `/active/ops/orders.md` 快捷写法：

```text
挖 土@(83,146):7
擦 x1=90 y1=140 x2=94 y2=142 priority=6
扫 areaId=base_floor priority=6
拆 建筑@(90,141):7
捕 小动物@(101,130):7 dryRun=true
```

液体请用 `擦`（mop），碎片/垃圾请用 `扫`（sweep）。

## 管理、计划与蓝图文件

### 管理类 Markdown

表格是只读快照。仅通过 `## Edit Commands` 下的一行非注释命令变更状态。

| 文件 | 常见命令 |
|---|---|
| `schedule.md` | `set_block`, `assign_dupe`, `create_schedule` |
| `priorities.md` | `priority`, `priority_settings` |
| `dupes.md` | `rename` |
| `food.md` | `food`, `food_policy` |
| `skills.md` | `learn_skill` |
| `research.md` | `research`, `clear_research` |

仅在需要机器可读状态时追加 `?format=json`。

### 计划文件

- `/active/buildings/plans.oni`：预览会走 `parse_plan`，确认执行则走 `building_control planning build_area`。
- `/active/infrastructure/*.oni`：显式 utility 计划走 `auto_connect`。
- `/active/ops/build.md`：当 utility 变更更适合 typed operation 时，提交单条 `auto_connect` 命令。

### 蓝图

`world_editor command=blueprint name=<name>` 支持 read/list/create/delete/use。蓝图 Markdown 也使用同一补丁协议。

## world-editor 批处理

`world_editor command=batch` 支持最多 20 个 `steps`/`items`，可包含 world-editor、game-control 或 navigation-control 参数对象。

- 最多一个可能变更的 step，且必须在最后。
- 前置 step 只能只读。
- `stopOnError` 默认 true。
- 禁止嵌套 world-editor batch。
- 外层 dry-run 与确认策略会继承。

当最终变更只依赖批处理前已知参数时可使用 batch。不要假设后置步骤能消费前置步骤的动态结果。

## 示例

### 离屏搭梯补丁

先执行上面的只读 zoom。复制真实的 headers 与行。Marker 的缩进是有意的，解析器可正确识别。

```text
world_editor command=edit path=/active/map/viewport.md task="搭梯子" dryRun=true
content="""
  <<<<<<< SEARCH
百位X: 0 0 0 0 0
十位X: 8 8 8 8 8
个位X: 1 2 3 4 5
Y=42: 粉 粉 空 空 水
  =======
百位X: 0 0 0 0 0
十位X: 8 8 8 8 8
个位X: 1 2 3 4 5
Y=42: 粉 粉 梯子:7 空 水
  >>>>>>> REPLACE
"""
```

先复核，再用新生成补丁执行 `dryRun=false confirm=true`。

### 新建挖掘命令

```text
world_editor command=edit path=/active/ops/orders.md task="挖掘取材料" dryRun=true
content="""
  <<<<<<< SEARCH
  =======
挖 土@(83,146):7
  >>>>>>> REPLACE
"""
```

### 重命名复制人

```text
world_editor command=edit path=/active/management/dupes.md task="重命名复制人" dryRun=false confirm=true
content="""
  <<<<<<< SEARCH
# rename name="Dig" newName="矿工"
  =======
rename name="Dig" newName="矿工"
  >>>>>>> REPLACE
"""
```

## 排错

| 症状 | 处理 |
|---|---|
| `SEARCH did not match` | 重读源文件并重生成补丁，勿重发旧文本 |
| 显式表头错误 | 使用同一次读取中的完整三表头，或对确实唯一的视口行移除全部表头 |
| 行定位歧义 | 增加 X 表头和更多 Y 行上下文 |
| 坐标偏移错误 | 停止手工偏移计算；改用显式表头与精确源预检 |
| 压缩 token 不一致 | 重新以 `format=edit compact=false` 读取 |
| 多格预览失败 | 在一个块内包含完整 footprint 与支撑上下文 |
| `partial=true` | 重读后仅对剩余格子重补丁 |
| 相机意外移动 | 使用 `syncView=false focusCamera=false` 的离屏 zoom；不要仅为编辑而聚焦 |
| 沙盒报告研究锁定 | 已加载 DLL 可能过期；不要为测试队列中加研究任务 |
