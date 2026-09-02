<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.6（中文）

**新功能**

- **错误标签修复可关闭角色类替换**：测试模块新增勾选项（默认开启）。取消后不再合并 / 替换同一角色家族的标签（含同图冲突与子级并入）；人数标签与 `solo` 仍会处理。命令行对应 `fix-tags --no-character-variants`。
- **YOLO 检测画面比例「自动」**：独立检测窗默认按每张图的宽高就近匹配 1:1 / 2:3 / 16:9 等预设，导出时每张图各自用自己的比例；仍可锁定某一比例。不影响预分桶或多重切割里的比例选择。

**优化**

- **工具菜单按三类分段**：处理工具 / 标注工具 / 预处理工具（平铺下拉；类别标题用淡色底栏区分，混色与标签类别着色相同，不可点）。YOLO 检测归处理工具；**角色标签审查**从标注工具打开。顶栏「测试」改名为「测试功能」，窗口里不再放角色审查。
- **快速替换独立窗口**：原测试模块里的能力挪到 **工具 → 快速替换…**（标注工具）。左侧选规范词、设阈值，右侧预览将并入的同类低频标签，底栏左侧显示短说明。
- **ONNX 推标改名 Tag 推标**：工具菜单（标注工具）、推标窗口标题、数据集右键「Tag 重新推标」、文件夹右键「Tag 推标此文件夹…」统一为「Tag 推标」（英文 **Tag tagger**）。引擎仍是本机 ONNX（WD14 / PixAI / CL）；命令行动词仍是 `onnx-tag` / `onnx-models`。
- **预分桶配平与 Gradient**：来源只留当前文件夹 / 全部图片；去掉「全部」快捷钮（填 0 仍表示不合并桶）。按当前批次复制补到能整除，「配平」列只显示多复制了几张（23 + 批次 5 → +2，总数 25）。新增 Gradient：步数按 `ceil(微批次 / Gradient)` 算优化器步；批次 2、Gradient 2 等效 BS=4，但配平和每次读取仍按 2 张。
- **若干窗口说明与布局**：多重切割改为左预览右设置（YOLO 行按需展开）；YOLO 检测底栏改表格并拆主/次按钮；视频抽帧、按标签分类、设置→翻译等说明收短或分组。

**修复**

- 图片标签面板恢复 Shift / Ctrl 多选：可一次选多行后批量删除、复制、改权重。类别筛选隐藏行时也不会再点选崩溃。
- **Win10 上 ONNX 无法加载原生库**：发布包把 `onnxruntime.dll` 放在 `win-x64/`，原先只调了未配对的 `AddDllDirectory`，CLI 路径还完全跳过；现在启动（含 CLI）按绝对路径预加载，并带上 VC++ CRT sidecar 与 Win10/11 清单。失败时给出完整异常链和可操作提示（整包解压、关掉兼容模式），不再只弹 `NativeMethods` 初始化失败。
- **大批量推标内存峰值**：ONNX 先在 ImageSharp 里缩到模型输入尺寸再转 GDI，避免几百张 2K–8K 原图各做一整幅 32bpp 拷贝；单张失败（含内存不足）不中止整批。
- **工具 → 替换透明背景点了没反应**：分组标题改成不可点的标签（不再用禁用菜单项挡第一项）；选色窗挂到主窗口上，并等菜单收起再锁界面。没选图 / 没有透明底时会弹出说明，处理后刷新数据集浏览器。
- **数据集「全部」行在单文件夹 / 平铺视图里也会显示**：右键「替换透明背景（全部图片）」等整库命令不再因为只有一层目录就消失。

## 错误标签修复

- **入口**：**测试功能** 菜单窗口的「错误标签修复」分组。
- **关闭角色类替换**：即使「子级并入」本来就是关的，修复器仍会处理同图角色家族冲突（例如同图同时有 `hatsune miku` 与 `racing miku` 时按出现量删掉败者）。现在可以单独关掉整类角色处理。
- **子级并入仍是子选项**：只有角色类修复开启时才能勾选；关掉角色类修复时会取消勾选并变灰，不会把「子级并入」的设置写成关。
- **CLI**：`fix-tags --no-character-variants` 跳过角色类替换。与 `--child-threshold` 同时使用时，阈值无效（帮助与运行时都会提示）。

## YOLO 检测

- **入口**：**工具 → YOLO 检测…**（多重切割里的 YOLO 模式仍用自己选定的比例）。
- **自动**：下拉第一项。按该图宽高比找最近预设，距离相同时取列表里更靠前的一项；无效尺寸回落到第一个预设。
- **锁定**：选中某一比例后整批导出都用该比例；不会把 `0:0` 写进预分桶共用的比例设置。

## 图片标签多选

- 图片标签网格默认允许多选（与「全部标签」一致）。
- Shift / Ctrl 按住时不会误触发拖放改序；类别筛选藏起部分行时，Shift 只选可见行。
- 删除、Ctrl+C、改权重都作用于当前选中的每一行。

## 工具菜单

- **三段平铺**：处理工具 / 标注工具 / 预处理工具（淡蓝 / 淡红 / 淡绿底栏，混色与标签类别着色相同，不可点）。
- **处理工具**：替换透明背景、视频格式转换、视频抽帧、背景移除、批量裁剪、多重切割、YOLO 检测。
- **标注工具**：**Tag 推标**（原 ONNX 推标）、LLM 打标、角色标签审查、快速替换。
- **预处理工具**：查找相似图片、扫描坏图、按标签分类到文件夹、预分桶。
- **测试功能**：顶栏「测试」改名；该窗口只留错误标签修复。角色标签审查走工具 → 标注工具。

## Tag 推标改名

- **界面**：**工具 → 标注工具 → Tag 推标…**，窗口标题「Tag 推标」，数据集右键「Tag 重新推标」，文件夹右键「Tag 推标此文件夹…」。英文为 Tag tagger。
- **没改的**：CLI `onnx-tag` / `onnx-models`；模型目录 `Models/`；引擎仍是本地 ONNX。

## 快速替换

- **入口**：**工具 → 快速替换…**（标注工具）。
- 左侧选规范词、设阈值，右侧列出将并入的同类低频标签，确认后整库替换。
- 同类按最后一个词判断（`black shoes` → `shoes`）；出现次数低于阈值才会被换。

## 其他

- 测试套件现为 **787**（角色类修复开关、`fix-tags --no-character-variants`、YOLO 最近预设与并列距离、图片标签多选辅助逻辑、预分桶配平与 Gradient、Win10 ONNX 原生库路径、500 张批跑与加载、替换透明背景填色）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.6 (English)

**New**

- **Tag fixer can skip character-family replacements**: a new Test-module checkbox (on by default). Uncheck it to leave same-family character tags alone (both same-image conflicts and rare-child folds); subject-count and `solo` fixes still run. CLI: `fix-tags --no-character-variants`.
- **YOLO detect aspect Auto**: the standalone detect window defaults to the nearest preset (1:1 / 2:3 / 16:9 / …) from each image's size and exports per image; you can still lock a ratio. Pre-bucket and multi-crop aspect pickers are unchanged.

**Improved**

- **Tools menu grouped into three sections**: Processing / Tagging / Preprocessing (flat dropdown; section titles use the same pale accent wash as tag-category row tints and are not clickable). YOLO detect sits under Processing; **character tag audit** opens from Tagging. The **Test** menu is renamed **Test functions** and no longer hosts the audit.
- **Quick replace gets its own window**: the existing fixer moves to **Tools → Quick replace…** (Tagging). Pick the keeper on the left, set the threshold, preview same-suffix low-count tags on the right, and read the short status on the bottom-left.
- **ONNX tagger renamed Tag tagger**: Tools → Tagging, the window title, dataset **Retag with Tag tagger**, and folder **Tag folder with Tag tagger…** now say **Tag tagger** (zh-CN **Tag 推标**). The engine is still local ONNX (WD14 / PixAI / CL); CLI verbs stay `onnx-tag` / `onnx-models`.
- **Pre-bucket added column and Gradient**: source is current folder or all images; the "All" shortcut is gone (0 still means do not merge). Copies until each bucket is divisible by the read batch; **Added** shows how many extras (23 + batch 5 → +2, total 25). **Gradient** divides optimizer steps by `ceil(microbatches / Gradient)`. Batch 2 and Gradient 2 is effective BS 4, but padding and each read still use 2 images.
- **Window copy and layout**: multi-crop is now preview-left / settings-right (YOLO controls expand only in YOLO mode); YOLO detect uses a table plus primary/secondary buttons; video extract, classify-by-tag, and Settings → Translations hints were shortened or regrouped.

**Fixed**

- Image-tags Shift/Ctrl multi-select works again: delete, copy, and weight edits apply to every selected row. Shift-click no longer crashes when a category filter hides some rows.
- **Win10 ONNX native load**: published builds keep `onnxruntime.dll` in `win-x64/`; the old `AddDllDirectory` hook was unpaired (and the CLI skipped it). Startup — including CLI — now preloads by absolute path, ships a VC++ CRT sidecar, and embeds a Win10/11 manifest. Failures show the full exception chain plus extract-the-zip / turn-off-compat-mode hints instead of a bare `NativeMethods` initializer error.
- **Large-batch tagging memory**: ONNX downscales in ImageSharp to the model input size before creating a GDI bitmap, so hundreds of 2K–8K sources no longer each materialize a full-resolution 32bpp copy. A single-image failure (including OOM) no longer aborts the rest of the batch.
- **Tools → Replace transparent background did nothing**: section titles are now labels (a disabled menu item was eating the first command's click); the color dialog is owned by the main window and the UI lock waits until the menu has closed. Missing selection / no transparent pixels show a message, and the dataset browser refreshes after a run.
- **The dataset "All" row stays visible** in single-folder and flat views, so whole-dataset commands on its context menu remain reachable.

## Tag consistency fixer

- **Entry**: the **Test functions** menu window, "Tag consistency fixer" group.
- **Turn off character-family work**: even with rare-child fold already off, the fixer still resolved same-image family conflicts (e.g. `hatsune miku` next to `racing miku` dropped the loser by dataset count). That entire character pass can now be skipped.
- **Rare-child fold stays a sub-option**: it is only available while character-family fix is on; turning that off unchecks and greys the fold box without persisting fold as off.
- **CLI**: `fix-tags --no-character-variants` skips character-family replacements. `--child-threshold` has no effect alongside that flag (help and runtime both say so).

## YOLO detect

- **Entry**: **Tools → YOLO detect…** (the YOLO mode inside multi-crop still uses its own chosen ratio).
- **Auto**: first combo item. Picks the closest preset to that image's aspect; equal distance keeps the earlier preset; invalid sizes fall back to the first preset.
- **Locked**: the chosen ratio applies to the whole export; `0:0` is never written into the shared pre-bucket aspect settings.

## Image-tags multi-select

- The image-tags grid allows multi-select by default (same as All Tags).
- Holding Shift/Ctrl does not start a drag-reorder; with hidden category-filter rows, Shift selects only visible rows.
- Delete, Ctrl+C, and weight edits apply to every selected row.

## Tools menu

- **Three flat sections**: Processing / Tagging / Preprocessing (pale blue / red / green washes, same blend as tag-category tints; titles are not clickable).
- **Processing**: replace transparent background, video convert, frame extract, remove background, batch crop, multi-crop, YOLO detect.
- **Tagging**: **Tag tagger** (formerly ONNX tagger), LLM tagging, character tag audit, quick replace.
- **Preprocessing**: find similar images, scan corrupted images, classify into folders by tag, pre-bucket.
- **Test functions**: the **Test** menu is renamed; that window keeps only the tag consistency fixer. Character tag audit lives under Tools → Tagging.

## Tag tagger rename

- **UI**: **Tools → Tagging → Tag tagger…**, window title "Tag tagger", dataset right-click **Retag with Tag tagger**, folder **Tag folder with Tag tagger…**. zh-CN: Tag 推标.
- **Unchanged**: CLI `onnx-tag` / `onnx-models`; `Models/` folder; the engine is still local ONNX.

## Quick replace

- **Entry**: **Tools → Quick replace…** (Tagging).
- Pick the keeper on the left, set the threshold, preview same-suffix low-count tags on the right, then confirm a dataset-wide replace.
- "Same category" means the last word (`black shoes` → `shoes`); only tags below the threshold are replaced.

## Other

- The test suite is now **787** (character-family fix toggle, `fix-tags --no-character-variants`, YOLO nearest-preset and equal-distance ties, image-tags selection helpers, pre-bucket added column and Gradient, Win10 ONNX native path, 500-image batch/load, transparent-background fill).
