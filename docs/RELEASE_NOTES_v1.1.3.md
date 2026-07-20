# BooruDatasetTagManager+ v1.1.3

A focused file-I/O and data-safety hardening pass (fixing the 8 risks confirmed by an internal I/O audit), plus a built-in image editor with a Photoshop-style layout (zoom, pan, eyedropper), Chinese-dictionary search across both tag lists, two new CL-family ONNX tagger models (including the gated cl_tagger_v2), and a configurable double-click quick action on the All Tags grid.

## Highlights

### File I/O & data safety (internal audit)

- **Failed saves no longer lose edits.** If any tag file fails to save on exit or dataset switch (read-only file, sharing violation, disk full…), the exit/switch is blocked and the unsaved edits are kept, instead of showing an error and proceeding anyway.
- **More resilient dataset loading.** The new dataset must fully load before the old one is replaced and disposed, so a failed load can no longer leave the app with a half-torn-down dataset. An unreadable subfolder or a locked tag file only skips that item (tolerant enumeration), with a "loaded N, failed M" summary at the end.
- **Transactional image deletion.** The image and its tag file are first staged into a recycle folder and only purged once every move succeeded; any failure rolls the whole delete back, eliminating "image gone, tags left behind" half-deletes.
- **Safe concurrent saving.** Tag/settings writes use unique temp-file names with a per-target lock, so the main window and the LLM/ONNX windows can save simultaneously without clobbering each other. The translation cache serializes appends/rewrites and replaces the file atomically.
- **Path boundary enforcement.** Transaction recovery on dataset load re-validates that every recovery target stays inside the dataset folder and quarantines suspicious transaction folders; the image sorter rejects category names containing `..`, path separators, or anything else that could escape the root.
- **Startup resilience.** An unreadable Languages folder degrades to raw-key UI text with a log entry instead of crashing before the main window appears.

### New: built-in image editor

Dataset context menu → **Edit image** opens a lightweight editor laid out like Photoshop — a slim symbol tool box on the left, an options bar on top, and a status bar with image size / zoom / tool hints:

- **Tools:** brush, eraser, **eyedropper** (click or drag to sample the brush color; dragging no longer makes the window jitter — the color swatch is redrawn in place instead of forcing a toolbar re-layout per sampled pixel), crop (drag a region, then apply), **hand** (pan), rotate left/right, flip horizontal/vertical, undo/redo.
- **Zoom & pan:** the canvas is drawn manually — **mouse-wheel zoom anchored at the cursor** (5%–3200%), **Ctrl+0** fit to window, **Ctrl+1** 100%, **Ctrl+±** zoom steps; pan with the hand tool, the middle mouse button, or by holding **Space** (temporary hand, like Photoshop).
- **Default Photoshop shortcuts:** **B** brush, **E** eraser, **I** eyedropper, **C** crop, **H** hand, `[` / `]` decrease/increase brush size, **Alt+click** samples a color while the brush is active, **Ctrl+Z** undo, **Ctrl+Shift+Z** / **Ctrl+Y** redo, **Ctrl+S** save, **Enter** apply crop, **Esc** cancel the crop selection. Toolbar tooltips show each shortcut.
- **Saving:** overwrite the original (atomic write — a failed save can never truncate the source) or save an `_edit` copy next to it with the caption file cloned so the copy keeps its tags. The default action (ask / overwrite / new file) is configurable under **Settings → UI → "Image editor: default save action"**.

### New ONNX models: the cl_tagger family

- **[CL] v1.02** — `Nonene/cl_tagger` (public mirror; WD EVA02 fine-tune, 448px). Preprocessing follows the author's `onnx_predict.py`: white-padded square, BGR, `(x/255−0.5)/0.5`, NCHW; raw logits pass through a numerically stable sigmoid; labels come from `tag_mapping.json` (index → tag/category). Default thresholds 0.45 / 0.45.
- **[CL] v2.00 / v2.01a 🔒** — `cella110n/cl_tagger_v2` (SigLIP2-so400m, 384px, ~107-108k tags; both variants selectable, each downloading from its own repo folder). Direct 384×384 bicubic resize, RGB, same normalization; labels from `model_vocabulary.json`; downloads include the `model.onnx.data` external-weights sidecar. Default threshold 0.55.
- **Gated-repo handling:** cl_tagger_v2 ships under the author's custom license that **forbids redistribution, public re-hosting and bundled distribution** — the app never bundles it. Before downloading, a notice summarizes the license and gating: log in to HuggingFace, accept the terms on the model page, request access, and enter your own access token (persisted DPAPI-encrypted like the API keys, sent as a Bearer header). A 401/403 response shows guidance, and manually downloaded files placed in the local `Models/` folder are picked up as-is.
- Per-model thresholds are stored under the catalog id, WD-style; both models run on DirectML with automatic CPU fallback and get the same corrupt-model detection (load-is-the-check, purge and re-download).

### All Tags: double-click quick action & selection fix

- **Double-click quick action** — single click selects, double-click runs a configurable action. Default: open **"Replace all"** with the double-clicked tag preselected as the source. Settings → General offers the All Tags toolbar functions instead: add/remove the tag on all / selected / filtered images, or filter images by the tag.
- **Selection no longer jumps after re-sorts** — with the list sorted by count, adding/removing a tag on other images re-sorts the list, and the grid used to keep the selection by row index, silently landing on a different tag. The selection is now anchored by tag text and restored after every list reset (multi-selection included).

### Tag search understands the Chinese dictionary

- Both the **All Tags** search box and the **new Image Tags search box** match with the priority *English prefix > English substring > translation column > `danbooru-0-zh.csv` dictionary (synonyms included)* — typing 头发 locates long hair, black hair, … even before online translations have loaded.
- The new Image Tags box mirrors the All Tags behavior: typing locates the first match, **Enter** jumps to the next one, **Esc** (or the clear button) resets, and a no-match query tints the box instead of silently eating keys. In multi-select view, continuation rows match through their group's tag text.

### UI & localization fixes

- **Settings overlap fixed.** The "Image editor: default save action" row on the Settings → UI tab overlapped its neighbor rows on high-DPI displays (controls added at runtime are excluded from WinForms auto-scaling); the row is now positioned relative to the already-scaled controls, so it stays on its own line and column-aligned at any DPI.
- **Dataset column names fully localized.** The dataset header right-click menu (column show/hide) previously mixed translated and raw property names; the File path / Image modified time / Tags modified time columns are now localized in all five languages, and the Image / Name columns gain proper zh-TW / ru-RU / pt-BR translations.
- **Clearer folder-loading menu name.** "Load folder with additional settings…" is renamed **"Load Folder (Custom Options)…"**, so the two File-menu entries share the same prefix and the parenthetical marks the difference; the options dialog is retitled **"Folder load options"**, and its checkboxes gain proper zh-TW / pt-BR translations.

### Docs

- The trilingual READMEs (zh / en / pt-BR) are restructured into five sections — changelog first, then quick start (install + build from source), a features table, and the per-feature guide; per-release details stay in `docs/RELEASE_NOTES_*.md`.

### Tests

- 69 new regression tests in this release: fault-injection I/O tests (save-failure, load-failure, delete-rollback, concurrency, path-escape, startup scenarios) plus image editor, tolerant enumeration, file deleter, tag-search suites (Chinese dictionary lookup, alias/translation match priority, wrap-around) and CL model coverage (tag_mapping/vocabulary parsing, stable sigmoid, normalization, catalog/gating flags, nested-path cache validation).
- **333 / 333 unit tests passing.**

## Verification

- `dotnet build BooruDatasetTagManager/BooruDatasetTagManager.csproj` — 0 errors
- `dotnet test BooruDatasetTagManager.Tests` — 333 / 333 passing
- Rendered-UI checks: main window (Image Tags search box), image editor (zh-CN, compact tool box + zoom status), Settings → General/UI tabs (new quick-action row, no overlap), gated-model notice dialog
- cl_tagger preprocessing verified against the author's `onnx_predict.py` (celll1/tagutl) and the cl_tagger_v2 model card; actually running the models requires downloading them (v2 additionally requires your own approved HuggingFace access)

## Install

Download `BooruDatasetTagManagerPlus-1.1.3-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe`.

Self-contained build for Windows x64; no separate .NET install required.

Local runs create **Models/**, **Cache/**, and **settings.json** next to the executable — locally generated data that is safe to delete (models can be re-downloaded from inside the app).

## 更新摘要（中文）

**文件 I/O 与数据安全（内部审计 8 项修复）**

- **保存失败不再丢编辑**：关闭程序或切换数据集时若有标签文件保存失败（只读、被占用、磁盘满等），将阻止退出/切换并保留未保存的修改，而不是提示错误后照常继续
- **加载数据集更稳**：新数据集完整加载成功后才替换并释放旧数据集；无权限子目录、被锁定的标签文件等只跳过该项（容错枚举），加载结束后汇总提示「成功 N、失败 M」
- **删除图片事务化**：图片与同名标签文件先移入暂存回收目录、全部成功才清理，任一失败自动回滚，杜绝「图片已删、标签残留」的半删除
- **并发写盘安全**：标签/设置保存改用唯一临时文件名并按目标加锁，主窗口与 LLM/ONNX 窗口同时保存不再互相覆盖；翻译缓存的追加与重写统一加锁并原子替换
- **路径边界防护**：打开数据集时的事务恢复会重新校验恢复目标必须位于数据集目录内，异常事务目录自动隔离；图片分类器拒绝含 `..`、路径分隔符等可逃逸根目录的分类名
- **启动容错**：语言目录不可读时降级为原始键显示并记录日志，不再在主窗口出现前崩溃

**新增：内置图片编辑器**

- 数据集右键「**编辑图片**」打开轻量编辑器，仿 Photoshop 布局：紧凑左侧工具栏 + 顶部选项栏 + 底部状态栏（尺寸 / 缩放 / 工具提示）
- **工具**：画笔、橡皮擦、**吸管**（点击或拖动取色；拖动取色不再引起窗口抖动——色块就地重绘，不再每个像素触发工具栏重布局）、裁切（拖选区域后应用）、**抓手**（平移）、左右旋转、水平/垂直翻转、撤销/重做
- **缩放与平移**：画布改为自绘——**滚轮缩放以光标为中心**（5%–3200%），**Ctrl+0** 适应窗口、**Ctrl+1** 100%、**Ctrl+±** 步进缩放；抓手工具、鼠标中键或按住**空格**（临时抓手，仿 PS）拖动平移
- **Photoshop 默认快捷键**：**B** 画笔、**E** 橡皮擦、**I** 吸管、**C** 裁切、**H** 抓手、`[` / `]` 调画笔大小、画笔状态 **Alt+点击** 临时取色、**Ctrl+Z** 撤销、**Ctrl+Shift+Z / Ctrl+Y** 重做、**Ctrl+S** 保存、**Enter** 应用裁切、**Esc** 取消选区；按钮悬停提示显示对应快捷键
- **保存**：覆盖原图（原子写，保存失败不会损坏原文件）或另存 `_edit` 副本并自动克隆同名标签文件；默认方式在 **设置 → 界面 →「图片编辑器:默认保存方式」** 选择

**新增：CL 系列 ONNX 推标模型**

- **[CL] v1.02**——`Nonene/cl_tagger`（公开镜像；WD EVA02 微调，448px）。预处理严格对照作者的 `onnx_predict.py`：白底补方 → 448 双三次 → BGR → `(x/255−0.5)/0.5` → NCHW；输出 logits 经数值稳定的 sigmoid；标签来自 `tag_mapping.json`。默认阈值 0.45 / 0.45
- **[CL] v2.00 / v2.01a 🔒**——`cella110n/cl_tagger_v2`（SigLIP2-so400m，384px，约 10.7–10.8 万标签；两个版本均可选，各自从对应子目录下载）。直接 384×384 双三次缩放、RGB、同款归一化；标签来自 `model_vocabulary.json`；下载包含 `model.onnx.data` 外部权重文件。默认阈值 0.55
- **受限仓库处理**：cl_tagger_v2 使用作者自定义许可，**禁止再分发、公开传播与捆绑分发**——软件不附带该模型。下载前弹出许可与访问提示：登录 HuggingFace → 在模型页同意条款并申请访问 → 填入自己的 Access Token（与 API 密钥同样 DPAPI 加密保存，以 Bearer 头发送）；401/403 给出引导，也可手动下载后放入本机 `Models/` 目录直接使用
- 每个模型的阈值按目录 id 独立记忆；与 WD 系列一样走 DirectML（失败自动回退 CPU）与「加载即校验」坏模型清除

**全部标签：双击快速操作与选中修复**

- **双击快速操作**：单击选择、双击执行可配置的快速操作。默认打开「**全部替换**」并把双击的标签预选为源标签；设置 → 常规 可换成 添加/删除标签到 全部・选中・过滤 图片、按标签过滤 等右侧工具栏功能
- **计数重排后选中不再跳位**：按出现次数排序时，在其他图片上增删标签会触发列表重排，网格按行号保持选择、悄悄落在别的标签上；现按标签文本锚定并在每次重排后恢复（多选同样保留）

**标签搜索支持中文字典**

- 「全部标签」搜索框与新增的「**图片标签**」搜索框统一按 **英文前缀 > 英文子串 > 翻译列 > `danbooru-0-zh.csv` 中文字典（含同义词）** 匹配——输入「头发」即可定位 long hair、black hair 等行，翻译尚未加载完也能命中
- 图片标签搜索框行为与全部标签一致：输入即定位、**Enter** 跳下一个匹配、**Esc**/清除按钮重置、无匹配粉色提示；多选模式的延续行按所属标签命中

**界面与本地化修复**

- **设置界面重叠修复**：设置 → 界面 的「图片编辑器:默认保存方式」行在高 DPI 屏幕上与相邻行重叠（运行时添加的控件不参与 WinForms 自动缩放），现改为相对已缩放控件定位，任何 DPI 下独占一行且与其他下拉框对齐
- **数据集列名翻译补全**：数据集表头右键菜单（列显示/隐藏）此前翻译不全，「文件路径 / 图片修改时间 / 标签修改时间」现已补全五语翻译，繁中 / 俄语 / 葡语的「图片 / 名称」列名也一并补译
- **加载菜单更名更直观**：「使用附加设置加载文件夹…」更名为「**加载文件夹（自定义选项）…**」，与「加载文件夹」同前缀、括号标出差异；加载选项对话框标题改为「**文件夹加载选项**」，该对话框的繁中 / 葡语文案补全翻译

**文档**

- 三语 README（中 / 英 / 葡）重构为五段结构：更新日志在最前，其后依次为快速开始（安装与源码构建）、功能一览、功能详解、致谢与许可；各版本明细继续保存在 `docs/RELEASE_NOTES_*.md`

**测试**

- 本版新增 69 项回归测试：故障注入 I/O 测试（保存失败、加载失败、删除回滚、并发、路径逃逸、启动场景）+ 图片编辑器、容错枚举、文件删除器、标签搜索（中文字典、别名/翻译匹配优先级、环绕搜索）与 CL 模型（tag_mapping / vocabulary 解析、稳定 sigmoid、归一化、目录与 gated 标记、嵌套路径缓存校验）套件
- **单元测试 333 / 333 全部通过**

See [v1.1.2 release notes](RELEASE_NOTES_v1.1.2.md) for the previous release.
