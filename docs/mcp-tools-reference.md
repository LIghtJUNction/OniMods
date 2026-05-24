# ONI MCP 工具参考

## 快速开始

- **MCP 服务器地址**：`http://localhost:8787/mcp/`
- **协议版本**：2025-11-25 / 2024-11-05
- **工具总数**：~320 个（`tools/list` 默认暴露约 60–80 个核心入口）
- **会话要求**：非 `initialize` 请求必须携带 `Mcp-Session-Id` 和协商后的 `Mcp-Protocol-Version`

> 提示：未在 `tools/list` 中看到的工具仍可直接 `tools/call` 按名称调用，或用 `tools_search detail=full` 检索完整注册表。

---

## 工具分类索引

### 殖民地与诊断 (colony)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_status` | read | none | 周期、复制人数、世界数、暂停/速度状态 |
| `colony_diagnostics` | read | none | 缺氧、断粮、过热等诊断汇总 |
| `colony_alerts` | read | none | 当前警报和通知列表 |
| `colony_report` | read | none | 殖民地周期报告 |
| `colony_summary` | read | none | 面向行动规划的摘要 |
| `diagnostic_settings_list` | read | none | 诊断显示模式与子条件状态 |
| `set_diagnostic_settings` | write | low | 修改诊断显示设置 |
| `notifications_list` | read | none | HUD 通知与可清除状态 |
| `click_notification` | execute | low | 点击通知聚焦目标 |
| `dismiss_notification` | execute | low | 清除通知 |

### 复制人管理 (dupes)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `dupes_list` | read | none | 列出所有复制人基本状态 |
| `dupes_detail` | read | none | 单个复制人详情 |
| `dupes_attributes` | read | none | 属性与特性 |
| `dupes_needs` | read | none | 需求、压力和士气 |
| `dupes_priorities_list` | read | none | 个人工作优先级 |
| `set_personal_priority` / `batch` | write | medium | 修改个人优先级（支持批量）|
| `dupes_skills` / `learn_skill` | read/write | medium | 技能点与技能学习 |
| `set_hat` / `rename_dupe` | write | low | 更换帽子、重命名 |
| `move_dupe` / `move_dupes_batch` | execute | low | 移动复制人到指定位置 |
| `assignables_list` / `set_assignable` | read/write | medium | 床、餐桌、太空服等分配 |
| `equipment` / `bionic_upgrades` | read | none | 装备槽与仿生人升级 |
| `direct_commands` / `todos` | read | none | 直接操作入口与当前差事 |

### 日程管理 (schedules)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `schedules_list` | read | none | 所有日程与复制人分配 |
| `create_schedule` | write | medium | 创建新日程 |
| `set_schedule_block` | write | medium | 设置日程时间段 |
| `assign_dupe_schedule` | write | medium | 给复制人分配日程 |
| `optimize_schedules` | write | medium | 自动优化日程 |

### 资源与库存 (resources)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `resources_inventory` | read | none | 资源库存摘要 |
| `resources_food` | read | none | 食物库存与保质信息 |
| `resources_pins` / `set_resource_pin` | read/write | low | 资源面板固定与通知开关 |
| `storage_list` / `storage_detail` | read | none | 储存建筑与详情 |
| `set_storage_filter` | write | medium | 修改储存过滤器 |
| `diet_status` / `set_diet_food` / `apply_diet_policy` | read/write | medium | 饮食权限与策略 |
| `storage_tile_selections` / `set` | read/write | low | 储存砖目标物品 |

### 建筑管理 (buildings)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `buildings_list` / `buildings_summary` | read | none | 建筑列表与统计摘要 |
| `buildings_search_defs` | read | none | 搜索可建造建筑定义 |
| `buildings_config_list` | read | none | 可配置建筑（阈值/阀门/门等）|
| `set_building_enabled` / `set_building_toggle` | write | low | 启用/禁用/开关建筑 |
| `configure_manual_delivery` / `copy_settings` | write | medium | 手动运送与复制设置 |
| `artables_list` / `set_artable_stage` | read/write | low | 艺术建筑外观 |
| `lights_list` / `set_light_color` | read/write | low | 灯光颜色 |
| `pixel_packs_list` / `set_pixel_pack_color` | read/write | low | Pixel Pack 颜色面板 |
| `geo_tuners_list` / `assign_geo_tuner` | read/write | medium | GeoTuner 喷泉分配 |
| `suit_lockers_list` / `control_suit_locker` | read/write | medium | 太空服柜配置 |
| `telepads_list` / `control_telepad` | read/write | medium | 打印舱控制 |
| `side_buttons_list` / `press_button` | read/execute | low | 通用侧屏按钮 |
| `checklists_list` / `related_entities_list` / `progress_bars_list` | read | none | 清单、关联对象、进度条 |

### 建造与命令 (orders)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `priorities_list` | read | none | 全局优先级设置 |
| `set_building_priority` / `set_priority_area` | write | low/medium | 建筑/区域优先级 |
| `plan_building` / `plan_building_rect` / `plan_many` | write | medium | 单建筑/矩形/多建筑规划 |
| `deconstruct_building` | write | **dangerous** | 拆除建筑（需 `confirm=true`）|
| `sweep_area` / `dig_area` | write | **dangerous** | 清扫/挖掘区域（需 `confirm=true`）|
| `mop_area` / `disinfect_area` / `attack` | write | medium | 拖地/消毒/攻击 |
| `cancel_area` / `harvest_area` / `capture_critters` | write | medium | 取消/收获/捕捉 |
| `empty_conduits` / `cut_conduits` | write | medium/**dangerous** | 倒空/切断管道 |

### 电力系统 (power)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `power_summary` | read | none | 电力摘要：发电、负载、电池，按 circuit 聚合 |

### 房间系统 (rooms)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `rooms_list` | read | none | 房间类型、大小、边界、对象计数和士气效果 |

### 自动化 (automation)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `automation_controls_list` | read | none | 逻辑/电力可配置控件 |
| `set_automatable_control` / `batch` | write | low | 自动化专用搬运开关 |
| `critter_sensors_list` / `set` / `batch` | read/write | low | 小动物/蛋计数传感器 |
| `comet_detectors_list` / `set` | read/write | low | 彗星探测器目标 |
| `cluster_location_sensors_list` / `set` | read/write | low | 星图位置传感器 |
| `logic_alarms_list` / `set` | read/write | low | 逻辑报警器通知设置 |

### 农业 (farming)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `farming_planting` / `set_planting` / `batch` | read/write | medium | 种植槽与种植请求 |
| `farming_harvestables` / `set_harvestable` | read/write | low | 可收获植物与收获标记 |
| `seed_catalog` | read | none | 种子目录 |
| `uproot_area` | write | medium | 铲除区域植物 |
| `genetic_analysis_stations` / `set` | read/write | low | 植物分析仪 |

### 畜牧 (ranching)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `critters_list` | read | none | 可抓捕小动物 |
| `dropoffs_list` / `configure` / `batch` | read/write | medium | 投放点过滤器与容量 |
| `incubators_list` / `configure` / `batch` | read/write | medium | 孵化器蛋请求 |
| `creature_lures_list` / `set` | read/write | low | 生物诱饵站 |

### 医疗 (medical)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `medical_patients` | read | none | 患者疾病与生命值 |
| `medical_clinics` / `set_clinic_threshold` / `batch` | read/write | low | 医疗床/诊所与阈值 |
| `doctor_stations` | read | none | 医生站药品库存 |
| `assign_medical_bed` | write | medium | 分配医疗床 |

### 火箭与太空 (rockets)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `rockets_list` / `rockets_status` / `rockets_detail` | read | none | 火箭列表、状态、详情 |
| `space_destinations_list` / `launch_pads_list` | read | none | 太空目的地与发射台 |
| `set_rocket_destination` / `request_rocket_launch` / `cancel_rocket_launch` | write/execute | medium | 目的地/发射/取消 |
| `rocket_modules_list` / `control` | read/write | medium | 模块管理 |
| `flight_utilities_list` / `control` | read/write | medium | 飞行模块操作 |
| `restrictions_list` / `set` | read/write | low | 火箭使用限制 |
| `crew_requests_list` / `set` | read/write | medium | 乘员召集 |
| `cargo_collectors` / `harvest_modules` | read | none | 货舱与钻探模块 |
| `self_destruct_list` / `trigger` | read/execute | **dangerous** | 火箭自毁（需 `confirm=true`）|
| `railguns_list` / `set` | read/write | medium | 轨道炮设置 |
| `missile_launchers_list` / `set` | read/write | medium | 导弹发射器 |

### 太空探索 (space)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `telescopes_list` / `control` | read/execute | low | 望远镜控制 |
| `starmap_analysis_targets` / `set` | read/write | low | 星图分析目标 |
| `warp_portals_list` / `control` | read/write | medium | 传送门控制 |
| `temporal_tears_list` / `consume` | read/execute | medium | 时间裂隙 |

### 剧情设施 (story)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `printerceptors_list` / `control` | read/write | medium | Printerceptor 拦截控制 |
| `remote_work_terminals_list` / `set` | read/write | low | 远程工作终端 dock |
| `lore_bearers_list` / `press` | read/execute | low | 阅读传说 |
| `artifacts_list` / `open` | read/execute | low | 文物分析 |
| `gene_shufflers_list` / `control` | read/write | medium | 基因重组 |

### 世界与地图 (world)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `world_list` | read | none | 已加载世界与激活世界 |
| `world_cell_info` | read | none | 指定格子元素、质量、温度、病菌 |
| `world_element_summary` | read | none | 世界元素质量和温度摘要 |
| `world_text_map` | read | none | 紧凑文本地图（支持 RLE 低 token）|
| `thermal_overheat_risk_scan` | read | none | 扫描建筑过热风险，按温差排序 |
| `area_define` / `area_get` / `area_list` / `area_forget` | read/write | low | 命名区域管理 |

### 相机与截图 (camera)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `camera_get_view` | read | none | 当前相机位置、缩放、激活世界 |
| `camera_set_view` / `camera_move` | execute | low | 设置/平移/跳转相机 |
| `camera_switch_view` | execute | low | 切换覆盖层（氧气/电力/温度/房间等）|
| `camera_focus_cell` / `camera_focus_dupe` | execute | low | 聚焦到格子或复制人 |
| `game_screenshot` | execute | low | 截图并返回 PNG 路径 |

### 游戏控制 (game)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `game_time` | read | none | 当前周期、时间百分比、暂停与速度 |
| `game_pause` / `game_resume` / `set_game_speed` | execute | low | 暂停/恢复/调速 |
| `list_saves` | read | none | 存档列表 |
| `save_game` / `load_save` / `quit_game` | execute | medium | 保存/加载/退出 |
| `set_sandbox_mode` | write | medium | 切换沙盒模式 |
| `list_dlc_activation` / `activate` | read/write | low | DLC 管理 |

### UI 交互 (ui)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `ui_actions_list` | read | none | 可安全触发的 UI Action 白名单 |
| `open_management_screen` / `trigger_ui_action` | execute | low | 打开管理界面/触发 UI 动作 |
| `game_notification_create` / `map_popup_text` | execute | low | 创建通知/地图浮动文字 |
| `map_marker_create` / `list` / `clear` | read/write | low | 临时地图标记管理 |

### 沙盒 (sandbox)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `sandbox_actions_list` / `sample_cell` | read | none | 沙盒操作列表与格子取样 |
| `paint_element` / `flood_fill_element` | write | **dangerous** | 绘制/填充元素（需 `confirm=true`）|
| `set_temperature_area` / `reveal_area` / `destroy_area` | write | **dangerous** | 调温/开图/销毁（需 `confirm=true`）|
| `spawn_entity` / `stamp_story_trait` | write | **dangerous** | 生成实体/放置故事特质（需 `confirm=true`）|
| `auto_plumb_building` | write | medium | 自动连接管道 |

### 研究 (research)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `research_status` / `list_research` | read | none | 研究状态与技术树 |
| `set_research` / `clear_research` | write | medium | 设置/清除研究目标 |

### 生产 (production)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `production_fabricators` / `production_recipes` | read | none | 制作站队列与配方 |
| `set_queue` / `batch` | write | medium | 设置制作队列 |
| `set_mutant_seeds` | write | low | 突变种子开关 |
| `configurable_consumers` / `set` | read/write | low | 可配置消费者 |

### 侧屏控件 (controls)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `filters_list` / `set_single_filter` / `set_tree_filter` | read/write | low | 气/液/固过滤器与元素传感器 |
| `side_options_list` / `set_direction` / `set_few_option` / `set_broadcast_channel` / `set_radbolt_direction` | read/write | low | 方向、选项、广播频道、辐射粒子方向 |
| `state_controls_list` / `set_capacity` / `set_checkbox` / `set_counter` / `set_time_range` | read/write | low | 容量、checkbox、计数器、时间范围 |
| `activation_ranges_list` / `set` / `batch` | read/write | low | 启停双阈值 |
| `n_toggles_list` / `set` | read/write | low | 多选侧屏控件 |
| `user_menu_actions_list` / `press` / `batch` | read/execute | low | 对象右键菜单（清扫、维修等）|
| `maintenance_actions_list` / `execute` / `batch` | read/execute | low | 维护操作（厕所清洁等）|

### 建筑高级配置 (buildings-config)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `set_threshold` / `set_slider` / `set_valve_flow` / `set_limit_valve` | write | low | 阈值、滑块、阀门流量、限量阀门 |
| `set_logic_timer` / `set_logic_ribbon_bit` | write | low | 逻辑计时器、Ribbon Bit |
| `set_door_state` | write | low | 门状态 |
| `get_access_control` / `set_access_control` | read/write | medium | 门禁权限 |
| `batch_set_building_configs` / `batch_set_automation_controls` | write | medium | 批量设置建筑配置 |

### 规划 Harness (planning)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `plan_harness_create` | write | medium | 创建规划-反馈-验证-实施会话 |
| `plan_harness_get` / `plan_harness_list` | read | none | 读取规划会话 |
| `plan_harness_record` | write | medium | 记录约束或反馈 |
| `plan_harness_validate` | read | none | 验证计划参数与工具存在性 |
| `plan_harness_execute` | execute | medium | 通过门禁后执行 plannedCalls |

### 工具发现与批量 (tools)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `tools_manifest` / `tools_search` / `tools_guide` | read | none | 工具清单/搜索/意图指南 |
| `tools_player_action_coverage` / `tools_static_audit` | read | none | 玩家操作覆盖审计/静态自检 |
| `tools_call_many` | execute | medium | 批量顺序调用（最多 20 个）|
| `database_query` | read | none | 查询游戏内置 Database/百科 |
| `edit_mark_request_create` / `list` / `clear` | read/write | low | 框选区域编辑请求管理 |

### 服务器与 MCP (server)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `server_status` | read | none | MCP 服务器状态 |
| `mcp_client_capabilities` | read | none | 客户端 sampling/elicitation/tasks 能力 |
| `mcp_sampling_request_create` / `mcp_elicitation_request_create` | read | none | 生成标准客户端请求对象 |
| `logs_tail` | read | none | 读取最近游戏日志 |

---

## 关键工作流

### 殖民地诊断流程

1. `colony_status` → 周期、复制人数、世界数
2. `colony_diagnostics` → 缺氧、断粮、过热诊断
3. `colony_alerts` → 当前警报
4. `resources_food` → 食物库存与保质期
5. `power_summary` → 电力系统健康度
6. 根据结果决定下一步：过热 → `thermal_overheat_risk_scan`；电力 → 电力审计流程

### 电力审计流程

1. `power_summary` → 整体电力状态（按 circuit 聚合）
2. 检查是否有 circuit 负载接近/超过 100%，或电池电量偏低
3. 如需明细，调 `buildings_config_list` 过滤电力相关建筑
4. 分析负载率、电池状态、导线是否过载
5. 给出优化建议：增加发电、增加电池、减少负载、分电路

### 房间规划流程

1. `rooms_list` → 所有房间类型、大小、边界和士气效果
2. 检查缺失的关键房间（Barracks、Great Hall、Washroom 等）
3. 对未满足条件的房间，用 `world_text_map` 或 `camera_switch_view`（rooms 覆盖层）定位
4. `plan_building` / `plan_building_rect` 补齐缺失建筑
5. 重新 `rooms_list` 验证房间是否成型

### 过热风险管理流程

1. `thermal_overheat_risk_scan`（或 `oni://thermal/overheat-risk?marginC=15`）→ 按温差排序扫描
2. 优先处理 `overheated` 状态设备
3. 对高风险区域用 `world_element_summary` 分析元素分布
4. 给出降温方案：增加冷却、改善通风、使用隔热砖、移除热源

### 火箭发射前检查

1. `rockets_status` / `rockets_detail` → 检查状态、燃料、氧化剂、乘员
2. `crew_requests_list` → 确认乘员已召集
3. `flight_utilities_list` → 检查货物/投放设置
4. `restrictions_list` → 确认使用限制已解除
5. `set_rocket_destination` → 设置目的地
6. `request_rocket_launch` → 请求发射

---

## 批量调用

`tools_call_many` 是万能批量工具：

- 最多 **20** 个子调用
- 支持 `dryRun: true` 预检（工具存在性、必填参数、危险工具 `confirm`）
- 支持 `defaults` / `defaultArguments` 合并到每个子调用
- 低 token 形态：`items: [{t:"tool_name",a:{...}}]`
- 默认 `requireAllValid: true`，任一预检失败则全部不执行
- 支持 `stopOnError: true`，首个执行错误处停止

领域批量工具也遵循同一套 `defaults` 约定：`user_menu_actions_batch_press`、`maintenance_actions_batch_execute`、`buildings_config_batch_set`、`production_queue_batch_set`、`activation_ranges_batch_set`、`receptacles_batch_control`、`storage_tile_selections_batch_set` 等。

---

## 低 Token 模式

| 场景 | 推荐方式 |
|------|----------|
| 工具搜索 | `tools_search detail=brief` |
| 工具清单 | `oni://tools/manifest?detail=brief` |
| 玩家操作查找 | `tools_player_action_coverage query=...&detail=brief` |
| 批量调用 | `items: [{t:"name",a:{}}]` + `defaults` |
| 文本地图初扫 | `world_text_map profile=scan encoding=rle` |
| 区域建筑速查 | `oni://buildings/configurables?areaId=xxx&limit=20` |

---

## 风险等级说明

| 等级 | 含义 | 需要 `confirm` |
|------|------|----------------|
| none | 只读查询，不修改存档 | 否 |
| low | 轻微影响（暂停、截图、开关建筑） | 否 |
| medium | 中等影响（修改优先级、分配、发射火箭） | 建议 `confirm=true` |
| dangerous | 可能大范围改变地图（挖掘、拆除、沙盒绘制） | **必须** `confirm=true` |

> 危险工具未传 `confirm=true` 会返回错误。`tools_call_many` 的 `dryRun` 会提前拦截此类问题。

---

## 常见别名映射

| 别名 | 正式名称 |
|------|----------|
| `get_colony_info` | `colony_status` |
| `get_duplicants` | `dupes_list` |
| `get_inventory` | `resources_inventory` |
| `pause_game` | `game_pause` |
| `resume_game` | `game_resume` |
| `set_game_speed` | `game_set_speed` |
| `get_buildings` | `buildings_list` |
| `get_building_summary` | `buildings_summary` |
| `thermal_risk_scan` / `overheat_risk_scan` | `thermal_overheat_risk_scan` |
| `schedule_list` | `schedules_list` |
| `power_circuits_summary` / `power_status` | `power_summary` |
| `room_list` / `rooms_overview` | `rooms_list` |
| `priorities_list` | `orders_priorities_list` |
| `priorities_set_area` | `orders_set_priority_area` |
| `buildings_plan` | `plan_building` |
| `buildings_plan_rect` | `plan_building_rect` |
| `buildings_plan_many` | `plan_many` |
| `conduits_cut` | `orders_cut_conduits` |

---

## Resources 速查

### 静态 Resources

| URI | 对应工具 | 说明 |
|-----|----------|------|
| `oni://colony/status` | `colony_status` | 殖民地状态 |
| `oni://colony/diagnostics` | `colony_diagnostics` | 诊断汇总 |
| `oni://colony/alerts` | `colony_alerts` | 警报 |
| `oni://colony/summary` | `colony_summary` | 行动规划摘要 |
| `oni://world/list` | `world_list` | 世界列表 |
| `oni://world/elements` | `world_element_summary` | 元素摘要 |
| `oni://power/summary` | `power_summary` | 电力系统摘要 |
| `oni://rooms/list` | `rooms_list` | 房间系统状态 |
| `oni://thermal/overheat-risk` | `thermal_overheat_risk_scan` | 过热风险扫描 |
| `oni://resources/inventory` | `resources_inventory` | 资源库存 |
| `oni://resources/food` | `resources_food` | 食物库存 |
| `oni://resources/pins` | `resources_pins` | 资源面板固定 |
| `oni://storage/list` | `resources_storage_list` | 储存列表 |
| `oni://diet/status` | `diet_status` | 饮食权限 |
| `oni://dupes` | `dupes_list` | 复制人列表 |
| `oni://dupes/priorities` | `dupes_priorities_list` | 个人优先级 |
| `oni://dupes/skills` | `dupes_skills_list` | 技能 |
| `oni://schedules` | `schedule_list` | 日程 |
| `oni://research/status` | `research_status` | 研究状态 |
| `oni://rockets/status` | `rockets_status` | 火箭状态 |
| `oni://plans` | `plan_harness_list` | 规划 Harness 会话 |
| `oni://mcp/sessions` | `mcp_client_capabilities` | MCP 会话与能力 |
| `oni://tools/manifest` | `tools_manifest` | 工具清单 |
| `oni://tools/guide` | `tools_guide` | 工具意图指南 |
| `oni://tools/player-action-coverage` | `tools_player_action_coverage` | 玩家操作覆盖审计 |
| `oni://tools/static-audit` | `tools_static_audit` | 静态接口审计 |
| `oni://game/time` | `game_time` | 游戏时间 |
| `oni://game/saves` | `game_saves_list` | 存档列表 |
| `oni://camera/view` | `camera_get_view` | 相机视图 |

### Resource Templates

| URI Template | 对应工具 | 说明 |
|--------------|----------|------|
| `oni://world/cell/{x}/{y}` | `world_cell_info` | 指定格子详情 |
| `oni://world/text-map{?...}` | `world_text_map` | 文本地图；`profile=scan&encoding=rle` 低 token 初扫 |
| `oni://power/summary{?worldId,includeDetails,limit}` | `power_summary` | 按世界过滤电力摘要 |
| `oni://rooms/list{?worldId,type,includeBuildings,limit}` | `rooms_list` | 按类型过滤房间 |
| `oni://thermal/overheat-risk{?worldId,marginC,limit}` | `thermal_overheat_risk_scan` | 按温差阈值过滤 |
| `oni://tools/manifest{?query,group,mode,risk,detail,limit}` | `tools_manifest` | 过滤后工具清单 |
| `oni://tools/search{?query,group,mode,risk,detail,limit}` | `tools_search` | 低 token 工具搜索 |
| `oni://tools/guide{?goal,detail}` | `tools_guide` | 按目标生成指南 |
| `oni://tools/read/{name}{?...}` | 任意 read 工具 | 将只读工具作为 resource 读取 |
| `oni://plans/{id}` | `plan_harness_get` | 读取指定规划会话 |

---

## Prompts 速查

| Prompt | 用途 | 关键参数 |
|--------|------|----------|
| `colony_triage` | 殖民地快速体检，优先发现缺氧、断粮、停电、复制人风险 | `focus`（oxygen/food/power/dupes）|
| `next_cycle_plan` | 根据当前状态生成下一周期行动计划 | `objective`, `riskTolerance` |
| `inspect_area` | 指定区域地图分析，优先使用文本地图 | `x1`, `y1`, `x2`, `y2` |
| `dupe_care_review` | 复制人需求、压力、日程和技能检查 | `dupe`（可选姓名）|
| `power_audit` | 电力系统健康度审计 | `worldId`, `detail` |
| `rooms_overview` | 房间系统状态与士气缺口检查 | `worldId`, `focus` |
| `thermal_audit` | 扫描过热风险与高温区域 | `worldId`, `marginC` |

---

> 本文档基于 ONI MCP Mod 源码生成，覆盖核心暴露层与完整注册表。完整参数请通过 `tools_search detail=full` 或 `oni://tools/manifest` 查询。
