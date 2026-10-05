---
name: oni-game-advisor
description: 兼容早期 oni-game-advisor 路径的缺氧机制和策略咨询入口，复用 oni-mcp-game-advisor 的只读顾问规则与阶段参考。
---

# ONI 游戏顾问兼容入口

读取并遵循 [OniMcp 游戏顾问](../oni-mcp-game-advisor/SKILL.md)。它维护事实来源、当前存档只读边界、工具发现和答复规则，避免这里再维护一份相同规则。

阶段规划和模块选型继续按顾问入口路由到 [阶段策略技能](../oni-mcp-colony-strategy/SKILL.md)。纯机制问题不必加载全部主题参考；回答具体方案时附对应原文链接。

这个兼容入口不授权游戏写入，也不调用已禁用的游戏内百科。
