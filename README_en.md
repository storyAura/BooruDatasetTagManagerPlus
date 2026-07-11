# BooruDatasetTagManager+ 1.1.2

[简体中文](README.md)

Windows tool for LoRA and character dataset tagging. Keeps the original folder-based `.txt` workflow and adds LLM tagging (Tags / Natural-language modes), character tag audit, and Chinese-localized tooling. **Default UI language is Simplified Chinese (zh-CN).**

![Main window](docs/images/main-window-wiki.png)

## Project lineage

This repository is a fork of **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**. It keeps the original folder-based tag editing workflow and adds LLM tagging (Tags / Natural language), character tag audit, and Chinese-localized tooling.

Licensed under [MIT License](LICENSE). Retain upstream copyright notices when redistributing modified builds.

## Features

| Module | Description |
| --- | --- |
| **LLM Settings** | OpenAI-compatible endpoint; separate text, auto-tag vision, and audit vision models; LLM concurrency |
| **LLM tagging** | Unified run window (ONNX-style): input source, **Tags / Tags→Natural-language modes**, prompt template, vision model, write mode, LLM concurrency. Tags mode writes back to the dataset; Natural-language mode (formerly TAG2NL) offers **Tags+NL / NL-only** content, a `_captioned` copy or in-place `.txt`, and can **run ONNX first** on untagged images |
| **Character tag audit** | Trigger + reference image + dataset inventory; two-stage AI review with transactional save |
| **ONNX tagger** | Unified WD14 + PixAI interface; HuggingFace download; dual thresholds; write modes; prefix/suffix tags |
| **Video tools** | Format conversion; all frames / by FPS / specific frames; bundled FFmpeg |
| **Background removal** | Built-in RMBG-1.4 ONNX, runs locally in the client — **no external service**; downloads the model on first use (~176 MB, or ~44 MB quantized); transparent or solid-color background (white by default), overwrite the original or save a `_nobg.png` copy |
| **Crop image** | Single or multi-region crop; export `_r1`/`_r2` to source folder; auto-import to dataset |
| **Multi-select tag review** | Select multiple images, press Shift+T for the visual editor; left tag list with occurrence counts; green = has tag, red = missing, click to toggle, one save across tags |

## What's New in 1.1.2

A broad security, stability, and usability hardening pass, plus a unified **LLM tagging** window.

- **Unified LLM tagging window** — Tags / Tags→Natural-language (formerly TAG2NL) modes; content format (Tags+NL / NL only), `_captioned` copy or in-place write, ONNX-first on untagged images; prompt template and tagging settings folded into the window; "AI vision tagging" renamed to "LLM tagging" with a global LLM concurrency setting
- **Background removal moved in-process** — built-in RMBG-1.4 ONNX, no external service; transparent / solid background, overwrite or save a `_nobg.png` copy
- **Robustness & data safety** — global crash backstop (`crash.log`); atomic zero-loss tag saving; batch tools never destroy originals; closing mid-job cancels safely; interruption-safe model downloads + integrity check before use
- **Security** — DPAPI-encrypted API keys; AI-server authentication & access control; `BinaryFormatter` removed
- **Usability** — one-click update check in Settings; multi-select tag review (Shift+T) with a left-side tag list; CSV-first translation (on by default)
- Removed the "AutoTagger preview" tab and the standalone TAG2NL menu; 264 unit tests passing

Full details (Added / Improved / Removed / Fixed): **[v1.1.2 release notes](docs/RELEASE_NOTES_v1.1.2.md)**.

## What's New in 1.1.1

Faster character-tag-audit save; unified **Crop image** dialog (multiple regions, same-folder `_r1/_r2` export, auto-import into the dataset).

Full details: **[v1.1.1 release notes](docs/RELEASE_NOTES_v1.1.1.md)**; older releases: [v1.1](docs/RELEASE_NOTES_v1.1.md) (WD14 catalog, PixAI fix) · [v1.0.5](docs/RELEASE_NOTES_v1.0.5.md) (unified ONNX tagger, video tools)

## vs. upstream

- LLM auto-tagging with four built-in prompt templates plus custom import/export
- LLM tagging "Natural language" mode (formerly TAG2NL) for batch captions (LLM concurrency 1–100)
- Character audit wizard (sparse vs. full style, local canonicalizer)
- Read-only source data; atomic writes, cancel support, per-file error isolation

## Workflow

1. **File → Load folder**
2. Edit tags; open **Danbooru Wiki** when needed
3. Configure models in **LLM Settings**
4. Run **Tools → LLM tagging** (Tags / Natural-language modes) or **Test → Character tag audit**

## LLM Settings

![LLM Settings](docs/images/llm-settings.png)

Connection, text model, vision models (auto-tag + character audit), LLM concurrency (shared by tag tagging and captioning; default 5, 1–100), and the fixed Natural-language prompt (read-only, independent of auto-tag templates).

## Auto-tag templates

![Auto-tag prompt templates](docs/images/auto-tag-prompt-templates.png)

Built-in Danbooru Tag, Natural Language, Mixed Mode, Natural Language 2. Custom templates export as JSON without credentials.

## LLM tagging

**Tools → LLM tagging**, right-click a dataset image → **LLM tagging**, or the tag-toolbar button.

![LLM tagging](docs/images/llm-tagger.png)

- Common: input source (selected / all), vision model, a **prompt template** dropdown, LLM concurrency; **Tagging settings…** opens the full prompt/params editor and **LLM settings…** configures the endpoint and models.
- **Tags mode** — image → tags, written back to the dataset per the write mode (replace / append / write-if-empty), with sort, prefix/suffix, and underscore post-processing.
- **Tags → Natural language mode (formerly TAG2NL)** — tags + image → a natural-language paragraph.
  - **Content format** — **Tags + natural language** (default, the original TAG2NL format) or **natural language only**.
  - **Destination** — **Save a copy** (default) to `dataset_captioned/` (source `.txt` read-only, existing skippable) or **write in place** into the image's own `.txt` (through the dataset manager, so memory and disk stay consistent).
  - **ONNX first if untagged** — when enabled, images with no tags are first tagged by the local WD14 ONNX tagger (offering to download the model if needed), then handed to the LLM — a tags → natural-language auto-pipeline.

## Character LoRA tag audit

**Test → Open character tag audit...**

1. **Setup** — trigger, sparse/full style, reference image  
   ![Audit setup](docs/images/character-tag-audit-setup.png)
2. **AI review** — text screening, then visual review (no step back; cancel to restart)
3. **Review & apply** — edit decisions, preview prompt, transactional save  
   ![Audit review](docs/images/character-tag-audit-review.png)

Sparse mode prunes non-core appearance tags locally after visual review; full mode keeps confirmed details.

## ONNX tagger

**Tools → ONNX tagger...** or right-click **Retag with ONNX** on selected images.

![Tools menu](docs/images/tools-menu.png)

![ONNX tagger](docs/images/onnx-tagger.png)

![Context menu ONNX retag](docs/images/context-menu-onnx-retag.png)

- Model picker: full WD14 catalog (12 models) and PixAI 0.9; per-model threshold memory
- Download from HuggingFace official or HF mirror; settings auto-save per model
- Write mode (replace / append / skip existing) and optional tag sort
- Post-processing: replace underscores with spaces (ONNX inference only), prefix/suffix tags
- Progress bar for batch tagging; right-click retag opens the dialog and starts automatically

## Video tools

**Tools → Video format conversion...** / **Frame extraction...**

![Video format conversion](docs/images/video-format-conversion.png)

![Video frame extraction](docs/images/video-frame-extraction.png)

- Convert between mp4, mkv, avi, webm, mov, flv; optional replace-original
- Extract all frames, by FPS, native FPS, or specific frame numbers with preview and lock-frame workflow
- Extracted frames import into the dataset; FFmpeg is bundled in Release builds

## Background removal

**Tools → Remove background**, or right-click a dataset image → **Remove background**.

![Background removal](docs/images/background-removal.png)

- Built-in RMBG-1.4 ONNX runs locally in the client — **no external service**; the model downloads on first use (~176 MB, or ~44 MB quantized; official / mirror source) and auto-loads once cached
- Scope: all images or selected only; background: **transparent** or **solid color** (white by default, with a color picker)
- Output: **overwrite the original** or **save a `_nobg.png` copy** (both remembered); "Removing test" previews a single image first
- Afterwards the grid thumbnails and preview refresh (replace mode) or the copies are imported into the dataset (save-a-copy mode)

## Multi-select tag review

Select multiple images and press **Shift+T**.

![Multi-select tag editor](docs/images/multi-select-tag-editor.png)

- Left tag list with occurrence counts (sorted by frequency); click to switch the reviewed tag
- **Green border = has the tag, red = missing**; click Yes/No on a thumbnail to toggle
- Edits across multiple tags are applied in one Save; right-click a thumbnail to open the preview

## Acknowledgments

- **[starik222](https://github.com/starik222)** — author of [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)
- **[FFmpeg](https://ffmpeg.org/)** — video processing (GPL component bundled in Releases)

## Install

**Recommended:** Download `BooruDatasetTagManagerPlus-*-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe` (self-contained; no separate .NET install required).

Build from source:

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — launch Release (or Debug)
- `quick_build.bat` — quick local build to `dist/` (not committed; downloads FFmpeg on first build)

Running locally creates **Models/** (downloaded ONNX weights), **Cache/** (e.g. video thumbnails), and **settings.json** (API keys and preferences) beside the executable. These are runtime-only and must not be committed; ONNX models are downloaded from inside the app.

Images sent for LLM tagging (including Tags → Natural language) or character audit go to your configured endpoint; background removal and video tools run entirely locally. API settings live in local `settings.json`.
