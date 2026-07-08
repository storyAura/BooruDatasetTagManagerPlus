# BooruDatasetTagManager+ v1.1.1

## Highlights

### Character tag audit save
- Faster apply-and-save: manifest written once per transaction instead of per file
- In-memory tag updates batched via `ExecuteBulkMutation` to avoid AllTags refresh storms
- Save progress UI (preparing / saving files / updating index)
- Lighter post-save refresh without re-triggering translation

### Unified crop image
- Single menu entry **Crop image** replaces separate single-crop and multi-crop flows
- Draw one or many regions on the canvas; live preview panel with dimensions
- Export to the **same folder** as the source image: `{basename}_r1`, `_r2`, … (original untouched)
- Cropped files are **auto-imported** into the loaded dataset after export

## Install

Download `BooruDatasetTagManagerPlus-1.1.1-win-x64.zip` from [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases), extract, and run `BooruDatasetTagManagerPlus.exe`.

Self-contained build for Windows x64; no separate .NET install required.

Local runs create **Models/**, **Cache/**, and **settings.json** next to the executable. These are runtime-only and are not committed to Git.

## 更新摘要（中文）

- 角色标签审查「应用并保存」加速：减少 manifest 写入、批量更新 AllTags、保存进度提示
- 统一「裁剪图片」：单次与多重裁切合并；导出到原图同目录 `{basename}_r1/_r2`；不覆盖原图
- 裁切完成后自动将新图片导入当前数据集

See [v1.1 release notes](RELEASE_NOTES_v1.1.md) for ONNX tagging and model reliability improvements.
