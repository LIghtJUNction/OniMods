# OniSim 多组分输运 — Stage 1 实施记录

日期：2026-11-07  
阶段：Stage 1 — Vulkan 算子装配与端到端基准框架  
范围：实验 GPU crate；不接入生产模拟 Tick、C# ABI、存档格式或游戏运行时。

## 已实现

- 将 `multicomponent_flux.wgsl` 装配到独立 wgpu 30.0.1 Compute Pipeline。
- 输入采用 NHWC 连续质量张量，并分别上传 x/y 速度、固体掩码和组分漂移速度。
- 通过两组 BindGroup 和两个 device-local storage buffer 实现 ping-pong。
- `run_steps_and_readback` 支持一次上传后连续 dispatch N 步，仅最后一步读回。
- CPU 参考实现与 GPU 使用相同的面速度、迎风 donor 选择、封闭边界和固体掩码。
- 上传阶段拒绝非有限输入、负质量、固体格非零质量；运行阶段检查二维 CFL：`dt * max(|vx| + max_c |vy + drift_c|) <= 1`。

## 基准定义

运行命令（在具备 Rust 1.99.1 和真实 Vulkan GPU 的机器上）：

```sh
cd mods/CycleTrim/OniSim/gpu
rtk proxy cargo run --locked --release --bin multicomponent-bench
```

固定测试形状：96×96×4、256×384×4。每种形状 5 次预热、31 次样本。

| 指标 | 测量边界 |
|---|---|
| CPU median | CPU 参考实现单步 |
| GPU end-to-end median | 五组输入上传 + 单次 dispatch + 同步 + 最终读回 |
| GPU resident 32-step | 一次上传 + 32 次依赖 dispatch + 一次最终同步/读回 |
| Resident per-step | 上一项总耗时除以 32；包含摊销上传和最终读回 |
| Max relative error | GPU 与 CPU 逐元素比较，门限 1e-5 |
| Mass relative error | 单步 GPU 输出与初始输入的逐组分总质量误差 |
| Invariants | 非负质量、固体格零质量 |

基准只选择真实离散或集成 Vulkan Adapter；没有硬件 Adapter 时以错误退出，不采用软件回退。

## 自动化验证

已在 OniSim diamond workflow 中加入独立 `gpu-operator` job：使用 Rust 1.99.1 执行 Naga WGSL 解析/验证、CPU 参考守恒与固体墙测试，并编译 Vulkan benchmark 的全部目标。该 job 不需要 GPU；真实 Adapter 的端到端测试仍由目标硬件运行。

## 当前验证状态

本次执行容器检查结果：

- `rustc`：不可用
- `cargo`：不可用
- PyTorch：`2.10.0+cpu`，CUDA 不可用
- Vulkan 硬件 Adapter：当前执行环境未提供可用设备

因此，当前提交记录的是 GPU pipeline、CPU/GPU 对照及端到端计时程序；**没有生成 GPU 性能数字**。需要在目标 Vulkan 机器上执行上述命令，取得 Adapter 型号、误差和计时 JSON 后，才能评定 Stage 1 硬件门禁。

## 生产晋级边界

Stage 1 仅验收单个多组分输运算子。压力投影、EOS 浮力、热扩散、相变、C# 兼容和生产状态常驻均不在本次交付范围。任何生产集成必须另行通过物理基准和游戏兼容性验收。
