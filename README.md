# BooruDatasetTagManager+ 1.0.5

[简体中文](README_zh_CN.md)

Windows tool for LoRA and character dataset tagging. Keeps the original folder-based `.txt` workflow and adds LLM vision tagging, TAG2NL captions, character tag audit, and Chinese-localized tooling. **Default UI language is Simplified Chinese (zh-CN).**

![Main window](docs/images/main-window-wiki.png)

## Project lineage

This repository is a fork of **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**. It keeps the original folder-based tag editing workflow and adds LLM tagging, TAG2NL, character tag audit, and Chinese-localized tooling.

Licensed under [MIT License](LICENSE). Retain upstream copyright notices when redistributing modified builds.

## Features

| Module | Description |
| --- | --- |
| **LLM Settings** | OpenAI-compatible endpoint; separate text, auto-tag vision, and audit vision models; TAG2NL concurrency |
| **LLM tagging** | Batch vision tagging for all images |
| **AI vision tagging** | Single or selected images with prompt templates |
| **TAG2NL** | Tags + image → natural-language caption in sibling `_captioned` folder |
| **Character tag audit** | Trigger + reference image + dataset inventory; two-stage AI review with transactional save |
| **ONNX tagger** | Unified WD14 + PixAI interface; HuggingFace download; dual thresholds; write modes; prefix/suffix tags |
| **Video tools** | Format conversion; all frames / by FPS / specific frames; bundled FFmpeg |
| **Background removal** | Tools menu and dataset context menu; requires local AiApiServer + rmbg2 model |

## What's New in 1.0.5

- **Unified ONNX tagger** — WD14 (eva02-large v3) and PixAI 0.9 in one dialog; HuggingFace / mirror download; separate general and character thresholds
- **Inference fixes** — WebP loading via ImageLoader; v3 `selected_tags.csv` parsing; preprocessing aligned with official wd-tagger
- **Tag post-processing** — Optional underscore→space (ONNX only); prefix/suffix tags on all write paths
- **Context-menu ONNX retag** — Opens the ONNX dialog with progress bar and auto-starts on selected images
- **Video tools** — Format conversion and frame extraction with preview, locked frames, and native FPS; FFmpeg bundled in Releases
- **UI cleanup** — Removed file-browse input sources and obsolete hint text from ONNX settings

## vs. upstream

- LLM auto-tagging with four built-in prompt templates plus custom import/export
- Native TAG2NL batch captions (concurrency 1–100)
- Character audit wizard (sparse vs. full style, local canonicalizer)
- Read-only source data; atomic writes, cancel support, per-file error isolation

## Workflow

1. **File → Load folder**
2. Edit tags; open **Danbooru Wiki** when needed
3. Configure models in **LLM Settings**
4. Run **LLM tagging**, **TAG2NL**, or **Test → Character tag audit**

## LLM Settings

![LLM Settings](docs/images/llm-settings.png)

Connection, text model, vision models (auto-tag + character audit), TAG2NL concurrency, and fixed TAG2NL prompt (read-only, independent of auto-tag templates).

## Auto-tag templates

![Auto-tag prompt templates](docs/images/auto-tag-prompt-templates.png)

Built-in Danbooru Tag, Natural Language, Mixed Mode, Natural Language 2. Custom templates export as JSON without credentials.

## TAG2NL

**Tools → TAG2NL** — writes to `dataset_captioned/`; source tags stay read-only. Format: original tags, one newline, natural-language paragraph.

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

- Model picker: WD14 eva02-large v3, PixAI 0.9, and catalog entries
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

Images sent for tagging, TAG2NL, or audit go to your configured endpoint. API settings live in local `settings.json`.
