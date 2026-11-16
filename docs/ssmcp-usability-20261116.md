# ssmcp 使用问题记录：CycleTrim 开发任务

记录日期：2026-11-16（Asia/Taipei）。本记录来自一次通过 ssmcp 操作开发机、检查并迭代 CycleTrim 的实际任务，以及对公开源码的只读核对。

## 结论与范围

任务后段重新发现客户端后，原生文件读取、AGENTS.md 解析和技能摘要已经可用，并完成了实际读取验证。初始连接只有 4 个终端工具，后续连接提供 32 个工具；这项差异记录为当时的交付与连接状态，读取能力已恢复。工程自动化仍需要更直接的命令完成判定、退出码、终端文本处理和能力身份信息。原生编辑、客户端 diff 显示、同步及端口转发尚未在本轮验收。

本记录区分三类证据：**实测**指本任务实际返回的工具结果；**源码确认**指固定提交中的实现；**待验证**指尚未做运行测试的行为。连接切换前后的结果分别标注。本轮没有部署或重启 ssmcp 服务器，没有改动 ssmcp 客户端，也没有启动任何 harness 或模型后端；技能操作仅用于读取索引。

### 已核对的对象

| 对象 | 观察结果 | 证据边界 |
| --- | --- | --- |
| 初始开发机连接 | `list({client_id})` 返回 4 个 `terminal.*` 工具；8 个新增 helper 均为 `undefined` | 历史连接状态；该客户端随后断开 |
| 连接失效提示 | 旧客户端调用返回 `client is unavailable; call list to refresh connected clients` | 错误明确要求重新发现客户端，没有静默报告成功 |
| 后续开发机连接 | 刷新 `list` 后找到新的 OniMods 客户端，目录为 32 个工具 | 反映任务后段实际可用的工具目录 |
| 后续 `code_mode` | `fs`、`skills`、`agents`、`context`、`resources`、`jobs`、`ports` 为 `object`；`rsync` 为 `function` | helper 已存在；其中已执行的读取验证见 SSMCP-03，其余能力未据此记为验收通过 |
| 开发机 PATH 中的命令 | `ssmcp --version` 返回 `0.4.1` | 版本字符串没有携带源码提交或能力清单 |
| 已核对的 GitHub 源码 | `cba9f752a605ca992a19a8bb698cac6d3d12e87d`，Cargo 版本 `0.4.1` | 下文源码结论限定于该提交 |
| 新原语的仓库验收记录 | 报告记载 Shell 目录为 32 个工具，并明确该报告对应轮次没有安装或重启生产服务 | 后续连接的工具数与报告一致；本轮没有确定实际运行构建或是谁更新了实例 |

`0.4.1` 这个版本号本身不足以定位前后差异。重新发现后能力已经恢复，但具体构建、服务进程和入口路由关系仍待核对；本文不把某一种部署原因写成已确认事实。[S1][S2][S3]

### 七项记录的当前状态

| 记录 | 当前分类 |
| --- | --- |
| SSMCP-01 版本与能力身份 | 后续连接仍缺少 `list` 中的版本、构建提交和能力修订号 |
| SSMCP-02 命令执行与任务 | 已实测命令退出码仍需调用方包装；固定源码的 `jobs.*` 仅管理同步任务 |
| SSMCP-03 文件与上下文原语 | 初始连接不可用，后续连接的读取能力已验证恢复；原生编辑和客户端显示待验收 |
| SSMCP-04 终端返回组织 | 保留初始连接实测及固定源码证据，未把后续读取原语的可用性当作终端格式修复 |
| SSMCP-05 终端分页状态 | 保留初始连接实测及固定源码证据 |
| SSMCP-06 harness 自动注册 | 固定源码的架构缺口；开发机全部 harness 安装与注册情况未实测 |
| SSMCP-07 PTY UTF-8 跨页解码 | 初始连接已复现；后续连接未重测；与 `fs.readText` 文件读取分开记录 |

## 问题清单

### SSMCP-01：入口缺少版本和能力协商信息

**优先级：P1。归属：服务端发现接口与客户端注册协议。证据：实测、源码确认。**

`list` 提供客户端标识、名称、类型、主机名和启动目录，但后续连接中仍没有运行构建标识、客户端版本、服务端版本、能力修订号或功能可用性摘要。本任务需要比较前后工具目录，再检查 JavaScript 全局对象，才能辨认连接切换带来的能力变化。同一启动目录还可能出现多个 Shell 客户端，选择时需要结合终端尾部内容辨认具体会话。

源码中的 `ClientMetadata` 仅含名称、启动目录、主机名和授权主体；私有协议携带协议版本，但没有用于区分同一软件版本不同构建的身份信息。新版 `ssmcp://limits` 已提供服务端版本；初始入口缺少 `resources` helper，后续入口已提供这个对象。将关键身份信息直接放在 `list` 中，可以减少首次诊断和连接切换时的额外查询。[S3][S4][S5]

旧客户端失效时，接口明确返回 `client is unavailable; call list to refresh connected clients`，按提示刷新后可找到新客户端。剩余需求是让新旧实例、同目录的多个 Shell 会话及各自能力更容易辨认。

**建议：**在 `list` 的顶层返回服务端 `version`、`build_commit`、`protocol_version` 和 `code_mode_api`；每个客户端返回其 `version`、`build_commit`、`catalog_revision` 及能力摘要。将稳定的逻辑客户端身份与每次连接的实例 ID 分开，增加启动时间和会话说明，便于辨认同目录的多个 Shell。保留现有明确的失效刷新提示；对能力缺失进一步区分客户端不支持、服务端不支持或需要重连。软件版本仍可保留简短形式，构建身份负责解释同版本的差异。

**验收条件：**只调用 `list` 与一次目标客户端目录查询，就能回答“哪些能力可用、缺在哪一端、需更新或重连哪一端”。

### SSMCP-02：Shell 缺少以单条命令为单位的执行结果和后台任务

**优先级：P1。归属：客户端命令执行层，以及 `code_mode` 的任务接口。证据：实测、源码确认。**

初始目录只有 `terminal.read`、`terminal.write`、`terminal.resize` 和 `terminal.status`；后续目录扩展为 32 项，增加了文件与上下文等原语。本轮实际使用的 Shell 执行仍通过终端读写：`terminal.write` 返回写入字节数，`terminal.status` 返回 Shell 进程状态和输出游标。一次写入成功之后，调用方仍需解析终端内容，才能判断那条构建或测试命令是否完成、退出码是多少。本任务的首次边界回归实际退出 101，终端读写工具仍正常返回；退出码由额外的 Python 执行包装记录，而非原生命令结果提供。

长任务、并行测试及超时后的继续观察尤其麻烦：已使用的终端接口没有对应单条命令的任务标识，无法按任务收集输出或取消指定任务。后续入口虽然已提供 `jobs` 对象，但固定提交中的 `jobs.*` 只管理 `sync` 类型的 rsync 任务，不能用于 Shell 构建进程。[S6][S7]

**建议：**保留交互终端，同时提供结构化 `shell.exec`，接受明确的命令与工作目录，返回 `job_id`、状态、退出码、耗时及有界输出；补齐按任务读取、等待、列出和取消。并行命令各自拥有输出游标和进程组。客户端仍显示实际执行命令与实时输出。`code_mode` 超时后，可通过同一个任务标识继续查询，避免无依据地重跑命令。

**验收条件：**并行运行一条成功命令、一条失败命令和一条持续输出的后台命令，分别得到正确的退出码、状态和日志；取消其中一条不影响其余任务。

### SSMCP-03：文件与上下文读取已恢复，原生编辑和客户端显示待验收

**优先级：P1（交付链观察）。归属：运行实例交付和能力发现。证据：前后连接均有实测、源码确认。状态：读取已恢复，编辑与显示待验证。**

初始连接需要借助 Shell 读取代码、AGENTS.md 和技能文件。任务后段旧客户端断开，刷新 `list` 后找到提供 32 个工具的新客户端，原生读取已经可用，并完成以下验证：

| 原语 | 实际调用与结果 | 已覆盖范围 |
| --- | --- | --- |
| `fs.readText` | 完整读取 `scripts/build_onisim_native.py`，返回 5,137 字节及非空 `sha256` | 真实项目文件的结构化完整读取 |
| `agents.resolve` | 目标为 `mods/CycleTrim/OniSim/native/src`；返回仓库 AGENTS 规则链、目标路径和根信息，没有 diagnostics | 目标路径对应的项目规则解析 |
| `skills.view` | `limit: 8`；总计 18 项，显示 8 项，并返回 `next_cursor` | 技能摘要索引与分页信息 |

这三项读取没有调用 harness 或模型。原生文件编辑、客户端 diff 画面及文件与技能操作的终端显示尚未实测，保留为独立验收项。

固定源码已经实现 `fs.readText/writeText/editText`、`skills.list/view/read`、`agents.list/read/resolve` 和 `context.roots`。`Runtime::tools()` 会把 Workspace、Context、Rsync 工具追加到选定后端的目录；因此，选择 Shell 本身不能解释该源码下只有 4 个工具的目录。初始差异按当时的交付与连接状态保留，后续读取恢复另行记录。新增原语具有明确的路径范围、分页与写入前置条件。[S2][S8]

**建议：**保留已接通并验证的原生读取链，结合 SSMCP-01 补齐运行构建与能力身份。后续用可恢复的测试文件验证精确编辑及其前置条件，并核对客户端显示的文件名、技能来源和差异。不要把 helper 存在或读取通过写成编辑与 UI 已验收。

**验收状态：**文件完整读取、项目规则解析和技能摘要分页已通过本次实际调用。精确编辑、陈旧内容拒绝以及用户终端的相应操作和 diff 展示仍待验收。

### SSMCP-04：面向模型的终端返回包含大量渲染内容和重复表示

**优先级：P2。归属：PTY 读取结果与 `code_mode` 结果组织。证据：实测、源码确认。**

初始连接中，默认交互 Shell 的提示符、光标移动、ANSI/OSC 控制序列及输入重绘进入了 `terminal.read`。每段数据同时以 `text` 与 `data_base64` 返回，原生 MCP 结果又同时包含文本形式的 JSON 和 `structuredContent`。若直接返回完整原生结果，模型需要处理多份相同内容。

这些现象影响内容提取和上下文开销。本次通过只选择需要的结构化字段，并使用简洁 Shell 提示符继续工作。本文没有观察到命令被重复执行，输入回显与多份数据表示也不用于作出这种判断。[S6][S9]

**建议：**为模型读取提供明确的 `format: text | raw`，文本模式保留命令输出并清理终端渲染控制；原始模式保留字节供需要完整终端语义的调用方使用。提供会检查 `isError` 的结果提取 helper，避免每次重复编写展开逻辑。客户端彩色日志与模型读取格式分别设计，用户仍可观看完整的终端过程。

**验收条件：**相同命令在彩色交互 Shell 和简洁 Shell 下返回相同的可读正文；二进制模式按字节保真；默认模型结果只携带一份所需正文。

### SSMCP-05：终端分页字段没有直接说明是否还有待取输出

**优先级：P2。归属：客户端终端读取协议。证据：实测、源码确认。**

初始连接的一次 `terminal.read` 最多返回 32 KiB。达到单次上限时，返回的 `truncated` 仍可为 `false`。固定源码解释了该字段的含义：它表示请求的游标是否早于环形缓冲区起点，也就是旧输出是否已被覆盖；它不表示本次是否读完所有已产生的输出。`output_eof` 则与终端输出流结束相关，长期运行的 Shell 通常不会结束。[S6]

调用方需要额外比较 `terminal.status` 的缓冲区末尾，或继续读到空结果，才能判断当前是否已追上输出。长编译日志还共享一个 1 MiB 的环形缓冲区，旧输出被覆盖后没有任务级日志引用。[S6]

**建议：**读取结果增加 `start_cursor`、`end_cursor`、`has_more` 和 `lost_bytes`；将覆盖丢失与正常分页明确区分。结构化命令任务应返回可继续读取的日志句柄，在有界保留策略内支持找回完整测试输出。

**验收条件：**读取超过一页的日志时，调用方可仅凭每页返回值完成遍历；落后于保留缓冲区时得到准确的丢失字节数。

UTF-8 跨页的实际文本损坏另见 SSMCP-07，避免把分页状态问题和字符解码问题混为一项。

### SSMCP-06：已核对源码仍采用单后端选择，没有汇总已安装 harness 的工具目录

**优先级：P1。归属：客户端后端注册架构。证据：源码确认；实际安装覆盖待验证。**

已核对的固定提交仍解析 `-t/--type`，`Backend::start` 根据一个 `Mode` 创建一个后端，`Backend::tools` 也只从该后端取工具。`Runtime::tools()` 追加的是 ssmcp 自有 Workspace、Context 和 Rsync 原语；该调用链没有自动汇总本机多个 harness 的内置工具、MCP 工具和插件工具。后续连接出现 32 项工具和技能索引，仅更新了本次可见能力记录，未用于推断全部 harness 的实际注册情况。[S8][S10]

如果希望以 Shell 作为统一入口自动发现已安装工具，这一实现仍需要额外的工具提供者注册层。本次没有调用模型后端，也没有验证开发机上每个 harness 的全部安装与配置状态。

**建议：**把用户终端与工具提供者注册分开；统一发现已支持的工具提供者，返回带来源和命名空间的工具目录，并显示未注册的原因。注册工具不应发起模型请求；调用模型本身继续作为需明确选择和授权的独立能力。提供重新扫描，避免只能重启整个连接才能更新目录。

**验收条件：**同一 Shell 客户端能列出多个已安装提供者的工具，名称冲突可解释，未配置的提供者有诊断；发现和调用非模型工具时模型调用计数为零。

### SSMCP-07：UTF-8 字符跨页时，文本结果被替换字符损坏

**优先级：P2。归属：客户端终端文本解码。证据：初始连接实测、固定源码确认。状态：后续连接未重测。**

本任务在初始连接上，对已有终端输出中的同一个中文字符“更”进行了只读重复取样，没有生成额外终端输出。一次读取 3 字节得到正确文字；按 1 字节逐页读取，同一组字节被分别解码成三个替换字符。

| 读取方式 | 返回的 `text` | 返回的 `data_base64` |
| --- | --- | --- |
| 从字符起点读取 3 字节 | `更` | `5pu0` |
| 第 1 个字节 | `�` | `5g==` |
| 第 2 个字节 | `�` | `mw==` |
| 第 3 个字节 | `�` | `tA==` |

三页的 `truncated` 均为 `false`，且 `next_cursor` 每次正确推进 1 字节。这说明字节仍在，受损的是独立解码后的文本；直接拼接各页 `text` 得到 `���`。源码对任意字节切片逐页调用 `String::from_utf8_lossy`，与实测一致。[S6]

后续连接尚未重测该 PTY 行为。`fs.readText` 是另一个文件读取原语：固定源码和文档说明它按 UTF-8 字符边界分页，并返回字节偏移与 `next_offset`；本轮也已成功完整读取项目文件。PTY 的跨页结果损坏不得扩展为文件读取同样受损，文件原语的跨页边界本轮未另做测试。[S2]

**建议：**文本模式在 UTF-8 字符边界分页，或提供跨页增量解码，并使返回游标始终对应实际消费的字节；原始模式继续提供完整字节。当前调用方可拼接 base64 解码后的字节，再统一进行 UTF-8 解码。

**验收条件：**中文和 emoji 位于不同分页边界时，分页后的文本拼接与完整读取一致；小页、空输出和缓冲区覆盖有明确语义，原始字节往返保持一致。


## 无副作用复现入口

先调用 `list`，选取返回的真实客户端 ID，再调用 `list({client_id})`。初始连接目录为以下 4 项；旧客户端断开并刷新发现后，新的 OniMods 客户端返回 32 项工具：

```text
terminal.read
terminal.write
terminal.resize
terminal.status
```

下列 `code_mode` 只检查全局对象是否存在，不操作客户端：

```javascript
return {
  fs: typeof fs,
  skills: typeof skills,
  agents: typeof agents,
  context: typeof context,
  resources: typeof resources,
  jobs: typeof jobs,
  rsync: typeof rsync,
  ports: typeof ports
};
```

初始连接的 8 项返回值均为 `"undefined"`。后续连接返回如下结果：

```json
{
  "fs": "object",
  "skills": "object",
  "agents": "object",
  "context": "object",
  "resources": "object",
  "jobs": "object",
  "rsync": "function",
  "ports": "object"
}
```

在后续连接中，以下三个读取已实际执行；`clientId` 应来自该次 `list` 返回，不能复用已失效连接的 ID：

```javascript
return await fs.readText(clientId, {
  path: "scripts/build_onisim_native.py"
});
```

```javascript
return await agents.resolve(clientId, {
  path: "mods/CycleTrim/OniSim/native/src"
});
```

```javascript
return await skills.view(clientId, { limit: 8 });
```

结果分别为完整 5,137 字节文件及摘要、无 diagnostics 的项目规则链、18 项中的前 8 项技能摘要及下一页游标。前后结果记录能力变化，具体进程和部署环节仍需构建身份信息。

## 与现有工作的关系

初次源码核对时，仓库开放列表只有 [PR #4：Improve terminal logs and harden deployment observability](https://github.com/LIghtJUNction/ssmcp/pull/4)，包含终端日志格式、颜色、分流、长度控制和部署观测工作。本文的客户端可读日志建议与它有交集；后续应在已有工作上核对覆盖范围。该 PR 的状态属于当时快照，面向模型的 PTY 读取格式、命令任务生命周期和能力协商需要分别验收。

已有工作区桥接和上下文原语有实现及验收记录；本轮后续连接已确认文件与上下文读取可用。原生编辑、客户端显示、跨主机同步和端口转发保留为未验收项。本轮没有创建重复 issue，也没有将连接能力变化归因于本轮的服务器部署或重启。

## 源码与文档依据

以下链接固定到本次核对提交，便于后续源码变化后复查。

- **S1 — 版本字段：**[Cargo.toml](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/Cargo.toml)。
- **S2 — 新原语与部署边界：**[docs/validation-context-primitives.md](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/docs/validation-context-primitives.md)、[docs/context.md](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/docs/context.md)。
- **S3 — list、usage 与服务端元信息：**[src/http.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/http.rs)，重点 `client_info`、`usage`、`list`、`server_info`。
- **S4 — 注册元数据与协议上限：**[src/wire.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/wire.rs)，重点 `ClientMetadata`、`Frame::Hello`、`MAX_READ`、`MAX_WRITE`。
- **S5 — 动态资源和能力说明：**[src/resources.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/resources.rs)，重点 `limits`。
- **S6 — 终端执行、分页与缓冲：**[src/pty.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/pty.rs)，重点 `Terminal::perform`、`Terminal::read`、`OUTPUT_CAPACITY`。
- **S7 — jobs 仅绑定同步任务：**[src/bridge.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/bridge.rs)，重点 `jobs.list/status/wait/cancel` 分发。
- **S8 — 向选定后端追加通用原语：**[src/runtime.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/runtime.rs)，重点 `Runtime::tools` 和 `Runtime::perform`。
- **S9 — 完整原生结果及 helper：**[src/code_mode.js](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/code_mode.js)，重点 `call`、`workspaceResult`、`fs`、`skills`、`agents`。
- **S10 — 后端选择与目录来源：**[src/client.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/client.rs) 的 `Options::parse` 和 `Backend::start` 调用；[src/backend/mod.rs](https://github.com/LIghtJUNction/ssmcp/blob/cba9f752a605ca992a19a8bb698cac6d3d12e87d/src/backend/mod.rs) 的 `Backend::start`、`Backend::tools` 和 `terminal_tools`。

## 证据自检

| 检查维度 | 本记录处理方式 |
| --- | --- |
| 实际贡献 | 给出 7 项具体使用问题、归属、建议和验收条件 |
| 表述清晰 | 分开入口、服务端、客户端和后端；保持 API 名称一致 |
| 证据强度 | 初始与后续连接分别标注；现场返回与固定源码分开；未把仓库测试报告写成本次执行结果 |
| 覆盖范围 | 已验证原生文件完整读取、规则链和技能摘要；未验证运行构建身份、原生编辑、客户端 diff 画面、跨主机同步、端口转发或全部 harness 安装 |
| 方案合理性 | 保留已恢复的读取能力；新增能力集中在命令任务与发现接口；PTY 解码与文件原语分开；没有据日志回显推断重复执行 |

