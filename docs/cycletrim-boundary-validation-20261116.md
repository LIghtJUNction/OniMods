# CycleTrim 0.4.24：公开网格边界修复与验证

日期：2026-11-16  
项目：LIghtJUNction/OniMods  
实现提交：[b66ae2890879211b48e5bc256eb535fc160f42bf](https://github.com/LIghtJUNction/OniMods/commit/b66ae2890879211b48e5bc256eb535fc160f42bf)

## 结果

CycleTrim 已从 0.4.23 更新到 **0.4.24**。本轮修复多组分输运中最后一行、最后一列有效格子被区域裁剪排除的问题，并统一相关的气体、液体、休眠判断、扩散归属和热传导区域范围。

修复前，新增最小回归能稳定触发失败；修复后，完整原生 Release 测试为 **229 通过、0 失败、19 忽略**。19 项忽略测试保留原有手动基准标记，不计入通过数。GPU 回归实际经过开发机的 NVIDIA GeForce RTX 4060 Laptop GPU / Vulkan 链路。Linux x64 发布包已构建，并核对了二进制、源码摘要与实现提交。

以上结论针对原生计算和打包。本轮遵守项目中保留的不再启动 ONI 的要求，未进行游戏运行、存档读写或整局帧率验收。

## 1. 缺陷为什么会发生

模拟器在真实网格四周增加一圈保护格，供邻居访问使用。公开坐标进入内部存储时会整体加一。因此，一个宽度为 4 的真实网格，其内部有效列是 1、2、3、4；保护列是 0 和 5。

Rust 的范围 `1..4` 不包含 4。旧实现把内部区域的排他上界裁剪到 `width` / `height`，最后一个真实格子就被漏掉了。正确范围应为 `1..width+1` / `1..height+1`。

| 坐标对象 | 4 × 4 示例 | 含义 |
| --- | --- | --- |
| 公开格子坐标 | x、y 均为 0–3 | 游戏提供的真实格子 |
| 内部有效格子坐标 | x、y 均为 1–4 | 公开坐标加一 |
| 内部区域范围 | `[1, 5) × [1, 5)` | 上界不包含在内，覆盖全部 16 格 |
| 外围保护格 | 任一轴为 0 或 5 | 不属于区域输运范围 |
| 原错误范围 | `[1, 4) × [1, 4)` | 最后一行、最后一列被排除 |

这不只是少计算几个边缘位置。物种发现和休眠快照如果也漏掉这些位置，就可能认为区域没有相应气体，或在边界发生变化后仍保持休眠。扩散归属掩码如果尺寸不同，还可能使 CPU 和 GPU 对哪些边已经处理过产生分歧。

坐标转换与修复可从[实现提交的代码差异](https://github.com/LIghtJUNction/OniMods/commit/b66ae2890879211b48e5bc256eb535fc160f42bf)逐项核对。

## 2. 修改范围

所有相关区域都使用一致的排他上界 `width + 1`、`height + 1`。外围保护格仍在范围之外。

| 文件 | 修改目的 |
| --- | --- |
| `multicomponent_staging.rs` | 气体暂存区域覆盖最后一行、最后一列 |
| `multicomponent_flux.rs` | 统一物种发现、液体扩散、休眠键和快照清理的区域范围 |
| `multicomponent_batch.rs` | 批处理与休眠判断覆盖完整有效区域 |
| `gas.rs` | CPU/GPU 扩散归属掩码使用完整区域尺寸，避免漏处理或重复处理 |
| `gpu_sim.rs` | 热传导区域映射覆盖最后的有效格子，并添加真实 GPU 回归 |

上述路径均位于 `mods/CycleTrim/OniSim/native/src/`。另外更新了版本、CHANGELOG 和 OniSim 工程进展文档。实现提交共修改 8 个文件，新增 366 行、删除 24 行，新增行主要用于回归测试。

此次没有改变 ABI、存档格式、物理系数或性能阈值。已有一项 4 × 4 世界守恒测试只累加了 3 × 3 格子，其求和范围同步改为全部 16 格；原有 `1e-4` 误差容限保持不变。

## 3. 新增的八项回归

这些测试检查可见行为和守恒关系，而不仅仅检查某个上界表达式。

| 测试 | 覆盖的风险 |
| --- | --- |
| `gas_transport_includes_last_public_row_and_column` | 通过真实 NewFrame 坐标转换，检查末行、末列和角点气体向相邻真空扩散、质量守恒，以及保护格不变 |
| `gas_species_inventory_includes_last_public_cells` | 只存在于最后有效格子的物种能够被发现 |
| `sleeping_gas_wakes_when_last_public_cell_changes` | 最后有效格子的变化能使休眠区域重新参与计算 |
| `liquid_diffusion_includes_last_public_cells` | 液体边界参与扩散，并保持各组分质量 |
| `gpu_owned_edges_at_last_public_cells_are_not_diffused_twice` | 已归属 GPU 的边不会被 CPU 重复扩散；未归属的边仍有 CPU 后备处理 |
| `gpu_gas_transport_includes_last_public_cells` | 实际 Vulkan 气体路径覆盖末行、末列与角点，掩码尺寸和质量正确 |
| `gpu_gas_batch_wakes_on_last_public_cell_changes` | 批处理在边界发生变化后正确唤醒并输运 |
| `thermal_deferred_regions_include_last_public_cells_but_not_halo` | 热传导映射包含有效边界，不包含外围保护格 |

GPU 气体直接调用和批处理测试显式强制走现有的测试用 GPU 路径，并断言 `gas_gpu_seen && !gas_cpu_seen`。日志识别的适配器为 NVIDIA GeForce RTX 4060 Laptop GPU。这样可以避免“测试名字含 GPU，实际命中了 CPU 快路径”的假通过。

## 4. 验证记录

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 修复前运行新增最小回归 | 失败，退出码 101 | 报错包含 `last public cell was excluded from gas staging` |
| 修复后按 `public` 筛选原生测试 | 26 通过、0 失败、3 忽略、219 未选中 | 包含全部八项新增回归，也包含已有相关测试 |
| 完整原生 Release 测试 | **229 通过、0 失败、19 忽略** | 测试阶段 31.31 秒；连同编译的命令用时 76.54 秒 |
| Linux Release 打包 | 成功，退出码 0 | `onim build --release -m CycleTrim` |
| 提交后刷新构建记录 | 成功，退出码 0 | 构建记录中的 commit 等于实现提交 b66ae289 |
| 包内原生库核对 | 一致 | 与显式平台目标生成的库逐字节比较 |
| 构建记录摘要核对 | 一致 | 源码摘要重新计算；二进制 SHA-256 与包内文件一致 |
| 源码包核对 | 本轮 8 个文件全部一致 | 解包内容与开发机工作文件逐字节比较 |
| Git 差异检查 | 通过 | `git diff --check` 与提交前暂存差异检查 |

完整测试命令：

```bash
cargo test --locked --release \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  -- --test-threads=1
```

定向回归命令：

```bash
cargo test --locked --release \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  public -- --nocapture --test-threads=1
```

开发机环境：Rust/Cargo 1.100.0、.NET SDK 10.0.114、NVIDIA GeForce RTX 4060 Laptop GPU。Rust 发布编译仍有 46 条告警；构建成功不等于告警已全部清理。本轮没有扩大到告警清理或手动性能基准测试。

## 5. 产物及可核对摘要

开发机项目根目录：

```text
/home/lightjunction/Documents/GITHUB/OniMods
```

| 产物 | 项目内路径 | 大小 |
| --- | --- | ---: |
| Linux x64 模组包 | `dist/CycleTrim.zip` | 3681974 字节 |
| 源码包 | `dist/CycleTrim-src.tar.gz` | 16114881 字节 |
| 原生库在模组包中的位置 | `OniSim/linux-x64/liboni_sim_core.so` | 由构建记录标识 |
| 原生构建记录 | `OniSim/linux-x64/build.json` | `gameRuntimeTested: false` |

最终模组包 SHA-256：

```text
ed682fe9e1168148584ec1890c4e2ccc8fff90e411485a0dfc824558265ea9da
```

最终源码包 SHA-256：

```text
0cb502b57e3dd18ccde4ea9e2d69857c364f923bc646e95d801b6499cc898b56
```

包内原生库 SHA-256：

```text
1d43dddff8c9ad8018b3ddf7854411f5a42f989eda023c4dbfe436e7d5c8151a
```

原生源码摘要：

```text
221f9d4cba8ae03cdb1cf20b9e8fea91bffa2f1b5ee4b75dcdc7052c128e6c37
```

构建脚本使用独立的平台目标输出目录，校验时使用的对应原生库是：

```text
mods/CycleTrim/OniSim/native/target/platform-build/x86_64-unknown-linux-gnu/release/liboni_sim_core.so
```

不能把另一个 Cargo 目标目录下的库当作打包输入。摘要算法和平台目录来源见[构建脚本](https://github.com/LIghtJUNction/OniMods/blob/b66ae2890879211b48e5bc256eb535fc160f42bf/scripts/build_onisim_native.py)。

开发机完整过程日志位于 `/tmp/cycletrim-20261116/`，包括修复前回归、定向回归、完整测试和发布构建日志。该目录是临时过程证据；本报告将最终结果、命令、提交和摘要保存在仓库中，避免依赖临时日志长期存在。

## 6. 结论边界与后续验收

现有证据支持：公开网格边界在本轮涉及的 CPU/GPU 区域算子中不再被遗漏；新增边界行为通过回归；完整原生测试通过；Linux x64 模组包与实现源码相符。

本轮没有测量整局 FPS、实际存档加载、长期游戏运行或其他操作系统的二进制。修复使之前被遗漏的有效格子重新参与计算，本身也不能直接推出更高帧率。需要评价整局性能时，应使用同一存档、相同游戏状态的成对基准，并在允许启动 ONI 的前提下执行。

本轮开发使用 ssmcp 连接用户开发机；同时形成的服务器使用体验记录见 [ssmcp-usability-20261116.md](ssmcp-usability-20261116.md)。OniMcp、OniOnline 等原有工作区修改未被纳入本轮提交。

## 证据与写作复核

- 事实：测试计数、提交和摘要来自本轮实际执行结果。
- 因果：坐标转换、排他上界与失败行为能够相互对应。
- 结构：先给结果，再解释缺陷、实现和验证。
- 表述：将原生正确性、打包一致性与整局体验分别说明。
- 可复现性：保留命令、版本、设备、产物路径与源码链接。

