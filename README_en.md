# BooruDatasetTagManager+ 1.2.2

[简体中文](README.md) | [Português do Brasil](docs/pt-BR/README_pt_BR.md)

Windows tool for LoRA and character dataset tagging, forked from **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**. It keeps the original "load a folder → edit the matching `.txt`" workflow and adds LLM tagging (Tags / Natural-language modes), character tag audit, local ONNX tagging, and a Chinese tag workflow. **Default UI language is Simplified Chinese (zh-CN).** Licensed under the [MIT License](LICENSE).

![Main window](docs/images/main-window-dataset-browser.png)

## Changelog

- **1.2.2** (current) — new **Similar image finder**: one click in the Tools menu scans the dataset (or the active folder scope) for duplicate / near-duplicate images with czkawka-style perceptual hashing — results in seconds, reviewed group by group (green frame = keep, red = delete, right-click for the original), with one-click "keep the largest file per group" and transactional deletion; the character tag audit grows from dual to **multi-character (up to 4)**: per-slot trigger word / reference / gender, empty slots are skipped, shared images receive subject-count tags, checkpoints carry over; new **File → Reload current dataset** (F5, configurable hotkey); the ONNX / LLM taggers' "Skipping existing tag lists" write mode now skips before inference and reports counts (no more silently discarded results or wasted LLM credits); every entry point shows a friendly prompt with no dataset loaded (several null-reference crashes fixed); stale tags from the previous dataset no longer linger after switching folders; the image-tags Category sort became a sticky toggle; the dataset search box matches the app's flat style; both tag panes gain a **category filter** dropdown and the dataset browser a **flat view** (ignore folder groups, one flat list); the Test module gains a **tag consistency fixer** (subject-count conflicts / solo on multi-subject images / character parent-child duplicates resolved by dataset counts with a trust threshold; the character catalog upgrades to ~26k real parent relations); the same exe now ships a full **headless CLI** (stats / bulk tag edits / export / fix-tags / ONNX tagging / LLM audit for automation). [Release notes](docs/RELEASE_NOTES_v1.2.2.md)
- **1.2.1** — second audit-fix wave: memory and data-safety hardening across the ONNX / network / image pipelines, faster first load, accessibility and i18n completion; the legacy Python AiApiServer backend is removed (old configs migrate automatically); the dataset browser can scope the root group alone and multi-folder selections scope to their union (All Tags counts follow); the dual-character audit gains checkpoints with retry of only the failed character; the floating preview no longer covers confirmation dialogs (the app looked frozen), and the tag list self-heals from mirror desynchronization (the "List desynchronization detected" error that blocked further tag edits); the LLM settings dialog no longer clips its API-key hint on high-DPI displays; the Debug menu drops its old developer-only test entries and becomes an opt-in debug mode (off by default; enabling it in settings shows the menu and writes debug.log); in-app update prompts now show the release notes in the UI language (independent Chinese and English versions). [Release notes](docs/RELEASE_NOTES_v1.2.1.md)
- **1.2.0** — dataset panel rebuilt as a unified folder-group browser (search, collapse, batch rename, per-folder quick tagging) with an embedded multi-image preview; semantic tag colors and category sort; danbooru character-catalog matching (colors + translated names); many translation, wiki-popup and audit-wizard fixes; audit-driven release and data-safety hardening (rename rollback, HF token confined to huggingface.co, clean-room packaging, LLM save gate, video-replace overwrite guard, fault-tolerant settings startup). [Release notes](docs/RELEASE_NOTES_v1.2.0.md)
- **1.1.3** — file-I/O and data-safety hardening (fixes the 8 risks confirmed by an internal audit: failed saves keep edits, transactional deletion, safe concurrent writes, …); adds the image editor, CL-family ONNX models, Chinese-dictionary tag search, and the All Tags double-click quick action. [Release notes](docs/RELEASE_NOTES_v1.1.3.md)
- **1.1.2** — unified LLM tagging window (Tags / Natural-language modes); in-process background removal (RMBG-1.4); crash backstop, atomic writes, encrypted keys, and other robustness/security hardening. [Release notes](docs/RELEASE_NOTES_v1.1.2.md)
- **1.1.1** — faster character-tag-audit save; unified Crop image dialog. [Release notes](docs/RELEASE_NOTES_v1.1.1.md)
- **1.1** — full WD14 catalog, per-model thresholds, PixAI fix. [Release notes](docs/RELEASE_NOTES_v1.1.md)
- **1.0.5** — unified ONNX tagger, video tools. [Release notes](docs/RELEASE_NOTES_v1.0.5.md)

## Getting started

Download `BooruDatasetTagManagerPlus-*-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe` (self-contained; no separate .NET install required).

1. **File → Load Folder**; *Load Folder (Custom Options)…* can additionally skip thumbnails (faster for large datasets) or read initial tags from image metadata (handy for fresh generations without `.txt` files yet); *Reload current dataset* (F5) refreshes the loaded folder from disk at any time
2. Edit tags directly: the All Tags and Image Tags search boxes understand the Chinese dictionary (typing 头发 finds long hair, black hair, …); double-clicking an All Tags row runs a quick action (opens "Replace all" by default, configurable in Settings); open the Danbooru Wiki for unfamiliar tags
3. Before using any LLM feature, configure your OpenAI-compatible endpoint and models in **LLM Settings**
4. Run **Tools → LLM tagging / ONNX tagger / Remove background / video tools / Find similar images**, or **Test → Open character tag audit** (the character tag audit and the tag consistency fixer both live there), as needed
5. Automation scripts can drive the very same exe from the command line: `BooruDatasetTagManagerPlus.exe help` lists every verb (stats / bulk tag edits / export / fix-tags / onnx-tag / audit)

### Build from source

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — launch Release (or Debug)
- `quick_build.bat` — quick local build to `dist/` (downloads FFmpeg on first build)

Running locally creates **Models/** (downloaded ONNX weights), **Cache/**, and **settings.json** (API keys and preferences) beside the executable. All are locally generated and safe to delete — settings reset to defaults, and models can be re-downloaded from inside the app.

## Features

| Module | Description |
| --- | --- |
| **Dataset browser** | Folder-group browser (search, collapse, rename / batch rename, per-folder quick tagging); flat view (ignore folders, one list); embedded preview (multi-select tiles); inline format·pixels·size |
| **Tag semantics** | 18-category light tints, category sort and category filter dropdowns; built-in danbooru character catalog (exact matching + "name (franchise)" translations + parent relations) |
| **LLM tagging** | Tags / Tags→Natural-language modes; OpenAI-compatible endpoint; prompt templates; LLM concurrency 1–100 |
| **Character tag audit** | Trigger word + reference image + dataset inventory; two-stage AI review; single / multi-character (up to 4); transactional save |
| **ONNX tagger** | Local WD14 catalog + PixAI + CL family; per-model threshold memory; HuggingFace download |
| **Background removal** | Built-in RMBG-1.4 ONNX, fully local — no external service; transparent or solid background |
| **Image editor** | Brush / eraser / eyedropper / crop / rotate & flip with Photoshop-style shortcuts; separate multi-region crop dialog |
| **Similar image finder** | czkawka-style perceptual-hash duplicate search (4 similarity levels); grouped keep/delete review; keep one per group; transactional deletion |
| **Tag consistency fixer** | Subject-count conflicts / solo on multi-subject images / character parent-child duplicates cleaned in one pass; child trust threshold (default 30); preview + undo |
| **Video tools** | Format conversion; all frames / by FPS / specific frames extraction; bundled FFmpeg |
| **Tag editing** | Chinese-dictionary search, All Tags double-click quick action, multi-select review (Shift+T), Danbooru Wiki |
| **Headless CLI** | The same exe, windowless: stats / bulk tag edits / export / fix-tags / ONNX tagging / LLM audit for automation |

## Feature guide

### Dataset browser & preview

The dataset panel is one unified browser: the search box filters folders and file names together; kohya repeat folders render as collapsible groups (multi-folder datasets open fully collapsed; expand-all / collapse-all and flat-view buttons sit next to the search box — flat view ignores the folder groups and renders the current scope + filter as one list, state persisted), and clicking a folder header scopes the dataset to it (All Tags counts, bulk operations and the audit wizard follow); image rows show the thumbnail, the name and `format · pixels · size`, with file-manager-style selection (Ctrl / Shift / Ctrl+A / arrows / context menu / Delete).

- **Folder right-click**: rename the folder (disk + in-memory remap, unsaved edits survive); batch rename images (prefix + numeric / letters / original name + suffix, live preview, `.txt` follows); tag the folder with ONNX / LLM
- **Embedded preview**: collapsible panel under the browser (View → Show preview, state persisted); multi-select tiles the first four images, double-click a cell to open it in the floating viewer; the floating window supports cursor-anchored zoom, drag pan, double-click fit ↔ 100 %, Ctrl+0 / Ctrl+1
- **Tag colors & category sort**: both tag panes tint rows across 18 semantic categories (character / copyright / hair / eyes / clothing …); the image-tags toolbar's *Category sort* is a sticky toggle: while checked, every newly selected image is grouped by category automatically (honoring "don't sort first N rows"), and the state persists; the All Tags category sort is opt-in (off by default); both toolbars also carry a **category filter** dropdown: pick one semantic category (hair / clothing / …) to show only its tags — stacking with search and count filters — and "All categories" restores everything
- **Character catalog**: ~330 k danbooru character tags ship in `Data/danbooru_character_tags.csv` (including ~26 k real parent/child relations) for exact character coloring, "name (franchise)" translations and the tag fixer's family grouping; can be disabled in Settings → Translation

### LLM tagging

Entry: **Tools → LLM tagging…**, the dataset context menu, or the tag-toolbar "Auto generate tags" button. First configure the OpenAI-compatible endpoint, text/vision models, and the global LLM concurrency (default 5, range 1–100) in **LLM Settings**.

![LLM Settings](docs/images/llm-settings.png)

![LLM tagging](docs/images/llm-tagger.png)

- **Tags mode** — image → tags, written back to the dataset per the write mode (replace / append / skip existing), with sort, prefix/suffix, and underscore post-processing; four built-in prompt templates (Danbooru Tag / Natural language / Hybrid / Natural language 2), custom templates export as JSON without credentials
- **Tags → Natural-language mode** (formerly TAG2NL) — tags + image → a natural-language caption; output format **Tags+NL / NL only**; saves a copy to `dataset_captioned/` by default (source `.txt` read-only, existing skippable) or writes in place into the image's own `.txt`
- **ONNX first if untagged** — images with no tags are first tagged by the local ONNX tagger, then handed to the LLM — an automatic tags → natural-language pipeline

### Character tag audit

Entry: **Test → Open character tag audit…**. Set the locked trigger word (always kept), the tagging style (**sparse** keeps core features / **full** keeps every correct detail), a minimum occurrence threshold, and a reference image; the AI then runs a text screening followed by a visual review (no step back — cancel and reopen to change parameters); finally review each decision (keep / delete / replace / unsure), preview the resulting character prompt, and **Apply & Save** writes transactionally with rollback on failure.

**Multi-character datasets** (up to 4) are supported: pick the Dual or Multi subject mode and give each character its own trigger word, reference image and gender (empty rows are skipped, so three-character datasets work too); images are attributed by trigger word, then by folder, shared images automatically receive subject-count tags (`2girls`, `multiple girls` and the like), the AI review, per-tag review and apply all run character by character, and a failed character can be retried alone (finished characters keep their results).

![Audit review](docs/images/character-tag-audit-review.png)

### ONNX tagger

Entry: **Tools → ONNX tagger…**, or right-click **Retag with ONNX** on selected images (starts automatically); the folder right-click **Tag folder with ONNX…** preselects the *Current folder* source and starts after you confirm the settings.

![ONNX tagger](docs/images/onnx-tagger.png)

- Models: full WD14 catalog (12 models) + PixAI 0.9 + CL family (cl_tagger v1.02, cl_tagger_v2 v2.00 / v2.01a 🔒); thresholds and settings remembered per model; download from HuggingFace official or mirror
- cl_tagger_v2 is a **gated repo** whose author license forbids redistribution and bundling — the app does not ship it; a license notice shows before download, and you must request access on HuggingFace and enter your own access token (stored DPAPI-encrypted), or place manually downloaded files into the `Models` folder
- Write mode (replace / append / skip existing), optional sort, underscore→space, prefix/suffix tags; progress bar for batch runs; the "Skipping existing tag lists" mode skips already-tagged images before inference and reports written / skipped counts on completion

### Background removal

Entry: **Tools → Remove background**, or the dataset context menu. Built-in RMBG-1.4 ONNX runs fully locally — **no external service**; one-click model download on first use (~176 MB, or ~44 MB quantized; official / mirror source).

![Background removal](docs/images/background-removal.png)

- Scope: all images or selected only; background: **transparent** or **solid color** (white by default, with a color picker); "Removing test" previews a single image first
- Output: **overwrite the original** or **save a `_nobg.png` copy** (choices remembered); thumbnails refresh or copies import automatically afterwards

### Image editor

Entry: dataset context menu → **Edit image**. Photoshop-style layout: compact tool box on the left, options bar on top, status bar at the bottom.

![Image editor](docs/images/image-editor.png)

- Photoshop-consistent shortcuts: **B** brush, **E** eraser, **I** eyedropper, **C** crop, **H** hand (or hold **Space**), `[`/`]` brush size, **Alt+click** samples a color, cursor-anchored wheel zoom, **Ctrl+0** fit, **Ctrl+1** 100%, **Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y** undo/redo (one stroke = one step, up to 15), **Enter** apply crop, **Ctrl+S** save
- Save **overwrites the original** (atomic write — a failed save cannot corrupt the file) or writes an **`_edit` copy** (caption file cloned and imported into the dataset); the default action is configurable under Settings → UI
- There is also a dataset context menu **Crop image** dialog: draw multiple regions at once, export `_r1/_r2…` to the source folder, auto-import into the dataset

![Multi-region crop](docs/images/crop-image-multi-region.png)

### Video tools

**Tools → Video format conversion… / Frame extraction…**. Convert between mp4 / mkv / avi / webm / mov / flv (optional replace-original); extract all frames, by FPS, at native FPS, or by specific frame numbers, with preview and a lock-frame workflow; results import into the dataset. FFmpeg is bundled in Release builds.

![Video frame extraction](docs/images/video-frame-extraction.png)

### Multi-select tag review

Select multiple images and press **Shift+T**: a left tag list (with occurrence counts, sorted by frequency) switches the reviewed tag; **green border = has the tag, red = missing** — click Y/N on a thumbnail to toggle; edits across multiple tags apply in one Save.

![Multi-select tag editor](docs/images/multi-select-tag-editor.png)

### Similar image finder

Entry: **Tools → Find similar images…**. Perceptual hashing in the spirit of [czkawka](https://github.com/qarmin/czkawka) (dHash + Hamming distance), computed straight from the in-memory thumbnails — thousands of images finish in seconds; with a folder scoped, only that folder is scanned, and videos are skipped.

- Four similarity levels (very high / high / medium / low); results are grouped; **green frame = keep, red frame = delete** — left-click toggles, right-click opens the full-size original, tooltips show file name and size, and a slider adjusts thumbnail size
- **Keep one per group (largest file)** marks everything else for deletion in one click (czkawka's default heuristic); every mark can still be adjusted by hand
- **Delete red-marked images** uses the same transactional deletion as the main window (image and tag file staged and removed together, restored on failure), then rescans automatically

### Tag consistency fixer

Entry: the **Test** menu window (the same one hosting the character tag audit), "Tag consistency fixer" group. It scans the current dataset (or the active folder scope), lists every planned change as an "image / remove / keep / reason" preview, and applies only after confirmation — as normal edits (per-image undo works, nothing auto-saves).

- **Subject-count conflicts**: `1boy` next to `2boys` drops the lower count (the highest per gender survives); `solo` on multi-subject images is removed too, while the semantically different `solo focus` is never touched
- **Character parent/child duplicates**: when several tags of one character family appear on the same image, dataset-wide counts vote for the survivor; families come from the catalog's real parent relations (`racing miku` ↔ `hatsune miku` renamed variants pair up, while different characters sharing a base name never merge)
- **Child trust threshold** (next to the run button; default 30, 0 disables): a child variant with fewer dataset occurrences than the threshold is not trusted and folds into its nearest trusted ancestor — scattered rare variants consolidate onto the main tag for more focused training

### Command line (CLI)

`BooruDatasetTagManagerPlus.exe` itself is a command-line tool: a known first argument runs windowless (redirectable output; exit codes 0/1/2 = ok / error / usage), anything else starts the GUI as usual. `help` shows the full usage:

- **Dataset operations**: `stats`; `list-images` / `list-tags` / `classify-tags` queries (filter by tags, semantic category, count); `add-tags` / `remove-tags` / `replace-tag` bulk edits (conditional targeting, `--dry-run`); `export` to JSON
- **`fix-tags`**: the consistency fixer's CLI twin — `--child-threshold` sets the trust threshold, `--catalog` points at a custom relations CSV
- **`onnx-models` / `onnx-tag`**: local ONNX tagging — list / auto-download models (`--hf-token` for gated repos), thresholds and write modes with GUI-equal semantics, "skip existing" filters before inference
- **`audit`**: the LLM character tag audit — reuses the API configuration and audit prompts saved in the GUI, runs the two-stage review, writes back transactionally; `--report` emits a JSON report, `--dry-run` shows decisions only
- Every write is an atomic replace; the tag format (comma-separated, lowercase, deduplicated) matches the GUI, so CLI and manual edits mix freely

### Data & privacy

- **LLM tagging and the character tag audit send images to your configured endpoint**; ONNX tagging, background removal, and video tools run entirely on your machine
- Settings (including DPAPI-encrypted API keys) live in the local `settings.json`; tag saves are atomic, batch image tools write to a temp file and only swap it in on success, and deletion is staged so a mid-way failure restores the files. Note: video conversion with "replace original" checked deletes the source video after a successful conversion
- **Debug mode** (Settings → General, off by default) shows a Debug menu and writes runtime info and exceptions to `debug.log` next to the executable (the menu can open it directly) — handy to attach when reporting issues

## Acknowledgments & license

- **[starik222](https://github.com/starik222)** — author of [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), which this project builds on
- **[FFmpeg](https://ffmpeg.org/)** — video processing (GPL component bundled in Releases)
- Licensed under the [MIT License](LICENSE); retain upstream copyright notices when redistributing modified builds
