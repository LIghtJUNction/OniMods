# 工坊封面视觉更新

本轮重新制作 CycleTrim、OniMcp、OniOnline 与 Jlib 的方形封面。保留原有识别元素：三色 C 与芯片、殖民者与终端、小行星与飞船、J 形连接器；统一暖白背景、深蓝文字和宽留白，减少缩略图中的装饰噪声。

这些封面是宣传插画，不是游戏截图、性能图表或兼容性证明。OniOnline 的副标题明确标注实验性多人。历史实拍与手绘说明图仍保留，各自代表的版本由所在文档说明。

## 成品

全部成品由内置 imagegen 编辑工具生成，逐张检查了标题、构图及文字。实际文件均为 **1254 × 1254 PNG**；没有再裁切、缩放或重绘。运行时 UI 资源不随这次工坊封面替换而改变。Jlib 的封面保存在 Jlib 子仓库。

| Mod | 仓库路径 | 字节数 | SHA-256 |
| --- | --- | ---: | --- |
| CycleTrim | `mods/CycleTrim/preview.png` | 1371336 | `f2bdd99064c938d9ed2487aa86265c46e28c98b8b7322363e8b37b64a1b3f145` |
| OniMcp | `mods/OniMcp/preview.png` | 1269834 | `2dcfb7c8efbdb7db112408cfd5e3eb4bb868ef9b4c49384e89c4311274dab8c3` |
| OniOnline | `mods/OniOnline/preview.png` | 1302480 | `fe2a347b2f10c74b71017206ede72ea3496678295f3a60ed57710b415810d110` |
| Jlib | `mods/Jlib/Jlib/preview.png` | 1241441 | `f283beb38b2d6ecc681a3dce4d4f7ce535504d63b955bb83356c51ff5de33ce1` |

工坊封面直接保存在 Git 中，使稳定的 main 图片地址与源码归档包含真实图片。其余 PNG 继续遵循现有 LFS 规则。

## 编辑提示词

以下为实际提交的完整提示词。每次调用只输出一张封面；后续图片用已完成封面作为风格参考。原版图片来自各 Mod 当时的 preview.png。

### CycleTrim

```text
Edit only the CycleTrim cover shown as the FIRST of the four supplied images. The other three are adjacent products for context, not edit targets. Create one finished square 1024 x 1024 Steam Workshop cover with a restrained modern developer-tool identity, generous whitespace, and a warm near-white background. Preserve the distinctive three nested C-shaped bands in teal, coral and golden yellow around a simple dark navy microchip. Refine the curves, optical alignment and spacing. Make the C-and-chip emblem occupy roughly the central 55 percent of the canvas with clear margins. Below it place the exact title "CycleTrim" in a clean dark navy geometric sans-serif, beautifully kerned, large and easy to read at small size. A much smaller restrained uppercase line below may read exactly "MULTICOMPONENT SIMULATION". No other words. Flat crisp graphic design, subtle paper-like softness only, no 3D gloss, no distressed texture, no bevels, no busy border. Keep the recognizable original brand motif. No performance numbers, no badges, no compatibility claims, no screenshots. This is a real project marketing cover, not a technical diagram. Output only this single CycleTrim cover.
```

### OniMcp

```text
Create a polished replacement for the OniMcp Steam Workshop cover, the SECOND of the five supplied images. The LAST image, the new light CycleTrim cover, is the style reference for this product family. Keep the recognizable cheerful dark-haired colony astronaut giving a thumbs-up from the OniMcp reference, but simplify it to a carefully drawn compact mascot with clean dark navy outlines and restrained warm orange clothing. Remove the old industrial metal frame, distress, large blue panels, tiny circuit clutter, and dark background. Use generous warm near-white whitespace matching the final CycleTrim reference. Behind the mascot, only a few very subtle blue-gray line shapes suggesting a terminal window and connection, sparse and subordinate. Below the mascot place the exact title "OniMcp" in large beautifully spaced dark navy geometric sans-serif. Below it, much smaller uppercase text "COLONY TOOLKIT". Single square product cover, balanced top and bottom margins, high legibility when reduced to 128 pixels. Refined friendly game-tool illustration, crisp and minimal, not a screenshot, no gradients or glossy 3D, no extra text, no numbers, no performance or compatibility badges. Output one image only.
```

### OniOnline

```text
Redesign the OniOnline cover, which is the SECOND of the five supplied images (the dark asteroid-and-rocket picture). Use the LAST TWO images, the light CycleTrim and OniMcp covers, as the precise visual-family reference: warm near-white background, generous whitespace, dark navy typography, restrained accent colors, clean minimal illustration. Preserve OniOnline's recognizable small asteroid, compact colony rocket and orbit-ring motif, but reduce the visual clutter dramatically. A rounded ochre asteroid with a small orange-and-charcoal rocket, surrounded by one thin teal orbit linking two small satellite dots; no starscape, no metal frame, no neon glow, no smoke cloud, no gritty texture. Keep an appealing game-like character, with clean deliberate line work, not generic corporate stock art. Center the emblem in the upper area. Below, exact large title "OniOnline" in dark navy geometric sans-serif. Below that, small text exactly "EXPERIMENTAL MULTIPLAYER". Beautiful spacing and optical alignment, comfortably clear margins. One square finished Steam Workshop cover. No performance numbers, no claim of universal compatibility, no extra words, no screenshot or fake UI. Output only one OniOnline image.
```

### Jlib

```text
Create a polished replacement for the Jlib Workshop cover, which is the SECOND of the five supplied images, showing an orange J-shaped industrial connector on a dark green background. The LAST THREE images are the approved light CycleTrim, OniMcp and OniOnline style references. Keep Jlib's unmistakable J-shaped connector concept, but simplify and refine it into a clean friendly engineering emblem: a warm orange J body, cream inner face, three short neatly drawn plug sockets, dark navy outlines, a few restrained bolts. Remove worn scratches, heavy shading and the dark background. Use the same warm near-white background, generous whitespace, precise alignment and restrained palette as the approved product family. The emblem is centered in the upper section; below it exact large title "Jlib" in dark navy geometric sans-serif. Beneath it, small uppercase text exactly "SHARED MOD FOUNDATION". Square cover, excellent legibility at small thumbnail size. No industrial outer frame, no decorative clutter, no compatibility promises, no version numbers, no other text, no screenshot or fake UI. Output exactly one finished image.
```
