---
name: oni-workshop-description
description: 为《缺氧》Steam 创意工坊模组撰写、校验或改写中英文 BBCode 描述。
---

# 创意工坊描述

- 根据源码、真实运行记录和当前工坊条目核实内容；区分已发布版本、开发构建、独立测试和计划，避免承诺未验证的兼容性、性能或截图效果。保留用户要求的风险和存档警告。
- 面向玩家用自然的社区语言说明功能、开发阶段、已知限制、安装依赖和反馈途径；只加入真实适用的 DLC、接口、快捷键及社区链接。中英文事实保持一致。保留用户指定的手绘动图与实拍，并说明画面所代表的版本。
- 本项目 AI 生成模组在描述最顶部使用已验证有效的 `assets/ai-generated-warning.png` 的稳定 `main` 图片链接，紧接以下文字；不要暗示对第三方依赖或继承素材的 AI 生成归属。若图片未托管，保留文字并报告图片待发布。其他项目先核实声明是否适用。

```bbcode
[img]VERIFIED_AI_WARNING_BANNER_URL[/img]
[h1]WARNING[/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]
```

- 所有图片链接必须实测返回图片；不用临时分支、占位链接或失效图片。DLC 兼容性用经核实的文字说明，不使用 Sgt_Imalas 的兼容图标。
- 每种语言限制 8000 字符，校验 BBCode 闭合。可使用 `scripts/generate_workshop_description.py` 生成和校验初稿；按实际需求编辑，不照搬无关模板。
- 用户授权更新页面时，先 `onim workshop page show <ID>`，再按 `oni-workshop-publish` 使用 `onim workshop page set` 提交中英文描述并回读。描述更新不应重传 ZIP。
