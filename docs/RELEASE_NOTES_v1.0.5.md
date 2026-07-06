# BooruDatasetTagManager+ v1.0.5

## Highlights

### ONNX tagger
- Unified dialog for WD14 (eva02-large v3) and PixAI 0.9
- HuggingFace official / HF mirror model download
- Separate general and character thresholds; write modes and optional tag sort
- Tag post-processing: underscore→space (ONNX only), prefix/suffix tags
- Right-click **Retag with ONNX** opens the dialog with progress bar and auto-starts

### Inference fixes
- WebP images load through `ImageLoader` instead of GDI+ `Bitmap`
- WD14 v3 `selected_tags.csv` format parsing fixed
- Preprocessing aligned with official SmilingWolf wd-tagger (padding, bicubic resize, dual thresholds)

### Video tools
- **Video format conversion** — mp4, mkv, avi, webm, mov, flv; optional replace original
- **Frame extraction** — all frames, by FPS, native FPS, or specific frames with preview and lock-frame workflow
- FFmpeg bundled in Release builds (`scripts/fetch_ffmpeg.ps1` on first local build)

### UI cleanup
- Removed file-browse input sources from ONNX and video tools
- Removed obsolete comma-separated hint from ONNX settings

## Install

Download `BooruDatasetTagManagerPlus-1.0.5-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe`.

Self-contained build for Windows x64; no separate .NET install required.

## 更新摘要（中文）

- 统一 ONNX 推标（WD14 eva02-large v3 + PixAI 0.9），HuggingFace / 镜像下载，双阈值与写入模式
- 修复 WebP 推理崩溃、v3 标签 CSV 解析、官方预处理对齐
- 标签后处理：下划线→空格（仅 ONNX）、前缀/后缀标签
- 右键 ONNX 重新推标显示进度条并自动开始
- 视频格式转换与视频抽帧（预览、锁定帧、原生 FPS）；Release 内置 FFmpeg
- 界面精简：移除文件浏览输入源与无用提示
