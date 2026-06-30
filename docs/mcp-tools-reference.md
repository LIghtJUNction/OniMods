# ONI MCP 工具参考

## 快速开始

- **MCP 服务器地址**：`http://localhost:8787/mcp/`
- **协议版本**：2025-11-25
- **公开工具总数**：8 个核心组合入口（历史细分工具已降为 hidden compatibility，精确名称仍可兼容调用）
- **会话要求**：非 `initialize` 请求必须携带 `Mcp-Session-Id` 和协商后的 `Mcp-Protocol-Version`

> **API 稳定性警告**：`oni_mcp` 在 `1.0.0` 之前不承诺工具、参数、资源和返回字段稳定。二创或第三方集成请锁定具体版本，并以运行时 `server_control domain=catalog action=manifest` / `oni://tools/manifest` 作为实际兼容依据。

> 提示：优先使用 8 个核心组合入口。未在 `tools/list` 中看到的旧工具仅作为兼容入口保留，仍可直接 `tools/call` 按精确名称调用。

---

## 工具分类索引

### 殖民地与诊断 (colony)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=snapshot action=get` | read | none | 高效状态快照：时间、诊断、食物、复制人、研究 |
| `colony_control domain=read action=status` | read | none | 周期、复制人数、世界数、暂停/速度状态 |
| `colony_control domain=diagnostic` | read/write | medium | 诊断汇总、告警、诊断设置与自动消毒：`action=diagnostics|alerts|list_settings|set_settings|set_auto_disinfect` |
| `colony_control domain=report` | read | none | 殖民地周期报告和面向行动规划的摘要；`action=report|summary` |
| `colony_control domain=notification` | read/write | medium | HUD 通知读取/点击/清除：`action=list/click/dismiss` |

### 复制人管理 (dupes)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=read action=dupes` | read | none | 列出所有复制人基本状态 |
| `dupes_control domain=info` | read | medium | 复制人基础只读信息：`action=detail/attributes/needs/status_check`；状态检查含位置、差事、需求、周边可达格 |
| `dupes_control domain=priority` | read/write | medium | 个人工作优先级读取/设置/批量/全局配置：`action=list/set/batch/settings_get/settings_set/reset` |
| `dupes_control domain=skill` | read/write | medium | 技能点与技能学习：`action=list/learn` |
| `dupes_control domain=hat` | read/write | medium | 更换帽子：`action=list/set` |
| `dupes_control domain=command` | execute | medium | 复制人直接动作与命名：`action=move_to/force_action/move_batch_to/rename/auto_rename`；强制动作使用 `commandAction=cancel_all/move_to/cancel_all_and_move` |
| `dupes_control domain=assignable` | read/write | medium | 床、餐桌、太空服等分配；`action=list/set/set_slot` |
| `dupes_control domain=side_screen` | read | medium | 复制人侧屏只读聚合：`action=direct_commands/equipment/todos/bionic_upgrades` |

### 日程管理 (schedules)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=management` | read/write | medium | 殖民地管理聚合：`kind=schedule/diet/research/medical`；保留各领域原 `action` |

### 资源与库存 (resources)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `read_control domain=resources` | read/write | medium | 资源读取、可拾取物搜索、资源面板固定与通知：`action=inventory/food/search_items/pins/set_pin` |
| `building_control domain=storage` | read/write | medium | 储存建筑列表/详情/过滤器：`action=list/detail/set_filter` |
| `schedule_control` / `diet_control` / `research_control` / `medical_control` | compatibility | hidden | 兼容旧入口；新调用使用 `colony_control domain=management kind=...` |
| `building_control domain=tile_selection` | read/write | medium | 储存砖目标物品：`action=list/set/batch` |
| `resources_control` | compatibility | hidden | 兼容旧入口；新调用使用 `read_control domain=resources` |

### 建筑管理 (buildings)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `read_control domain=buildings` | read | none | 建筑列表与统计摘要；`action=list/summary` |
| `building_control domain=planning` | read/execute | medium | 建造规划组合入口：`action=search_defs/materials/preview/placement_candidates/auto_connect` |
| `building_control domain=config` | read/write/execute | dangerous | 建筑配置：`action=list/list_automation/set_enabled/set_toggle/set_threshold/set_slider/set_valve_flow/set_limit_valve/set_logic_timer/set_logic_ribbon_bit/set_door_state/get_access/set_access/copy_settings/visual` |
| `configure_manual_delivery` / `copy_settings` | write | medium | 手动运送与复制设置 |
| `building_control domain=special kind=artable` | read/write | low | 艺术建筑外观：`action=list/set_stage` |
| `building_control domain=config action=visual kind=light` | read/write | medium | 灯光颜色：`action=list/set_color` |
| `building_control domain=config action=visual kind=pixel_pack` | read/write | medium | Pixel Pack 颜色面板：`action=list/set_color/copy_colors` |
| `build_planning_control` / `building_config_control` / `facility_control` | compatibility | hidden | 兼容旧入口；新调用使用 `building_control domain=planning/config/space_building/space_story/special/story_facility` |
| `visual_control` | compatibility | hidden | 兼容旧入口；新调用使用 `building_control domain=config action=visual kind=... visualAction=...` |
| `building_control domain=side_surface surface=geo_tuner` | read/write | medium | GeoTuner 喷泉分配：`action=list/list_geysers/assign` |
| `building_control domain=side_surface surface=facility` | read/write/execute | medium | 分发器、太空服柜、传说、打印舱、文物分析：`kind=dispenser/suit_locker/lore_bearer/telepad/artifact` |
| `building_control domain=side_surface` | read/write | high | 通用侧屏 surface：`kind=button/checklist/progress/related`，`action=list/press/focus` |
| `buildings_read_control` | compatibility | hidden | 兼容旧入口；新调用使用 `read_control domain=buildings` |

### 建造与命令 (orders)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `orders_control` | read/write/execute | **dangerous** | 订单聚合：`domain=area/priority/designation`；区域订单、优先级、指定/取消类操作都经此入口路由，保留各子动作 `confirm=true` 安全限制 |
| `navigation_control` | read/execute | medium | 可视 agent 指针聚合入口：`action=get/user_mouse/aim_cell/aim_world/nudge/select_tool/say/left_click/hold_left/jump/jump_point/clear`；跳转点子动作使用 `jumpPointAction=set/list/clear` |
| `priority_control` / `orders_area_action` / `designation_control` | compatibility | hidden | 兼容旧入口；新调用使用 `orders_control domain=priority/area/designation` |

### 电力系统 (power)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `read_control domain=infrastructure` | read | none | 电力/房间聚合读取：`action=power_summary/power_ports/rooms` |
| `infrastructure_read_control` | compatibility | hidden | 兼容旧入口；新调用使用 `read_control domain=infrastructure` |

### 房间系统 (rooms)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `read_control domain=infrastructure action=rooms` | read | none | 房间类型、大小、边界、对象计数和士气效果 |

### 自动化 (automation)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=config` | read/write/execute | dangerous | 逻辑/电力可配置控件：`action=list_automation` |
| `building_control domain=side_surface surface=automation` | read/write | medium | 自动化侧屏：`kind=automatable/critter_sensor`，`action=list/set/batch` |
| `building_control domain=space_building kind=comet_detector` | read/write | low | 彗星探测器目标：`action=list/set_target` |
| `building_control domain=space_building kind=cluster_location_sensor` | read/write | low | 星图位置传感器：`action=list/set` |
| `building_control domain=side_surface surface=misc kind=logic_alarm` | read/write | medium | 逻辑报警器通知设置：`action=list/set` |

### 农业 (farming)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=bio bioDomain=farming` | read/write | medium | 种植槽、种子目录、种植请求、收获标记和铲除；`action=list_planting/seed_catalog/list_harvestables/set_harvestable/set_planting/batch_set_planting/uproot` |
| `farming_planting_control` | compatibility | hidden | 兼容旧入口；新调用使用 `colony_control domain=bio bioDomain=farming` |
| `seed_catalog` | read | none | 种子目录 |

### 畜牧 (ranching)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=bio bioDomain=ranching kind=critters action=critters` | read | medium | 可抓捕小动物 |
| `colony_control domain=bio bioDomain=ranching kind=dropoff` | read/write | medium | 投放点过滤器与容量；`action=list/configure/batch` |
| `colony_control domain=bio bioDomain=ranching kind=incubator` | read/write | medium | 孵化器蛋请求；`action=list/configure/batch`，`incubatorAction=set/cancel/remove_occupant` |
| `ranching_control` | compatibility | hidden | 兼容旧入口；新调用使用 `colony_control domain=bio bioDomain=ranching` |
| `building_control domain=special kind=creature_lure` | read/write | low | 生物诱饵站：`action=list/set_bait` |

### 医疗 (medical)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `medical_patients` | read | none | 患者疾病与生命值 |
| `doctor_stations` | read | none | 医生站药品库存 |
| `assign_medical_bed` | write | medium | 分配医疗床 |

### 火箭与太空 (rockets)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=rocket rocketDomain=ops` | read/write/execute | medium | 火箭列表、状态、详情、太空目的地、发射台、目的地、往返、降落平台、发射/取消：`action=list/status/detail/list_destinations/list_launch_pads/set_destination/set_round_trip/set_landing_pad/request_launch/cancel_launch` |
| `set_rocket_destination` / `request_rocket_launch` / `cancel_rocket_launch` | write/execute | medium | 目的地/发射/取消 |
| `building_control domain=rocket rocketDomain=module` | read/write | medium | 模块管理：`action=list/list_defs/swap_up/swap_down/remove/cancel_remove/add/replace` |
| `building_control domain=rocket rocketDomain=flight_utility` | read/write | medium | 飞行模块操作：`action=list/empty/set_auto_deploy/set_target/clear_target/choose_duplicant/clear_duplicant` |
| `building_control domain=rocket rocketDomain=restriction` | read/write | low | 火箭使用限制：`action=list/set` |
| `building_control domain=rocket rocketDomain=crew_request` | read/write | medium | 乘员召集：`action=list/set` |
| `cargo_collectors` / `harvest_modules` | read | none | 货舱与钻探模块 |
| `building_control domain=rocket rocketDomain=self_destruct` | read/execute | **dangerous** | 火箭自毁：`action=list/trigger`，触发需 `confirm=true` |
| `building_control domain=space_building kind=railgun` | read/write | medium | 轨道炮设置：`action=list/set_launch_mass` |
| `building_control domain=special kind=missile_launcher` | read/write | medium | 导弹发射器：`action=list/set_ammunition` |

### 太空探索 (space)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=space_story` | read/write/execute | high | 太空/故事侧屏：`kind=telescope/starmap_analysis/warp_portal/temporal_tear/process_conditions` |
| `starmap_analysis_targets` / `set` | read/write | low | 星图分析目标 |
| `building_control domain=space_story` | read/write/execute | high | 传送门、时间裂隙和过程条件：`kind=warp_portal/temporal_tear/process_conditions` |

### 剧情设施 (story)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=story_facility` | read/write | medium | 剧情设施：`kind=printerceptor/poi_tech_unlock/remote_work_terminal/genetic_analysis_station`，含远程工作终端 dock 与植物分析仪种子权限 |
| `building_control domain=side_surface surface=facility` | read/execute | medium | 传说阅读、打印舱导航、文物分析弹窗：`kind=lore_bearer/telepad/artifact` |
| `building_control domain=special kind=gene_shuffler` | read/write | medium | 基因重组：`action=list/complete/request_recharge/cancel_recharge/toggle_recharge` |

### 世界与地图 (world)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=read action=worlds` | read | none | 已加载世界与激活世界 |
| `read_control domain=world action=cell_info` | read | none | 指定格子元素、质量、温度、病菌 |
| `read_control domain=world action=element_summary` | read | none | 世界元素质量和温度摘要 |
| `read_control domain=world action=text_map` | read | none | 文本地图，默认 standard plain，用固定宽度 token 输出每格；RLE 仅用于大范围低 token 扫描 |
| `read_control domain=world action=area_snapshot` | execute | low | 区域上下文包：文本地图 + utility overlay + 可选截图 |
| `read_control domain=world action=layout_candidates` | read | none | 平面布局候选：房间矩形、需挖掘/铺砖、危险和连通性评分 |
| `read_control domain=world action=thermal_overheat_risk` | read | none | 扫描建筑过热风险，按温差排序 |
| `read_control domain=area` | read/write | low | 命名区域、自动地图块和区域拼接管理：`action=define/get/list/blocks/merge/forget` |

### 相机与截图 (camera)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `navigation_control` | execute | low | 相机聚合入口：`action=get_view/set_active_world/set_view/move/switch_view/focus_cell/focus_dupe/screenshot/coordinate_screenshot` |

地图分析默认使用 `read_control domain=world action=text_map` 或 `read_control domain=world action=area_snapshot`，截图只作为视觉补充：

| 场景 | 首选 | 原因 |
|------|------|------|
| 挖掘、铺砖、建造、电线/管路规划 | `read_control domain=world action=area_snapshot preset=planning/utilities` | 返回精确坐标、plain 地形、建筑/复制人对象、utility overlay、规划摘要 |
| 平面结构候选 | `read_control domain=world action=layout_candidates purpose=lab|barracks|bathroom` | 返回候选矩形、评分、需挖掘、需铺砖、危险格和可达性 |
| 常规地形/对象读图 | `read_control domain=world action=text_map profile=standard encoding=plain` | 返回 areaId、origin、行列坐标和固定宽度 token，适合 agent 直接规划 |
| 全图分块巡检 | `read_control domain=area action=blocks` → `read_control domain=world action=text_map areaId=blkN` | 把世界切成 `blk*` 小块句柄，逐块读取；`read_control domain=world action=text_map includeChunks=true` 可在大区域响应中内联每块预览 |
| 大范围低 token 初扫 | `read_control domain=world action=text_map profile=scan encoding=rle` | 输出更短但可读性差，仅在范围很大时使用 |
| 精确校验某个格子 | `read_control domain=world action=cell_info` | 返回单格元素、质量、温度、可见性 |
| 房间视觉、装饰、作物阶段、UI 覆盖层确认 | `navigation_control action=switch_view` + `navigation_control action=screenshot` | 这些信息更接近人眼观察，文本地图可能不表达完整视觉状态 |

`read_control domain=world action=text_map profile=standard encoding=plain` 的文本行使用固定宽度 token，不再要求 agent 解读单字母格子：`sol` 自然固体，`tile` 已建地砖/地基，`oxy/po2/co2/hyd` 气体，`liq` 液体，`bld/dup/itm/bp` 为建筑、复制人、散落物和蓝图 overlay。建筑/蓝图会按完整 footprint 覆盖所有占用格；`bld_anchor`/`bp_anchor` 是 lower-left anchor，调用指针建造时点对象列表里的 `anchor`。执行建造/挖掘/清扫类工具时使用地图返回的世界绝对坐标。

`read_control domain=area action=define` 生成手工区域 `a*`；`read_control domain=area action=blocks` 按世界边界自动生成地图块 `blk*`；`read_control domain=world action=text_map` / `read_control domain=world action=area_snapshot` 大区域自动分块生成 `snap*`。默认块大小约 `40x40`，可传 `blockWidth/blockHeight/maxCells` 调整。`blk*`、`snap*` 与 `a*` 是同一种 areaId 协议，可直接用于 `read_control domain=world action=text_map areaId=blk7`、`read_control domain=world action=area_snapshot areaId=snap7`，以及支持 areaId 的整块工具。

多个区域可以临时拼接：`areaId=blk1+blk2+blk3` 会按同一世界内这些区域的外接矩形读取或编辑。需要长期复用时，用 `read_control domain=area action=merge areaIds=["blk1","blk2","blk3"]` 生成新的 `a*` 句柄。拼接不是多边形裁剪；非相邻区域会包含中间空隙，`dryRun=true` 会返回 `continuity=false`、`gapCellsPercent` 和 warning。

`read_control domain=world action=text_map view=temperature` 输出温度 token：`frz/cold/mild/hot/xhot`。`read_control domain=world action=area_snapshot` 默认返回 JSON，`maps.base` 是基础地形/对象，并额外返回 `areaDescription` 概括主要固体/液体区段。`terrain/construction` 默认包含地图行；`utilities/planning/all` 默认省略地图行，只保留摘要、对象和规划信息，需要原始网格时显式传 `includeRows=true`。`profile=scan` 会把 overlay 改成稀疏输出。`planning.hazards` 默认返回按行区段和少量坐标样本，避免把大面积危险格完整 dump 出来。`includeScreenshot=true` 会保存当前相机画面路径，但截图不自动保证覆盖传入矩形，除非调用前先移动相机。

### 游戏控制 (game)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `game_control domain=speed action=time` | read | none | 当前周期、时间百分比、暂停与速度 |
| `game_control domain=speed` | execute | low | 统一暂停/恢复/调速：`action=pause/resume/set_speed` |
| `game_control domain=save` | read/execute | dangerous | 存档列表、保存、加载、退出：`action=list/save/load/quit` |
| `game_control domain=state` | write | medium | 切换沙盒模式：`action=set_sandbox_mode` |
| `game_control domain=dlc` | read/write | dangerous | DLC 管理：`action=list/activate` |

### UI 交互 (ui)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `game_control domain=ui uiDomain=action` | read/execute | low | 可安全触发的 UI Action 白名单、管理界面和动作：`action=list/open_management/trigger` |
| `game_control domain=ui uiDomain=feedback` | execute | low | 创建通知/地图浮动文字、管理临时地图标记：`action=notification/popup/marker`；标记用 `markerAction=create/list/clear` |
| `ui_action_control` / `ui_feedback_control` | compatibility | hidden | 兼容旧入口；新调用使用 `game_control domain=ui uiDomain=action/feedback/edit_mark` |

### 沙盒 (sandbox)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `game_control domain=sandbox` | read/write | **dangerous** | 沙盒读取、区域操作、实体生成和地图文本指定：`kind=read/area/entity/map_designate`；地图编辑用 `search`/`designate` 自动定位，默认 `dryRun=true`，执行需 `confirm=true` |

### 研究 (research)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `colony_control domain=management kind=research action=status` | read | none | 当前研究目标、队列和进度 |
| `colony_control domain=management kind=research action=list` | read | none | 搜索技术树与可研究科技 |
| `colony_control domain=management kind=research` | write | medium | 设置/清除研究目标：`action=set/clear` |

### 生产 (production)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=production` | read/write | medium | 制作站队列、配方和突变种子开关；`action=list_fabricators/list_recipes/set/batch/mutant_seed_list/mutant_seed_set` |
| `building_control domain=side_surface surface=misc kind=configurable_consumer` | read/write | medium | 可配置消费者：`action=list/set_option` |

### 侧屏控件 (controls)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=filter` | read/write | medium | 气/液/固过滤器、元素传感器、树形/平铺过滤器；`action=list/set`，`kind` 参数选择 any/single/tree/flat |
| `building_control domain=side_surface surface=option` | read/write | medium | 方向、选项、广播频道、辐射粒子方向；`action=list/set`，`kind` 参数选择具体控件类型 |
| `building_control domain=config action=state_list/state_set` | read/write | medium | 容量、checkbox、计数器、时间范围；`kind` 参数选择具体控件类型 |
| `building_control domain=side_surface surface=activation` | read/write | medium | 启停双阈值：`action=list/set/batch` |
| `building_control domain=side_surface surface=misc kind=n_toggle` | read/write | medium | 多选侧屏控件：`action=list/set` |
| `building_control domain=side_surface surface=user_menu/maintenance` | read/execute | high | 用户菜单动作聚合：`domain=user_menu action=list/press/batch`，`domain=maintenance action=list/execute/batch` |

### 建筑高级配置 (buildings-config)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `building_control domain=config` | read/write/execute | dangerous | 启用、手动开关、阈值、滑块、阀门流量、限量阀门、逻辑计时器、Ribbon Bit、门状态、门禁权限、复制设置和批量配置：`action=list/list_automation/set_enabled/set_toggle/set_threshold/set_slider/set_valve_flow/set_limit_valve/set_logic_timer/set_logic_ribbon_bit/set_door_state/get_access/set_access/copy_settings/batch_set/batch_set_automation` |

### 工具发现与批量 (tools)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `server_control domain=catalog action=manifest/search/guide` | read | none | 工具清单/搜索/意图指南 |
| `server_control domain=catalog action=coverage` / `server_control domain=catalog action=static_audit` | read | none | 玩家操作覆盖审计/静态自检 |
| `server_control domain=catalog action=surface_audit` | read | none | surface 覆盖审计：`surface=side_screen/user_menu/management/tool_menu/ui_menu/global_control/notification` |
| `surface_audit_control` | compatibility | hidden | 兼容旧入口；新调用使用 `server_control domain=catalog action=surface_audit surface=...` |
| `server_control domain=batch action=call_many` | execute | medium | 批量顺序调用（最多 20 个）|
| `server_control domain=program action=execute` | execute | medium | 受限流程 DSL：变量、if/while/repeat、条件调用工具 |
| `read_control domain=knowledge kind=database action=query` | read | none | 查询游戏内置 Database/百科 |
| `read_control domain=knowledge kind=guide action=query` | read | none | 查询结构化玩家机制/公式速查（热量、制氧、保鲜、养殖、电力、自动化等）|
| `game_control domain=ui uiDomain=edit_mark` | read/write | low | 框选区域编辑请求管理：`action=create/list/clear` |

### 服务器与 MCP (server)

| 工具 | 模式 | 风险 | 用途 |
|------|------|------|------|
| `server_control` | read | none | 推荐：服务器/MCP 组合入口，`domain=diagnostics action=status/capabilities/logs_tail` 或 `domain=client_request action=create_sampling/create_elicitation` |

---

## 关键工作流

### 殖民地诊断流程

1. `colony_control domain=read action=status` → 周期、复制人数、世界数
2. `colony_control domain=diagnostic action=diagnostics` → 缺氧、断粮、过热诊断
3. `colony_control domain=diagnostic action=alerts` → 当前警报
4. `read_control domain=resources action=food` → 食物库存与保质期
5. `read_control domain=infrastructure action=power_summary` → 电力系统健康度
6. 根据结果决定下一步：过热 → `read_control domain=world action=thermal_overheat_risk`；电力 → 电力审计流程

### 电力审计流程

1. `read_control domain=infrastructure action=power_summary` → 整体电力状态（按 circuit 聚合）
2. 检查是否有 circuit 负载接近/超过 100%，或电池电量偏低
3. 如需明细，调 `building_control domain=config action=list/list_automation` 过滤电力相关建筑
4. 分析负载率、电池状态、导线是否过载
5. 给出优化建议：增加发电、增加电池、减少负载、分电路

### 房间规划流程

1. `read_control domain=infrastructure action=rooms` → 所有房间类型、大小、边界和士气效果
2. 检查缺失的关键房间（Barracks、Great Hall、Washroom 等）
3. 对未满足条件的房间，用 `read_control domain=world action=text_map` 或 `navigation_control action=switch_view`（rooms 覆盖层）定位
4. `navigation_control action=select_tool` + `action=left_click/hold_left` 补齐缺失建筑
5. 重新 `read_control domain=infrastructure action=rooms` 验证房间是否成型

### 过热风险管理流程

1. `read_control domain=world action=thermal_overheat_risk`（或 `oni://thermal/overheat-risk?marginC=15`）→ 按温差排序扫描
2. 优先处理 `overheated` 状态设备
3. 对高风险区域用 `read_control domain=world action=element_summary` 分析元素分布
4. 给出降温方案：增加冷却、改善通风、使用隔热砖、移除热源

### 火箭发射前检查

1. `building_control domain=rocket rocketDomain=ops action=status/detail` → 检查状态、燃料、氧化剂、乘员
2. `building_control domain=rocket rocketDomain=crew_request action=list` → 确认乘员已召集
3. `building_control domain=rocket rocketDomain=flight_utility action=list` → 检查货物/投放设置
4. `building_control domain=rocket rocketDomain=restriction action=list` → 确认使用限制已解除
5. `set_rocket_destination` → 设置目的地
6. `request_rocket_launch` → 请求发射

---

## 批量调用

`server_control domain=batch action=call_many` 是万能批量工具：

- 最多 **20** 个子调用
- 支持 `dryRun: true` 预检（工具存在性、必填参数、危险工具 `confirm`）
- 支持 `defaults` / `defaultArguments` 合并到每个子调用
- 低 token 形态：`items: [{t:"tool_name",a:{...}}]`
- 默认 `requireAllValid: true`，任一预检失败则全部不执行
- 默认 `responseMode: "summary"`，只返回每项状态和截断文本；需要完整子工具内容时显式传 `responseMode: "full"`
- `responseMode: "errors"` 只返回错误项，同时用 `attempted` / `executed` / `omitted` 区分尝试、实际执行和省略结果
- 支持 `stopOnError: true`，首个执行错误处停止
- 同批次重复的 write/execute 调用会返回 `warnings`。看到零效果结果时不要原样重复同一工具和同一区域，应读取结果字段后更换工具或修正参数。

领域批量工具也遵循同一套 `defaults` 约定：`building_control domain=side_surface surface=user_menu action=batch`、`building_control domain=side_surface surface=maintenance action=batch`、`building_control domain=config action=batch_set`、`building_control domain=production action=batch`、`building_control domain=side_surface surface=activation action=batch`、`building_control domain=receptacle action=batch`、`building_control domain=tile_selection action=batch` 等。

`server_control domain=program action=execute` 用于需要条件分支或循环的小型 agent 流程。它执行受限 JSON DSL，不执行任意 C#/shell 代码；脚本通过 `op=call` 或快捷 `jump`、`nudge`、`select`、`click`、`drag`、`readCell` 调用现有 MCP 工具。字符串 `$name.path` 会读取变量路径，工具返回 JSON 可用 `saveAs` 保存后参与条件判断：

```json
{
  "program": {
    "steps": [
      { "op": "jump", "x": 80, "y": 135 },
      { "op": "readCell", "saveAs": "cell" },
      {
        "op": "if",
        "when": { "eq": [ "$cell.element", "Water" ] },
        "then": [
          { "op": "select", "tool": "mop" },
          { "op": "click", "confirm": true }
        ],
        "else": [
          { "op": "nudge", "direction": "right", "steps": 1 }
        ]
      }
    ]
  },
  "maxSteps": 80
}
```

清扫/拖地区分：

```json
{ "name": "orders_control", "arguments": { "domain": "area", "action": "sweep", "x1": 10, "y1": 20, "x2": 20, "y2": 30, "confirm": true } }
{ "name": "orders_control", "arguments": { "domain": "area", "action": "mop", "x1": 10, "y1": 20, "x2": 20, "y2": 30, "confirm": true } }
```

`orders_control domain=area action=sweep` 只处理固体散落物/碎片。地上的水、污水、漏液或任何液体格子使用 `domain=area action=mop`。如果 sweep 返回 `marked=0`、`liquidCellsInRect>0` 或 `mopHint`，不要重复调用同一区域 sweep。

建造蓝图优先使用可视 agent 指针。使用 `building_control domain=planning action=search_defs/materials` 选择建筑和材料，然后 `building_control domain=planning action=preview` 预检单个 lower-left anchor；确认后用 `navigation_control action=jump/aim_cell` → `navigation_control action=select_tool tool=build` → `navigation_control action=left_click/hold_left` 放置。`navigation_control action=jump code=mouse` 可把指针跳到玩家当前鼠标所在格；指针移动不会默认移动相机，确实需要跟镜头时传 `moveCamera=true`。`building_control domain=planning action=search_defs` 的 `placement.anchor=lowerLeftCell` 表示 x/y 或指针格是 footprint 左下锚点，不是视觉中心。

省略 `agentId` 时使用全局默认 `agent` 指针；显式传入 `agentId` 时跨 session 复用同一指针，适合多步定位、选工具、点击链路。多步操作建议第一步用 `navigation_control action=get agentId=planner` 或 `action=jump/aim_cell agentId=builder` 建立指针，之后每次 `navigation_control` 都传同一个 `agentId`。可见动作尽量传 `displayText`，用 6-40 字给玩家说明当前位置、已选工具或即将执行的操作。需要第二个并行指针时，换一个 `agentId`。不再需要某个指针时，用 `navigation_control action=clear agentId=...` 删除它及其跳转点。回到主菜单或游戏世界未加载时，指针会自动隐藏。

这些指针动作大多都支持可选 `displayText`，传入后会立刻在 agent 指针旁短暂显示该文本。

`navigation_control action=hold_left` 默认只允许 1x1 footprint 的建筑。床、厕所、机器等多格建筑建议逐个 anchor 用 `navigation_control action=left_click` 放置。只有明确接受重复 footprint 拖拽时才传 `allowFootprintDrag=true`。

电线、管路、梯子和砖块路线使用指针拖拽；折线拆成多段水平/垂直 `navigation_control action=hold_left`：

```json
{
  "plannedCalls": [
    { "name": "navigation_control", "arguments": { "action": "jump", "x": 80, "y": 135 } },
    { "name": "navigation_control", "arguments": { "action": "select_tool", "tool": "build", "prefabId": "Wire", "material": "auto" } },
    { "name": "navigation_control", "arguments": { "action": "hold_left", "direction": "right", "length": 9, "confirm": true } }
  ]
}
```

快速画线使用 `navigation_control action=hold_left`：

```json
{ "direction": "right", "length": 9, "confirm": true }
```

折线使用 `path` / `points` / `pts`：

```json
{ "p": "Wire", "path": [[80,135],[88,135],[88,138]] }
```

`line/l` 和 `path/points` 只接受水平/垂直段。`r` 是矩形填充，不要把 `r:[x1,y1,x2,y2]` 当作普通直线，除非宽或高为 1。

---

## 低 Token 模式

| 场景 | 推荐方式 |
|------|----------|
| 工具搜索 | `server_control domain=catalog action=search detail=brief` |
| 工具清单 | `oni://tools/manifest?detail=brief` |
| 玩家操作查找 | `server_control domain=catalog action=coverage query=...&detail=brief` |
| 机制/公式速查 | `oni://guide/mechanics?query=电解器&detail=brief` |
| 批量调用 | `items: [{t:"name",a:{}}]` + `defaults` + 默认 `responseMode=summary` |
| 常规观察 | `colony_control domain=snapshot action=get profile=brief` |
| 文本地图初扫 | `read_control domain=world action=text_map profile=standard encoding=plain` |
| 区域规划快照 | `read_control domain=world action=area_snapshot preset=construction includeRows=false` |
| 区域建筑速查 | `oni://buildings/configurables?areaId=xxx&limit=20` |

---

## 风险等级说明

| 等级 | 含义 | 需要 `confirm` |
|------|------|----------------|
| none | 只读查询，不修改存档 | 否 |
| low | 轻微影响（暂停、截图、开关建筑） | 否 |
| medium | 中等影响（修改优先级、分配、发射火箭） | 建议 `confirm=true` |
| dangerous | 可能大范围改变地图（挖掘、拆除、沙盒绘制） | **必须** `confirm=true` |

> 危险工具未传 `confirm=true` 会返回错误。`server_control domain=batch action=call_many` 的 `dryRun` 会提前拦截此类问题。

---

## 常见别名映射

| 别名 | 正式名称 |
|------|----------|
| `get_colony_info` | `colony_control domain=read action=status` |
| `get_duplicants` | `colony_control domain=read action=dupes` |
| `get_inventory` | `read_control domain=resources action=inventory` |
| `pause_game` | `game_control domain=speed action=pause` |
| `resume_game` | `game_control domain=speed action=resume` |
| `set_game_speed` | `game_control domain=speed action=set_speed` |
| `get_buildings` | `read_control domain=buildings action=list` |
| `get_building_summary` | `read_control domain=buildings action=summary` |
| `thermal_risk_scan` / `overheat_risk_scan` | `read_control domain=world action=thermal_overheat_risk` |
| `disinfect_area` | `orders_control domain=area action=disinfect` |
| `power_circuits_summary` / `power_status` | `read_control domain=infrastructure action=power_summary` |
| `room_list` / `rooms_overview` | `read_control domain=infrastructure action=rooms` |
| `orders_control` | `orders_action_control`, `map_orders_control` |
| `priorities_list` | `orders_control domain=priority action=list` |
| `priorities_set_area` | `orders_control domain=priority action=set_area` |

---

## Resources 速查

### 静态 Resources

| URI | 对应工具 | 说明 |
|-----|----------|------|
| `oni://colony/status` | `colony_control domain=read action=status` | 殖民地状态 |
| `oni://colony/diagnostics` | `colony_control domain=diagnostic action=diagnostics` | 诊断汇总 |
| `oni://colony/alerts` | `colony_control domain=diagnostic action=alerts` | 警报 |
| `oni://colony/report` | `colony_control domain=report action=report` | 殖民地周期报告 |
| `oni://colony/summary` | `colony_control domain=report action=summary` | 行动规划摘要 |
| `oni://world/list` | `colony_control domain=read action=worlds` | 世界列表 |
| `oni://world/elements` | `read_control domain=world action=element_summary` | 元素摘要 |
| `oni://power/summary` | `read_control domain=infrastructure action=power_summary` | 电力系统摘要 |
| `oni://rooms/list` | `read_control domain=infrastructure action=rooms` | 房间系统状态 |
| `oni://thermal/overheat-risk` | `read_control domain=world action=thermal_overheat_risk` | 过热风险扫描 |
| `oni://resources/inventory` | `read_control domain=resources action=inventory` | 资源库存 |
| `oni://resources/food` | `read_control domain=resources action=food` | 食物库存 |
| `oni://resources/pins` | `read_control domain=resources action=pins` | 资源面板固定 |
| `oni://storage/list` | `building_control domain=storage action=list` | 储存列表 |
| `oni://diet/status` | `colony_control domain=management kind=diet action=status` | 饮食权限 |
| `oni://dupes` | `colony_control domain=read action=dupes` | 复制人列表 |
| `oni://dupes/status-check` | `dupes_control domain=info action=status_check` | 复制人状态/被困检查 |
| `oni://dupes/priorities` | `dupes_control domain=priority action=list` | 个人优先级 |
| `oni://dupes/skills` | `dupes_control domain=skill action=list` | 技能 |
| `oni://schedules` | `colony_control domain=management kind=schedule action=list` | 日程 |
| `oni://research/status` | `colony_control domain=management kind=research action=status` | 研究状态 |
| `oni://story/poi-tech-unlocks` | `building_control domain=story_facility kind=poi_tech_unlock action=list` | 信息传送通道 |
| `oni://rockets/status` | `building_control domain=rocket rocketDomain=ops action=status` | 火箭状态 |
| `oni://mcp/sessions` | `server_control domain=diagnostics action=capabilities` | MCP 会话与能力 |
| `oni://tools/manifest` | `server_control domain=catalog action=manifest` | 工具清单 |
| `oni://tools/guide` | `server_control domain=catalog action=guide` | 工具意图指南 |
| `oni://guide/mechanics` | `read_control domain=knowledge kind=guide action=query` | 机制公式速查 |
| `oni://tools/player-action-coverage` | `server_control domain=catalog action=coverage` | 玩家操作覆盖审计 |
| `oni://tools/static-audit` | `server_control domain=catalog action=static_audit` | 静态接口审计 |
| `oni://game/time` | `game_control domain=speed action=time` | 游戏时间 |
| `oni://game/saves` | `game_control domain=save action=list` | 存档列表 |
| `oni://game/dlc` | `game_control domain=dlc action=list` | DLC 存档激活状态 |
| `oni://camera/view` | `navigation_control action=get_view` | 相机视图 |

### Resource Templates

| URI Template | 对应工具 | 说明 |
|--------------|----------|------|
| `oni://world/cell/{x}/{y}` | `read_control domain=world action=cell_info` | 指定格子详情 |
| `oni://world/search{?...}` | `read_control domain=world action=search` | 地图元素、建筑、散落物和复制人搜索 |
| `oni://world/text-map{?...}` | `read_control domain=world action=text_map` | 文本地图；默认 `profile=standard&encoding=plain` 固定宽度 token，大范围低 token 初扫才用 `profile=scan&encoding=rle` |
| `oni://power/summary{?worldId,includeDetails,limit}` | `read_control domain=infrastructure action=power_summary` | 按世界过滤电力摘要 |
| `oni://rooms/list{?worldId,type,includeBuildings,limit}` | `read_control domain=infrastructure action=rooms` | 按类型过滤房间 |
| `oni://thermal/overheat-risk{?worldId,marginC,limit}` | `read_control domain=world action=thermal_overheat_risk` | 按温差阈值过滤 |
| `oni://game/saves{?type,limit}` | `game_control domain=save action=list` | 按位置读取存档列表 |
| `oni://game/dlc{?includeCosmetic}` | `game_control domain=dlc action=list` | 读取 DLC 存档激活状态 |
| `oni://tools/manifest{?query,group,mode,risk,detail,limit}` | `server_control domain=catalog action=manifest` | 过滤后工具清单 |
| `oni://tools/search{?query,group,mode,risk,detail,limit}` | `server_control domain=catalog action=search` | 低 token 工具搜索 |
| `oni://tools/guide{?goal,detail}` | `server_control domain=catalog action=guide` | 按目标生成指南 |
| `oni://guide/mechanics{?query,category,detail,limit}` | `read_control domain=knowledge kind=guide action=query` | 按关键词/分类查询机制、公式和边界条件 |
| `oni://tools/read/{name}{?...}` | 任意 read 工具 | 将只读工具作为 resource 读取 |
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

> 本文档基于 ONI MCP Mod 源码生成，覆盖 8 个核心组合入口及其主要 domain/action。完整参数请通过 `server_control domain=catalog action=search detail=full` 或 `oni://tools/manifest` 查询。
