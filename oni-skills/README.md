# oni-skills: 缺氧 Agent 技能与高阶攻略库

> **声明：本项目为 OniMods 的可选子项目（Optional Subproject）。**
> 核心 Mod（OniMcp、CycleTrim 等）在不依赖本子项目的情况下即可独立编译与运行。
> 本仓库旨在为人类玩家、AI Agent 与自动化控制提供高质量、结构化、经验证的《缺氧》（Oxygen Not Included）游戏机制与高阶攻略，**欢迎社区共同维护与完善**。
>
> 独立 GitHub 仓库地址：[https://github.com/LIghtJUNction/oni-skills](https://github.com/LIghtJUNction/oni-skills)

---

## 项目定位

在《缺氧》中，纯粹的低级操作（如随便挖格子、盲目建造）往往会导致基地的崩溃（例如热量失控爆温、复制人跑腿浪费、食物短缺、气压失衡）。

高阶玩家（大牛）与常规玩家的核心区别，在于对**游戏物理底层逻辑**的理解以及**成熟模块化工程方案**的应用。`oni-skills` 将这些宝贵的社区智慧沉淀为标准化、机器可读、人类易查的模块知识库。

## 目录结构

```text
oni-skills/
├── README.md               # 项目介绍与导航
├── SKILL.md                # Agent Skill 规范入口
├── CONTRIBUTING.md         # 攻略提交与共同完善规范
└── guides/                 # 核心攻略与模块分类
    ├── veteran-rules.md        # 大牛核心心法（默认禁用自动消毒/温控/水封/气体动力学）
    ├── opening-and-base.md     # 开局生存与基地布局（1~50周期关键节点）
    ├── oxygen-and-water.md     # 制氧与水循环（SPOM/高压制氧/污水自循环）
    ├── food-and-ranching.md    # 食物与生态养殖（哈奇/壁虎/食物保鲜/孵化节电）
    ├── heat-and-industry.md    # 工业与热控黄金搭档（精炼金属/ST-AT热吞噬/石油提炼）
    ├── geysers-and-taming.md   # 喷孔与地热驯化（蒸汽泉/火山/天然气泉）
    ├── automation-and-power.md # 电网拓扑与自动化（主干电网/智能电池联动/逻辑门）
    └── space-and-rocketry.md   # 太空与火箭技术（流星防护/太阳能/分级推进）
```

## 核心设计原则

1. **机制优于死记**：不仅给出“怎么造”，更讲透“为什么这样造”（热容、导热率、物态变化、相变判定）。
2. **拒绝作弊资源**：所有攻略必须完全基于原版生存模式（Survival Mode）与官方 DLC 真实机制，严禁依赖沙盒/Debug 刷资源。
3. **经验证的健壮性**：推荐的模块设计应具备容错性（如过载保护、防漏气设计、防管道冻结/气化破裂机制）。
4. **共同完善**：随 ONI 官方版本更新，机制可能发生微调，发现差异欢迎提交 PR 修正或补充变体方案。
