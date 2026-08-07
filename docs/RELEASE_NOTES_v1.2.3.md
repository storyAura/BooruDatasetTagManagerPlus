<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.3（中文）

安全 / I/O 加固与标签筛选模式修复：对照内部审计修复密钥落盘、路径穿越、下载竞态、GDI 泄漏等问题；并修正「点 NOT 得 OR」的筛选模式按钮，换标签立即重筛、再点当前模式取消筛选。新增工具「扫描坏图」，修复「替换透明背景」对 WebP 报内存溢出，并新增按当前文件夹 / 全部图片的批量替换。

## 替换透明背景

- **修复 WebP 报「Out of Memory」**：旧实现用 GDI+ 的 `Bitmap.FromFile()` 解码、`Bitmap.Save(path, RawFormat)` 编码，二者都不支持 WebP——解码直接抛 `OutOfMemoryException`（GDI+ 对无法识别的格式就报这个），即使侥幸载入也写不回 WebP。现改用与 ONNX 推标 / 图片编辑器同一条链路：`ImageLoader.GetImageFromFile()`（ImageSharp 解码，顺带按 EXIF 方向摆正）+ `ImageEditorSaveService.Encode()` 按目标扩展名编码，WebP / TIFF / GIF 等格式都能正常读写。
- **新增批量入口（文件夹右键菜单）**：在数据集浏览器的文件夹组头上右键 → **替换透明背景（当前文件夹）** 处理右键点中的文件夹（Ctrl / Shift 多选时为这几个文件夹的并集），**替换透明背景（全部图片）** 处理整个数据集；在置顶的「全部」行上右键时前者不可用。**工具 → 替换透明背景** 仍只处理选中图片。三者共用同一实现，颜色对话框（纯色 / 随机色 / 随机取自颜色列表）行为一致。
- **先扫描再替换**：运行前先过一遍——按扩展名只保留能带 alpha 的格式（PNG / WebP / GIF），再逐张解码检查是否真的存在透明像素（发现第一个非全不透明像素即停）；只有确实带透明底的图片才会被改写。JPG / BMP 等无 alpha 格式、以及有 alpha 通道但整张不透明的 PNG / WebP 都会跳过，不会被无意义地重新编码。扫描过程状态栏显示「正在检查透明背景 (x/y)」，确认框会写明「将替换 N 张（已检查 M 张）」，全部不透明时直接提示并结束。
- 完成后状态栏汇总「已替换 N 张，跳过 M 张」，避免一堆不透明图片时看起来像什么都没发生。
- 视频文件自动跳过；写盘走 `SafeFile` 的「写临时文件 + 原子替换」，中途失败不会毁掉原图；替换后清掉预览缓存并重建缩略图，侧栏预览不再显示旧图。
- 单张图片失败只记录到错误汇总（最多列出 20 条），不会中断整批。

## 扫描坏图

- 入口：**工具 → 扫描坏图…**。在当前数据集（含文件夹范围）内逐张用 ImageSharp 尝试解码，找出缺失 / 空文件 / 无法解码 / 尺寸无效的图片。
- 审查墙与相似图片同款交互：**绿框 = 保留，红框 = 删除**，左键切换；默认全部标红；可一键全标红 / 全标绿；坏图用占位 X 图，不依赖已损坏的缩略图。
- **删除标红图片**走与主界面相同的事务化删除（图片与标签文件一起），完成后自动重新扫描。

## 标签筛选模式

- **不再「点 NOT 得 OR」**：旧的模式按钮点击时先切到下一个模式再筛选，而图标显示的是当前模式——图标是 NOT 时点它，实际按 OR（含该标签）筛选，反选看起来时灵时不灵。现改为下拉菜单明选（交集 AND / 并集 OR / 反选 NOT / 异或 XOR，当前模式打勾），点哪个用哪个；未选标签就点筛选会在状态栏提示，未加载数据集时也不再空引用崩溃。
- **换标签立刻重筛**：数据集筛选已开启时，在「全部标签」中点选其它标签会立即按当前模式重新筛选，无需再点一次筛选按钮。
- **再点当前模式取消**：筛选开启时再次点击当前已选中的模式（例如 NOT 下再点 NOT）会退出数据集筛选。

## 安全 / I/O 加固

- DPAPI 加密失败时中止保存，不再把 API 密钥以明文写入 `settings.json`。
- LLM 打标路径正确释放 WebP / 视频帧 `Image`，避免批量打标 GDI 泄漏。
- 文件夹重命名拒绝 `..` 路径穿越，并校验落在数据集根内。
- Hugging Face 模型下载按路径加锁，避免并发写坏 `.partial`。
- 更新包资产文件名经消毒后再落盘。
- 角色标签审查删除门控改用本地语义分类，模型无法把受保护标签误标为可删。
- Caption 输出路径强制落在输出根内。
- ffmpeg 日志捕获设上限（64 KB）。
- 主题与 TagsDB 缓存改用 `SafeFile` 原子写。

## 其他

- 测试套件从 530 增长到 **564**（新增安全 / I/O 审计回归、坏图扫描与透明底预扫描单元测试）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.3 (English)

A security / I/O hardening patch plus a tag-filter mode fix: internal-audit fixes for secret persistence, path traversal, download races, and GDI leaks; the "click NOT, get OR" filter-mode bug; and live re-filter on tag change / toggle-off by re-clicking the active mode. Also adds **Tools → Scan corrupted images**, fixes the out-of-memory failure when replacing a transparent background on WebP files, and adds current-folder / whole-dataset batch runs.

## Transparent background replacement

- **Fixes "Out of Memory" on WebP**: the old code decoded with the GDI+ `Bitmap.FromFile()` and encoded with `Bitmap.Save(path, RawFormat)`, neither of which supports WebP — decoding threw `OutOfMemoryException` (what GDI+ reports for formats it cannot parse) and even a successful load could not be written back as WebP. It now uses the same path as the ONNX tagger and the image editor: `ImageLoader.GetImageFromFile()` (ImageSharp decode, EXIF orientation applied) plus `ImageEditorSaveService.Encode()` keyed on the target extension, so WebP / TIFF / GIF round-trip correctly.
- **New batch entries (folder context menu)**: right-click a folder group header in the dataset browser → **Replace transparent background (current folder)** processes the folder you clicked (the union of the folders, when several are Ctrl/Shift-selected), and **Replace transparent background (all images)** processes the whole dataset; on the pinned "All" row the folder variant is disabled. **Tools → Replace transparent background** still applies to the selection only. All three share one implementation, so the color dialog (solid / random / random-from-list) behaves identically.
- **Scans before it writes**: a pre-pass keeps only alpha-capable formats (PNG / WebP / GIF) by extension, then decodes each one to check whether it really has transparent pixels (stopping at the first non-opaque pixel). Only files with an actual transparent background are rewritten — alpha-less formats (JPG, BMP, …) and fully opaque PNG / WebP files are skipped instead of being pointlessly re-encoded. The status bar shows "Checking for transparent backgrounds (x/y)", the confirmation states how many will be replaced out of how many were checked, and an all-opaque scope simply reports that and stops.
- On completion the status bar summarizes "N replaced, M skipped", so a folder of opaque images does not look like nothing happened.
- Videos are skipped; writes go through `SafeFile` (temp file + atomic replace), so a mid-write failure cannot destroy the original. After each replacement the preview cache is dropped and the thumbnail rebuilt, so the sidebar no longer shows the old image.
- A failure on one image is collected into an error summary (first 20 listed) instead of aborting the batch.

## Corrupted image scanner

- Entry: **Tools → Scan corrupted images…**. Walks the loaded dataset (honoring folder scope) and tries ImageSharp decode on each file — reports missing / empty / undecodable / invalid-size images.
- Review wall matches the similar-image finder: **green = keep, red = delete**, left-click toggles; defaults to all marked for delete; bulk mark-all keep/delete; broken files use an X placeholder instead of a dataset thumbnail.
- **Delete red-marked images** uses the same transactional delete as the main window (image + caption), then rescans.

## Tag filter mode

- **No longer applies OR when the icon says NOT**: the old mode button cycled to the NEXT mode on click while its icon showed the CURRENT one — clicking the NOT icon actually applied an OR filter (images WITH the tag), so inverse filtering seemed to fail at random. It is now a dropdown listing the four modes (AND / OR / NOT / XOR, active one checked): the mode you pick is the mode applied. Filtering with no tag selected now shows a status hint, and with no dataset loaded it no longer crashes with a null reference.
- **Re-filters immediately when you pick another tag**: with a dataset tag-filter already active, selecting a different All Tags row re-applies the current mode right away — no second click on the filter button.
- **Re-clicking the active mode cancels**: while a filter is on, choosing the already-selected mode again (e.g. NOT while already on NOT) exits the dataset filter.

## Security / I/O hardening

- DPAPI encrypt failure aborts the settings save — API keys are no longer written as plaintext.
- LLM tagging disposes WebP / video-frame `Image` instances (fixes a batch GDI leak).
- Folder rename rejects `..` traversal and keeps paths under the dataset root.
- Hugging Face model downloads are serialized per target path (no concurrent `.partial` corruption).
- Update zip asset filenames are sanitized before download.
- Character tag audit delete gating uses the local semantic classifier so the model cannot mislabel protected tags as deletable.
- Caption output paths are contained under the output root.
- ffmpeg captured logs are capped (64 KB).
- Color scheme and TagsDB cache writes use atomic `SafeFile`.

## Other

- The test suite grows from 530 to **564** (new security / I/O audit regressions, corrupted-image scanner and transparent-background pre-scan unit tests).
