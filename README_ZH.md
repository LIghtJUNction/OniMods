# OniMods

[English](README.md)

[![ONI MCP Server 预览图](mods/oni_mcp/preview.png)](mods/oni_mcp/README.md)

<div align="center">
  <p>
    <a href="Cargo.toml"><img alt="Rust" src="https://img.shields.io/badge/Rust-2024-f46623?logo=rust&logoColor=white" /></a>
    <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/License-MIT-green.svg" /></a>
    <a href="mods/oni_mcp/README.md"><img alt="ONI MCP docs" src="https://img.shields.io/badge/ONI%20MCP-docs-2f80ed" /></a>
    <a href="docs/mcp-tools-reference.md"><img alt="Tools reference" src="https://img.shields.io/badge/Tools-reference-8b5cf6" /></a>
  </p>
</div>

<p align="center">
  <a href="#快速入口">快速入口</a>
  · <a href="#oni-mcp-server">ONI MCP</a>
  · <a href="#onim">onim</a>
</p>

缺氧（Oxygen Not Included）Mod 工程仓库，包含：

- `onim`：本仓库内置的缺氧 Mod 开发 CLI，用于初始化、构建、安装、卸载和发布 Mod。
- `oni_mcp`：让 AI 助手通过 MCP 读取并操作缺氧殖民地的 Mod。

- `onim` 负责初始化、构建、安装、卸载和发布。
- `oni_mcp` 负责把殖民地数据和受控操作暴露给 MCP 客户端。
- 适合 Mod 开发、AI 辅助游玩和工具集成。

> **兼容性警告**：`oni_mcp` 在 `1.0.0` 之前 API 仍可能发生不兼容变更。二创、插件、脚本或第三方客户端请锁定具体版本，并以运行时 `tools_manifest` / `oni://tools/manifest` 为准做兼容适配。

## 快速入口

| 项目 | 路径 | 说明 |
|------|------|------|
| `onim` | [src/](src/) | Rust 编写的 Mod 开发工具链 |
| `oni_mcp` | [mods/oni_mcp/](mods/oni_mcp/) | ONI MCP Server Mod |
| `oni_mcp` 中文文档 | [mods/oni_mcp/README.md](mods/oni_mcp/README.md) | 安装、连接和功能说明 |
| `oni_mcp` English docs | [mods/oni_mcp/README_EN.md](mods/oni_mcp/README_EN.md) | English installation and usage guide |

## ONI MCP Server

[ONI MCP Server 文档](mods/oni_mcp/README.md)

<details>
<summary>展开 Mod 介绍</summary>

`oni_mcp` 是为缺氧量身定制的 MCP 服务器 Mod。安装后，支持 MCP 的 AI 客户端可以通过本地 HTTP 接口读取殖民地状态、分析局势，并在授权后执行游戏操作。

目前公开工具面是一组会出现在 `tools/list` 中的聚合入口：

- `world_editor`：代码文件风格的世界编辑器。将存档视为虚拟目录，客户端通过读取文件观察状态，并通过 `edit` 配合 SEARCH/REPLACE 块来下达修改意图（如挖掘、建造、连接管线和设置优先级）。
- `colony_control`：殖民地全局诊断、日程、研究、医疗与整体控制管理。
- `server_control`：MCP 服务状态、会话清单、截图任务和后台任务管理。

它目前更适合作为“半自动操作助手”。复杂世界规划、长期自治游玩和高质量决策仍然需要人工监督。

相关 AI Skill 实现参考：[zhuiyun.skill](https://github.com/LIghtJUNction/zhuiyun.skill)

</details>

<details>
<summary>展开开发备忘</summary>

想法备忘：实现第二个“agent 指针”，始终指向格子中心并渲染到屏幕上，让 AI 的所有操作基于这个可视指针执行。相比直接基于坐标下命令，这可能更稳定，也更容易观察和调试。

省流结论：不要指望当前 AI 能长期自主玩好缺氧这类复杂模拟游戏。现阶段更现实的方向是让 AI 读取更多游戏数据、执行明确的小任务，并在玩家确认下辅助完成局部规划。

</details>

## onim

`onim` 是缺氧 Mod 开发工具链，覆盖从项目初始化到 Steam 创意工坊发布的常用流程。

## 一句话开始

```bash
cargo install --path .
onim setup
onim init MyMod
onim dev -m MyMod
```

## 常用工作流

```bash
# 1. 安装 onim CLI
cargo install --path .

# 2. 交互式初始化：检测游戏路径、检查依赖、写入配置
onim setup

# 3. 创建新 Mod
onim init MyMod --author YourName --desc "Mod 描述"

# 4. 开发迭代
onim dev -m MyMod          # 构建并安装到游戏 Dev 目录
onim build -m MyMod        # 仅构建
onim info                  # 查看已安装的 Mod

# 5. 正式发布
onim install -m MyMod      # Release 构建并安装到 Local 目录
onim publish -m MyMod      # 上传到 Steam 创意工坊

# 6. 清理
onim uninstall -m MyMod    # 从游戏目录卸载
```

`-m <name>` 用于指定 Mod。不指定时，`onim` 使用 [oni-mods.toml](oni-mods.toml) 中的 `default_mod`。

首次构建前需要生成本机 MSBuild 配置：

```bash
cp Directory.Build.props.example Directory.Build.props
# 然后把 Directory.Build.props 里的 OniGamePath 改为本机缺氧安装目录
```

也可以运行 `onim setup` 自动检测并写入配置。

## 命令速查

| 命令 | 作用 |
|------|------|
| `onim setup` | 初始化项目配置，检测游戏路径和依赖 |
| `onim init <name>` | 从模板创建新 Mod |
| `onim build` | 构建 Mod，`--release` 为 Release 构建 |
| `onim dev` | 构建并安装到游戏 `mods/Dev/` |
| `onim install` | Release 构建并安装到 `mods/Local/` |
| `onim uninstall` | 卸载 Mod，支持 `--scope dev/local/all` |
| `onim info` | 查看已安装的 Dev、Local 和 Steam Mod |
| `onim publish` | 发布到 Steam 创意工坊，支持 `--gui` |
| `onim list` | 列出配置文件中的 Mod |

## 目录结构

```text
.
├── oni-mods.toml          # onim 配置文件，记录默认 Mod 和 Mod 列表
├── Directory.Build.props.example  # 本机 MSBuild 配置模板
├── Directory.Build.props          # 本机生成配置，不提交
├── Cargo.toml             # onim CLI
├── src/                   # onim 源码
├── mods/                  # Mod 项目目录
│   ├── OniModTemplate/    # Mod 模板
│   └── oni_mcp/           # ONI MCP Server
└── oni/src/               # 游戏反编译源码参考
```

## 依赖

- [Rust](https://rustup.rs/)：编译 `onim`
- [.NET SDK](https://dotnet.microsoft.com/download)：构建 Mod
- `unzip`：安装构建产物时解压
- `tar`：打包源码

`onim setup` 会自动检查依赖并提示安装方式。
