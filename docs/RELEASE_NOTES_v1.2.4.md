<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.4（中文）

针对性修复：多人角色审查结果页角色下拉同步、WD14 错色标签、超长文件名原子保存失败、LLM 大批量打标二次确认；错误标签修复的子级并入改为默认关闭并可逐条勾选；ONNX 推标按置信度排序；文件名以 `_` 结尾时打标不再报错；视频抽帧新增按百分比随机抽取；数据集可按类型排序。

## 多人角色审查

- **「应用并保存」后左上角角色下拉会跟进到下一个角色**：多人模式下中间角色的「应用并保存」本来就会切到下一角色的审查表与参考图，但下拉框仍停在上一角色（例如仍显示角色A）。现 `SwitchResultProfile` 会同步 `comboResultProfile.SelectedIndex`。

## WD14 ONNX 推标

- **修复通道顺序导致的错色标签**：GDI `Format24bppRgb` 内存已是 BGR，预处理器却再做了一次 R↔B，实际喂给模型的是 RGB，容易把发色等标签推错（例如绿发出现 `blue hair`）。现按 CL v1 约定直接按 BGR 打包 NHWC；alpha 仍先合成到白底，不会向模型送 RGBA。
- **按置信度排序，不再按 CSV 字母序**：`selected_tags.csv` 的 general/character 行本身是 A–Z，`SortMode.None` 以前原样写出，看起来像「打标自动按首字母排序」。现 WD14 / PixAI / CL 在返回前按置信度降序（与官方 wd14-tagger 一致）；新用户默认排序为置信度。`Alphabetical` 仍可强制 A–Z。LLM 打标不改模型自己的顺序。

## 超长文件名保存

- **原子写临时名不再接到目标文件名后面**：danbooru 式超长图片名（逗号分隔标签作文件名）本身合法，但 `SafeFile` / 角色审查事务在旁边生成 `原名.{guid}.tmp` 时会把 Windows 单段文件名撑过 255 字符，报「文件名、目录名或卷标语法不正确」。现改为同目录短名 `.bdtm-{guid}.tmp`，主窗口保存与审查「应用并保存」均受益。
- **文件名以 `_` 结尾可以打标/写 caption**：`foo_.png` 会配对 `foo_.txt`；LLM 请求按扩展名给出正确 MIME（含 `.jpeg`），不再把 `foo_.jpeg` 当成 `application/octet-stream` 被接口拒绝。

## LLM 打标

- **超过 200 张时二次确认**：解析输入列表后、开始任务前弹出 Yes/No，提示张数与可能的 API 费用，避免误选整库后误点开始。ONNX 推标本地免费，不加此门闩；CLI 不变。

## 错误标签修复

- **子级并入父级默认关闭**：服装差分（如 `kayoko (dress) (blue archive)`）不再因为数据集出现量 < 30 就自动并入父级。测试模块改为可勾选，勾选后仍用数据集出现量（不是 CSV 的 `post_count`）与默认阈值 30。CLI `--child-threshold` 默认改为 0。
- **预览表可逐条勾选**：默认全选，可全选/全不选，确定后只应用勾中的行。

## 视频抽帧

- **按百分比随机抽帧**：抽帧页新增「随机抽帧」，滑条 1–100%（默认 10%）。**分布抽帧**把视频均分成 N 段、每段随机取一帧；**区域抽帧**随机取一段连续的 N 帧。百分比与模式会记住。指定帧 / 随机抽帧的状态显示「当前/总数」和源帧号，不再跟着 ffmpeg 单帧任务的 `frame=0` / `frame=1` 闪烁。勾选删除源视频后抽帧窗口自动关闭。

## 数据集浏览

- **可按类型排序**：搜索框右侧新增排序按钮。类型按扩展名分组（jpg 与 jpeg 算一类；png、webp、gif 等图片，以及 mp4、webm、mkv 等视频，各自一类），同类内仍按文件名。也可切回文件名或图片/标签修改时间；选择写入设置，下次打开仍在。

## 其他

- 测试套件从 564 增长到 **611**（WD14 通道打包、超长文件名原子写、审查事务长名写入、子级并入默认关、ONNX 置信度排序、`_` 结尾文件名、随机抽帧规划、抽帧进度、按类型排序）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.4 (English)

Targeted fixes: multi-character audit profile dropdown sync, WD14 wrong-color tags, atomic-save failures on very long filenames, a confirmation before large LLM tagging batches, rare-child fold off by default with per-row preview checks, ONNX confidence sort, tagging files whose names end with `_`, random percentage frame extraction, and dataset sort by file type.

## Multi-character tag audit

- **Profile dropdown advances with Apply**: in multi-character review, Apply on a non-final profile already switched the grid and reference preview to the next character, but the top-left dropdown stayed on the previous one (e.g. still showed Character A). `SwitchResultProfile` now syncs `comboResultProfile.SelectedIndex`.

## WD14 ONNX tagging

- **Fixes channel-order wrong-color tags**: GDI `Format24bppRgb` memory is already BGR; the preprocessor swapped R↔B again and fed RGB into a BGR model, scrambling hue tags (e.g. green hair yielding `blue hair`). Packing now follows the CL v1 convention (BGR NHWC as-is). Alpha is still composited onto white; the model is not given RGBA.
- **Sorts by confidence, not CSV alphabetical order**: general/character rows in `selected_tags.csv` are already A–Z, so `SortMode.None` used to write tags in that order. WD14 / PixAI / CL now return confidence-descending order (matching the official wd14-tagger); new installs default the sort combo to Confidence. `Alphabetical` still forces A–Z. LLM tagging keeps the model's own order.

## Long-filename saves

- **Atomic temps use short sibling names**: danbooru-style long basenames (comma-separated tags as the filename) are legal, but `SafeFile` / the character-audit transaction appending `.{guid}.tmp` to that basename pushed the Windows path component past 255 characters (`ERROR_INVALID_NAME`). Temps are now short same-directory names `.bdtm-{guid}.tmp`, covering both main-window Save All and audit Apply.
- **Filenames ending with `_` can be tagged**: `foo_.png` pairs with `foo_.txt`; LLM requests send the correct MIME from the extension (including `.jpeg`) so `foo_.jpeg` is no longer rejected as `application/octet-stream`.

## LLM tagging

- **Confirm when more than 200 images are selected**: after resolving the input list and before starting the job, a Yes/No dialog shows the count and warns about API cost. Local ONNX tagging is unchanged; the CLI is unchanged.

## Tag consistency fixer

- **Rare-child fold is off by default**: costume variants such as `kayoko (dress) (blue archive)` are no longer folded into the parent just because they appear fewer than 30 times. The Test module now has an opt-in checkbox; when checked, dataset occurrence counts (not CSV `post_count`) and the existing threshold of 30 apply. CLI `--child-threshold` now defaults to 0.
- **Preview rows are checkable**: all checked by default, with Select all / Select none; OK applies only the ticked rows.

## Video frame extraction

- **Random percentage sampling**: the extract page now has Random (percentage), a 1–100% slider (default 10%). **Distributed** takes one random frame from each equal slice of the clip; **Regional** takes a random contiguous block of that many frames. Percent and mode are remembered. Specific and random extract status now shows current/total plus the source frame number, instead of flickering through ffmpeg's per-call `frame=0` / `frame=1`. Choosing to delete the source videos closes the extract window.

## Dataset browser

- **Sort by type**: a sort button next to the search box groups by extension (jpg and jpeg together; image types such as png, webp, gif and video types such as mp4, webm, mkv each their own group), then by name within the group. Name and image/tag dates remain available; the choice is saved and restored on the next launch.

## Other

- The test suite grows from 564 to **611** (WD14 channel packing, long-name SafeFile writes, long-name audit transaction commits, rare-child fold default-off, ONNX confidence sort, trailing-`_` filenames, random-frame planning, extract progress, file-type sort).
