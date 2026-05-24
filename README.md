# OniMods
Oxygen Not Included Mods

## oni_mcp
[ONI MCP Server](mods/oni_mcp/README.md)


## onim
缺氧 (Oxygen Not Included) Mod 开发工具链。

## 一句话

```bash
cargo install --path . && onim setup && onim init MyMod && onim dev
```

## 完整工作流

```bash
# 1. 安装 onim CLI
cargo install --path .

# 2. 交互式初始化（检测游戏路径、检查依赖、写入配置）
onim setup

# 3. 创建新 Mod
onim init MyMod --author YourName --desc "Mod描述"

# 4. 开发迭代
onim dev -m MyMod          # 构建 → 安装到游戏 Dev 目录
onim build -m MyMod        # 仅构建
onim info                  # 查看已安装的 Mod

# 5. 正式发布
onim install -m MyMod      # Release 构建 → 安装到 Local
onim publish -m MyMod      # 打开 OniUploader 上传创意工坊

# 6. 清理
onim uninstall -m MyMod    # 从游戏目录卸载
```

## 命令速查

| 命令 | 作用 |
|------|------|
| `onim setup` | 初始化项目配置（游戏路径、依赖检查） |
| `onim init <name>` | 从模板创建新 Mod |
| `onim build` | 构建 Mod（`--release` 为 Release） |
| `onim dev` | 构建 + 安装到游戏 `mods/Dev/` |
| `onim install` | Release 构建 + 安装到 `mods/Local/` |
| `onim uninstall` | 卸载（`--scope dev/local/all`） |
| `onim info` | 查看已安装的 Mod（Dev/Local/Steam） |
| `onim publish` | 打开 OniUploader 发布到创意工坊 |
| `onim list` | 列出配置的 Mod |

`-m <name>` 指定 Mod，不指定则用默认 Mod。

## 目录

```
.
├── oni-mods.toml          ← 配置文件（游戏路径、Mod 列表）
├── Directory.Build.props  ← MSBuild 游戏 DLL 引用配置
├── Cargo.toml             ← onim CLI
├── src/                   ← onim 源码
├── mods/                  ← Mod 项目目录
│   ├── OniModTemplate/    ← 模板
│   └── MyMod/             ← 你的 Mod
└── oni/src/               → 游戏反编译源码（参考）
```

## 依赖

- [Rust](https://rustup.rs/)（编译 onim）
- [.NET SDK](https://dotnet.microsoft.com/download)（构建 Mod）
- `unzip`（Linux/macOS）
- `tar`（打包源码）

`onim setup` 会自动检查并提示安装。
