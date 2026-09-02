# BooruDatasetTagManager+ 1.2.6

[简体中文](README.md) | [Português do Brasil](docs/pt-BR/README_pt_BR.md)

A Windows tagger for LoRA and character datasets, forked from **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**.

Each image has a matching `.txt` file for its tags — load a folder and edit them. You can also auto-tag with an LLM or the local **Tag tagger** (formerly ONNX tagger; still WD14 / PixAI / CL), run a character audit, and search tags in Chinese. The UI defaults to Simplified Chinese. [MIT License](LICENSE).

![Main window](docs/images/main-window-dataset-browser.png)

## Changelog

- **1.2.6** (current) — **ONNX tagger renamed Tag tagger** (Tools menu, window title, and right-click; the engine is still local ONNX); tag fixer can skip character-family replacements; YOLO detect aspect can auto-match per image; image-tags Shift/Ctrl multi-select works again; Win10 native-library load failures and large-batch tagging memory spikes are hardened. Most of the rest is UI/workflow polish: grouped Tools menu, standalone quick replace, pre-bucket pad-to-batch and Gradient, tighter window copy and layouts. [Release notes](docs/RELEASE_NOTES_v1.2.6.md)
- **1.2.5** — New: batch crop, multi-crop, YOLO detect, pre-bucket, two-level category filter, classify images into folders by tag; settings now live in Documents. Fixed: ONNX download deleting a locked model, translation hanging. [Release notes](docs/RELEASE_NOTES_v1.2.5.md)
- **1.2.4** — Fixed WD14 wrong-color tags, saves on very long filenames, and the multi-character audit dropdown; ONNX results sort by confidence; random-percentage frame extraction; sort the dataset by file type. [Release notes](docs/RELEASE_NOTES_v1.2.4.md)
- **1.2.3** — Corrupted-image scanner; folder / all-images batch transparent-background fill; fixed tag filter “click NOT, get OR”; key and path hardening. [Release notes](docs/RELEASE_NOTES_v1.2.3.md)
- **1.2.2** — Similar-image finder, multi-character audit (up to 4), category filters, flat view, tag consistency fixer, CLI, F5 reload; fixed “skip existing tags” burning LLM credits. [Release notes](docs/RELEASE_NOTES_v1.2.2.md)
- **1.2.1** — Removed the Python backend; multi-folder browser scope; retry only the failed audit character; faster first load and safety hardening. [Release notes](docs/RELEASE_NOTES_v1.2.1.md)
- **1.2.0** — Folder-group browser with embedded preview; category tints and sort; bundled danbooru character catalog. [Release notes](docs/RELEASE_NOTES_v1.2.0.md)
- **1.1.3** — Image editor, CL-family ONNX, Chinese-dictionary search; failed saves no longer drop edits. [Release notes](docs/RELEASE_NOTES_v1.1.3.md)
- **1.1.2** — Unified LLM tagging window; in-process RMBG background removal; crash backstop and encrypted keys. [Release notes](docs/RELEASE_NOTES_v1.1.2.md)
- **1.1.1** — Faster character-audit save; unified crop-image dialog. [Release notes](docs/RELEASE_NOTES_v1.1.1.md)
- **1.1** — Full WD14 catalog, per-model thresholds, PixAI fix. [Release notes](docs/RELEASE_NOTES_v1.1.md)
- **1.0.5** — Unified ONNX tagger, video tools. [Release notes](docs/RELEASE_NOTES_v1.0.5.md)

## Getting started

Download `BooruDatasetTagManagerPlus-*-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe` (self-contained; no separate .NET install required).

1. **File → Load Folder**; *Load Folder (Custom Options)…* can additionally skip thumbnails (faster for large datasets) or read initial tags from image metadata (handy for fresh generations without `.txt` files yet); *Reload current dataset* (F5) refreshes the loaded folder from disk at any time
2. Edit tags directly: the All Tags and Image Tags search boxes understand the Chinese dictionary (typing 头发 finds long hair, black hair, …); Shift/Ctrl multi-select on Image Tags applies delete, copy, and weight edits to every selected row; double-clicking an All Tags row runs a quick action (opens "Replace all" by default, configurable in Settings); open the Danbooru Wiki for unfamiliar tags
3. Before using any LLM feature, configure your OpenAI-compatible endpoint and models in **LLM Settings**
4. Open **Tools** as needed: **Processing tools** (replace transparent background / video tools / remove background / batch crop / multi-crop / YOLO detect), **Tagging tools** (Tag tagger / LLM tagging / character tag audit / quick replace), **Preprocessing tools** (find similar images / scan corrupted images / classify into folders by tag / pre-bucket); the **Test functions** window still hosts the tag consistency fixer
5. Automation scripts can drive the very same exe from the command line: `BooruDatasetTagManagerPlus.exe help` lists every verb (stats / bulk tag edits / export / fix-tags / onnx-tag / audit)

### Build from source

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — launch Release (or Debug)
- `quick_build.bat` — quick local build to `dist/` (downloads FFmpeg on first build)

Running locally creates **Models/** (downloaded ONNX weights) and **Cache/** beside the executable. **settings.json** (API keys and preferences) lives in `Documents\BooruDatasetTagManagerPlus` so Debug / Release / dist copies share one config; a missing Documents file is copied from beside the exe, and if Documents already has an empty config while the exe-adjacent file still has a recognizable API (endpoint / keys / site profiles), only those API fields are merged in. All are locally generated and safe to delete — settings reset to defaults, and models can be re-downloaded from inside the app.

## Features

| Group | Includes |
| --- | --- |
| **Tagging** | LLM (tags / captions) · **Tag tagger** (local ONNX: WD14 / PixAI / CL) · character audit (up to 4) |
| **Tags** | Chinese search, category tints and L1/L2 filter, consistency fixer, filter images by tag |
| **Images** | Editor, batch crop, multi-crop (incl. YOLO), pre-bucket, background removal / fill |
| **Cleanup** | Folder browser + preview, classify into folders by tag, bucket-by-resolution, similar images, corrupted-file scan, video convert / frames |
| **CLI** | Same exe: stats, bulk edits, export, Tag tagger (`onnx-tag`), audit |

## Feature guide

### Dataset browser & preview

The dataset panel is one unified browser: the search box filters folders and file names together; kohya repeat folders render as collapsible groups (multi-folder datasets open fully collapsed; expand-all / collapse-all, flat-view and sort buttons sit next to the search box; flat view ignores the folder groups and renders the current scope + filter as one list), and clicking a folder header scopes the dataset to it (All Tags counts, bulk operations and the audit wizard follow); image rows show the thumbnail, the name and `format · pixels · size`, with file-manager-style selection (Ctrl / Shift / Ctrl+A / arrows / context menu / Delete).

- **Sort**: the button next to the search box orders by name, type, or modification date. Type groups by extension (jpg / jpeg together; png, webp, mp4, webm, … each their own group — images and videos use the same rule), then by name within the group; the choice is remembered
- **Folder right-click**: rename the folder (disk + in-memory remap, unsaved edits survive); **F2** or **double-click** a group header also renames; batch rename images (prefix + numeric / letters / original name + suffix, live preview, `.txt` follows); **Tag folder with Tag tagger…** / tag the folder with LLM
- **Embedded preview**: collapsible panel under the browser (View → Show preview, state persisted); multi-select tiles the first four images, double-click a cell to open it in the floating viewer; the floating window supports cursor-anchored zoom, drag pan, double-click fit ↔ 100 %, Ctrl+0 / Ctrl+1
- **Tag colors & category sort**: both tag panes tint and group by the **primary** category (hair / clothing / character …); the image-tags toolbar's *Category sort* is a sticky toggle: while checked, every newly selected image is grouped by primary automatically (honoring "don't sort first N rows"), and the state persists; the All Tags category sort is opt-in (off by default)
- **Category filter**: each pane has a two-column menu. Tick a primary on the left to filter that whole group; hover lists its secondaries on the right. Multi-select and search by category name. Stacks with text and count filters
- **General-tag catalog**: ~100 k danbooru general tags in `Data/danbooru_dataset_general.csv` with L1/L2 categories, loaded at startup; `long_hair` and `long hair` both match. Unknown tags land in General
- **Character catalog**: ~330 k danbooru character tags ship in `Data/danbooru_character_tags.csv` (including ~26 k real parent/child relations) for exact character coloring, "name (franchise)" translations and the tag fixer's family grouping; can be disabled in Settings → Translation. The general CSV has no character names — the Character bucket still comes from this table / the Danbooru type column

![Tag category filter](docs/images/tag-category-filter.png)

### Tools menu

**Tools** is one flat dropdown split into three pale-tinted section bars (not clickable; the wash uses the same blend as tag-category row tints, so light and dark themes both stay readable):

- **Processing tools**: replace transparent background, video convert, frame extract, remove background, batch crop, multi-crop, YOLO detect
- **Tagging tools**: Tag tagger, LLM tagging, character tag audit, quick replace
- **Preprocessing tools**: find similar images, scan corrupted images, classify into folders by tag, pre-bucket

The tag consistency fixer still lives in the **Test functions** window; character tag audit can be opened from Tagging tools or from that window.

### LLM tagging

Entry: **Tools → LLM tagging…**, the dataset context menu, or the tag-toolbar "Auto generate tags" button. First configure the OpenAI-compatible endpoint, text/vision models, and the global LLM concurrency (default 5, range 1–100) in **LLM Settings**.

![LLM Settings](docs/images/llm-settings.png)

![LLM tagging](docs/images/llm-tagger.png)

- **Tags mode** — image → tags, written back to the dataset per the write mode (replace / append / skip existing), with sort, prefix/suffix, and underscore post-processing; four built-in prompt templates (Danbooru Tag / Natural language / Hybrid / Natural language 2), custom templates export as JSON without credentials
- **Tags → Natural-language mode** (formerly TAG2NL) — tags + image → a natural-language caption; output format **Tags+NL / NL only**; saves a copy to `dataset_captioned/` by default (source `.txt` read-only, existing skippable) or writes in place into the image's own `.txt`
- **Run Tag tagger first if untagged** — images with no tags go through **Tag tagger** (the same local ONNX models) first, then the LLM — an automatic tags → natural-language pipeline

### Character tag audit

Entry: **Tools → Character tag audit…** (the **Test functions** window still has the same entry). Set the locked trigger word (always kept), the tagging style (**sparse** keeps core features / **full** keeps every correct detail), a minimum occurrence threshold, and a reference image; the AI then runs a text screening followed by a visual review (no step back — cancel and reopen to change parameters); finally review each decision (keep / delete / replace / unsure), preview the resulting character prompt, and **Apply & Save** writes transactionally with rollback on failure.

**Multi-character datasets** (up to 4) are supported: pick the Dual or Multi subject mode and give each character its own trigger word, reference image and gender (empty rows are skipped, so three-character datasets work too); images are attributed by trigger word, then by folder, shared images automatically receive subject-count tags (`2girls`, `multiple girls` and the like), the AI review, per-tag review and apply all run character by character, and a failed character can be retried alone (finished characters keep their results).

![Audit review](docs/images/character-tag-audit-review.png)

### Tag tagger

Formerly **ONNX tagger**. From 1.2.6 the UI says **Tag tagger** (Simplified Chinese: **Tag 推标**) on the **Tools → Tagging** menu, the window title, dataset right-click **Retag with Tag tagger**, and folder right-click **Tag folder with Tag tagger…**. The engine is unchanged: local ONNX (WD14 / PixAI / CL), weights still land in `Models/`, and the CLI verbs stay `onnx-tag` / `onnx-models`.

Entry: **Tools → Tag tagger…**, or right-click **Retag with Tag tagger** on selected images (starts automatically); the folder right-click **Tag folder with Tag tagger…** preselects the *Current folder* source and starts after you confirm the settings.

![Tag tagger](docs/images/onnx-tagger.png)

- Models: full WD14 catalog (12 models) + PixAI 0.9 + CL family (cl_tagger v1.02, cl_tagger_v2 v2.00 / v2.01a 🔒); thresholds and settings remembered per model; download from HuggingFace official or mirror
- After download the app checks the model; a file briefly locked by antivirus/indexer is retried and kept, not treated as corrupt and deleted. Missing native runtime and other environment errors also leave a finished download in place
- cl_tagger_v2 is a **gated repo** whose author license forbids redistribution and bundling — the app does not ship it; a license notice shows before download, and you must request access on HuggingFace and enter your own access token (stored DPAPI-encrypted), or place manually downloaded files into the `Models` folder
- Write mode (replace / append / skip existing), optional sort, underscore→space, prefix/suffix tags; progress bar for batch runs; the "Skipping existing tag lists" mode skips already-tagged images before inference and reports written / skipped counts on completion

### Quick replace

Entry: **Tools → Quick replace…**. Pick the keeper on the left, set the threshold, preview same-suffix low-count tags on the right, and read the short status on the bottom-left before confirming a dataset-wide replace. "Same category" means the last word (`black shoes` → `shoes`); only tags below the threshold are replaced.

### Background removal

Entry: **Tools → Remove background**, or the dataset context menu. Built-in RMBG-1.4 ONNX runs fully locally — **no external service**; one-click model download on first use (~176 MB, or ~44 MB quantized; official / mirror source).

![Background removal](docs/images/background-removal.png)

- Scope: all images or selected only; background: **transparent** or **solid color** (white by default, with a color picker); "Removing test" previews a single image first
- Output: **overwrite the original** or **save a `_nobg.png` copy** (choices remembered); thumbnails refresh or copies import automatically afterwards

### Transparent background replacement

Entry: **Tools → Replace transparent background** (selection); folder group header context menu in the dataset browser → **Replace transparent background (current folder)** / **Replace transparent background (all images)**. Fills transparent areas with a chosen solid color, a random color, or a random pick from a color list you build in the dialog.

- **Scans first**: alpha-capable formats (PNG / WebP / GIF) are picked by extension, then each is decoded to confirm it really has transparent pixels — alpha-less formats (JPG, …) and fully opaque PNG / WebP files are skipped rather than re-encoded
- Decoding goes through ImageSharp (`ImageLoader`) and encoding through `ImageEditorSaveService` keyed on the target extension, so **WebP / TIFF / GIF work** (older builds failed on WebP with "Out of Memory")
- The folder entry processes the folder you right-clicked (the union, when several folders are multi-selected); "all images" covers the whole dataset. The confirmation states how many will be replaced out of how many were checked, and both the scan and the replacement report progress in the status bar; videos are skipped
- Overwrites use a temp file plus atomic replace, so a mid-write failure cannot destroy the original; a single failed image is reported in a summary instead of aborting the batch

### Image editor

Entry: dataset context menu → **Edit image**. Photoshop-style layout: compact tool box on the left, options bar on top, status bar at the bottom.

![Image editor](docs/images/image-editor.png)

- Photoshop-consistent shortcuts: **B** brush, **E** eraser, **I** eyedropper, **C** crop, **H** hand (or hold **Space**), `[`/`]` brush size, **Alt+click** samples a color, cursor-anchored wheel zoom, **Ctrl+0** fit, **Ctrl+1** 100%, **Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y** undo/redo (one stroke = one step, up to 15), **Enter** apply crop, **Ctrl+S** save
- Save **overwrites the original** (atomic write — a failed save cannot corrupt the file) or writes an **`_edit` copy** (caption file cloned and imported into the dataset); the default action is configurable under Settings → UI
- There is also a dataset context menu **Crop image** dialog: draw multiple regions at once, export `_r1/_r2…` to the source folder, auto-import into the dataset
- **Batch crop**: dataset context menu or **Tools → Batch crop**; draw a free rectangle on a reference image (lock 1:1 / 2:3 / 16:9 / … or enter a custom ratio) and apply that same pixel crop to every image of that resolution; overwrite in place or save `_crop` copies (tag files are cloned)

![Multi-region crop](docs/images/crop-image-multi-region.png)

### Multi-crop tool

Entry: dataset context menu, folder-header context menu, or **Tools → Multi-crop…**. You do not have to click an image first: the source can be **selected images**, **current folder**, or **all images**. The left pane previews crop boxes on the first source image (YOLO mode does not run detect here). Downscale large images to training gears, or cut them to a chosen ratio, keeping high-frequency detail where possible. **Originals are never overwritten**; each ticked gear writes a new file, clones the caption, and imports it into the dataset.

- **Scale only**: keep the original aspect and shrink the long edge to each ticked gear
- **Center-crop to ratio**: take the largest centered 1:1 / 2:3 / 16:9 / … rectangle, then downscale
- **Split into tiles**: lay gear-sized windows on the source pixels (last row/column flush to the edge); downscale only when a tile is still larger than the gear
- **Random-position crop**: N crops per image (default 1, max 32); the aspect rectangle is placed uniformly in the remaining slide range, then downscaled
- **YOLO detect crop**: pick a deepghs anime detector from the dropdown — **Person** (v1.1 n/s/m, v1.2 s, v1.3 s; default v1.1 small), **Face** (v1.3 s, v1.4 n/s), **Head** (v1.6 s, v2.0 n/s); MIT, not gated, standard YOLOv8 ONNX. Each box is expanded to the chosen ratio then geared; images with no hit are skipped. You can also import your own YOLOv8 ONNX; download source is shared with **Tag tagger** (HuggingFace / hf-mirror)
- Default gears 512 / 768 / 896 / 1024 / 1280 / 1536, multi-select, plus custom values 64–8192 (snapped down to a multiple of 64); Lanczos downscale with no upscaling; images already smaller than a gear are skipped
- Also **Tools → YOLO detect…**: a separate window draws boxes, lets you keep/drop them, optionally **Open in Tag tagger** for the kept crops, then exports; the same model dropdown, download source and *Download model* button live there. Aspect defaults to **Auto** (nearest preset from each image's width/height: 1:1 / 2:3 / 16:9 / …); you can still lock a ratio

### Pre-bucket

Entry: dataset context menu, folder-header context menu, or **Tools → Pre-bucket…**. Source is the current folder or all images. Set resolution / min·max side / step, then **letterbox** each image with white borders onto that exact size and write it into a `{width}x{height}` folder under the current dataset. Captions are cloned. After writing, the source images and any emptied source folders are removed.

- **Why**: each resolution bucket is batched on its own, so many leftover-heavy buckets can push actual steps well above the theoretical figure. Snapping images onto fewer fixed sizes makes the trainer use the count you chose
- **Bucket settings**: resolution (default 1536×1536), min / max side, and step (usually 64). *Do not upscale (pad only)* is on by default — small images get white borders only
- **Target count**: type a number, or tap 4 / 8 / 12 / 16. 0 keeps every aspect-assigned bucket; N merges neighboring ratios down to N folders
- **Added (配平)**: copies existing images until each bucket is divisible by batch, and the column shows **how many extras** (23 + batch 5 → +2, total 25). Does not pad to batch × Gradient
- **Step estimate**: repeats / batch / Gradient / epochs produce a **theoretical** count and the **actual** count after bucketing. Gradient only affects optimizer steps: batch 2 and Gradient 2 is effective BS 4, but each step still reads 2 images

### Video tools

**Tools → Video format conversion… / Frame extraction…**. Convert between mp4 / mkv / avi / webm / mov / flv (optional replace-original); extract all frames, by FPS, at native FPS, by specific frame numbers, or a random percentage (distributed across the clip or a contiguous regional block; slider default 10%), with preview and a lock-frame workflow; results import into the dataset. FFmpeg is bundled in Release builds.

![Video frame extraction](docs/images/video-frame-extraction.png)

### Multi-select tag review

Select multiple images and press **Shift+T**: a left tag list (with occurrence counts, sorted by frequency) switches the reviewed tag; **green border = has the tag, red = missing** — click Y/N on a thumbnail to toggle; edits across multiple tags apply in one Save.

![Multi-select tag editor](docs/images/multi-select-tag-editor.png)

### Classify into folders by tag

Entry: **Tools → Classify into folders by tag…**. Tick tags, confirm, and images that have **every** selected tag are **moved** into one folder under the dataset root — `.txt` / `.caption` files follow.

- **Rules**: an image must have all ticked tags to move; anything missing one stays put
- **Folder name**: type one, or leave it blank for `Mix`. A name that already exists becomes `Mix_2`, then `Mix_3`
- **Scope**: all images or the current folder. The tag list is searchable; the preview shows how many images will move
- **Rename**: after the folder appears, **F2** or **double-click** a group header (or right-click rename) to turn it into a kohya-style `10_miku`

### Similar image finder

Entry: **Tools → Find similar images…**. Perceptual hashing in the spirit of [czkawka](https://github.com/qarmin/czkawka) (dHash + Hamming distance), computed straight from the in-memory thumbnails — thousands of images finish in seconds; with a folder scoped, only that folder is scanned, and videos are skipped.

- Four similarity levels (very high / high / medium / low); results are grouped; **green frame = keep, red frame = delete** — left-click toggles, right-click opens the full-size original, tooltips show file name and size, and a slider adjusts thumbnail size
- **Keep one per group (largest file)** marks everything else for deletion in one click (czkawka's default heuristic); every mark can still be adjusted by hand
- **Delete red-marked images** uses the same transactional deletion as the main window (image and tag file staged and removed together, restored on failure), then rescans automatically

### Corrupted image scanner

Entry: **Tools → Scan corrupted images…**. Walks the loaded dataset (honoring folder scope) and tries to decode each image — reports damaged, empty, or missing files; videos are skipped.

- Results show on a review wall; **green = keep, red = delete** (defaults to all marked for delete); left-click toggles; tooltips show file name and reason; a slider adjusts thumbnail size
- **Delete red-marked images** uses the same transactional deletion as the main window, then rescans

### Tag consistency fixer

Entry: the **Test functions** menu window, "Tag consistency fixer" group. It scans the current dataset (or the active folder scope), lists every planned change as an "image / remove / keep / reason" preview, and applies only after confirmation — as normal edits (per-image undo works, nothing auto-saves).

- **Subject-count conflicts**: `1boy` next to `2boys` drops the lower count (the highest per gender survives); `solo` on multi-subject images is removed too, while the semantically different `solo focus` is never touched
- **Character parent/child duplicates** (checkbox in the Test module; on by default): when several tags of one character family appear on the same image, dataset-wide counts vote for the survivor; families come from the catalog's real parent relations (`racing miku` ↔ `hatsune miku` renamed variants pair up, while different characters sharing a base name never merge). Uncheck it to leave character names alone; subject-count and `solo` fixes still run
- **Rare-child fold** (available while character-family fix is on; off by default): when enabled, a child variant with fewer dataset occurrences than the threshold (default 30) is not trusted and folds into its nearest trusted ancestor — scattered rare variants consolidate onto the main tag for more focused training. Turning character-family fix off also turns this off. The preview table lets you tick which rows to apply.

### Command line (CLI)

`BooruDatasetTagManagerPlus.exe` itself is a command-line tool: a known first argument runs windowless (redirectable output; exit codes 0/1/2 = ok / error / usage), anything else starts the GUI as usual. `help` shows the full usage:

- **Dataset operations**: `stats`; `list-images` / `list-tags` / `classify-tags` queries (filter by tags, L1/L2 category, count; `--category` accepts `头发` or `Hair`, or `头发/发色` for a secondary); `add-tags` / `remove-tags` / `replace-tag` bulk edits (conditional targeting, `--dry-run`); `export` to JSON
- **`fix-tags`**: the consistency fixer's CLI twin — `--no-character-variants` skips character-family replacements, `--child-threshold` sets the trust threshold (default 0 = off; ignored with `--no-character-variants`), `--catalog` points at a custom relations CSV
- **`onnx-models` / `onnx-tag`**: CLI twin of **Tag tagger** (local ONNX) — list / auto-download models (`--hf-token` for gated repos), thresholds and write modes with GUI-equal semantics, "skip existing" filters before inference. The verb names are unchanged so old scripts keep working
- **`audit`**: the LLM character tag audit — reuses the API configuration and audit prompts saved in the GUI, runs the two-stage review, writes back transactionally; `--report` emits a JSON report, `--dry-run` shows decisions only
- Every write is an atomic replace; the tag format (comma-separated, lowercase, deduplicated) matches the GUI, so CLI and manual edits mix freely

### Data & privacy

- **LLM tagging and the character tag audit send images to your configured endpoint**; **Tag tagger**, background removal, and video tools run entirely on your machine
- **Settings file** `settings.json` (UI preferences + LLM/API, keys DPAPI-encrypted) is written to `Documents\BooruDatasetTagManagerPlus`; Settings → General shows the path. Debug / Release / `dist` copies share that file; a missing Documents file is copied from beside the exe (including `.bak`); if Documents already exists without API config but the exe-adjacent file still has a recognizable endpoint or keys, only those API fields are merged in. The old file is left in place. A different PC still needs keys re-entered
- Tag saves are atomic, batch image tools write to a temp file and only swap it in on success, and deletion is staged so a mid-way failure restores the files. Note: video conversion with "replace original" checked deletes the source video after a successful conversion
- **Debug mode** (Settings → General, off by default) shows a Debug menu and writes runtime info and exceptions to `debug.log` next to the executable (the menu can open it directly) — handy to attach when reporting issues

## Acknowledgments & license

- **[starik222](https://github.com/starik222)** — author of [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), which this project builds on
- **[FFmpeg](https://ffmpeg.org/)** — video processing (GPL component bundled in Releases)
- Licensed under the [MIT License](LICENSE); retain upstream copyright notices when redistributing modified builds
