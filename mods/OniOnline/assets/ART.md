# OniOnline 菜单插画

## 新版资源

| 文件 | 用途 | 排版约束 |
| --- | --- | --- |
| `online-card-v2.png` | 新游戏类型选择中的 Online 竖版卡片 | 独立标题栏由游戏绘制；插画按原始比例填满并裁切，主体留在中央，避免把重要细节放在边缘。 |
| `online-lobby-v2.png` | 大厅横幅探索稿，当前界面未使用 | 实机复核后，大厅保留原生纯色列表和灰字空状态，避免宣传横幅和口号造成网页或演示文稿观感。 |
| `onionline-logo-v1.png` | 完整透明品牌插画，1254 × 1254 RGBA | 无文字，方便多语言共用；用于封面或较大的品牌展示。 |
| `onionline-icon-v1.png` | 简化透明 Mod 图标，1254 × 1254 RGBA | 大色块、粗轮廓和低饱和裂隙；大厅标题栏仅用作小标识，不作插画横幅。 |
| `../preview.png` | 创意工坊方形封面，1254 × 1254 RGB | 独立品牌字标 `ONI ONLINE`；不能当作当前完整星球迁移功能的截图。 |

新版方向是手绘漫画轮廓、低饱和星空与有明显形状差异的小行星，使用少量温暖灯光和冷暖对比。与原生菜单搭配的主体颜色是灰紫、莓紫与暖白，不采用发光霓虹边框。插画只用于类型选择卡；大厅使用功能明确的原生式列表、名单和聊天区域。多个星球和小型火箭只表达联机主题，不代表本版本已经同步星球数据。

Unity 通过 `OnlineArtwork` 从当前 Mod 的 `assets/` 目录读取并缓存 PNG。卡片的标题、细边框和悬停状态由静态 UI 绘制；插画在遮罩内按原始宽高比裁切，避免旧版横图被压成细长竖图。卡片没有复制原生动画组件。资源缺失或无法解码时保留纯色底板，避免影响开局入口。

## Logo 与封面提示词

2026-10-21 使用内置 `imagegen` 工具生成，未使用 CLI 回退。透明图标原样保留 alpha；封面以图标为参考另行生成。主题是砂岩星球、夸张的黄色行星发动机与紫色裂隙，使用手绘墨线、磨损涂色和莓红金属细节。图标无字；品牌名在封面中独立排版，不把语言相关的界面文字画进素材。

透明标识（`transparent_background=true`，全新生成）：

```text
Use case: logo-brand. Asset type: final transparent game mod icon for OniOnline, an Oxygen Not Included multiplayer mod. Create an original, production quality square emblem with a genuine transparent background. Design one instantly readable compact silhouette: a small lumpy sandstone asteroid with a comically over-engineered yellow industrial planet engine bolted to its underside, partly passing through a tilted violet and blue temporal rift ring, with a much smaller second asteroid visible beyond the ring. The idea is carrying your own planet to visit friends. Visual language must feel at home in Oxygen Not Included: hand-inked uneven dark charcoal contours, eccentric chunky shapes, flat painted color with very restrained dry-brush wear, muted sandstone tan, grey purple mechanical metal, berry-red details and a small warm yellow engine glow. Funny industrial colony science, charming and slightly absurd, not a sleek esports badge. Strong simple shapes still readable at 64px, very few details, no busy scenery, no excessive particles. Centered composition with comfortable transparent padding, no enclosing square, no background, no white matte, no checkerboard. No text or lettering, no slogan, no watermark, no copied official logo. Do not depict interfaces, computer network symbols, weapons, humans or characters. Deliver the finished colored hand-drawn icon, not a sketch sheet or mockup.
```

封面（`transparent_background=false`，以上述透明标识为唯一图像参考）：

```text
Use case: logo-brand. Asset type: finished square Steam Workshop cover for the OniOnline game mod. Use the attached transparent original asteroid-engine-rift mark as the exact brand illustration. Preserve its sandstone planet, chunky yellow machine, berry red accents and purple rift. Compose a clean official-quality hand-painted colony game expansion cover: subdued charcoal grey-purple space backdrop with a few faint hand-inked stars and almost invisible hexagonal star-chart lines, no neon scenery. Give the illustration breathing room in the upper two thirds. The lower third has one large, beautifully legible custom hand-lettered wordmark reading exactly "ONI ONLINE", cream-white rough condensed block capitals with dark ink outline and slight uneven playful baseline, matching Oxygen Not Included's handmade industrial cartoon visual language without copying the official logo. The title must be dominant and readable as a small Workshop thumbnail; very restrained berry-red title plate behind the lettering is acceptable. No extra text, no subtitle, no badges, no fake screenshots, no UI mockup, no border/frame, no watermark, no characters. This is a finished single square artwork, not a collection of variants.
```

小图标（`transparent_background=true`，以完整透明标识为品牌参考）：

```text
Use case: logo-brand. Asset type: OniOnline small UI icon, to be displayed at only 32 to 64 pixels. Using the reference only for the brand concept and colors, redesign it into a radically simpler ORIGINAL flat hand-inked symbol in the visual language of Oxygen Not Included's small story-trait icons. One large chunky warm sandstone asteroid with TWO simple surface marks. ONE squat oversized yellow industrial thruster bolted beneath it, with one berry-red rectangular detail and one small flat ochre exhaust shape. ONE muted grey-violet curved rift stroke behind the asteroid, no concentric rings and no white/blue neon. Very thick irregular dark charcoal outline with thick interior divisions, chunky almost silly silhouette, flat muted painted fills, almost no texture. Only these three big shapes: rock, thruster, arc. Remove all tiny pipes, bolts, wires, small satellites, floating fragments, stars, complex engine internals, metallic gradients and glow effects. The rock must be more prominent than the arc. Centered compact square mark with 12 percent transparent padding, actual transparent background, no lettering, no badge container, no background, no checkerboard, no watermark, no characters. It must read clearly at 32px, not just look detailed when large. Finished single icon, no mockup or comparison sheet.
```

## 第一阶段历史概念图

文件：`online-background-concept.png`。

2026-10-20 使用内置 `imagegen` 绘图工具生成，原始文件保留在 Codex 生成目录。已查看生成结果：包含温带、冰冻、火山与未知小行星，使用不同旗帜或标记表示玩家，以火箭表达跨星球访问。图中没有玩家姓名和菜单文字。

第一阶段曾作为 Online 卡片的静态背景接入 Unity，并经真实游戏截图检查。这是一张横版方向草图，旧版将其拉伸成竖卡，造成星球和建筑变形；新版卡片已改用单独的竖版资源。后续玩家名、所有权及标记需由游戏实时绘制并翻译，不能把草图中的静态旗帜当成真实玩家标记。这张历史草图保留作设计记录。

### 历史提示词

```text
Use case: stylized-concept
Asset type: wide landscape game new-colony mode selection background, for the independent Oxygen Not Included mod OniOnline.
Primary request: an original playful hand-drawn 2D space-colony illustration showing several small different asteroids, each controlled by a different player, with small rockets connecting their colonies. Roughly 16:9 landscape.
Scene: whimsical deep-space starfield, orbiting rocky asteroids with visible cutaway interiors containing tiny colony rooms, pipes, plants, water pools, oxygen equipment, and expressive little cartoon space workers. One warm temperate asteroid, one icy asteroid, one volcanic asteroid, and a distant unexplored silhouetted asteroid. Small blank colored ownership pennants or player markers above the three inhabited worlds, each a different shape as well as color. A small rocket traveling between worlds and faint orbital navigation arcs communicate multiplayer planetary travel.
Style: charming ink contours, textured painted flat colors, exaggerated comic shapes, lively readable silhouettes, complementary to a humorous 2D underground colony-management game. Original composition and characters; no copied logos, official artwork, or UI.
Composition: a unified cinematic illustration, planets large enough to read, varied scales and depth, keep the upper central area relatively uncluttered so a real localized Online title can later be drawn by game UI. The background should work behind a mode-selection card.
Constraints: no lettering, no player names, no text of any kind, no watermarks, no UI buttons. Solid opaque background. This is a concept draft, not a screenshot.
```
