# ssmcp 使用问题记录：CycleTrim 开发任务

记录日期：2026-11-16（Asia/Taipei）。本记录来自一次通过 ssmcp 操作开发机、检查并迭代 CycleTrim 的实际任务，以及对公开源码的只读核对。

## 结论与范围

当前连接可以完成终端操作，但工程自动化需要调用方自行处理命令完成判定、退出码、终端噪声和分页。文件、技能、AGENTS.md、同步及端口映射的实现已经存在于已核对的源码；本次连接没有暴露这些能力。下一步应先把运行中的服务端、客户端与实际工具目录对应起来，再接通现有能力，并补齐结构化 Shell 任务。

本记录区分三类证据：**实测**指本任务实际返回的工具结果；**源码确认**指固定提交中的实现；**待验证**指尚未做运行测试的行为。本文记录使用问题和改进方向，本轮没有改动 ssmcp 服务器或客户端，也没有启动任何模型后端。

### 已核对的对象

| 对象 | 观察结果 | 证据边界 |
| --- | --- | --- |
| 本次开发机客户端 | `list({client_id})` 返回 4 个 `terminal.*` 工具 | 反映这条连接实际可用的工具目录 |
| 本次 `code_mode` | `fs`、`skills`、`agents`、`context`、`resources`、`jobs`、`rsync`、`ports` 均为 `undefined` | 反映当前入口执行环境，没有确定服务端进程的源码提交 |
| 开发机 PATH 中的命令 | `ssmcp --version` 返回 `0.4.1` | 版本字符串没有携带源码提交或能力清单 |
| 已核对的 GitHub 源码 | `cba9f752a605ca992a19a8bb698cac6d3d12e87d`，Cargo 版本 `0.4.1` | 下文源码结论限定于该提交 |
| 新原语的仓库验收记录 | 报告记载 Shell 目录为 32 个工具，并明确当轮没有安装或重启生产服务 | 报告与本次连接的 4 工具目录有差异；具体部署、进程和路由关系仍待核对 |

`0.4.1` 这个版本号本身不足以定位差异。当前连接可能涉及不同构建、未更新的服务进程或入口路由；本文不把任何一种原因写成已确认事实。[S1][S2][S3]

## 问题清单

### SSMCP-01：入口缺少版本和能力协商信息

**优先级：P1。归属：服务端发现接口与客户端注册协议。证据：实测、源码确认。**

`list` 提供客户端标识、名称、类型、主机名和启动目录，但没有运行构建标识、客户端版本、服务端版本、能力版本或功能可用性摘要。需要同时读取源码、验收文档、原生工具目录，再用 JavaScript 检查全局对象，才能发现文档中的能力在当前连接中不可用。

源码中的 `ClientMetadata` 仅含名称、启动目录、主机名和授权主体；私有协议携带协议版本，但没有用于区分同一软件版本不同构建的身份信息。新版 `ssmcp://limits` 已提供服务端版本，但本次入口连 `resources` helper 也没有，无法依赖这个资源完成初始诊断。[S3][S4][S5]

**建议：**在 `list` 的顶层返回服务端 `version`、`build_commit`、`protocol_version` 和 `code_mode_api`；每个客户端返回其 `version`、`build_commit`、`catalog_revision` 及能力摘要。对声明过但当前缺失的能力，返回可区分的原因，例如客户端不支持、服务端不支持或需要重连。软件版本仍可保留简短形式，构建身份负责解释同版本的差异。

**验收条件：**只调用 `list` 与一次目标客户端目录查询，就能回答“哪些能力可用、缺在哪一端、需更新或重连哪一端”。

### SSMCP-02：Shell 缺少以单条命令为单位的执行结果和后台任务

**优先级：P1。归属：客户端命令执行层，以及 `code_mode` 的任务接口。证据：实测、源码确认。**

实际目录只有 `terminal.read`、`terminal.write`、`terminal.resize` 和 `terminal.status`。`terminal.write` 返回写入字节数；`terminal.status` 返回 Shell 进程状态和输出游标。一次写入成功之后，调用方仍需解析终端内容，才能判断那条构建或测试命令是否完成、退出码是多少。

长任务、并行测试及超时后的继续观察尤其麻烦：当前接口没有对应单条命令的任务标识，无法按任务收集输出或取消指定任务。源码中的 `jobs.*` 只管理 `sync` 类型的 rsync 任务，不能用于 Shell 构建进程。[S6][S7]

**建议：**保留交互终端，同时提供结构化 `shell.exec`，接受明确的命令与工作目录，返回 `job_id`、状态、退出码、耗时及有界输出；补齐按任务读取、等待、列出和取消。并行命令各自拥有输出游标和进程组。客户端仍显示实际执行命令与实时输出。`code_mode` 超时后，可通过同一个任务标识继续查询，避免无依据地重跑命令。

**验收条件：**并行运行一条成功命令、一条失败命令和一条持续输出的后台命令，分别得到正确的退出码、状态和日志；取消其中一条不影响其余任务。

### SSMCP-03：原生文件和上下文能力未贯通到本次连接

**优先级：P1。归属：运行实例交付和能力发现。证据：实测、源码确认。**

本次只能借助 Shell 读取代码、AGENTS.md 和技能文件。当前入口没有原生文件读写或上下文 helper，也无法通过这些原生调用验证客户端的文件读取提示、修改差异和技能使用提示。

仓库已经实现 `fs.readText/writeText/editText`、`skills.list/view/read`、`agents.list/read/resolve` 和 `context.roots`。`Runtime::tools()` 会把 Workspace、Context、Rsync 工具追加到选定后端的目录；因此，固定为 Shell 后端本身不能解释最新源码下只有 4 个工具的目录。代码中的这些新增原语具有明确的路径范围、分页与写入前置条件。[S2][S8]

**建议：**复用现有实现，核对运行中的服务端与客户端构建，完成端到端接通；接通后以一个真实项目验证文件读取、精确编辑、技能摘要和规则链，并核对客户端显示的文件名、技能来源及差异。失败时回传明确的能力缺失信息。

**验收条件：**目标连接能够直接读取项目规则和技能摘要，原生读取及编辑返回结构化结果，用户终端展示相应操作；这些步骤无需拼接 Shell 读写脚本。

### SSMCP-04：面向模型的终端返回包含大量渲染内容和重复表示

**优先级：P2。归属：PTY 读取结果与 `code_mode` 结果组织。证据：实测、源码确认。**

默认交互 Shell 的提示符、光标移动、ANSI/OSC 控制序列及输入重绘进入了 `terminal.read`。每段数据同时以 `text` 与 `data_base64` 返回，原生 MCP 结果又同时包含文本形式的 JSON 和 `structuredContent`。若直接返回完整原生结果，模型需要处理多份相同内容。

这些现象影响内容提取和上下文开销。本次通过只选择需要的结构化字段，并使用简洁 Shell 提示符继续工作。本文没有观察到命令被重复执行，输入回显与多份数据表示也不用于作出这种判断。[S6][S9]

**建议：**为模型读取提供明确的 `format: text | raw`，文本模式保留命令输出并清理终端渲染控制；原始模式保留字节供需要完整终端语义的调用方使用。提供会检查 `isError` 的结果提取 helper，避免每次重复编写展开逻辑。客户端彩色日志与模型读取格式分别设计，用户仍可观看完整的终端过程。

**验收条件：**相同命令在彩色交互 Shell 和简洁 Shell 下返回相同的可读正文；二进制模式按字节保真；默认模型结果只携带一份所需正文。

### SSMCP-05：终端分页字段没有直接说明是否还有待取输出

**优先级：P2。归属：客户端终端读取协议。证据：实测、源码确认。**

一次 `terminal.read` 最多返回 32 KiB。本次达到单次上限时，返回的 `truncated` 仍可为 `false`。源码解释了该字段的含义：它表示请求的游标是否早于环形缓冲区起点，也就是旧输出是否已被覆盖；它不表示本次是否读完所有已产生的输出。`output_eof` 则与终端输出流结束相关，长期运行的 Shell 通常不会结束。[S6]

调用方需要额外比较 `terminal.status` 的缓冲区末尾，或继续读到空结果，才能判断当前是否已追上输出。长编译日志还共享一个 1 MiB 的环形缓冲区，旧输出被覆盖后没有任务级日志引用。[S6]

**建议：**读取结果增加 `start_cursor`、`end_cursor`、`has_more` 和 `lost_bytes`；将覆盖丢失与正常分页明确区分。结构化命令任务应返回可继续读取的日志句柄，在有界保留策略内支持找回完整测试输出。

**验收条件：**读取超过一页的日志时，调用方可仅凭每页返回值完成遍历；落后于保留缓冲区时得到准确的丢失字节数。

**待验证补充：**该源码先按任意字节上限切片，再对每页单独调用 `String::from_utf8_lossy`，UTF-8 字符跨页时存在替换字符问题。可用中文或 emoji 位于分页边界的测试验证；本次未在运行连接上制造该情形，因此不记为已实测故障。[S6]

### SSMCP-06：已核对源码仍采用单后端选择，没有汇总已安装 harness 的工具目录

**优先级：P1。归属：客户端后端注册架构。证据：源码确认；实际安装覆盖待验证。**

当前源码仍解析 `-t/--type`，`Backend::start` 根据一个 `Mode` 创建一个后端，`Backend::tools` 也只从该后端取工具。`Runtime::tools()` 追加的是 ssmcp 自有 Workspace、Context 和 Rsync 原语；该调用链没有自动汇总本机多个 harness 的内置工具、MCP 工具和插件工具。[S8][S10]

这与“客户端统一表现为 Shell，已安装且受支持的 harness 工具自动注册到 code_mode”的目标仍有差距。本次没有调用模型后端，也没有验证开发机上每个 harness 的全部安装与配置状态。

**建议：**把用户终端与工具提供者注册分开；统一发现已支持的工具提供者，返回带来源和命名空间的工具目录，并显示未注册的原因。注册工具不应发起模型请求；调用模型本身继续作为需明确选择和授权的独立能力。提供重新扫描，避免只能重启整个连接才能更新目录。

**验收条件：**同一 Shell 客户端能列出多个已安装提供者的工具，名称冲突可解释，未配置的提供者有诊断；发现和调用非模型工具时模型调用计数为零。

## 无副作用复现入口

先调用 `list`，选取返回的真实客户端 ID，再调用 `list({client_id})`。本次目录为以下 4 项：

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

本次 8 项返回值均为 `"undefined"`。这段结果和工具目录足以记录当前入口的能力；定位到具体进程和部署环节还需要构建身份信息。

## 与现有工作的关系

检查时仓库开放列表只有 [PR #4：Improve terminal logs and harden deployment observability](https://github.com/LIghtJUNction/ssmcp/pull/4)，包含终端日志格式、颜色、分流、长度控制和部署观测工作。本文的客户端可读日志建议与它有交集；后续应在已有工作上核对覆盖范围。面向模型的 PTY 读取格式、命令任务生命周期和能力协商需要分别验收。

已有工作区桥接和上下文原语有实现及验收记录，优先解决其实际连接可用性。本文没有创建重复 issue，没有宣称已有 PR 已合并或线上问题已修复。

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
| 实际贡献 | 给出 6 项具体使用问题、归属、建议和验收条件 |
| 表述清晰 | 分开入口、服务端、客户端和后端；保持 API 名称一致 |
| 证据强度 | 现场返回与固定源码分别标注；未把仓库测试报告写成本次执行结果 |
| 覆盖范围 | 未验证生产进程身份、客户端 diff 画面、跨主机同步或全部 harness 安装 |
| 方案合理性 | 优先接通已有原语；新增能力集中在命令任务与发现接口；没有据日志回显推断重复执行 |

