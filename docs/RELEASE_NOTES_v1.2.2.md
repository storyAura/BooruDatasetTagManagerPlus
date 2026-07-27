<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.2（中文）

功能版本：新增「相似图片筛选」——参考 [czkawka](https://github.com/qarmin/czkawka) 的感知哈希方案，秒级找出数据集中的重复 / 近似重复图片并按组清理；角色标签审查从双角色升级为**多角色模式（最多 4 人）**；另有「重新导入当前数据集」（F5）、打标写入模式「跳过已有标签列表」的 P0 修复，以及一轮"未加载数据集"健壮性排查与界面修复。

## 相似图片筛选（新功能）

入口：**工具 → 查找相似图片…**（需先加载数据集）。

- **czkawka 式感知哈希**：每张图计算 64 位差值哈希（dHash），按汉明距离贪心分组。哈希直接用内存中的缩略图计算，不重复读盘——数千张图的数据集秒级出结果；数据集加载时关闭了预览图的情况下自动改为后台按需解码，界面不卡顿。
- **范围与过滤**：左侧浏览器选中某个文件夹（或多文件夹并集）时只扫描该范围；视频文件自动跳过。
- **相似度四档**：极高（几乎相同）/ 高 / 中 / 低，对应逐渐放宽的哈希距离阈值；改档后重新点击扫描即可。
- **分组审查墙**（与多选标签编辑器同一套交互）：每组一个标题（第 N 组 · M 张），**绿框 = 保留，红框 = 待删除**；左键切换，右键全屏查看**原图**（不是缩略图），悬停显示文件名与大小，滑块调整缩略图尺寸。结果过多时只渲染前 1000 张并在状态栏如实说明。
- **每组保留一张（最大文件）**：一键把每组中体积最大的图标记为保留、其余标记为待删除（czkawka 的默认策略），随后仍可逐张手动调整。
- **删除标红图片**：确认后走与主界面相同的事务化删除（图片与标签文件先移入暂存目录、全部成功才清除，中途失败自动恢复），主窗口的数据集、全部标签计数同步刷新，窗口随后自动重新扫描剩余图片。
- **健壮性**：扫描运行中关闭窗口会先取消后台任务、收尾后再真正关闭（与 ONNX / 视频工具同一套「运行中不硬关」约定）；单张图片损坏只跳过该图，不中断整次扫描。

## 角色标签审查：多角色模式（最多 4 人）

主体模式在原有 单人 / 双人 基础上新增**多角色（最多 4 人）**：

- 向导中按槽位（A–D）逐行设定每个角色的触发词、参考图、性别与文件夹；**留空的角色行自动跳过**，因此三角色数据集直接用多角色模式即可。多角色模式要求至少填满两行，且各角色触发词必须互不相同。
- 图片归属沿用「先触发词、再文件夹」的两级判定；多人同图按各角色性别自动补齐 `2girls` / `multiple girls` / `1boy` 等主体数标签（已有更高人数标签时不会去矛盾覆盖）。
- 多个角色对同一标签给出冲突替换时保留原标签（成对合并规则推广到全部在场角色）。
- 断点续跑沿用并扩展：任一角色失败时，已完成角色的付费结果保留为断点，可**只重试失败的角色**；每张参考图仍在任何付费调用前先完整解码验证。
- 每槽位性别改为数组持久化（`CharacterTagAuditGenders`）；旧配置的 A / B 两字段在加载时自动规范化迁移，无需手动处理。

## 重新导入当前数据集（文件菜单，F5）

- 「文件 → 重新导入当前数据集」直接用当前数据集的根目录从磁盘重新加载：外部工具改图、增删文件后一键刷新，无需重新选文件夹。
- 有未保存修改时先弹保存确认（保存失败会中止重载，编辑不丢失）；当初以"不加载预览图"方式载入的大数据集，重新导入同样不加载预览图，不会突然变慢。
- 默认热键 F5，可在 设置 → 快捷键 修改；菜单文字自动显示当前键位。

## 打标写入模式：「跳过已有标签列表」不再静默（P0 修复）

- 旧行为：ONNX / LLM 打标在该模式下照常对所有图片推理，然后**静默丢弃**已有标签图片的结果，最后仍显示"推标完成"——看起来就像"改阈值、换模型都没用"，LLM 还会白白扣费。
- 新行为：两条打标链路在推理 / 付费调用**之前**就跳过已有标签的图片；全部被跳过时直接弹窗说明并建议切换写入模式，部分跳过时完成语如实报告"已处理 X 张、跳过 Y 张"。
- 三种写入模式的语义由新增的 `TagWriteServiceTests` 回归锁定（跳过 = 只填补零标签图；追加 = 去重追加到末尾、不动现有标签；替换 = 全量覆盖）。

## 稳定性与界面修复

- **未加载数据集防护**：排查全部按钮 / 菜单入口，修复 6 处在未加载数据集时会空引用崩溃或提示误导的入口（全部/共有标签切换、清除全部标签过滤、退出图片过滤、在全部标签中定位当前标签、标签撤销/重做、全部替换），统一改为「未加载数据集」提示。
- **换文件夹残留旧标签**：加载新文件夹后，图片标签面板不再显示上一个数据集的标签（旧列表甚至属于已销毁的数据集）；数据集网格无选中行时标签面板一律清空。
- **图片标签「类别排序」变为记忆开关**：勾选后切换图片自动按类别排序，状态持久保存；再点一次关闭。
- **数据集搜索框样式统一**：去掉圆角胶囊和放大镜图标，改为贴合当前配色方案的扁平方角输入框，搜索栏更紧凑。

## 其他

- 缩略图审查控件的右键预览支持按原始文件解码显示（相似图片窗口用它看原图；多选标签编辑器行为不变）。
- 五个语言文件（简中 / 繁中 / 英 / 俄 / 葡）同步补齐相似图片筛选与多角色审查的全部界面文本。
- 测试套件从 481 增长到 **496**（新增：dHash 哈希稳定性 / 缩放与亮度不变性 / 分组边界、多角色归属与合并、性别数组规范化、打标写入模式语义等回归）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.2 (English)

A feature release: the new **Similar image finder** — perceptual hashing in the spirit of [czkawka](https://github.com/qarmin/czkawka) that surfaces duplicate / near-duplicate images in seconds for group-by-group cleanup — and the character tag audit growing from dual to a **multi-character mode (up to 4)**. Plus a File-menu **Reload current dataset** (F5), a P0 fix for the "skip existing" tagging write mode, and a no-dataset robustness sweep with UI fixes.

## Similar image finder (new)

Entry: **Tools → Find similar images…** (load a dataset first).

- **czkawka-style perceptual hashing**: every image gets a 64-bit difference hash (dHash), greedily clustered by Hamming distance. Hashes are computed straight from the in-memory thumbnails with no extra disk reads — datasets of thousands of images finish in seconds; when the dataset was loaded without previews the scan decodes small thumbs in the background instead, keeping the UI responsive.
- **Scope & filtering**: with a folder (or a multi-folder union) selected in the browser, only that scope is scanned; video files are skipped automatically.
- **Four similarity levels**: very high (near identical) / high / medium / low, mapping to progressively looser hash-distance ceilings; change the level and scan again.
- **Grouped review wall** (the multi-select tag editor's visual language): one header per group ("Group N · M images"), **green frame = keep, red frame = delete**; left-click toggles, right-click shows the **full-size original** (not the thumbnail) fullscreen, tooltips show file name and size, and a slider adjusts thumbnail size. Oversized result sets render only the first 1000 images and say so honestly in the status line.
- **Keep one per group (largest file)**: one click keeps the largest file of each group and marks the rest for deletion (czkawka's default heuristic); every mark can still be adjusted by hand afterwards.
- **Delete red-marked images**: after confirmation, deletion uses the same transactional pipeline as the main window (image and tag sidecar staged together and only then purged; a mid-way failure restores the files), the main window's dataset and All Tags counts refresh immediately, and the window rescans the survivors automatically.
- **Robustness**: closing the window mid-scan cancels the background job first and closes only once it has unwound (the same "no hard close while a job runs" convention as the ONNX and video dialogs); a single corrupt image is skipped without aborting the scan.

## Character tag audit: multi-character mode (up to 4)

The subject mode gains **Multi-character (up to 4)** on top of the existing Single / Dual:

- The wizard shows one row per slot (A–D) with each character's trigger word, reference image, gender and folder; **empty rows are skipped**, so a three-character dataset simply uses the multi mode. The mode requires at least two filled rows, and every character must use a distinct trigger word.
- Image attribution keeps the "trigger word first, folder second" two-stage rule; shared images automatically receive subject-count tags (`2girls` / `multiple girls` / `1boy`, …) derived from the present characters' genders — an already-present higher count is respected, never contradicted.
- When several characters propose conflicting replacements for the same tag, the original tag is kept (the pairwise merge rule generalized across all present characters).
- Checkpointing carries over and extends: if any character fails, the finished characters' paid results are kept as a checkpoint and you can **retry only the failed character**; every reference image is still fully decoded and validated before any paid model call.
- Per-slot genders now persist as an array (`CharacterTagAuditGenders`); old configs with the previous A / B fields are normalized automatically on load — nothing to reconfigure.

## Reload current dataset (File menu, F5)

- **File → Reload current dataset** re-imports the loaded folder from disk using its current root — one keypress picks up external edits, added or removed files, no folder dialog needed.
- Unsaved changes prompt to save first (a failed save aborts the reload, nothing is lost); a dataset that was loaded without preview thumbnails reloads without them too, so large datasets don't suddenly crawl.
- Default hotkey F5, configurable under Settings → Hotkeys; the menu label shows the current binding automatically.

## Tagging write mode: "Skipping existing tag lists" is no longer silent (P0 fix)

- Old behavior: both the ONNX and LLM taggers still ran inference on every image, then **silently discarded** the results for already-tagged images and reported plain success — which read as "changing the threshold or model does nothing", and the LLM path burned API credits for nothing.
- New behavior: both pipelines skip already-tagged images BEFORE inference / paid calls; when everything was skipped a message box explains why and suggests switching the write mode, and partial skips are reported honestly ("processed X, skipped Y").
- The three write modes' semantics are locked by the new `TagWriteServiceTests` (skip fills only untagged images; append dedups and appends without touching existing tags; replace overwrites).

## Stability & UI fixes

- **No-dataset guards**: a sweep across every button / menu entry fixed six spots that crashed (null reference) or showed misleading messages with no dataset loaded (all/common tags switch, clear all-tags filter, exit image filter, locate-tag-in-all-tags, tag undo/redo, replace-all); they now show the friendly "no dataset loaded" prompt.
- **Stale tags after switching folders**: the image-tags pane no longer keeps showing the previous dataset's tags (a list that even belonged to an already-disposed dataset) after loading a new folder; the pane clears whenever the dataset grid has no selected rows.
- **Image-tags "Category sort" became a sticky toggle**: while checked, every newly selected image is sorted by category automatically; the state persists across sessions.
- **Dataset search box restyled**: the rounded web-style pill (and its magnifier icon) became a flat, square, theme-following field on a more compact strip.

## Other

- The thumbnail review control's right-click preview can decode the original file for display (used by the similar-images window; the multi-select tag editor's behavior is unchanged).
- All five language files (Simplified / Traditional Chinese, English, Russian, Portuguese) gained the complete UI text for the similar image finder and the multi-character audit.
- The test suite grows from 481 to **496** (new regressions: dHash stability / resize & brightness invariance / grouping edge cases, multi-character attribution and merging, gender-array normalization, tagging write-mode semantics).
