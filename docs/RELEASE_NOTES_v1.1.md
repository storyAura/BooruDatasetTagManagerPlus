# BooruDatasetTagManager+ v1.1

## Highlights

### ONNX tagger and models
- Full WD14 catalog (12 models) in the model picker, including vit-large v3 and eva02-large v3
- **PixAI v0.9 fix** — selects `prediction` / `logits` output correctly; v0.9 CSV format; WebP via ImageLoader; DirectML failure falls back to CPU
- **Per-model WD14 thresholds** — vit-large default 0.26 no longer inherits eva02-large 0.52 when reopening or switching models
- WD14 large-model inference: DirectML hang recovery via CPU retry; explicit output tensor name; NHWC / NCHW input size detection

### Batch tagging and UI
- Split ONNX inference (background) from tag writes (UI thread); fixes batch `BindingSource` circular-reference errors
- Progress bar shows last / average / ETA / elapsed inference time
- Model download completion dialog; status resets to ready-to-tag
- Post-batch `SaveAll` runs in background; single-image selection uses synchronous tag grid refresh to reduce UI freezes

## Install

Download `BooruDatasetTagManagerPlus-1.1-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe`.

Self-contained build for Windows x64; no separate .NET install required.

Local runs via `test_start.bat` or `quick_build.bat` create **Models/**, **Cache/**, and **settings.json** next to the executable. These are runtime data and are not committed to Git; download ONNX models from inside the app.

## 更新摘要（中文）

- WD14 全系列 12 款模型可选；PixAI v0.9 推理修复（多输出张量、CSV、WebP、DirectML 回退 CPU）
- WD14 按模型独立阈值（vit-large 0.26 不再误用 eva02 的 0.52）；大模型 DirectML 挂起时自动回退 CPU
- 批量推标修复 BindingSource 错误；进度条显示推理耗时与 ETA
- 模型下载完成提示；推标结束后后台保存、单图同步刷新标签列表
- 本地 `Models/`、`Cache/`、`settings.json` 为运行时数据，不入库

See [v1.0.5 release notes](RELEASE_NOTES_v1.0.5.md) for the previous major ONNX / video release.
