# ONI 744825：运行中新增星球的原生边界

2026-10-21，本机已安装版本的方法体调查；没有启动游戏、修改游戏或 CycleTrim，也没有执行星球导入。

**结论：原生确实支持运行中新增 WorldContainer，但已验证的调用是火箭内舱，并且只能使用现有全局模拟网格中放得下的空矩形。没有找到可安全扩大正在运行的完整网格、并同步重建所有附属系统的原生入口。星图有空格，不代表物理网格还有位置。**

因此，现有存档若确实有足够大的未使用矩形，可以继续验证不重新载入整局的真实星球接入；普通存档不保证放得下另一颗 256×384 的星球。不能把火箭内舱创建成功、WorldContainer 注册成功，或复制 Grid 数组，称为完整星球迁移成功。

## 证据与复现

| 输入 | SHA-256 |
| --- | --- |
| `OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll` | `6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1` |
| `OxygenNotIncluded_Data/Managed/Assembly-CSharp-firstpass.dll` | `117fcc6962eeed82b4929347b9a466fb41e807a01b5a9a7acc8bdc446da276c7` |
| `OxygenNotIncluded_Data/Plugins/libSimDLL.so` | `85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d` |

安装目录为 `/home/lightjunction/.local/share/Steam/steamapps/common/OxygenNotIncluded`。实际反编译的 `Game.OnPrefabInit` 含当前 changelist `744825`。

使用本机 `ilspycmd 10.0.1.8346`，例如：

```sh
rtk proxy /home/lightjunction/.dotnet/tools/ilspycmd -t ClusterManager /path/to/OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll
```

原始输出仅保留在忽略目录：

- `../.local-tests/native-transfer-boundary-20261021/`：`Grid.decompiled.cs`、`BestFit.decompiled.cs`、`ClusterManager.decompiled.cs`、`GridSettings.decompiled.cs`、`SimMessages.decompiled.cs`、`Sim.decompiled.cs`、`Game.decompiled.cs`。
- `../.local-tests/native-audit-20261021/`：`WorldContainer.cs`、`SaveLoader.cs`。
- `../.local-tests/native-world-boundary-20261021/`：`AsteroidConfig.cs`、`AsteroidGridEntity.cs`、`ClusterGrid.cs`、`ClusterGridEntity.cs`、`WorldInventory.cs`、`RoomProber.cs`、`Pathfinding.cs`、`GameNavGrids.cs` 及本次输入清单。

这些是匹配本机二进制的方法体和静态 API 证据；不是实机验收。没有验证 `libSimDLL.so` 内部实现，更没有执行它的分配或迁移接口。原始源码、游戏 DLL 和存档不随此记录提交。

## 网格分配：有可用入口，但不会自动扩容

| API | 已确认行为 | 限制 |
| --- | --- | --- |
| `BestFit.GetGridOffset(IList<WorldContainer>, Vector2I, out Vector2I)` | 根据已注册世界的矩形寻找位置，返回所需总尺寸；矩形间保留间隔。 | 只计算，不分配；会对扩宽发断言。调用前先拒绝宽度超过现有 Grid 的请求。 |
| `Grid.GetFreeGridSpace(Vector2I, out Vector2I)` | 调用 BestFit，只有候选总宽高不超过当前 Grid 时才初始化原生真空区域并刷新 roomProber。 | **它不是只读预检**。成功会修改原生模拟区域。放不下则返回 false。 |
| `SimMessages.SimDataResizeGridAndInitializeVacuumCells(...)` | 向 SimDLL 发送网格尺寸、区域尺寸及偏移。 | 名字含 Resize 不代表完整游戏支持动态扩容；原生正常调用者已经将尺寸限制在现有全局 Grid 内。 |
| `Grid.FreeGridSpace(size, offset)` | 向模拟器释放区域，清理相应 Element 并刷新 roomProber。 | 不是完整星球卸载：未完成对象、world index、生态区、星图和存档引用的统一清理。 |

本地定位：`Grid.decompiled.cs:2099`、`BestFit.decompiled.cs:86`、`SimMessages.decompiled.cs:1156`。

`GridSettings.Reset(width,height)` 从头建立 Grid 数组、对象层以及若干管线系统，并将世界索引置为未归属状态。它不是保留运行中殖民地的扩容函数。`Game.OnPrefabInit` 又依据当时的 Grid 尺寸创建管线、导航、CellChangeMonitor 和其他全局系统；`RoomProber` 构造时也建立 `Grid.CellCount` 长度的缓存。扩大几组托管数组不足以迁移这些系统、原生指针及活动路径查询。

完整存读档启动路径会先 Reset Grid、初始化 Sim、AllocateCells、加载原生模拟和对象，再 InitializeWorldGrid；该路径不能在运行中的同一个世界上直接重放。

## 动态 WorldContainer 的真实生命周期

`ClusterManager.CreateRocketInteriorWorld(GameObject, string, Action)` 是确实存在的运行中入口，顺序包括：

1. 用固定 `ROCKETRY.ROCKET_INTERIOR_SIZE`（本机为 32×32）申请已有 Grid 中的空间。
2. 从 `GetNextWorldId()` 获取未使用的 native world ID。
3. 添加 WorldInventory 和 WorldContainer，调用 `SetRocketInteriorWorldDetails`。
4. 对新区域逐格写入 `Grid.WorldIdx`，向 Pathfinding 标记导航脏格。
5. Stamp 内舱模板，建立内舱生态区信息，触发原生完成事件并建立轨道对象。

这里证明了“不整局重载也能出现新世界”，但没有证明“可以用任意大小的火箭内舱冒充星球”：该入口固定尺寸，设置 module-interior 标志，依赖真实 CraftModuleInterface、内舱模板和火箭生命周期。销毁入口还会疏散复制人、转移资源；不能拿它直接回滚普通星球。

普通星球路径是私有 `ClusterManager.CreateAsteroidWorldContainer(ProcGenGame.WorldGen)`：实例化真实 Asteroid prefab，设置 world ID 和 WorldGen 元数据，初始化 AsteroidGridEntity 的 AxialI 位置，然后激活。它本身不分配/导入模拟地形；正常世界生成完成调用者另写 WorldIdx 和导航脏格。

| 部件 | 初始化与注册 |
| --- | --- |
| Asteroid prefab | 包含 SaveLoadRoot、WorldInventory、WorldContainer、AsteroidGridEntity、OrbitalMechanics 和季节/告警状态机。 |
| WorldContainer | OnPrefabInit 获取 WorldInventory 并向 ClusterManager 注册；SetID 同时设置 ParentWorldId。OnCleanUp 本身不从 ClusterManager 移除，不能只 Destroy 一个组件。 |
| WorldInventory | OnPrefabInit 订阅全局物品事件；OnSpawn 延后一帧扫描现有 Pickupable。导入对象后还需验证库存刷新和归属。 |
| AsteroidGridEntity / ClusterGridEntity | Init 设置名称、星图坐标和图标；OnSpawn 注册到 ClusterGrid，清理时解除星图注册。 |
| 导航 / 星系 | 使用已分配全局网格；原生创建内舱会写 WorldIdx、标记新区域导航脏格并刷新 roomProber。没有独立“新增世界即自动修复所有路径”的保证。 |

本地定位：`ClusterManager.decompiled.cs:229,529`；`WorldContainer.cs:451,558,602,799`；`AsteroidConfig.cs:7`；`WorldInventory.cs:69,127`；`ClusterGridEntity.cs:105`。

`WorldContainer.SetWorldDetails(WorldGen)` 不只读取宽高：还读取边界、隐藏高度、世界配置、生态区、故事/世界特质、季节、日照和辐射等。`SetWorldDetails(null)` 会把世界设成覆盖整张 Grid 的开局星球，**不能作为外来星球的初始化捷径**。现有公开 SetRocketInteriorWorldDetails 也不能替代普通星球元数据恢复。

## 两种“空位”和原生容量

- 星图坐标属于 `ClusterGrid` 的 AxialI 六边形集合；一个坐标可以包含若干不同图层的实体。修改 AsteroidGridEntity.Location 不会移动或扩大物理 Grid 区域。
- 模拟空间属于全局二维矩形网格，WorldContainer 的 WorldOffset/WorldSize 标出实际区域。没有玩家占领的野生星球仍是已注册世界，仍占模拟空间，不能覆盖。
- BestFit 只参考已注册 WorldContainer 的矩形，不会替导入器验证遗留对象、其他系统引用和资源。真实导入前必须再次确认候选区域及边界没有活动对象；不能据“名单中没有 owner”判空。
- `Grid.WorldIdx` 为 byte，原生 `GetNextWorldId()` 搜索 `0..254`，`255` 是未归属值；火箭内舱也消耗 world ID。这是引擎世界容量，和网络玩家数量不是一回事。
- 原生星系生成 BestFit 额外预留的约 136 格宽空间是内舱布局余量，不能保证放下一颗 256×384 外来星球。具体存档必须读实际布局。

## 下一步最小可执行切片

先做**只读容量预检**，在 Steam 启动的暂停测试副本内报告：全局 Grid 宽高、已注册世界的实际矩形、未使用 native world ID、256×384 候选矩形、目标星图 hex 是否适合放普通星球。只调用 BestFit 计算候选，不调用会修改原生区域的 GetFreeGridSpace。该报告不能承诺导入成功。

有足够大空矩形时，下一项实验是仅在一次性测试副本中恢复一个真实 Asteroid 容器及其原始元数据，保持 Grid 总尺寸不变，写入 world index 并检查导航/库存/星图注册。需要证明：新增世界唯一、所有矩形不重叠、宿主原世界摘要不变、存档后经 Steam 重新加载仍正确。原生地形、建筑、管道、生态区和对象引用恢复完成之前，不发布“可导入完整星球”的状态，也不释放源星球的唯一计算权。

放不下时停止这条原地导入路径。可继续研究在**加载前**为新的测试副本预留额外空间，或完整存档迁移并重新加载；二者都需要专门处理原生 Sim 数据及对象的坐标/引用，不是调整存档宽高或复制 Grid 数组。当前没有经验证的实现。

预留很大的 Grid 还会扩大原生分配、托管数组、导航和星系缓存。远端按视口 lazy 同步能减少网络采样与传输，不会自动免除星系主人的全局模拟网格内存成本；必须在目标机器实际测量后再决定预留容量。

即使空矩形足够，尚未解除的阻碍仍包括：完整星球的 Sim/疾病/背墙数据恢复、生态区与 WorldDetailSave 重定位、所有对象的世界/格子/实例引用映射、世界季节与空间事件、存档恢复及失败回滚。另行调查的复制人 archive 能导出若干 root，不会自动解决这些世界级问题。
