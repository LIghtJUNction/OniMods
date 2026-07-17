# OniModTemplate

## 索引

- [项目用途](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniModTemplate/README.md#项目用途)
- [快速开始](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniModTemplate/README.md#快速开始)
- [变更日志](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniModTemplate/README.md#变更日志)
- [参考链接](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniModTemplate/README.md#参考链接)
- [相关项目](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniModTemplate/README.md#相关项目)

## 项目用途

这是一个基础的 ONI Mod 模板项目，用于快速搭建新 Mod。

- 用 `onim init` 创建 mod 时，模板提供默认目录结构和构建配置。
- 在 `Patches/` 目录放你自己的补丁代码。
- 可复用逻辑放 `Core/`，运行时 UI/Overlay 放 `UI/`，配置放 `Config/`（按需创建）。
- 根目录只保留 `ModInfo.cs` 与工程/元数据文件。
- 适合从空项目开始做新特性、补丁验证与发布流程。

## 快速开始

从仓库根目录运行：

```bash
onim build -m OniModTemplate
onim dev -m OniModTemplate
```

## 变更日志

当前工作区该模板暂无提交历史，请参考：

- [CHANGELOG.md](CHANGELOG.md)

## 参考链接

- 仓库: [LIghtJUNction/OniMods](https://github.com/LIghtJUNction/OniMods)
- 模板工程: [OniModTemplate.csproj](OniModTemplate.csproj)
- Mod 选项库: [PLib](https://github.com/peterhaneve/PLib)

## 相关项目

- [FastTrack](https://github.com/peterhaneve/FastTrack)（ONI patching）
- [Harmony](https://github.com/pardeike/Harmony)（补丁框架）
- [zhuiyun.skill](https://github.com/LIghtJUNction/zhuiyun.skill)（ONI agent 工作流）
