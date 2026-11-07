# 游戏反编译源码使用规范

- 原版《缺氧》（Assembly-CSharp.dll）反编译源码统一存放于仓库根目录：
  `/home/lightjunction/Documents/GITHUB/OniMods/decompiled/`
- 该目录下包含 3,962 个 `.cs` 文件，已被 `.gitignore` 忽略。
- 所有 Agent 需查阅游戏内部逻辑、UI 渲染、复制人行为、建筑机制、数据结构时，统一对 `decompiled/` 执行 `rg` 或代码分析，禁止在其他目录随意生成新的反编译文件。
