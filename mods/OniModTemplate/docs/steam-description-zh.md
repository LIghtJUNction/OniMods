欢迎来到 OniModTemplate！一份可以直接编译的《缺氧》模组空白模板。
用这个项目，[b]新模组从 ModInfo、标准目录和 onim 构建路径开始。[/b]

补丁放在 Patches/。共用逻辑放在 Core/。覆盖层放在 UI/。选项放在 Config/。根目录只留入口和工程文件。

[h2]当前阶段: v0.1.7[/h2]
[i]这是开发脚手架，不是殖民地模组。在存档里启用模板不会增加玩法。[/i]
[i]发布前请复制成新的模组。不要把模板的 static ID 当成成品创意工坊条目发出去。[/i]

[h3]支持的 DLC:[/h3]

[h3]MOD 拓展接口 (API)[/h3]
模板面向游戏模组 API 2，并声明支持全部内容。
在仓库根目录构建和安装：

onim build -m OniModTemplate
onim dev -m OniModTemplate

PLib 仍然是可选的。新模组需要选项、翻译或共用界面时再加。Jlib 的消费者应引用共享提供者，不要再合并一份私有副本。

[h3]你会得到什么[/h3]
一套适合 Harmony 补丁的固定布局、一个 ModInfo 入口，以及 onim 已经认识的打包元数据。
没有玩法钩子，没有必需前置，这个模板也没有创意工坊页面。

[h3]操作指南与体验优化 (QoL)[/h3]
仅有的操作，是你以后自己加上去的那些。

* 根目录保留 ModInfo.cs 和工程文件。
* 补丁放在 Patches/，可复用代码放在 Core/。
* 需要本地调试安装时运行 onim dev。
* 第一次发布前换掉标题、static ID 和这两份说明。

[h3]由 onim 完成[/h3]
本模组来自 [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]。onim 是这个仓库里的工具。它把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线。

onim dev 放进游戏里试。onim build 打包。onim publish 发到 Steam。

[h3]开源贡献[/h3]
非常欢迎社区贡献！
如果你有优化、Bug 修复或新功能建议，欢迎提交 Pull Request。

请确保你的提交条理清晰并在必要处附带说明文档。

项目地址：[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniModTemplate]GitHub 仓库传送门[/url]

[h3]免责声明[/h3]
请注意这份模板会随仓库一起调整目录结构。
我们非常重视大家的反馈与建议！
它没有已发布的创意工坊 ID。第一次公开条目要走分阶段的 Legacy ZIP 流程，不能用目录上传。

[h3]联系我们[/h3]
提问请送到 [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url]。

祝制作愉快。这一页是空白的复制人登记表，细菌得你自己带。
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]
