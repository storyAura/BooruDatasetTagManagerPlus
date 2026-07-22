# BooruDatasetTagManager+ v1.2.0

The dataset panel is rebuilt from the ground up, tags get semantic colors and a category sort, character tags gain an offline danbooru catalog for exact matching and proper translated names, and the character audit wizard receives a batch of workflow and reliability fixes.

## Dataset panel: unified folder browser

The flat dataset grid and the separate folder sidebar are merged into one module:

- **Search box** filters folders and file names together.
- **Collapsible kohya folder groups** (chevron to fold, header click to scope the dataset — AllTags counts, bulk operations and the audit wizard all follow the scope). Multi-folder datasets open **fully collapsed** by default; expand-all / collapse-all buttons sit next to the search box. Single-folder datasets render as a flat list without group noise.
- **Image rows** show the thumbnail, the name and a dim info line — `WEBP · 1200×1600 · 356 KB` (computed lazily per visible row).
- **Selection** works like a file manager: click, Ctrl, Shift ranges, Ctrl+A, keyboard arrows; the full right-click context menu, Delete, and Ctrl+V tag pasting all still work. Internally the legacy grid remains the data/selection authority, so every existing operation is untouched.
- **Folder right-click menu**:
  - *Rename folder…* — renames the directory on disk and remaps every loaded path in place, so unsaved tag edits survive without a reload.
  - *Batch rename images…* — prefix + counter (numeric `001`, letters `a…z, aa`, or keep original name) + suffix, with a live preview. Renames run in two phases through temp names, so swapped name sets (`1↔2`) cannot collide; caption `.txt` files follow their images; all targets are validated before anything moves.
  - *Tag folder with ONNX… / LLM…* — scopes to the folder and opens the tagger with the new **Current folder** input source preselected (both tagger windows gained that third source). Nothing auto-runs; you confirm prefix/suffix tags and settings first.

## Preview

- The right-pane **Preview tab is gone**; its replacement is a collapsible preview panel docked under the dataset browser (expanded state persists, header chevron / 视图 → 显示预览 / hotkey all toggle it). It no longer clears while the settings dialog is open.
- **Multi-select preview**: selecting several images tiles the first four side by side; the header shows the total count; double-clicking a cell opens that image in the floating viewer.
- The **floating preview window** is now a real image viewer: wheel zoom anchored at the cursor, drag to pan, double-click toggles fit ↔ 100 %, Ctrl+0 fits, Ctrl+1 is 100 % — the window itself no longer resizes.

## Tag semantics: colors, sorting, character catalog

- Both tag panes tint rows by semantic category (18 light hues, theme-aware): character, copyright, artist, subject count, hair, eyes, body, expression, clothing, accessories, objects, animals, food, action, composition, background, style, meta.
- A **Category sort** button orders the current image's tags by those groups (honoring "don't sort first N rows"); the All Tags pane gets an opt-in category-sort toggle (off by default, persisted).
- The autocomplete tag DB now **retains the danbooru type column** (general/artist/copyright/character/meta) that its CSV format always carried — the cached DB rebuilds once automatically.
- New offline **character catalog** `Data/danbooru_character_tags.csv` (~330 k danbooru character tags with alternative names and franchise): exact Character classification even without a tag DB, and character translations render as `译名 (franchise)` — e.g. `hattori_hanzou_(samurai_spirits)` → `服部半藏 (samurai spirits)`. Catalog hits outrank stale machine-translation cache entries (manual translations still win). A settings toggle (default on) skips loading it entirely on low-memory machines.

## Translation & Danbooru wiki

- Translating no longer freezes the UI: the wiki popup and the AllTags refill after folder-scope changes both run on a background worker (the old per-tag UI-thread loop was quadratic).
- The wiki popup **auto-translates** into non-English UI languages, with an original ↔ translation toggle; it shows the wiki's curated **example post thumbnails** (click opens the post), renders only the intro section, and follows the app color scheme.

## Character audit wizard

- The standard-image gallery follows dataset folder-scope changes — even when a previous load is still streaming thumbnails.
- Malformed model responses are recovered harder: tolerant JSON extraction (prose/fence stripping, outermost-object cut) plus a full fresh-request retry after a failed repair.
- **Dual-character audits** now name the other character in both audit stages, so shared-image features (hair length/color, outfits) are attributed by the reference image instead of tag frequency; each character's final prompt normalizes `2girls` / `multiple girls` to that character's own `1girl` / `1boy`.
- 应用并保存 flows **character by character**: it validates and advances to the next character first, and only writes to disk from the last one.
- Reference-image name labels are width-capped with ellipsis (hover for the full name); trigger-word boxes no longer show two overlapping suggestion lists; the review page keeps its rightmost controls reachable (larger minimum window size).
- The main window's **Add tag (Ctrl+E)** opens an autocomplete input dialog (inserting after the selected row) instead of appending empty rows; when no tag DB is installed, autocomplete falls back to the current dataset's own tags.

## Fixes & housekeeping

- Fixed the startup crash `Parameter is not valid` (folder gallery handing disposed bitmaps to an ImageList).
- LLM settings: the fixed natural-language prompt block is hidden and the dialog is slimmer.
- Batch-rename temp files keep their extensions, avoiding antivirus ransomware false positives (a behavior monitor really did kill the test host over the old pattern).
- Removed the unused "Change selection" button and the shown-count label from the dataset toolbar.
- Right-click delete now refreshes the browser immediately (deleted rows no longer linger as empty boxes).
- The multi-select tag grid caps its image-name column so long kohya file names cannot squeeze the tags out of view.
- All five UI languages are complete for every new string; the test suite grew from 357 to 439 green tests.

## Release & data-safety hardening (post-audit)

A 20-round full-repo audit was independently re-verified finding by finding; the six release blockers it confirmed are all fixed in this build:

- **Batch rename**: a directory squatting on a target name is rejected up front; a mid-batch caption failure rolls the image back from its real location instead of a stale temp name; renaming a caption-less image retargets its future save path, so a later save can no longer resurrect the old base name.
- **Gated model downloads**: the Hugging Face token is only ever sent to huggingface.co — when a token is present the download is forced off mirrors entirely.
- **Release packaging**: `publish_release.bat` publishes into a fresh temp staging directory (`dist` is no longer zipped, so local settings/caches cannot leak into the ZIP), verifies the EXE's real ProductVersion against the release version, strips PDB/LIB files, and checks every GitHub CLI step separately.
- **LLM tagger**: any pre-run save failure now lists the failed files and sends zero model requests (including the ONNX pre-tag path); "In place" output is flushed to disk before success is reported, and files that fail to persist are listed in the summary.
- **Video convert**: cross-format "replace original" refuses to overwrite an unrelated sibling file — pre-checked before transcoding and enforced again at the final move.
- **Settings/theme startup**: hand-edited `settings.json` / `ColorScheme.json` with null members can no longer crash the app before the window appears; numeric settings are range-checked; a corrupt settings file is recovered from its `.bak` backup before falling back to defaults.

---

## 更新摘要（中文）

- **数据集面板重构**:文件夹分组浏览器(搜索、默认折叠、全部展开/折叠、缩略图行显示 格式·尺寸·大小)与数据集列表合并为一个模块;选择操作向文件管理器看齐(Ctrl/Shift/Ctrl+A/右键菜单/Delete/Ctrl+V 全保留)。
- **文件夹右键**:重命名文件夹(磁盘 + 内存原地重映射,未保存编辑不丢)、批量重命名图片(前缀 + 数字/字母/保留原名 + 后缀,实时预览,两阶段防撞车,txt 跟随)、ONNX / LLM 快捷打标(预选"当前文件夹"来源,不自动开跑)。
- **预览**:右侧 Preview 标签页移除,改为数据集底部可折叠预览面板(状态持久化,打开设置不再消失);多选并排显示前 4 张,双击单元格弹独立窗;独立预览窗重做(光标锚点缩放、拖拽平移、双击适应↔100%、Ctrl+0/Ctrl+1)。
- **标签语义**:两个标签面板 18 类浅色着色;图片标签"类别排序"按钮(遵守"不排序前 N 行");全部标签可选类别排序(默认关);词库保留 danbooru 类型列。
- **角色名单**:内置 33 万行 danbooru 角色目录,角色标签精确识别 + 译名显示为「译名 (作品)」,优先于旧机翻缓存;设置可关(默认开)。
- **翻译与 Wiki**:翻译全部转后台线程,切文件夹/翻译 Wiki 不再卡顿;Wiki 弹窗自动翻译(可切原文)、显示示例图、只保留简介、跟随主题。
- **角色审查向导**:画廊跟随范围切换;JSON 解析容错 + 整轮重试;双角色互相点名对方、按参考图归属特征;单角色提示词 2girls→1girl 归一;"应用并保存"逐角色流转;触发词双弹层重叠修复;参考图名限宽;添加标签改为补全对话框(无词库时用数据集标签补全)。
- **修复**:启动 GDI+ 崩溃、LLM 设置隐藏固定提示词、批量重命名防杀软误报、右键删除即时刷新、多选模式图片名列限宽、移除底部冗余按钮与计数;5 语言 i18n 全量补齐;测试 357 → 439。
- **发布与数据安全加固**(全库审计复核后修复全部发布阻断项):批量重命名的目录占位预检与失败按实际位置回滚,无标签图片改名后保存路径跟随新名;门禁模型的 HF 令牌只发送给 huggingface.co(带令牌时强制官方源);发布脚本改用全新临时目录打包(不再压缩 `dist`)并核对 EXE 真实版本号;LLM 打标前保存失败即中止(零请求),In Place 完成即落盘;视频跨格式替换不再覆盖同名的另一份文件;配置/主题文件缺字段不再阻断启动,损坏时自动从 `.bak` 备份恢复。
