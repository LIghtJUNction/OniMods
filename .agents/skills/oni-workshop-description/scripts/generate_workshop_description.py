#!/usr/bin/env python3
"""
generate_workshop_description.py

Generate authentic, high-impact Steam Workshop descriptions for Oxygen Not Included mods
following the ONI Together community flavor.

Features:
- Plain-text descriptions of supported DLC
- 1:1 English and Simplified Chinese BBCode output
- Built-in validation for Steam's 8,000 character limit and BBCode tag matching
- Directly integrates with the OniMods publish workflow (`onim workshop page`)
"""

import argparse
import os
import re
import sys
from typing import Dict, Any, Optional

# Describe only the selected compatibility; verify it against the mod before publishing.
DLC_TEXT = {
    "all": ("Base game and all DLCs.", "支持游戏本体及所有 DLC。"),
    "no-dlc": ("Base game only.", "仅支持游戏本体。"),
    "vanilla": ("Base game only.", "仅支持游戏本体。"),
    "spaced-out-only": ("Spaced Out! only; base game without it is not supported.", "仅支持眼冒金星；不支持单独运行游戏本体。"),
    "no-base": ("Spaced Out! only; base game without it is not supported.", "仅支持眼冒金星；不支持单独运行游戏本体。"),
    "frosty-required": ("The Frosty Planet Pack is required.", "需要寒霜星球包。"),
    "dlc2-required": ("The Frosty Planet Pack is required.", "需要寒霜星球包。"),
    "bionic-required": ("The Bionic Booster Pack is required.", "需要仿生扩展包。"),
    "dlc3-required": ("The Bionic Booster Pack is required.", "需要仿生扩展包。"),
    "prehistoric-required": ("The Prehistoric Planet Pack is required.", "需要史前星球包。"),
    "dlc4-required": ("The Prehistoric Planet Pack is required.", "需要史前星球包。"),
    "aquatic-required": ("The Aquatic Planet Pack is required.", "需要水洋星球包。"),
    "dlc5-required": ("The Aquatic Planet Pack is required.", "需要水洋星球包。"),
}

DEFAULT_GIF = ""

KNOWN_BANNERS = {
    "jlib": "https://raw.githubusercontent.com/LIghtJUNction/Jlib/main/Jlib/preview.png",
    "cycletrim": "https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/preview.png",
    "onimcp": "https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniMcp/preview.png",
    "onionline": "https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniOnline/preview.png",
}

def render_description_en(data: Dict[str, Any]) -> str:
    """Build English BBCode description."""
    parts = []
    if data.get("ai_warning_banner_url"):
        parts.append(f"[img]{data['ai_warning_banner_url']}[/img]")
    parts.append("[h1]CONGRATULATIONS![/h1]\n[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]\n")

    # 1. Header Banner
    if data.get("banner_url"):
        parts.append(f"[img]{data['banner_url']}[/img]\n")

    # 2. Hero Pitch / Hook
    mod_name = data.get("name", "Mod Name")
    tagline = data.get("tagline", "The definitive experience!")
    core_pitch = data.get("core_pitch", "supercharges your colony with seamless gameplay.")
    sub_pitch = data.get("sub_pitch", "Collaborate, optimize, and let your dupes thrive.")

    parts.append(f"Welcome to {mod_name}! {tagline}")
    parts.append(f"With this mod, [b]{core_pitch}[/b]\n")
    if sub_pitch:
        parts.append(f"{sub_pitch}\n")

    # 3. Current Stage & Caveat
    stage = data.get("stage", "v0.1.0 Beta")
    stage_warning = data.get("stage_warning", "This is an experimental build, so expect bugs and occasional crashes.")
    mod_compat_warning = data.get("mod_compat_warning", "Use other mods at your own discretion as they may cause issues or instability.")

    parts.append(f"[h2]Current Stage: {stage}[/h2]")
    parts.append(f"[i]{stage_warning}[/i]")
    parts.append(f"[i]{mod_compat_warning}[/i]\n")

    # 4. Supported DLC
    parts.append("[h3]Supported DLC:[/h3]")
    parts.append(DLC_TEXT[data.get("dlc_badge", "all")][0] + "\n")

    # 5. Modding API / Extensibility (Optional)
    if data.get("api_info"):
        parts.append("[h3]MODDING API[/h3]")
        parts.append(data["api_info"] + "\n")

    # 6. Synchronization / Roadmap / Features
    if data.get("roadmap_info"):
        roadmap_title = data.get("roadmap_title", "What is synchronized?")
        parts.append(f"[h3]{roadmap_title}[/h3]")
        parts.append(data["roadmap_info"] + "\n")

    # 7. Controls & QoL
    if data.get("controls"):
        parts.append("[h3]Controls and Quality of Life[/h3]")
        parts.append(data.get("controls_intro", "The mod includes handy quality of life features for smoother gameplay:") + "\n")
        for item in data["controls"]:
            parts.append(f"* {item}\n")

    # 8. onim workflow, then contributions
    parts.append("[h3]Built with onim[/h3]")
    parts.append("This mod comes from [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]. onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing.\n")
    parts.append("onim dev tries it in game. onim build packs it. onim publish sends it to Steam.\n")
    if data.get("github_url"):
        parts.append("[h3]Contributions[/h3]")
        parts.append("Contributions are welcome!")
        parts.append("If you have improvements, fixes, or new features, feel free to open a Pull Request.\n")
        parts.append("Please make sure your changes are clear and well-documented where necessary.\n")
        parts.append(f"You can find the [url={data['github_url']}]GitHub repository here[/url]\n")

    # 9. Disclaimer
    parts.append("[h3]Disclaimer[/h3]")
    parts.append("Please remember that this mod is under active development and will receive regular updates and improvements.")
    parts.append("Your feedback and suggestions are highly appreciated.")
    parts.append(data.get("disclaimer_extra", "Report any issues you run into on GitHub or Discord.") + "\n")

    # 10. Contact & Community
    parts.append("[h3]Contact[/h3]")
    if data.get("discord_url"):
        parts.append(f"[url={data['discord_url']}]Join our Discord community[/url] to connect with other players, suggest features, and stay up-to-date with releases.\n")
    
    closing_quote = data.get("closing_quote", "That's about it, have fun out there and remember. When together oxygen is not included.")
    parts.append(closing_quote)
    
    gif_url = data.get("closing_gif", DEFAULT_GIF)
    if gif_url:
        parts.append(f"[img]{gif_url}[/img]\n")

    # 11. Support / Donation
    if data.get("support_url"):
        parts.append("Like what I'm doing? You can support my work:")
        support_label = data.get("support_label", "Ko-Fi")
        parts.append(f"[url={data['support_url']}]{support_label}[/url]")

    return "\n".join(parts).strip()


def render_description_zh(data: Dict[str, Any]) -> str:
    """Build Simplified Chinese BBCode description."""
    parts = []
    if data.get("ai_warning_banner_url"):
        parts.append(f"[img]{data['ai_warning_banner_url']}[/img]")
    parts.append("[h1]CONGRATULATIONS![/h1]\n[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]\n")

    # 1. Header Banner
    if data.get("banner_url"):
        parts.append(f"[img]{data['banner_url']}[/img]\n")

    # 2. Hero Pitch / Hook
    mod_name = data.get("name_zh", data.get("name", "Mod Name"))
    tagline = data.get("tagline_zh", "终极游玩体验！")
    core_pitch = data.get("core_pitch_zh", "为您的殖民地注入前所未有的强力体验。")
    sub_pitch = data.get("sub_pitch_zh", "探索、规划与协作，让您的复制人们欣欣向荣！")

    parts.append(f"欢迎来到 {mod_name}！{tagline}")
    parts.append(f"有了这个模组，[b]{core_pitch}[/b]\n")
    if sub_pitch:
        parts.append(f"{sub_pitch}\n")

    # 3. Current Stage & Caveat
    stage = data.get("stage", "v0.1.0 Beta")
    stage_warning = data.get("stage_warning_zh", "本模组目前处于测试阶段，可能会遇到 Bug 或偶尔崩溃。")
    mod_compat_warning = data.get("mod_compat_warning_zh", "请谨慎搭配其他第三方模组使用，部分模组可能引发冲突或稳定性问题。")

    parts.append(f"[h2]当前阶段: {stage}[/h2]")
    parts.append(f"[i]{stage_warning}[/i]")
    parts.append(f"[i]{mod_compat_warning}[/i]\n")

    # 4. Supported DLC
    parts.append("[h3]支持的 DLC:[/h3]")
    parts.append(DLC_TEXT[data.get("dlc_badge", "all")][1] + "\n")

    # 5. Modding API / Extensibility (Optional)
    if data.get("api_info_zh") or data.get("api_info"):
        parts.append("[h3]MOD 拓展接口 (API)[/h3]")
        parts.append(data.get("api_info_zh", data.get("api_info")) + "\n")

    # 6. Synchronization / Roadmap / Features
    if data.get("roadmap_info_zh") or data.get("roadmap_info"):
        roadmap_title = data.get("roadmap_title_zh", "特性与路线图")
        parts.append(f"[h3]{roadmap_title}[/h3]")
        parts.append(data.get("roadmap_info_zh", data.get("roadmap_info")) + "\n")

    # 7. Controls & QoL
    if data.get("controls_zh") or data.get("controls"):
        parts.append("[h3]操作指南与体验优化 (QoL)[/h3]")
        parts.append(data.get("controls_intro_zh", "模组包含多项贴心的协同与便利功能，让操作更加丝滑：") + "\n")
        controls = data.get("controls_zh") or data.get("controls")
        for item in controls:
            parts.append(f"* {item}\n")

    # 8. onim 全流程，然后是开源贡献
    parts.append("[h3]由 onim 完成[/h3]")
    parts.append("本模组来自 [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]。onim 是这个仓库里的工具。它把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线。\n")
    parts.append("onim dev 放进游戏里试。onim build 打包。onim publish 发到 Steam。\n")
    if data.get("github_url"):
        parts.append("[h3]开源贡献[/h3]")
        parts.append("非常欢迎社区贡献！")
        parts.append("如果你有优化、Bug 修复或新功能建议，欢迎提交 Pull Request。\n")
        parts.append("请确保你的提交条理清晰并在必要处附带说明文档。\n")
        parts.append(f"项目地址：[url={data['github_url']}]GitHub 仓库传送门[/url]\n")

    # 9. Disclaimer
    parts.append("[h3]免责声明[/h3]")
    parts.append("请注意本模组仍在紧密迭代中，我们将保持频繁更新与维护。")
    parts.append("我们非常重视大家的反馈与建议！")
    parts.append(data.get("disclaimer_extra_zh", "遇到任何问题欢迎向我们反馈汇报。") + "\n")

    # 10. Contact & Community
    parts.append("[h3]联系我们[/h3]")
    if data.get("discord_url"):
        parts.append(f"[url={data['discord_url']}]加入我们的社区[/url] 与其他小伙伴交流心得、提出建议并获取最新动态！\n")
    
    closing_quote = data.get("closing_quote_zh", "祝大家游戏愉快，时刻牢记：即使困难重重，也不要停止呼吸。")
    parts.append(closing_quote)
    
    gif_url = data.get("closing_gif", DEFAULT_GIF)
    if gif_url:
        parts.append(f"[img]{gif_url}[/img]\n")

    # 11. Support / Donation
    if data.get("support_url"):
        parts.append("如果喜欢我的工作，欢迎赞助支持：")
        support_label = data.get("support_label_zh", data.get("support_label", "赞助通道"))
        parts.append(f"[url={data['support_url']}]{support_label}[/url]")

    return "\n".join(parts).strip()


def validate_description(text: str, lang: str = "en") -> None:
    """Validate character limits and BBCode syntax."""
    length = len(text)
    print(f"[{lang.upper()}] Character count: {length} / 8000 limit", file=sys.stderr)
    if length > 8000:
        raise ValueError(f"Steam Workshop description for {lang} exceeds limit: {length} > 8000 chars!")

    # Check BBCode balance for common tags
    paired_tags = ["b", "i", "u", "h1", "h2", "h3", "strike", "spoiler", "list", "olist", "table"]
    for tag in paired_tags:
        open_count = len(re.findall(rf"\[{tag}\]", text, re.IGNORECASE))
        close_count = len(re.findall(rf"\[/{tag}\]", text, re.IGNORECASE))
        if open_count != close_count:
            print(f"Warning: Tag [{tag}] mismatch ({open_count} open vs {close_count} close)", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description="Generate Steam Workshop BBCode descriptions in ONIT community style")
    parser.add_argument("--name", default="OniOnline", help="Mod name")
    parser.add_argument("--name-zh", default="OniOnline 联机框架", help="Chinese Mod name")
    parser.add_argument("--tagline", default="The definitive multiplayer experience!", help="Tagline")
    parser.add_argument("--tagline-zh", default="缺氧多人联机框架！", help="Chinese Tagline")
    parser.add_argument("--core-pitch", default="all players share controls over a single colony.", help="Core pitch")
    parser.add_argument("--core-pitch-zh", default="支持多人加入共享房间，多端同步与局域网/Steam联机。", help="Chinese core pitch")
    parser.add_argument("--stage", default="v0.5.0 Alpha", help="Current Stage")
    parser.add_argument("--dlc", default="all", choices=list(DLC_TEXT.keys()), help="Supported DLC compatibility key (verify before publishing)")
    parser.add_argument("--banner", default="", help="Header banner image URL")
    parser.add_argument("--github", default="https://github.com/LIghtJUNction/OniMods", help="GitHub repository URL")
    parser.add_argument("--discord", default="", help="Discord URL")
    parser.add_argument("--output-dir", default="", help="Directory to save steam-description-en.md and zh.md")

    parser.add_argument("--ai-warning-banner", default="", help="Verified public URL of the English AI-generated warning image")

    args = parser.parse_args()

    data = {
        "ai_warning_banner_url": args.ai_warning_banner,
        "name": args.name,
        "name_zh": args.name_zh,
        "tagline": args.tagline,
        "tagline_zh": args.tagline_zh,
        "core_pitch": args.core_pitch,
        "core_pitch_zh": args.core_pitch_zh,
        "stage": args.stage,
        "dlc_badge": args.dlc,
        "banner_url": KNOWN_BANNERS.get(args.banner.lower(), args.banner) if args.banner else "",
        "github_url": args.github,
        "discord_url": args.discord,
        "controls": [
            "Player Cursors: Real-time pointer sync showing dupe targets and active tools",
            "Pinging and Annotations: Fully synchronized map markers for collaborative planning",
            "Quick Chat: Press Ctrl+T in colony to communicate with fellow duplicants",
        ],
        "controls_zh": [
            "玩家光标同步：实时显示队友指向位置与正在使用的工具状态",
            "地图标记与涂鸦：全量网络同步的坐标标记，规划基地更直观",
            "快捷通信：在殖民地中按 Ctrl+T 打开快捷聊天框快速交流",
        ]
    }

    desc_en = render_description_en(data)
    desc_zh = render_description_zh(data)

    validate_description(desc_en, "en")
    validate_description(desc_zh, "zh")

    if args.output_dir:
        os.makedirs(args.output_dir, exist_ok=True)
        en_path = os.path.join(args.output_dir, "steam-description-en.md")
        zh_path = os.path.join(args.output_dir, "steam-description-zh.md")
        with open(en_path, "w", encoding="utf-8") as f:
            f.write(desc_en + "\n")
        with open(zh_path, "w", encoding="utf-8") as f:
            f.write(desc_zh + "\n")
        print(f"Generated {en_path} and {zh_path}")
    else:
        print("=== ENGLISH ===")
        print(desc_en)
        print("\n=== SIMPLIFIED CHINESE ===")
        print(desc_zh)


if __name__ == "__main__":
    main()
