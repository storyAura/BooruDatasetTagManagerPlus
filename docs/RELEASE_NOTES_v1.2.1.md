<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.1（中文）

由第二轮全库审计驱动的加固与清理版本：ONNX、网络与图片管线的内存与数据安全修复，legacy Python 后端彻底移除，数据集浏览器更聪明的范围筛选（根目录组、多文件夹并集），以及绝不丢弃已付费结果的双角色审查断点机制。

## 移除 legacy Python 后端

- 外置 Python `AiApiServer/` 后端**整体删除**，连同其客户端（`AiApiClient` + DTO）、"ai-api-server" 自动打标提供方、AiApiServer 设置窗口与 moondream2 自动裁剪功能（约删除 9,600 行）。背景移除与打标早在数个版本前就已全部在客户端内完成。
- 仍选择已删除提供方的旧 `settings.json` 会在启动时**自动迁移**到 OpenAI 兼容提供方——无需任何重新配置。
- 休眠已久的 `WebPWrapper` 原生绑定（WebP 解码早已移交 ImageSharp）一并移除。

## 数据集浏览器：根目录筛选与多文件夹并集

- 根目录组 `(root)` 现在拥有自己的显式范围键：点击其表头会筛选出直接位于数据集根目录下的图片，而不是默默等同于"全部"。根目录组的重命名仍然禁用。
- **Ctrl/Shift 多选文件夹表头会把数据集范围切换为所选文件夹的并集**——图片列表与全部标签计数实时跟随选择；全部取消选择则回落到"所有文件夹"。文件夹右键打标使用同一并集范围。
- 折叠分组（或收窄搜索过滤）会把被隐藏的图片从网格镜像两侧的选择中剪除，删除/批量操作不会再碰到浏览器已经不显示的行。
- 浏览器搜索输入加入 200ms 防抖；首次数据集加载以一次排序构建全部标签列表，取代逐标签的有序插入（此前在大标签集上是 O(n²)）。

## 双角色审查：断点续跑

- 若角色 A 已完成而角色 B 的审查失败，A 的**已付费结果会作为断点保留**：向导会报告哪个角色失败、原因、已完成角色大约消耗了多少 tokens，并提供**只重试失败角色**的选项，不为已完成的角色重复计费。
- 两张参考图会在第一次模型调用前**真正解码**（损坏或标错的文件现在会在花钱之前就失败），进度行也会预先说明模型请求的最大数量。

## 可靠性与数据安全（审计第二批）

- **ONNX 打标器（WD14 / PixAI / CL）：** 图片预处理不再物化巨大的 maxDim×maxDim 中间图（10,000px 的图片曾经要 ~300 MB），也不再泄漏临时位图；DirectML 会话失败先在 CPU 上重试，然后才判定文件损坏；用户取消、缺失原生运行库与缺失文件不再清空数百 MB 的模型缓存；标签 CSV 必须至少包含一行数据才算就绪；续传下载在 416 时校验服务器的 `Content-Range` 总长；会话输出名保留模型的真实大小写；`SessionOptions` 原生句柄会被释放。
- **网络与更新器：** Danbooru wiki 预览只从 `donmai.us` 主机经 HTTPS 下载图片并限制 5 MB；Danbooru 与 GitHub 请求在 429 时遵循 `Retry-After` 并只做一次有界重试；更新检查只接受 win-x64 zip（不再回退到任意资源），下载后校验 GitHub 的 `sha256` 摘要；指向远程主机的明文 HTTP 端点保存前需要显式确认。
- **密钥与供应链：** DPAPI 加密失败会警告而不是悄悄把 API 密钥存成明文；明文→加密迁移会连续保存两次，让旧明文也从 `settings.json.bak` 中轮换掉；打包的 ffmpeg 下载固定到特定 BtbN 构建并校验（压缩包 + 二进制大小），不再跟随滚动的 `latest` 标签。
- **图片管线：** 多区域裁剪导出按扩展名用真实格式编码（含 webp/tif），原子写入、防重名、不留半批；裁剪预览不再每个区域泄漏一张全尺寸位图；图片编辑器的撤销历史改为 512 MB 字节预算上限，取代固定 15 张全图快照；EXIF 方向在加载时烘焙，相机照片不再横躺显示（与保存）；moondream 时代的 `Image.FromFile` 文件锁从剩余工具中清除。
- **文件与视频：** 删除会解析图片真实的字幕扩展名（`.txt` 或 `.caption`），视频与图片走同一条可回滚的分段删除路径；合法但为空的审查事务清单不再让数据集加载崩溃；ffprobe 失败会抛错而不是默默报出全零的视频信息；取消/失败的转换会清理半成品输出；抽帧导入会排除此前抽帧的残留文件。
- **翻译：** 整条翻译链传递 `CancellationToken`，回退超时（或调用方取消）会真正取消在途 HTTP 请求，而不是任其在后台继续运行。
- **标签列表完整性：** 图片标签列表内部维护着一个文本镜像，网格重绑定遗弃单元格编辑事务时它可能悄悄失步（之后的批量替换/删除报 "List desynchronization detected"，失败的变更还会进一步破坏列表）。现在程序化替换/删除会先提交悬挂的编辑事务，网格重绑定前冲掉未完成编辑，检测到失步时**自动修复**（重建镜像并在可执行文件旁写出 `ErrorData.json`），不再在变更中途抛异常。

## 界面、无障碍、多语言

- 慢速的多选标签表构建不再覆盖其后选中图片的标签；批处理任务运行时主窗口拒绝关闭；自动打标设置探测会先停掉计时器再等待（不再堆叠探测、重复列表）。
- 悬浮预览窗从"永远置顶"改为从属窗口：仍浮在主窗口之上，但不再遮挡模态确认框（被藏住的确认框曾让整个程序看起来像卡死）。
- 标签表格加入 Tab 键顺序；视频工具窗口适配小屏（低于 1080×820 可滚动）；LLM/ONNX/视频窗口支持 Esc 关闭；审查向导 DPI 感知；搜索未命中改在状态栏播报而非仅靠颜色。
- **高 DPI 下的 LLM 设置对话框：** 对话框现在像应用的其他窗口一样声明 96-DPI 设计基线，API 密钥行也留出了余量——125% 显示缩放下，密钥列表下方"多个密钥轮流使用"的提示曾被拦腰截断。
- zh-TW/ru/pt-BR 中 42 条未翻译（英文占位）的文案已补齐，图片分拣窗口完成全量本地化（五种语言各新增 17 个键）。
- **程序内更新提示按界面语言显示：** 从本版本起，发行说明以中英两份独立全文发布（以语言标记分隔），启动时与手动检查更新的弹窗只显示与界面语言匹配的那份；启动提示文案本身也完成本地化（此前是硬编码英文）。
- README 不再以绝对化措辞承诺"批量工具绝不破坏原文件"（视频替换原文件本来就会按设计删除源文件——现已如实说明）；UI 结构文档描述了当前的浏览器/内嵌预览布局。

## 工程与杂项

- **Debug 菜单改造为可选调试模式。** 旧的开发者专用测试项（分拣设置、空图片网格窗、手动裁剪）已移除。新的**调试模式**开关（设置 → 常规，默认关闭）会显示 Debug 菜单，并把带时间戳的运行信息写入可执行文件旁的 `debug.log`——启动版本/系统信息，以及 `crash.log` 每条记录的镜像；菜单中的"打开调试日志"可直接打开该文件。
- `global.json` 固定 .NET SDK 下限（稳定版 ≥ 8.0，最新主版本）；`test_start.bat` 启动最新构建的程序而不是过期的 Release；`check_update.bat` 返回真实退出码；`publish_release.bat` 在把版本参数展开进路径与 `gh` 命令之前先校验它。
- 测试套件从 439 增长到 **481**（新增回归：预览 URL 白名单、仅表头 CSV、翻译超时取消、根范围哨兵、多文件夹并集范围、双审查断点/续跑、发行说明语言分节）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.1 (English)

A hardening and cleanup release driven by the second wave of the full-codebase audit: memory and data-safety fixes across the ONNX, network and image pipelines, the legacy Python backend removed for good, smarter dataset-browser scoping (root group, multi-folder union), and checkpointed dual-character audits that never throw away paid results.

## Legacy backend removed

- The external Python `AiApiServer/` backend is **deleted entirely**, together with its client (`AiApiClient` + DTOs), the "ai-api-server" auto-tag provider, the AiApiServer settings window and the moondream2 auto-crop feature (~9,600 lines removed). Background removal and tagging have been fully in-client for several releases.
- Old `settings.json` files that still select the removed provider **migrate automatically** to the OpenAI-compatible provider at startup — nothing to reconfigure.
- The dormant `WebPWrapper` native bindings (WebP decoding moved to ImageSharp long ago) are also gone.

## Dataset browser: root scope and multi-folder union

- The root group `(root)` now has its own explicit scope key: clicking its header filters to images directly under the dataset root, instead of silently meaning "everything". Renaming stays disabled for the root group.
- **Ctrl/Shift multi-selecting folder headers now scopes the dataset to the union of the selected folders** — the image list and the All Tags counts follow the selection live; deselecting everything falls back to "all folders". Folder right-click tagging uses the same union scope.
- Collapsing a group (or narrowing the search filter) prunes the now-hidden images from the selection on both sides of the grid mirror, so delete/bulk actions can no longer touch rows the browser no longer shows.
- Browser search input is debounced (200 ms), and the first dataset load builds the All Tags list with a single sort instead of a per-tag sorted insert (previously O(n²) on large tag sets).

## Dual-character audit: checkpoints

- If character B's audit fails after character A already completed, A's **paid result is kept as a checkpoint**: the wizard reports which character failed, why, and roughly how many tokens the completed characters consumed, then offers to **retry only the failed character** without re-billing the finished one.
- Both reference images are **actually decoded** before the first model call (a corrupt or mislabeled file now fails before any money is spent), and the progress line states the maximum number of model requests up front.

## Reliability & data safety (audit wave 2)

- **ONNX taggers (WD14 / PixAI / CL):** the image preprocessor no longer materializes a giant `maxDim×maxDim` intermediate (a 10,000 px image used to cost ~300 MB) and no longer leaks temporary bitmaps; DirectML session failures retry on CPU before anything is declared corrupt; user cancels, missing native runtimes and missing files no longer purge a multi-hundred-MB model cache; a labels CSV must contain at least one data row to count as ready; resumed downloads validate the server's `Content-Range` total on 416; session output names keep the model's real casing; `SessionOptions` native handles are disposed.
- **Network & updater:** Danbooru wiki previews only download HTTPS images from `donmai.us` hosts with a 5 MB cap; Danbooru and GitHub requests honor `Retry-After` on 429 with one bounded retry; the update checker only accepts the win-x64 zip (no more falling back to arbitrary assets) and verifies the GitHub `sha256` digest after download; plain-HTTP endpoints to remote hosts now require explicit confirmation before saving.
- **Secrets & supply chain:** a DPAPI encryption failure warns instead of silently storing the API key as plain text, and a plaintext-to-encrypted migration re-saves twice so the old plaintext also rotates out of `settings.json.bak`; the bundled ffmpeg download is pinned to a fixed BtbN build and validated (archive + binary sizes) instead of following a rolling `latest` tag.
- **Image pipeline:** multi-region crop export encodes real formats per extension (webp/tif included) with atomic writes, collision-free names and no half-exported batches; the crop preview no longer leaks a full-size bitmap per region; the image editor's undo history is capped by a 512 MB byte budget instead of a fixed 15 full-bitmap snapshots; EXIF orientation is baked in on load, so camera photos stop showing (and saving) sideways; the moondream-era `Image.FromFile` file locks are gone from the remaining tools.
- **Files & video:** deletion resolves the image's real caption extension (`.txt` or `.caption`) and deletes videos through the same staged, rollback-capable path as images; a legal-but-empty audit transaction manifest no longer crashes dataset loading; ffprobe failures throw instead of silently reporting zeroed video info; canceled/failed conversions clean up their partial output; frame-extraction imports exclude leftovers of earlier extractions.
- **Translation:** the whole translator chain takes a `CancellationToken`, so a fallback timeout (or caller cancel) actually cancels the in-flight HTTP request instead of leaving it running in the background.
- **Tag list integrity:** the image-tags list keeps an internal text mirror that could silently desynchronize when a grid rebind abandoned a cell-edit transaction (later bulk replace/delete then crashed with "List desynchronization detected", and the failed mutation corrupted the list further). Programmatic replace/remove now commit dangling edit transactions first, grid rebinds flush pending edits, and a detected desync **self-heals** (rebuilding the mirror and writing `ErrorData.json` next to the executable) instead of throwing mid-mutation.

## UI, accessibility, i18n

- A slow multi-select tag-table build can no longer overwrite the tags of an image selected afterwards; the main window refuses to close while a batch job is running; the auto-tagger settings probes stop their timer before awaiting (no more stacked probes and duplicated lists).
- The floating preview window is an owned window instead of always-on-top: it still floats above the main window, but no longer covers modal confirmation dialogs (a hidden confirm box used to make the whole app look frozen).
- Tag grids joined the Tab order; the video tools window fits small screens (scrollable below 1080×820); LLM/ONNX/video windows close on Esc; the audit wizard is DPI-aware; search misses are announced in the status bar instead of color-only.
- **LLM settings dialog on high-DPI displays:** the dialog now declares its 96-DPI design baseline like the app's other windows, and the API-key row gained slack — at 125 % display scale the "stored keys rotate per request" hint under the key list used to be clipped in half.
- 42 untranslated (English-copy) strings across zh-TW/ru/pt-BR are translated, and the image sorter windows are fully localized (17 new keys in all five languages).
- **Update prompts follow the UI language:** starting with this release, release notes ship as two independent full versions (Chinese and English, separated by language markers), and both the startup update prompt and the settings-window update check show only the section matching the UI language; the startup prompt text itself is localized too (it was hardcoded English before).
- READMEs no longer promise "batch tools never destroy originals" in absolute terms (video replace-original deletes the source by design — now stated); the UI structure document describes the current browser/embedded-preview layout.

## Housekeeping

- **Debug menu reworked into an opt-in debug mode.** The old developer-only test entries (sorter settings, empty image-grid window, manual crop) are removed. A new **debug mode** toggle (Settings → General, off by default) shows the Debug menu and writes timestamped runtime info to `debug.log` next to the executable — startup version/OS, plus a mirror of every `crash.log` entry — and the menu's "Open debug log" item opens the file directly.
- `global.json` pins the .NET SDK floor (stable ≥ 8.0, latest major); `test_start.bat` launches the newest built binary instead of a stale Release; `check_update.bat` returns a real exit code; `publish_release.bat` validates its version argument before expanding it into paths and `gh` commands.
- Test suite grows from 439 to **481** (new regressions: preview-URL allowlist, header-only CSV, translation-timeout cancellation, root-scope sentinel, multi-folder union scope, dual-audit checkpoint/resume, release-notes language sections).
