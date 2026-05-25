# OniMods
Oxygen Not Included Mods

## oni_mcp

新想法💡备忘
整第二个鼠标，永远指向一个格子中间，给渲染到屏幕，然后AI所有的操作全部是基于这个鼠标来行动。
这样效果肯定更好，而且这样显示效果也会更好，更有观赏性。（目前直接让模型基于坐标来，实在是效率太低太低了）

[ONI MCP Server](mods/oni_mcp/README.md)
省流：劝你不要妄想AI可以玩这种复杂的游戏，除非哪天AI把arc-agi-3测试通过了还差不多（那个比缺氧简单一万倍，AI通过率都极低，这种复杂游戏以目前的AI水平来说约等于不可能完成的任务）

- 这是为缺氧量身定制的mcp服务器，基本大多数玩家可执行的操作都已经暴露为mcp工具。
- 但是这和会不会用是两码事。
- 因此还需要搭配技能（.agents/skills)，教会agent如何使用这些工具。
- 因为工具量巨大，我做了分组和搜索工具，目前让agent自主玩游戏，还属于是一个很遥远的目标。
- 但是作为你的游戏顾问还是不错的，AI可以读取相当多的游戏数据，也能较好的完成一些简单任务，像调整日程，帮你修改建筑设置，设置门禁权限，以及复制人重命名这些操作，均可实现。
- 但是像世界规划这种复杂任务，我还在尝试看看能不能尽量做成。
这个是mcp服务器，不是完整的agent实现，后续如果有人想赋予复制人灵魂（闲着没事和复制人聊聊天），可以直接复用本项目。
- 相关 AI Skill 实现参考：[zhuiyun.skill](https://github.com/LIghtJUNction/zhuiyun.skill)

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
