# 来源、版本与核验范围

查阅日期：2026-10-23。此日期表示资料核查日期，不表示全部机制都已在当日最新游戏构建中实测。原文与 Wiki 的更新时间不同；使用时还要对照实际构建号。

## 本轮读到了什么

大叔追云彩的 [缺氧各种常用模块及数据大全](https://www.yuque.com/u25332524/ftq4u7)目录显示 146 篇文档，本次也读取了目录请求返回的 146 条文档元数据。累计打开下列 8 篇页面：读取其中 7 篇的相关正文；铁锈制氧页以图片为主，只读到少量文字。没有逐篇读完整个知识库，也未下载作者存档、复刻图片图纸或运行作者测试。

| 原文 | 页面日期或范围 | 本库使用位置 |
| --- | --- | --- |
| [细节技巧汇总（冷知识）](https://www.yuque.com/u25332524/ftq4u7/nn8f4t) | 部分内容标注更新至 U59；读取与本轮有关的条目 | 气体分层、水门失效、光照、保鲜；不照搬微量锁温方案 |
| [游戏电路知识](https://www.yuque.com/u25332524/ftq4u7/noqzgd) | 2022-08-06 | 过载、变压器限流与电池；并联/串联按实际端口连接澄清 |
| [蒸汽机+液冷热量计算](https://www.yuque.com/u25332524/ftq4u7/qgn49c) | 2021-06-01 | 热量收支、火山热负荷；不把示例机数与倒水量普遍化 |
| [常压制氧](https://www.yuque.com/u25332524/ftq4u7/xperdi) | 含 2021、2022 年多个版本 | 不同结构产率、启动、堵管恢复、余氢去向 |
| [高压制氧](https://www.yuque.com/u25332524/ftq4u7/gx0k5b) | 正文标 2021-08-18；目录记录 2026-03-15 更新 | 引导气体与结构差异；当前版本蓝图仍待验证 |
| [肉食供应的计算方法](https://www.yuque.com/u25332524/ftq4u7/umi7pk) | 2021-06-11 | 启动延迟、稳态、完整生命周期与补种 |
| [盐水泉调谐专题](https://www.yuque.com/u25332524/ftq4u7/tsugvcvl8vb44g78) | 正文含 2026-01-29 的版本 D；目录记录 2026-02-04 更新 | 盐与漂白石收支、避免断调、独立储电；重新计算门槛，不声称复现作者模块 |
| [铁锈制氧模块](https://www.yuque.com/u25332524/ftq4u7/agepam) | 正文标 2021-06-29；目录记录 2021-12-24 更新 | 仅确认专题存在与少量正文，逐格图片方案未核验；本库产量另据 Wiki |

目录的 `content_updated_at` 与正文内作者日期不是同一字段：例如常压制氧记录更新至 2026-06-22，细节技巧记录更新至 2026-07-06。它们不能证明每张图纸都随之更新，更不能代替适用构建号。这些文章提供作者的工程经验，不代表作者认可本库全部建议。本库重新组织文字，机制与版本变化再查 Wiki 和官方说明；不复制原文图纸、截图或下载包。

## Wiki 访问的限制

本轮尝试直接打开 Wiki 的 Mealwood、Food、Power Transformer、Metal Refinery 页面，抓取返回 403。随后使用网站限定搜索，读取搜索工具返回的索引文本，包括部分较长条目正文和数据表。**这些是检索索引证据，不是已成功打开每个页面全文或锁定页面修订号。**

各攻略在对应段落列有直接链接。主要核对了食物、冷媒、蒸汽机、电解、泵、净水、脱盐、储气、电池、电路、房间、火箭和太空条目。未列出具体修订号的数据，投入危险工程前须再次对照游戏面板；检索日期不能证明索引已经涵盖最新补丁。

发现过期或冲突时的处理：

- [食物资源条目](https://oxygennotincluded.wiki.gg/wiki/Food_%28Resource%29)中，未加保鲜香料的普通易腐食物需深冻与无菌气氛同时满足；Freshener Spice 是例外，见本页并行复核记录。不采用旧攻略里普通食物仅靠常温二氧化碳就永久保鲜的说法。
- [房间叠层](https://oxygennotincluded.wiki.gg/wiki/Room_Overlay)中部分旧条件与历史说明并存；房间奖励和新版装饰分类结合官方 U57 说明处理，不再要求用“装饰度大于 20”统一判断。
- [眼冒金星火箭总表](https://oxygennotincluded.wiki.gg/wiki/Rocketry_%28Spaced_Out%29)自身提示过期，不作为当前精确航程表。
- [喷孔驯化指南](https://oxygennotincluded.wiki.gg/wiki/Guide/Geyser_and_Vent_Taming)有相互不一致的旧数据与单位，本文只引用按热负荷配设备的思路，未采用其全部火山温度和机数表。

## 后续补充：管路与选型

同日继续改进时，Chrome 连接不可用，语雀直接抓取也不可用，因此没有新增语雀正文阅读记录；管道篇对作者第 26、40 项的引用来自上轮已读的细节技巧正文。新增 Wiki 查询仍通过网站限定搜索获取索引文本；直接打开 Gas Filter 与 Bridge Tricks 也返回 403。

| 核对内容 | 具体来源 | 采用范围 |
| --- | --- | --- |
| 容量与流量 | [液体管道](https://oxygennotincluded.wiki.gg/wiki/Liquid_Pipe)、[气体管道](https://oxygennotincluded.wiki.gg/wiki/Gas_Pipe)、[轨道](https://oxygennotincluded.wiki.gg/wiki/Conveyor_Rail)、[装载器](https://oxygennotincluded.wiki.gg/wiki/Conveyor_Loader) | 额定上限、流向与装载条件；不把额定值当现场实际流量 |
| 桥接与循环 | [桥接技巧](https://oxygennotincluded.wiki.gg/wiki/Bridge_Tricks)、[液体桥](https://oxygennotincluded.wiki.gg/wiki/Liquid_Bridge)、[储液库](https://oxygennotincluded.wiki.gg/wiki/Liquid_Reservoir) | 优先分支、补液、缓冲；表格是连接关系，不是已实测的逐格蓝图 |
| 分气及出口等待 | [过滤器](https://oxygennotincluded.wiki.gg/wiki/Gas_Filter)、[气体截断器](https://oxygennotincluded.wiki.gg/wiki/Gas_Shutoff)、[循环过滤](https://oxygennotincluded.wiki.gg/wiki/Guide/Loop_Filters) | 区分过滤与信号放行，不把单个省电分流器视为故障安全系统 |
| 空管与空轨信号 | [液体温度](https://oxygennotincluded.wiki.gg/wiki/Liquid_Pipe_Thermo_Sensor)、[气体元素](https://oxygennotincluded.wiki.gg/wiki/Gas_Pipe_Element_Sensor)、[液体元素](https://oxygennotincluded.wiki.gg/wiki/Liquid_Pipe_Element_Sensor)、[轨道温度](https://oxygennotincluded.wiki.gg/wiki/Conveyor_Rail_Thermo_Sensor) | 分别采用对应条目，避免把温度传感器保留信号的行为套给元素传感器 |
| 温控与合并逻辑 | [液冷机](https://oxygennotincluded.wiki.gg/wiki/Thermo_Aquatuner)、[自动化线](https://oxygennotincluded.wiki.gg/wiki/Automation_Wire)、[AND 门](https://oxygennotincluded.wiki.gg/wiki/AND_Gate) | 区分冷媒许可、环境限温与多个输出直连 |
| 特殊相变与停流 | [元素](https://oxygennotincluded.wiki.gg/wiki/Elements)、[隔热水管](https://oxygennotincluded.wiki.gg/wiki/Insulated_Liquid_Pipe) | 小包阈值只作进阶机制说明，未验证当前版本复杂合包工况 |

新增选型表是本库基于已列机制的工程建议；8 人用水、双电解气量、过渡池热容量和假设喷孔的算例均为计算，不是作者实测或当前存档参数。

## 官方版本依据

| 来源 | 确认的变化 | 使用边界 |
| --- | --- | --- |
| [Klei 700386 / U57](https://kleiforums.com/game-updates/oni-alpha/700386-r2633/) | 新增 Banquet Hall、降低旧餐厅士气奖励、调整装饰建筑判定、实验室不再要求光源 | 已打开官方正文并查阅太空收集、研究模块相关段落；具体 +4/+6 数值同时据 Wiki 房间条目 |
| [Klei 736649 / U59](https://kleiforums.com/game-updates/oni-alpha/736649-r2750/) | 多种原需辅助塑料的建筑改用垫圈；液体内气泡变化；加入绝缘导线 | 已打开官方正文；该更新能说明旧方案存在复核需求，不能单凭补丁断言所有旧高压模块已失效 |

## 后续补充：养殖补种与太空任务

同日继续核对养殖与太空条目，新增内容仍以 Wiki 网站限定搜索返回的索引正文为依据；没有新增语雀正文或实机验证。Klei U57 / 700386 官方页面已成功打开，并定位到开采与收集拆分、研究模块收集不可再生磁盘的说明。

| 内容 | 来源 | 使用范围 |
| --- | --- | --- |
| 补种时间、蛋的存活率 | [哈奇](https://oxygennotincluded.wiki.gg/wiki/Hatch)、[孵化器](https://oxygennotincluded.wiki.gg/wiki/Incubator)、[蛋](https://oxygennotincluded.wiki.gg/wiki/Egg) | 理想孵化与成年时间、避免长期把补种蛋存在物流系统；不保证牧场主实际唱到 |
| 移入、移出与计数 | [放生点](https://oxygennotincluded.wiki.gg/wiki/Critter_Drop-Off)、[拾取点](https://oxygennotincluded.wiki.gg/wiki/Critter_Pick-Up)、[小动物传感器](https://oxygennotincluded.wiki.gg/wiki/Critter_Sensor) | 区分 U51 后两个建筑的功能，物种、蛋与幼崽仍应在面板核对 |
| 太空收集与研究 | [Klei U57](https://kleiforums.com/game-updates/oni-alpha/700386-r2633/)、[DLC 研究模块](https://oxygennotincluded.wiki.gg/wiki/Research_Module_%28Spaced_Out%29)、[数据磁盘](https://oxygennotincluded.wiki.gg/wiki/Data_Disk) | 区分制作、采集、开采与运输；初始太空磁盘不可再生 |
| 新燃料路线 | [本体生物柴油发动机](https://oxygennotincluded.wiki.gg/wiki/Biodiesel_Engine)、[DLC 生物柴油发动机](https://oxygennotincluded.wiki.gg/wiki/Biodiesel_Engine_%28Spaced_Out%29) | 补充可选路线，不固定跨版本航程与燃料用量 |
| 舱内消耗与食品 | [复制人](https://oxygennotincluded.wiki.gg/wiki/Duplicant)、[氧石](https://oxygennotincluded.wiki.gg/wiki/Oxylite)、[Berry Sludge](https://oxygennotincluded.wiki.gg/wiki/Berry_Sludge)、[脱水机](https://oxygennotincluded.wiki.gg/wiki/Dehydrator)、[复水器](https://oxygennotincluded.wiki.gg/wiki/Rehydrator) | 普通复制人的基础预算，区分可食食物与复水所需资源；未覆盖全部特质或仿生复制人 |
| 舱壁与货舱接口 | [Spacefarer Module](https://oxygennotincluded.wiki.gg/wiki/Spacefarer_Module)、[游戏说明字段](https://oxygennotincluded.wiki.gg/wiki/Module%3AI18n/strings/buildings) | 区分地面直连、对外排放与货舱接头，避免因观察侧不同把端口名称解释反；未验证坐标布局 |

新增食品缺口、补种时长、6 周期飞行消耗与复水量为算例，仅验证算术；任务缓冲时间与资源余量仍由实际存档和任务确定。

## 并行复核：生存、工业、太空与水处理

本次三组分别复核生存养殖、工业运输和太空，主代理补制氧与水处理，并统一复算。Chrome 连接在此阶段恢复，新增读取盐水泉调谐与铁锈制氧两页；前文“当时不可用”的记录保留其时间范围。Wiki 新增数据仍为站点限定索引，直接打开多个建筑页继续返回 403；Klei U57 / U59 正文已成功读取。

| 内容 | 具体来源 | 本库采用与限制 |
| --- | --- | --- |
| 保鲜香料与灌溉 | [香料研磨机](https://oxygennotincluded.wiki.gg/wiki/Spice_Grinder)、[Gristle Berry](https://oxygennotincluded.wiki.gg/wiki/Gristle_Berry)、[Swampy Delights](https://oxygennotincluded.wiki.gg/wiki/Swampy_Delights) | 修正保鲜的绝对说法；按最终烹饪热量算理想种植量，不含收获和工时损失 |
| 房间升级 | [Klei U57](https://kleiforums.com/game-updates/oni-alpha/700386-r2633/)、[Great Hall](https://oxygennotincluded.wiki.gg/wiki/Great_Hall)、[Banquet Hall](https://oxygennotincluded.wiki.gg/wiki/Banquet_Hall) | 补宴会厅禁普通餐桌等条件；Great Hall 的具体装饰要求存在索引冲突，仍需当前游戏房间叠层确认 |
| 蒸汽机自冷 | [蒸汽机](https://oxygennotincluded.wiki.gg/wiki/Steam_Turbine)、[基地冷却](https://oxygennotincluded.wiki.gg/wiki/Guide/Base_Cooling) | 区分 100℃停转与损坏；95℃回水的自冷上界不能支持 200℃满流工况 |
| 火山与过滤 | [熔融铝](https://oxygennotincluded.wiki.gg/wiki/Molten_Aluminum)、[金属火山](https://oxygennotincluded.wiki.gg/wiki/Metal_Volcano)、[循环过滤](https://oxygennotincluded.wiki.gg/wiki/Guide/Loop_Filters) | 补假设铝火山显热预算及单阀 999.9 g/s 限制；平均预算不证明峰值，未把传感器“入口前”推成“必须与白口同格” |
| 发动机与氧化剂 | [DLC 氢气发动机](https://oxygennotincluded.wiki.gg/wiki/Hydrogen_Engine_%28Spaced_Out%29)、[小固态氧化剂舱](https://oxygennotincluded.wiki.gg/wiki/Small_Solid_Oxidizer_Tank)、[DLC 液态氧化剂舱](https://oxygennotincluded.wiki.gg/wiki/Liquid_Oxidizer_Tank_%28Spaced_Out%29) | 修正氢气火箭隐含液氧必需的写法；独立预算燃料与氧化剂，不套本体比例 |
| 收集与登陆 | [DLC 研究模块](https://oxygennotincluded.wiki.gg/wiki/Research_Module_%28Spaced_Out%29)、[平台](https://oxygennotincluded.wiki.gg/wiki/Rocket_Platform)、[Trailblazer](https://oxygennotincluded.wiki.gg/wiki/Trailblazer_Module) | 补收集停留时间、平台材料与单程着陆；未验证具体布局和施工时间 |
| 供氧与除菌 | [铁锈脱氧机](https://oxygennotincluded.wiki.gg/wiki/Rust_Deoxidizer)、[气压服存放柜](https://oxygennotincluded.wiki.gg/wiki/Atmo_Suit_Dock)、[净水器](https://oxygennotincluded.wiki.gg/wiki/Water_Sieve)、[氯气除菌](https://oxygennotincluded.wiki.gg/wiki/Guide/Disinfect_Germs) | 补替代供氧库存预算、首次充装需求、净化与除菌区别；不保证任意罐数均能全流除菌 |
| 盐水调谐 | [作者专题](https://www.yuque.com/u25332524/ftq4u7/tsugvcvl8vb44g78)、[调谐仪](https://oxygennotincluded.wiki.gg/wiki/Geotuner)、[盐水](https://oxygennotincluded.wiki.gg/wiki/Salt_Water)、[漂白石料斗](https://oxygennotincluded.wiki.gg/wiki/Bleach_Stone_Hopper) | 区分盐的理想闭环与黄金、制造能力、供电和工时；公式按实际喷发时段比较，不拿跨休眠平均值直接代入 |

## 尚未得到的证据

- 未取得当前存档的构建号、内容包与 Mod 清单；攻略不承诺覆盖所有组合。
- 未在当前 ONI 里测试水封、高压制氧、ST/AT、火山、保鲜或火箭；文中的验收步骤是后续操作要求，不是已通过的结果。
- 本地格式、链接与计算检查结果见下方核验记录。外部链接未逐一做可达性验证。需要宣称稳定运行时，应另记实际构建号、存档条件、观察周期及故障恢复结果。

## 本地核验记录

2026-10-23，并行修订汇总后：

- `quick_validate.py .`：技能格式通过。
- Python 复算 47 项算例：通过，覆盖此前 24 项及本次新增的作物灌溉、氧化剂、收集与登陆、自冷、火山、过滤、铁锈库存和调谐收支。只验证公式的算术关系与给定参数，不模拟游戏。
- 检查 13 篇 Markdown 的 35 个本地链接：目标均存在；同时检查了未跟踪文件的行末空白。
- `git diff --check`：通过。
- 共列出 112 个不同的外部 URL；这是链接清单统计，不是全部可达或全文读取的证明。

以上结果针对 OniMods 中的文档检查，不包含 ONI 实机测试，也不代表独立 oni-skills 仓库已同步。
