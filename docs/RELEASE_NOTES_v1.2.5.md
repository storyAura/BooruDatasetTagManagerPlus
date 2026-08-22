<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.5（中文）

**新功能**

- **批量裁剪**：同一分辨率的图片共用一个裁剪框；自由框选，或锁定 1:1、2:3、16:9 等比例 / 自定义长宽比；可覆盖原图或另存 `_crop`。
- **多重切割**：来源为选中 / 当前文件夹 / 全部；模式包括仅压缩、居中裁切、切片分割、随机位置切割、YOLO 检测切图；按长边档位另存新文件，不覆盖原图。
- **YOLO 检测**：独立窗口画检测框、勾选保留，可选 ONNX 反推标签后再导出切图。模型下拉含 deepghs 动漫全身 / 脸 / 头（默认全身 small），也可导入自定义 YOLOv8 ONNX。
- **标签一二级筛选**：内置约 10 万条 Danbooru 通用标签目录。左侧勾选一级，悬停后右侧列出二级；可多选、可搜索。角色名仍靠 Danbooru 类型 / 角色表。
- **按标签分类到文件夹**：勾选若干标签后，同时带齐这些标签的图片移入一个新文件夹；可命名，留空则为 `Mix`，重名自动 `Mix_2` / `Mix_3`。
- **预分桶**：按分辨率分桶，给图片加白边扩到固定尺寸后写入当前数据集下的 `{宽}x{高}` 文件夹；默认不放大，可合并到 4 / 8 / 12 / 16 个桶，并估算训练步数。写完删除已分出的原图和已空的原文件夹。
- 数据集文件夹组头支持 **F2** / **双击** 改名。
- `settings.json`（含 LLM/API 配置）改存「文档\BooruDatasetTagManagerPlus」，换安装目录读同一份。

**修复**

- ONNX 推标下载完成后，文件被占用或环境缺运行库时不再把模型当损坏删掉。
- 翻译标签：词表 / 缓存命中立刻写入译文列；中文界面下错误的默认「俄语」改回简体中文，避免整表卡住。
- 批量裁剪「应用到全部」不再因预览图已释放报错。
- 按标签分类勾选标签时不再崩溃。

## 批量裁剪

- **入口**：数据集右键「批量裁剪…」，或 **工具 → 批量裁剪…**。
- **同一分辨率才一起裁**：以当前参考图的像素大小为准；分辨率不同的图片和视频会跳过。只选了一张时，默认包含当前视图中所有相同分辨率的图片。
- **交互**：在参考图上拖出选区；可拖动、拖角/边调整。长宽比可选自由、原版、自定义，以及 1:1 / 1:2 / 2:1 / 2:3 / 3:2 / 3:4 / 4:3 / 16:9 / 9:16。位置与宽高也可直接填数字。
- **写出**：覆盖原图（先确认）或另存 `_crop` 副本（标签文件一并复制并导入数据集）。

## 多重切割工具

- **入口**：数据集右键「多重切割…」、文件夹组头「多重切割此文件夹 / 全部」，或 **工具 → 多重切割…**。来源为选中 / 当前文件夹 / 全部。始终另存新文件，原图保留；标签文件一并复制并导入数据集。
- **五种模式**：**仅等比例压缩**把长边压到勾选档位；**居中裁切到比例**先取图内最大居中矩形再压缩；**切片分割到比例**在原图上铺档位大小的窗口；**随机位置切割**每张切 N 块（默认 1）；**YOLO 检测**以检测框中心扩成选定比例再压档，未检出则跳过。
- **YOLO**：下拉可选 deepghs 动漫全身 / 脸 / 头多档（默认全身 `person_detect_v1.1_s`），也可导入自定义 YOLOv8 ONNX。下载源与 ONNX 推标共用。
- **档位**：默认 512 / 768 / 896 / 1024 / 1280 / 1536，可多选或自定义。已经小于该档的图会跳过，不放大。

## YOLO 检测

- **入口**：多重切割选「YOLO 检测」，或 **工具 → YOLO 检测…**。
- **模型**：全身 / 脸 / 头（默认全身 small）。脸/头模型裁的是脸或头，适合特写。未下载时点「下载模型」，或直接检测时自动下载。

## 标签类别一二级筛选

- **数据**：约 10 万条 Danbooru 通用标签，带一二级分类。通用标签先查这份表；查不到进「一般」。角色名仍靠 Danbooru 类型 / 角色表。
- **界面**：两个标签面板均为左右两栏。勾一级即筛整个大类；悬停后右侧列出二级。可多选、可搜索。

## 预分桶

- **入口**：数据集右键「预分桶…」、文件夹组头「预分桶此文件夹 / 全部」，或 **工具 → 预分桶…**。写出到当前数据集下的 `{宽}x{高}` 文件夹。标签文件一并复制；写完后删除已分出的原图和已空的原文件夹。
- **分桶设置**：分辨率（默认 1536×1536）、最小 / 最大边、划分单位（通常 64）。「不放大（只加白边）」默认开启。
- **目标桶数**：全部 = 按宽高比各成一桶；填 N 或点 4 / 8 / 12 / 16 则把相近比例合并到 N 个文件夹。
- **步数估算**：按重复 / 批次 / Epochs 估算理论步数和分桶后的实际步数。

## 按标签分类到文件夹

- **入口**：**工具 → 按标签分类到文件夹…**。勾选标签后**移动**同时带齐这些标签的图片与标签文件。
- **文件夹名**：可填写；留空则为 `Mix`。已有同名文件夹时自动变成 `Mix_2`、`Mix_3`。
- **改名**：新文件夹出现后，组头 **F2** 或 **双击** 即可改成 kohya 风格名称。

## 配置路径

- 整份 `settings.json`（界面偏好 + LLM/API 配置）写到「文档\BooruDatasetTagManagerPlus」。设置 → 常规 显示实际路径。
- 文档目录还没有文件时，从程序目录复制一次。文档里已是空配置、但程序目录仍有旧 API 时，只把 API 字段迁过去。本机换安装目录会读同一份；换电脑仍需重填密钥。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.5 (English)

**New**

- **Batch crop**: one crop rectangle shared across every image of the same resolution; free selection or locked ratios (1:1, 2:3, 16:9, …) / custom aspect; overwrite or save `_crop` copies.
- **Multi-crop**: source is selected / current folder / all images; modes are scale-only, center-crop, tile-split, random-position crop, and YOLO detect crop; writes new files at long-edge gears and never overwrites originals.
- **YOLO detect**: standalone window to draw boxes, keep or drop them, optionally ONNX-tag kept crops, then export. Dropdown covers deepghs anime person / face / head (default full-body small), plus custom YOLOv8 ONNX import.
- **Two-level category filter**: bundled ~100k danbooru general-tag catalog. Tick a primary on the left, hover to list secondaries on the right; multi-select and searchable. Character names still use Danbooru type / the character table.
- **Classify into folders by tag**: tick tags, then images that have every selected tag move into one folder; type a name or leave it blank for `Mix`. A name that already exists becomes `Mix_2` / `Mix_3`.
- **Pre-bucket**: letterbox each image onto a fixed bucket size and write `{width}x{height}` folders under the current dataset; pad-only by default; merge down to 4 / 8 / 12 / 16 buckets and estimate training steps. After writing, source images and emptied source folders are removed.
- Dataset folder headers can be renamed with **F2** or a **double-click**.
- `settings.json` (including LLM/API profiles) now lives in `Documents\BooruDatasetTagManagerPlus`, so install folders share one file.

**Fixed**

- ONNX tagger downloads no longer delete a model that is still locked or fails to load because a native runtime is missing.
- Tag translation: built-in dictionary / cache hits appear immediately. A leftover default translation target of Russian on a Chinese UI is remapped to Simplified Chinese so the column no longer hangs empty.
- Batch-crop Apply-all no longer errors when the preview image has already been released.
- Classify-into-folders no longer crashes when ticking tags.

## Batch crop

- **Entry**: dataset context menu *Batch crop…*, or **Tools → Batch crop…**.
- **Same resolution only**: the reference image's pixel size is the key; different sizes and videos are skipped. With a single selection, every image of that size in the current view is included by default.
- **Interaction**: drag a rectangle on the reference; move it or resize from corners/edges. Aspect can be free, original, custom, or 1:1 / 1:2 / 2:1 / 2:3 / 3:2 / 3:4 / 4:3 / 16:9 / 9:16. Position and size can also be typed.
- **Write**: overwrite in place (confirm first) or save `_crop` copies (tag files are cloned and imported).

## Multi-crop tool

- **Entry**: dataset context menu *Multi-crop…*, folder-header *Multi-crop this folder / all images*, or **Tools → Multi-crop…**. Source is selected / current folder / all. Always writes new files (originals stay); caption files are cloned and imported.
- **Five modes**: **Scale only** shrinks the long edge to each ticked gear; **Center-crop to ratio** takes the largest centered rectangle then downscales; **Split into tiles** lays gear-sized windows on the source; **Random-position crop** takes N crops per image (default 1); **YOLO detect** expands each detection to the chosen ratio then applies gears, skipping images with no hit.
- **YOLO**: dropdown covers deepghs anime person / face / head (default full-body `person_detect_v1.1_s`), plus custom YOLOv8 ONNX import. Download source is shared with the ONNX tagger.
- **Gears**: defaults 512 / 768 / 896 / 1024 / 1280 / 1536, multi-select or custom. Images already smaller than a gear are skipped; never upscales.

## YOLO detect

- **Entry**: Multi-crop's YOLO detect mode, or **Tools → YOLO detect…**.
- **Models**: person / face / head (default full-body small). Face/head models crop the face or head — better for close-ups. If the file is missing, *Download model* fetches it, or Detect downloads automatically.

## Two-level tag category filter

- **Data**: ~100k danbooru general tags with L1/L2 categories. General tags look up this table first; unknown tags land in General. Character names still use Danbooru type / the character table.
- **UI**: both tag panes use a two-column picker. Tick a primary to filter that whole group; hover lists secondaries on the right. Multi-select and searchable.

## Pre-bucket

- **Entry**: dataset context menu *Pre-bucket…*, folder-header *Pre-bucket this folder / all images*, or **Tools → Pre-bucket…**. Writes `{width}x{height}` folders under the current dataset. Captions are cloned. After writing, source images and emptied source folders are removed.
- **Bucket settings**: resolution (default 1536×1536), min / max side, step (usually 64). *Do not upscale (pad only)* is on by default.
- **Target count**: All keeps every aspect-assigned bucket; type N or tap 4 / 8 / 12 / 16 to merge neighboring ratios down to N folders.
- **Step estimate**: repeats / batch / epochs produce a theoretical count and the actual count after bucketing.

## Classify into folders by tag

- **Entry**: **Tools → Classify into folders by tag…**. Tick tags, then **move** images that have every selected tag, along with their caption files.
- **Folder name**: type one, or leave it blank for `Mix`. A name that already exists becomes `Mix_2`, then `Mix_3`.
- **Rename**: after the folders appear, **F2** or **double-click** a group header to give it a kohya-style name.

## Settings path

- The whole `settings.json` (UI preferences + LLM/API profiles) is written to `Documents\BooruDatasetTagManagerPlus`. Settings → General shows the actual path.
- If Documents has no file yet, the exe-adjacent file is copied once. If Documents already exists without API config but the old file still has one, only those API fields are merged in. Switching install folders on the same Windows user reuses the same file; a different PC still needs keys re-entered.
