# BooruDatasetTagManager+ v1.1.2

Security, stability, and performance hardening pass. No workflow or UI changes.

## Highlights

### Crash fixes
- **Preview-tab hotkey no longer crashes.** After the AutoTagger preview tab was removed, the "focus preview tab" hotkey still selected tab index 2 (out of range for the remaining two tabs) and crashed; it now targets the correct tab and moves to Ctrl+4, and the orphaned Ctrl+4 "focus AutoTagger preview" hotkey was removed. In the same pass, missing/stale localization was fixed across all five languages: the save-error dialog no longer shows a raw `TipSaveErrors` key, the two unlocalized translation-service names and the Traditional-Chinese language-menu entry display properly, built-in prompt-template names localize in the LLM tagging window, the "Tagging settings…" dialog gets its own title, and dead keys from the removed TAG2NL/AutoTagger-preview/server-based background-removal flows were deleted.
- **Deleting tagged images no longer crashes.** The dataset grid is bound to a plain `List<DataItem>`, which does not raise change notifications; after removing rows the grid kept stale entries with null cells, throwing `IndexOutOfRangeException` on paint and then a repeated `ArgumentNullException` (null key) in `LoadSelectedImageToGrid`. The grid now re-reads its row count via `CurrencyManager.Refresh()`, and the tag-loading code guards against stale/removed rows.
- **Deleting several images at once no longer crashes with `ImageAnimator` "Parameter is not valid".** A PictureBox was being asked to animate an already-disposed image on `WM_SHOWWINDOW`. Two causes were fixed: (1) `GetImageFromFileWithCache` could return a disposed shared instance when a concurrent removal disposed the cache entry mid-clone — the cache now clones under its own lock (`TryGetClone`) and never hands out the shared instance; (2) the main-window and separate preview PictureBoxes now detach the previous image before disposing it, so a later show never animates a disposed image.

### Security
- **Tag database format.** `List.tdb` is now serialized with JSON (Newtonsoft.Json) instead of `BinaryFormatter`, removing a deserialization code-execution risk. Legacy/corrupt caches fail safe and rebuild from the source CSV/txt.
- **API key at rest.** The OpenAI-compatible API key in `settings.json` is encrypted with Windows DPAPI (`CurrentUser` scope). Existing plaintext keys are read once and re-written encrypted on the next save.
- **External-call hardening.**
  - ffmpeg is invoked via `ProcessStartInfo.ArgumentList` (per-argument escaping) instead of a hand-quoted command string.
  - Google-translate queries are URL-encoded; the broken manual HTML-entity decoder was removed.
  - HuggingFace download paths are validated to stay inside the `Models/` directory (path-traversal guard).
- **AiApiServer hardening.**
  - The server now binds to `127.0.0.1` by default; pass `--listen` to accept remote connections and `--port` to change the port (default 50051).
  - Optional `--api-key <key>` requires every request to carry a matching `X-Api-Key` header. The client sends it automatically when `AutoTagger.ApiKey` is set in `settings.json` (stored DPAPI-encrypted like the OpenAI key).
  - `VIDEO_PATH` requests are validated (no `..` segments, video-extension whitelist, file must exist) before the path reaches a model.
  - Request bodies are capped at 512 MB (`MAX_CONTENT_LENGTH`), preventing memory-exhaustion payloads.
  - Served by multi-threaded waitress (added to `requirements.txt`) so status endpoints stay responsive during long inference; falls back to the Flask dev server if waitress is missing.
  - The three copy-pasted OOM-retry blocks were unified into `force_unload_all()`, and bare `except: pass` handlers now log failures.
- **DPAPI decryption feedback.** If a stored key cannot be decrypted (settings copied from another machine or user account), the app now shows a localized warning at startup asking to re-enter the key instead of silently presenting an empty field.

### Stability
- **Change detection.** "Unsaved changes" is now order-independent (a structure signature over the key set) and per-item modification uses exact tag-text comparison instead of a 32-bit hash, so edits can no longer collide and be silently dropped or falsely flagged. `SaveAll` resets each item's saved snapshot after a successful write.
- **Bounded image cache.** The preview/thumbnail cache is a capacity-limited LRU that disposes evicted images (previously an unbounded dictionary that never released GDI handles). Callers receive an independent clone, so an eviction can never dispose an image still bound to a control.
- **Settings load.** `AppSettings.LoadData` now retries a bounded number of times and handles write failures instead of looping, falling back to defaults when a settings file cannot be recovered.
- **Delete feedback.** File-delete failures are logged and surfaced to the user; only successfully deleted items are removed from the dataset, keeping on-disk and in-memory state consistent.
- **Deterministic dataset teardown.** `DatasetManager` implements `IDisposable`; opening a new folder unbinds the grid and disposes every thumbnail of the previous dataset instead of leaving thousands of GDI+ bitmaps to finalizers (which could exhaust the process GDI-handle limit on repeated folder switches).
- **Preview images always disposed.** The main-window and separate-window previews previously leaked the old image whenever image caching was enabled (the default); since the cache hands out caller-owned clones, the old image is now disposed on every swap. The transparent-background tool also disposes replaced thumbnails.
- **RemoveMany count consistency.** Global tag counts are now decremented only after an item is actually removed from the dataset, so a concurrent removal can no longer leave `AllTags` counts out of sync.

### Crash & data-safety hardening (full-codebase audit)

A dedicated crash/IO audit of the whole codebase (~37k lines, client + AI server) produced a second round of systematic fixes:

- **Global exception backstop.** `Application.ThreadException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` are now registered; unhandled errors are appended to `crash.log` (falling back to `%LOCALAPPDATA%` on read-only installs) and surfaced as a localized dialog instead of silently terminating the process.
- **Startup chain resilience.** A corrupt/missing `ColorScheme.json`, language file, or `settings.json` no longer hard-crashes before any window exists: broken files are backed up as `.corrupt` and defaults are used; an unknown UI culture or missing language falls back to `en-US`; a failed tag-database load shows an error instead of leaving the splash screen open forever. Tag CSV loading tolerates per-file failures and count overflows.
- **Atomic writes everywhere it matters.** New `SafeFile` helper (temp file + `File.Replace`): dataset `.txt` saves can no longer be truncated by a locked file or full disk, one failed file no longer aborts `SaveAll` (failures are listed and stay marked modified), and `settings.json` is written atomically with a `.bak` (a mid-write crash used to silently reset all settings, including API keys).
- **Batch tools never destroy sources.** Crop, background removal, and transparent-background replacement encode to a temp file and swap only on success; per-image failures are skipped and reported; all five batch operations now release the UI lock in `finally` (an error used to leave the entire window permanently disabled). The transparent-background tool also stops reading UI controls from its worker thread.
- **Closing mid-job is safe.** The ONNX tagger, video tools, video convert, and character-audit wizard defer a close requested while a job runs (cancel + close after the job unwinds), eliminating an uncatchable native AccessViolation (ONNX session disposed under an in-flight `Run`) and thread-pool `ObjectDisposedException` process kills. Progress callbacks are marshaled through a shared dispose-safe helper.
- **Cancellation actually cancels.** `Process.WaitForExitAsync(token)` replaces an uncancellable wait, so cancelling a conversion/extraction really kills the ffmpeg child (it used to keep running and writing to disk for minutes). Cancelling an ONNX batch now applies and saves the tags of images already processed instead of discarding everything.
- **"Replace original" conversion fixed.** It used to pass the input file as ffmpeg's output (`-y` truncates the output before reading — guaranteed failure on modern ffmpeg, source destruction on old builds). Conversion now writes `_convert_tmp` and atomically replaces the original on success; `ConvertAsync` additionally rejects identical input/output paths.
- **Locale-safe prompt weights.** `(tag:1.1)` weight parsing uses invariant culture (it used to throw `FormatException` for every weighted dataset on decimal-comma locales such as ru-RU/de-DE); malformed weights fall back to 1.
- **Interruption-safe model downloads.** HuggingFace downloads write to `.partial` and are renamed only after a Content-Length check; a truncated `model.onnx` (>1 MB) can no longer masquerade as a valid cached model and permanently break tagging. A run/download re-entrancy race in the ONNX dialog was also closed.
- **Integrity check before use.** All local models (WD14, PixAI, background removal) are integrity-checked at load time — loading the ONNX session (and parsing the csv/json sidecars) *is* the check. A corrupt or incomplete file throws `OnnxRuntimeException`, which the service catches, deletes the bad file(s), and rethrows as a localized `ModelCorruptedException` so the caller re-downloads a clean copy (the background-removal dialog auto-retries the download once; the ONNX tagger re-prompts on the next run). Genuine environment errors (missing native runtime, unsupported input precision) are excluded so a valid model is never wrongly deleted. Verified against real garbage/truncated ONNX files (all throw a catchable `OnnxRuntimeException`).
- **Misc B-class crash fixes.** Empty-selection / removed-item guards on all grid-driven batch actions, clipboard operations hardened, null-safe translation-column toggle, broken preview frames no longer crash the scrub/playback handlers, `Form_BGRemover` preview no longer disposes the image stream before painting, `Form_CropImage` detection guards empty AI results, the image sorter reports per-file copy failures instead of aborting disabled, out-of-range persisted settings are clamped when opening settings windows, and the "manual crop" menu uses a file picker instead of a hardcoded developer path.

### AiApiServer resilience (audit round)

- The pyvips runtime download moved out of import time into model load, with a request timeout and post-extraction validation — an offline first start used to hang the server before it could bind its port, and a half-extracted runtime was never repaired.
- Optional heavy dependencies (`qwen_vl_utils`, `keye_vl_utils`, version-sensitive transformers classes) are imported lazily, so a missing package disables just that model instead of preventing the whole server from starting; three unused `transformers.video_utils.load_video` imports (broken by transformers version drift) were removed.
- VRAM OOM recovery (`force_unload_all` + global resets) now runs under the interrogator lock, so it can no longer release models/CUDA state under another thread's active inference.
- Client-supplied video parameters are clamped (`fps` ≤ 60, `max_frames` ≤ 768, `min/max_pixels` ≤ 16 MP, `max_new_tokens` ≤ 4096), and an explicit `Image.MAX_IMAGE_PIXELS = 100_000_000` cap bounds decode memory.

### Multi-select tag editing UX

- **Visual tag editor (Shift+T) upgraded.** A left-side list now shows every tag of the selected images with occurrence counts ("tag (3/6)"), sorted by frequency; clicking a tag re-colors the image wall for that tag, so reviewing many tags no longer requires closing the dialog and pre-selecting another row (the button also works without a pre-selected tag now). The current tag and the green/red border legend are displayed prominently, per-tag pending edits survive switching, and one Save applies the changes of every touched tag.
- **Multi-select tag table readability.** The table stores one row per (tag, image) pair with the tag text only on the group's first row, which read as a wall of empty rows; continuation rows now display their group tag dimmed (display-only, editing semantics unchanged).

### Update check

- **"Check for updates" button in Settings.** Release installs query the latest GitHub release, show the release notes, and download the win-x64 zip next to the executable (progress on the button, `.partial` download, falls back to the user's Downloads folder or the release page when needed). When the app runs from a source checkout (`.git` + solution file found above the executable), it offers `git pull --ff-only` instead and shows git's output.
- **Standalone `check_update.bat`.** The same dual-mode logic as a script: `git pull --ff-only` in a checkout, otherwise a PowerShell-driven GitHub API check that compares the local `BooruDatasetTagManagerPlus.exe` file version and downloads the newest zip. The script ships in the repo root and inside the release package.

### Performance
- O(1) tag membership check in `EditableTagList` (count-map mirror) instead of a linear scan.
- O(1) tag-database parent check via a HashSet.
- File-extension checks use a case-insensitive `HashSet` instead of array scans.
- Folder loading uses `Interlocked` for progress instead of serializing the parallel body behind a semaphore.
- Selection restore after grid refresh uses a `HashSet` (was O(rows × selected)).
- Tag-id allocation (`GetNextId`) is now a monotonic counter instead of an O(n) max-scan per insert, making per-image tag-list construction linear.
- `DeduplicateTags` is a single O(n) pass (was O(n²)) and releases its lock via try/finally; it also fixes a latent index bug when a tag appeared three or more times.
- "Change selection" dialog result application uses a dictionary lookup instead of a per-row LINQ scan (which could also crash via `.First()` on missing entries).
- `cmd_args.get_args()` is cached (`lru_cache`), so `torch_gc()` no longer re-runs `argparse` on every model unload.
- Dataset loading caps parallelism at half the cores and throttles progress events (1 per 32 images, marshaled to the UI thread) — full-width parallel decode used to spike memory and storm the status bar cross-thread.
- Thumbnail conversion copies pixel rows directly (Bgra32 → GDI+) instead of encoding to PNG and decoding again — two fewer full encode/decode passes per image on every folder load.
- Video preview scrubbing seeks with `-ss` by timestamp (a long video no longer decodes from frame 0 for every scrubbed frame); extracted preview PNGs are deleted right after entering the in-memory cache instead of accumulating in `%TEMP%`; the video-thumbnail cache directory is capped at 2000 files.
- The character-audit apply phase runs its per-file disk transaction on a background thread (a large dataset used to freeze the wizard with the progress label never repainting), and `Application.DoEvents()` was replaced with a plain repaint.
- GDI handle leaks fixed in the tag-image grid (thumbnails now disposed on close) and the right-click zoom preview (form now disposed).

### Background removal — now in-process (local ONNX)
- **No external service required.** Background removal previously called the external `AiApiServer` (PyTorch RMBG-2.0). It now runs entirely inside the client with ONNX Runtime and the official **RMBG-1.4** ONNX weights (`briaai/RMBG-1.4`), so users no longer need to start a Python server. RMBG-1.4 is used instead of 2.0 because the 2.0 repo is gated and cannot be downloaded anonymously.
- **First-use download, DirectML acceleration.** The model is downloaded from HuggingFace on first use (~176 MB full, or ~44 MB quantized) via the existing resumable, integrity-checked downloader (official site or `hf-mirror.com`), cached under `Models/`. Inference uses DirectML with automatic CPU fallback, matching the WD14/PixAI taggers. Preprocessing (RGB, 1024×1024 bilinear, /255, mean 0.5 / std 1.0) and the min-max mask postprocessing mirror BRIA's RMBG-1.4 reference; verified end-to-end against the real ONNX model (float32 I/O, correct foreground/background separation).
- **Redesigned dialog.** The "check connection" flow was replaced with a precision picker (full / quantized), a download source, a download-and-load button with a progress bar, and the same "removing test" preview. A cached model auto-loads when the dialog opens, so it is usable immediately. New output options: **transparent or solid-color background** (white by default, with a color picker) and **overwrite the original or save a `<name>_nobg.png` copy** — both persisted. Non-RGB inputs (RGBA / grayscale / palette) are handled natively by the ImageSharp pipeline. UI naming unified to "background removal / remove background" (the RMBG model number remains only in the model dropdown).
- **Dialog bug fixes.** The three radio groups (removing mode / background / output) were all in one GroupBox, so WinForms made them mutually exclusive — they now sit in separate panels and are independently selectable. The "removing test" preview flashed and vanished because a `using` form was shown non-modally (disposed on scope exit); it is now modal.
- **Refresh after processing (IO-safe).** After a batch, replace-mode rebuilds the grid thumbnails and reloads the current preview; save-a-copy mode imports the new `_nobg.png` files into the list. All refresh reads happen after the writes complete, go through ImageSharp (which does not lock files) with the cache already cleared, and read each file at most once — no read/write races or file locks.
- The `AiApiServer` RMBG-2.0 editor and its ModelScope source remain available for anyone using the HTTP API directly, but the desktop client no longer depends on them.
- **Verified** the background-fill compositing against a synthetic semi-transparent image (transparent area filled with white, foreground preserved) and, earlier, the full model pipeline against the real ONNX weights.

### Unified LLM tagging

- **One window for all external-LLM tagging.** The scattered "AI vision tagging" toolbar buttons, the "AutoTagger preview" staging tab, and the standalone TAG2NL feature are consolidated into a single **LLM tagging** window (Tools → LLM tagging, or right-click a dataset image), modeled on the ONNX tagger: input source (selected / all), a **Tags / Natural-language** mode selector, vision model, write mode, sort, prefix/suffix, underscore replacement, LLM concurrency, and a cancelable progress bar with the same job-control guard as the ONNX tagger.
- **Tags mode** calls the external LLM concurrently (bounded by the LLM concurrency setting) and writes results straight back to the dataset per the write mode (`TagWriteService` + batched mutation) — replacing the old preview-then-apply grid.
- **Natural-language mode** is the former TAG2NL: save a `_captioned` copy (default, non-destructive) or write the caption into the image's own `.txt` in place (routed through the dataset manager via `PromptParser`, so in-memory and on-disk stay consistent).
- **Unified naming & concurrency.** "AI vision tagging" is renamed to "LLM tagging" across all five UI languages; the former "TAG2NL concurrency" is now the single external-LLM concurrency setting (default 5, range 1–100).
- **Removed** the "AutoTagger preview" tab, the standalone TAG2NL menu, its dialogs (`Form_LlmT2NlConfirm` / `Form_LlmT2NlProgress`), and the dead `RunLlmT2NlAsync` / `RetagSelectedWithLlmAsync` paths.
- **Content format & modes** — the Natural-language mode is renamed "Tags → Natural language"; a content-format choice (Tags + natural language, the original TAG2NL format, or natural language only) is independent of the output destination (`_captioned` copy or in-place).
- **ONNX-first auto-pipeline** — in Natural-language mode, images with no tags can be tagged by the local WD14 ONNX tagger first (with a download prompt if the model is missing) and then captioned, so the LLM always has reference tags.
- **Settings folded in** — a prompt-template dropdown and a "Tagging settings…" button (full prompt/params editor) live in the window; the "LLM tagging for all images" tool item and the standalone "auto-tagger settings" menu are removed, and the tag-toolbar button opens the window.

### Settings

- The "use danbooru-0-zh.csv before online translation" toggle moved from the Test module into **Settings → Translations** and now **defaults on** (checks the local table before any online lookup).

### Tests & cleanup

- New unit tests for `ImageLruCache` (capacity eviction disposes, clone independence, replace/remove/clear disposal, concurrent access) and `SecretProtector` (round-trip, idempotent protect, legacy plaintext pass-through, fail-closed on corrupted/forged payloads).
- New unit tests for `SafeFile` (atomic create/overwrite, UTF-8 no-BOM encoding, `.bak` backup, locked destination fails loudly without truncating the original), `PromptParser` culture handling (dot-decimal weights under ru-RU/de-DE, malformed-weight fallback, unbalanced brackets), and the new video-convert path semantics (replace-original targets the original name, temp path is a distinct sibling).
- Dead code removed (`DatasetManager.GetImageList`, `EditableTagList.RebuildTagCounts`, `ImageLruCache.TryGet`); the update-check `HttpClient` is shared; an empty `catch` in Explorer integration now logs.
- Unified LLM tagging adds tests for `TagPostProcessor` underscore handling on `OpenAiSettings` and the `CaptionGenerationService` in-place caption sink; the standalone TAG2NL methods and dialogs were removed.

## Verification

- `dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows` — 0 errors (Release also clean)
- `dotnet test BooruDatasetTagManager.Tests` — 264 / 264 passing
- `python -m py_compile` over `main.py`, `cmd_args.py`, `utilities.py`, `pyvips_dll_handler.py`, and the moondream2/qwen25/qwen3/keye interrogators — OK; `--listen` / `--port` / `--api-key` argument parsing verified

## Install

Download `BooruDatasetTagManagerPlus-1.1.2-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe`.

Self-contained build for Windows x64; no separate .NET install required.

Local runs create **Models/**, **Cache/**, and **settings.json** next to the executable — locally generated data that is safe to delete (models can be re-downloaded from inside the app).

## 更新摘要（中文）

- **修复删除带标签图片时的崩溃**：网格绑定的 `List<DataItem>` 不发出变更通知，删除后残留空单元格行导致 `IndexOutOfRangeException` 与反复的 `ArgumentNullException`；现用 `CurrencyManager.Refresh()` 同步行数并对失效行做空值保护
- **修复多图删除时的预览崩溃**：PictureBox 在窗口显示时动画一张已释放的图片导致 `ImageAnimator` 报 `Parameter is not valid`；图片缓存改为持锁克隆（`TryGetClone`）消除释放竞态，主窗口与独立预览均在释放旧图前先解除控件引用
- **安全**：标签缓存改 JSON（去除 BinaryFormatter 反序列化执行风险，损坏自动重建）；API 密钥用 DPAPI 加密（自动迁移旧明文）；ffmpeg 用 ArgumentList、翻译查询 URL 编码、HuggingFace 下载路径防穿越
- **稳定**：变更检测顺序无关 + 精确文本比较；图片缓存改带上限 LRU 并释放；设置加载限次重试；删除失败有日志与提示
- **性能**：标签查找、扩展名判断、标签库父项均 O(1)；文件夹加载真正并行
- **背景移除改为客户端本地 ONNX**：改用内置 **RMBG-1.4** ONNX 权重（`briaai/RMBG-1.4`）在客户端本地抠图，**彻底去掉对外部 `AiApiServer` 的依赖**（RMBG-2.0 仓库为 gated、无法匿名下载，故用 1.4）；首次使用从 HuggingFace 官方源或 `hf-mirror.com` 下载（约 176 MB，或量化版约 44 MB），经既有的断点续传 + 完整性校验下载器缓存到 `Models/`；DirectML 加速、失败回退 CPU，已下载模型打开窗口即自动加载；新增透明/纯色背景（默认白、带取色器）与替换原图/另存 `_nobg.png` 副本（均记忆）；RGBA/灰度/调色板图由 ImageSharp 原生处理，不再因 3 通道归一化崩溃；`AiApiServer` 的 RMBG-2.0 编辑器仍保留给直接调用 HTTP API 的用户
- **AI 服务端加固**：默认仅监听 `127.0.0.1`（远程访问加 `--listen`，端口用 `--port` 指定）；可选 `--api-key` 鉴权（客户端在 `settings.json` 的 `AutoTagger.ApiKey` 填同一值，DPAPI 加密保存）；视频路径校验（拒绝 `..`、限视频扩展名）；请求体上限 512MB；改用 waitress 多线程服务器；三处重复的 OOM 重试代码合并为 `force_unload_all()` 并记录日志
- **内存/GDI 泄漏修复**：`DatasetManager` 实现 `IDisposable`，打开新文件夹时释放旧数据集全部缩略图；预览图切换时始终释放旧图（此前开启缓存时从不释放）；`RemoveMany` 先移除成功再扣减全局标签计数
- **更多性能优化**：选中恢复、标签去重、标签 ID 分配从 O(n²) 降为 O(n)；`cmd_args` 解析结果缓存
- **全局崩溃兜底**：注册全局异常处理并写入 `crash.log`（安装目录只读时写 `%LOCALAPPDATA%`）；未处理错误改为本地化提示，不再无声退出
- **启动不再硬崩**：`ColorScheme.json`、语言文件、`settings.json` 损坏或缺失时自动备份 `.corrupt` 并用默认值继续；未知语言回退 en-US；标签库加载失败明确提示，不再永久卡在启动画面
- **标签保存零丢失**：新增 `SafeFile` 原子写（临时文件 + `File.Replace`）——磁盘满 / 文件被占用不再清空原 `.txt`，单文件失败不中断整批并弹出失败清单；`settings.json` 原子写 + `.bak`（此前写一半崩溃会静默重置全部设置）
- **批量处理不破坏原图**：裁剪 / 去背景 / 换底色先写临时文件、成功后才替换；单张失败跳过并汇总；五个批量操作的界面锁全部移入 `finally`（出错不再永久锁死界面）；换底色不再从后台线程读 UI 控件
- **任务中关窗安全**：ONNX 推标 / 视频转换 / 抽帧 / 角色审查向导运行中关窗改为「取消 + 收尾后自动关闭」，消除原生 AccessViolation 与线程池崩溃；取消现在会真正杀掉 ffmpeg 子进程；ONNX 中途取消保留已完成图片的标签
- **修复「替换原文件」转码**：原实现输入输出同一文件（现代 ffmpeg 必然失败，老版本会就地销毁源视频）；现改临时文件 + 原子替换，并在 `ConvertAsync` 增加同路径硬校验
- **非英文区域解析修复**：`(tag:1.1)` 权重改用固定区域性解析（俄语 / 德语等小数逗号区域此前加载即报错）；畸形权重回退为 1
- **模型下载抗中断**：HuggingFace 下载改 `.partial` + Content-Length 校验后改名；半截 `model.onnx` 不再被误判为有效缓存
- **加载前完整性检测**：使用本地模型（WD14 / PixAI / 背景移除）前以「加载即校验」做完整性检测：模型文件损坏或不完整时自动清除坏文件并提示重新下载（背景移除自动重下一次），不再卡在反复报错；环境类错误（缺原生运行库等）不会误删有效模型
- **热键与本地化修复**：「聚焦预览标签页」热键在预览页移除后仍指向越界索引、按下即崩，已修正并顺延为 Ctrl+4，同时移除指向已删页面的孤儿热键；补齐保存失败弹窗缺失的 `TipSaveErrors`（此前显示裸键名并丢失错误详情）、翻译服务下拉两个未本地化选项与语言菜单「繁體中文」项（含错别字）；LLM 打标窗口内置模板名随界面语言显示，「打标设置…」弹窗获得独立标题；清除原 TAG2NL / AutoTagger 预览 / 旧版服务端背景移除遗留的死键与过时提示（含「请启动 AiApiServer 并安装 RMBG-2.0」）
- **背景移除界面修复**：三组单选（移除模式 / 背景 / 输出方式）不再互斥、可各自独立选择（此前同处一个 GroupBox 被 WinForms 强制互斥）；「移除测试」预览不再一闪而过（改为模态显示，此前 `using` 窗体非模态显示后立即被释放）
- **加载与预览性能**：并行解码限半核 + 进度事件 1/32 节流并封送 UI；缩略图去 PNG 中转直接拷贝像素行；视频预览 `-ss` 按时间定位、临时帧即用即删、缩略图缓存上限 2000；审查向导写盘移后台线程；修复标签图片网格与右键预览的 GDI 句柄泄漏
- **AI 服务端稳态**：pyvips 运行库下载移出 import 阶段（加超时 + 解压校验，离线首启不再挂死）；qwen / keye 可选依赖惰性导入（缺包只禁用对应模型）；OOM 恢复移入全局锁；抽帧参数加上限（fps≤60、max_frames≤768 等）；图像像素显式上限 100MP
- **多选标签编辑体验**：可视化编辑器（Shift+T）新增左侧标签列表（含出现次数、按频次排序），点击即切换校对目标，无需预先选中标签行再打开；当前标签与绿/红框图例醒目展示；跨标签的修改一次保存统一生效。多选标签表的延续行改为灰显所属标签，不再呈现大片"空行"
- **设置内一键检查更新**：软件设置右下角新增「检查更新」按钮——发布版比对 GitHub Releases 并直接下载最新 win-x64 压缩包（按钮显示进度、`.partial` 防半截、必要时回退 Downloads 目录或发布页）；源码运行自动检测仓库目录并执行 `git pull --ff-only`；另附双模式 `check_update.bat`（仓库根目录 + 发布包内）
- **统一 LLM 打标**：零散的「AI 视觉打标」按钮、「AutoTagger 预览窗口」暂存页与独立的 TAG2NL 合并为单一 **LLM 打标** 窗口（工具 → LLM 打标 / 数据集右键），仿 ONNX 界面：输入来源（选中 / 全部）、**标签 / 自然语言两种模式**、视觉模型、写入模式、排序、前后缀、下划线、LLM 并发与可取消进度条。标签模式并发调用外接 LLM 并按写入模式直接写回数据集；自然语言模式即原 TAG2NL（另存 `_captioned` 副本或就地写回 `.txt`）。「AI 视觉打标」在五种语言中统一改名为「LLM 打标」，原「TAG2NL 并发」升级为外接 LLM 的全局并发（默认 5，1–100）
- **LLM 打标窗口增强**：「自然语言」改名「标签→自然语言」；新增**内容格式**（标签+自然语言 / 仅自然语言）与「无标注时先用 ONNX 推标」自动流水线；窗口内选**提示词模板** +「打标设置…」；移除工具「对全部图片 LLM 打标」与设置「自动标签器设置」（已并入窗口）
- **翻译设置**：「翻译前优先使用 danbooru-0-zh.csv」开关从「测试」移入 **设置 → 翻译**，并默认开启
- **测试与清理**：新增 `ImageLruCache`、`SecretProtector`、`SafeFile`、`PromptParser` 区域性、视频转换路径语义、LLM 打标下划线后处理与自然语言内容格式单元测试（264/264 通过）；DPAPI 解密失败启动时本地化提示重新输入密钥；清理死代码（含原 TAG2NL 方法与对话框）

See [v1.1.1 release notes](RELEASE_NOTES_v1.1.1.md) for the character tag audit save and unified crop image changes.
