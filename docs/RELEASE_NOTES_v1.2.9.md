<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.9（中文）

**新功能**

- **替换透明背景不用先选图**：**工具 → 替换透明背景** 打开后直接检测整个数据集，不再提示「请先选择图片」。窗口里新增「范围」下拉框，默认「全部」，也可以选某个子文件夹（括号里是图片数），换范围会重新检测。状态行实时显示「正在检查透明背景（x/y）」，结束后显示「检测到 N 张透明背景图片（已检查 M 张 PNG / WebP）」。检测中或一张都没找到时「确定」不可点。中途关窗会立即停止检测。文件夹右键里的「当前文件夹 / 全部图片」两个入口不变。
- **视频抽帧：按指定数量抽帧**：新增「指定数量」模式，在整段视频里均匀抽取 N 帧（每段取中间一帧，避开片头片尾的淡入淡出）。
- **视频抽帧：预览快捷键**：←/→ 移动 1 帧，Shift+←/→ 5 帧，Ctrl+←/→ 10 帧，PgUp/PgDn 100 帧，空格播放/暂停，L 锁定当前帧。新增 ±5 / ±10 / ±100 步进按钮；点击滑条空白处跳 5 帧。在帧列表或数字框里打字时方向键仍用于编辑。

**修复**

- **带括号的角色 / 作品标签被拆开，并且每次保存多套一层括号**（感谢 [@furjifurji](https://github.com/furjifurji) 的详细报告，[#2](https://github.com/storyAura/BooruDatasetTagManagerPlus/issues/2)）。开启「加载时修复标签」时，`moskva (azur lane)` 读进来会变成 `moskva` 和权重 1.1 的 `azur lane` 两个标签，保存后写成 `moskva, (azur lane)`，再次加载保存又变成 `((azur lane))`，越存越多。原因是解析器照搬 WebUI 的加权正则，在整行上把所有括号都当成加权。现在先按分隔符切成一个个标签，只有整项被括号包住时才算加权：`(tag:1.2)`、`((tag))`、`[tag]`。标签内部的括号原样保留，所以 `moskva (azur lane)`、`olivier (brown dust)` 都是一个权重 1 的标签；`(moskva (azur lane):1.3)` 读成权重 1.3 的 `moskva (azur lane)`。
- **保存时不再给普通括号加反斜杠**。之前所有括号都会写成 `\(`，训练器的分词器会把反斜杠当成多出来的 token。现在权重为 1 的标签按原样保存；只有带权重的标签，或以 `(` / `[` 开头、否则会被读成加权的标签（如 `(o)`）才转义。旧版存下的 `moskva \(azur lane\)` 读入后还原为 `moskva (azur lane)`，下次保存会去掉反斜杠。
- 已经被旧版拆开的标注（如 `moskva, ((azur lane))`）无法自动判断原样，需要手动改回一次（可用 **工具 → 快速替换** 或数据集批量替换）；改回后不会再继续套括号。
- **PixAI Tagger v1.0 选了 GPU 实际却在 CPU 上跑**。v1.0 的 ONNX 导出把 341 个 Reshape 节点标成 `allowzero=1`，DirectML 不接受，于是每次都悄悄退回 CPU。首次加载时会在原文件旁生成一份去掉该标记的图文件（`model.dml.onnx`，约 2.6 MB，与原模型共用 `model.onnx.data`），这些形状都是不含 0 的常量，计算结果不变。同时改回完整图优化：8 GB 显卡上从约 100 秒/张降到约 3.4 秒/张。
- **视频随机抽帧很慢**。之前每抽一帧都要从头解码一次视频（抽 200 帧要几分钟）；现在随机抽帧和指定数量抽帧都在一次解码中写出所有帧（每 500 帧一批，避免命令行过长）。
- ONNX 推标在 debug.log 里记录模型实际跑在 GPU 还是 CPU，以及 DirectML 失败、退回 CPU 的原因，方便排查「GPU 没用上」。

## 其他

- 测试套件从 871 增长到 **892**（带括号标签读写往返不再叠加括号、整项加权识别、转义还原、PixAI v1.0 `allowzero` 修补、均匀抽帧规划、单次解码的 select 过滤器、视频预览快捷键）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.9 (English)

**New**

- **Replace transparent background no longer needs a selection**: **Tools → Replace transparent background** opens and scans the whole dataset instead of asking you to select images first. The dialog gets a Scope list, "All folders" by default, or any single sub-folder (image count in brackets); changing it re-scans. A status line shows "Checking for transparent backgrounds (x/y)" and then "Found N images with a transparent background (M PNG / WebP checked)". OK stays disabled while scanning or when nothing is found. Closing the dialog stops the scan at once. The folder context-menu entries (current folder / all images) are unchanged.
- **Frame extraction: a fixed count**: a new "Fixed count" mode extracts N frames spread evenly over the video (the middle frame of each equal segment, skipping fade-in/out edges).
- **Frame extraction: preview hotkeys**: ←/→ moves 1 frame, Shift+←/→ 5, Ctrl+←/→ 10, PgUp/PgDn 100, Space plays/pauses, L locks the current frame. New ±5 / ±10 / ±100 step buttons; clicking the slider track beside the thumb jumps 5 frames. Arrow keys still edit text while you type in the frame list or number boxes.

**Fixes**

- **Character / series tags with brackets were split apart, and gained a bracket pair on every save** (thanks [@furjifurji](https://github.com/furjifurji) for the detailed report, [#2](https://github.com/storyAura/BooruDatasetTagManagerPlus/issues/2)). With "Fix tags when loading" on, `moskva (azur lane)` loaded as two tags, `moskva` and `azur lane` at weight 1.1, saved as `moskva, (azur lane)`, then `((azur lane))` after the next load and save, and so on. The parser ran WebUI's attention regex over the whole line and treated every bracket as weight syntax. It now splits on the separator first and reads weight only when brackets wrap the whole item: `(tag:1.2)`, `((tag))`, `[tag]`. Brackets inside a tag stay literal, so `moskva (azur lane)` and `olivier (brown dust)` are each one tag at weight 1, and `(moskva (azur lane):1.3)` reads as `moskva (azur lane)` at weight 1.3.
- **Saving no longer escapes plain brackets.** Every bracket used to be written as `\(`, and trainer tokenizers count the backslash as an extra token. Tags at weight 1 are now saved as they are; only weighted tags, or tags that start with `(` / `[` and would otherwise read back as weight (such as `(o)`), are escaped. A `moskva \(azur lane\)` saved by an older version loads as `moskva (azur lane)` and loses the backslashes on the next save.
- Captions an older version already split (such as `moskva, ((azur lane))`) cannot be restored automatically; fix them once by hand (**Tools → Quick replace** or a dataset-wide replace). After that they no longer gain brackets.
- **PixAI Tagger v1.0 ran on CPU even with GPU selected.** The v1.0 ONNX export marks its 341 Reshape nodes `allowzero=1`, which DirectML rejects, so every session quietly fell back to CPU. On first load the app writes a copy of the graph without that flag next to the original (`model.dml.onnx`, about 2.6 MB, sharing `model.onnx.data`); all those shapes are constants without zeros, so results are identical. Full graph optimization is back on as well: about 100 s per image drops to about 3.4 s on an 8 GB card.
- **Random frame extraction was slow.** Each frame used to decode the video from the start (200 frames took minutes); random and fixed-count extraction now write every frame in one decode pass (in batches of 500 to keep the command line short).
- The ONNX taggers now log to debug.log whether the model actually runs on GPU or CPU, and why DirectML failed and fell back to CPU, to make "the GPU isn't used" easy to diagnose.

## Other

- The test suite grows from 871 to **892** (bracket tags round-trip without growing brackets, whole-item weight detection, escape restore, PixAI v1.0 `allowzero` patch, even frame planning, the single-pass select filter, video preview hotkeys).
