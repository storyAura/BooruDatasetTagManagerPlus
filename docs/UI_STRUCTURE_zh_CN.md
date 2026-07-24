# BooruDatasetTagManagerPlus 当前 UI 结构说明

这份文档记录当前 WinForms UI 的主要结构，供后续重构前梳理边界使用。当前项目仍以 `MainForm` 为中心，很多交互直接写在窗体事件中，重构时可以优先拆出 UI action、数据服务和视图模型。

## 主窗口

主窗口类：

```text
BooruDatasetTagManager/Form1.cs
BooruDatasetTagManager/Form1.Designer.cs
```

窗体标题为 `BooruDatasetTagManagerPlus`。

主窗口由 `ToolStripContainer`、`SplitContainer`、`MenuStrip`、`StatusStrip` 与若干运行时构建的控件组成，整体布局是三栏加底部状态栏：

- 左侧：数据集浏览器模块（`DatasetBrowserView`：搜索框 + 可折叠 kohya 文件夹分组 + 缩略图行），下方是可折叠的内嵌预览面板（`DatasetPreviewPanel`）。
- 中间：当前图片 tags。
- 右侧：All/Common tags。
- 顶部：菜单、图片列表工具栏、tag 工具栏、All tags 工具栏。
- 底部：状态栏。

注意：旧的右侧 Preview tab 已移除；预览要么显示在左侧底部的内嵌面板（`PreviewType=PreviewInMainWindow`），要么用独立缩放窗口 `Form_preview`（`SeparateWindow`）。

## 左侧数据集区域

核心控件：

```text
DatasetBrowserView   （可见的浏览器）
DatasetPreviewPanel  （内嵌预览面板）
gridViewDS           （隐藏，但仍是选择与操作的权威数据源）
```

作用：

- `DatasetBrowserView` 展示数据集图片：多文件夹数据集按 kohya 文件夹分组，组头可折叠，点击组头把数据集范围切到该文件夹；单文件夹为平铺列表。支持 Ctrl/Shift/Ctrl+A 多选与搜索过滤（200ms 防抖）。
- **`gridViewDS` 隐藏但保留**：所有既有操作仍读取它的 `SelectedRows`；浏览器把用户选择镜像进网格，网格侧的程序化选择变化再镜像回浏览器。折叠分组会把被隐藏的选择从两侧同步剪掉。
- 当前选择会驱动中间 tag 列表和预览更新。
- 相关操作包括加载文件夹、保存全部、过滤、删除图片和 tags、打开图片所在目录。
- 图片右键菜单：打开所在目录、删除图片和 tags、移除背景、裁剪图片、编辑图片（`Form_ImageEditor`，PS 式左侧工具栏与快捷键）、ONNX 重新推标、LLM 打标、视频处理（仅视频文件）。
- 组头右键菜单：重命名文件夹（磁盘 + 内存原位重映射）、批量重命名图片、对该文件夹跑 ONNX / LLM 打标。
- 隐藏网格的表头右键菜单：切换各列显示/隐藏（列名已全部本地化，选择持久化在 `Settings.DatasetHiddenColumns`）。

主要数据来源：

```text
Program.DataManager
DatasetManager        （ActiveFolder 驱动文件夹范围）
DatasetFolders.cs     （DatasetFolderIndex 路径计算，已链接进测试工程）
```

## 中间当前 tags 区域

核心控件：

```text
gridViewTags
toolStripTags
```

主要列：

- `ImageTags`：tag 文本。
- `Translation`：翻译列，开启翻译模式后显示。

主要操作：

- 添加 tag。
- 删除 tag。
- 撤销/重做。
- 上移/下移 tag。
- 复制/粘贴 tags。
- 设置当前 tag 列表到所有图片。
- 权重调整。
- prompt 排序。
- 工具栏搜索框：按 英文前缀 > 英文子串 > 翻译 > 中文字典（`danbooru-0-zh.csv`，含同义词）定位行，Enter 下一个、Esc 清除。
- 在全部标签中查找。
- 多选标签校对（Shift+T，打开 `Form_TagImagesGrid`）。
- LLM 打标按钮（打开 `Form_LlmTagger`）。
- 当前 tag 右键菜单。

添加 tag 弹窗：

```text
Form_addTag
AutoCompleteTextBox
```

在简体中文界面下，`AutoCompleteTextBox` 会混入 `danbooru-0-zh.csv` 的中文候选，选择后写入英文 tag。

## 右侧 All/Common tags 区域

核心控件：

```text
gridViewAllTags
toolStripAllTags
tabAllTags
```

主要列：

- `TagsColumn`：tag。
- `TranslationColumn`：翻译。
- `CountColumn`：出现次数。

主要操作：

- 切换 All tags / Common tags。
- 添加 tag 到所有图片。
- 从所有图片移除 tag。
- 替换 tag。
- 添加/移除 selected/filtered 图片中的 tag。
- tag 过滤。
- 根据名称或计数排序。
- 工具栏搜索框：匹配规则与图片标签搜索一致（英文前缀 > 子串 > 翻译 > 中文字典）。
- 双击快速操作：默认打开「全部替换」并预选源标签，可在 设置 → 常规 更换为其他工具栏功能。
- 计数重排后选中按标签文本锚定恢复（不再按行号跳位）。
- All tags 右键菜单。

替换 tag 弹窗：

```text
Form_replaceAll
AutoCompleteTextBox
```

源 tag 保持下拉选择；新 tag 使用自动补全输入框。简体中文界面下，可以输入中文查找英文 tag。

## LLM 打标窗口

核心窗体：

```text
Form_LlmTagger
```

作用：

- 统一的外接 LLM 打标入口（工具菜单、数据集右键、tag 工具栏按钮均可打开）。
- 两种模式：**标签**（图片 → 标签，按写入模式直接写回数据集）与 **标签→自然语言**（原 TAG2NL）。
- 自然语言模式支持内容格式（标签+自然语言 / 仅自然语言）、另存 `_captioned` 副本或就地写回 `.txt`、无标注时先用 ONNX 推标。
- 窗口内可直接选提示词模板；「打标设置…」打开 `Form_AutoTaggerOpenAiSettings`（提示词/参数），「LLM 设置…」打开 `Form_AiServerSet`（端点/密钥/模型）。

说明：旧的「AutoTagger 预览窗口」tab（`tabAutoTags` / `gridViewAutoTags`）已从界面移除，打标结果不再经预览中转；legacy Python AiApiServer 后端及其设置窗（`Form_AutoTaggerSettings`）、moondream2 裁剪功能已整体删除，旧配置在启动时自动迁移到 OpenAI 兼容提供方。

## ONNX 推标窗口

核心窗体：

```text
Form_OnnxTagger
```

作用：

- 本地 WD14 / PixAI ONNX 推标（工具菜单或数据集右键「ONNX 重新推标」打开）。
- 模型下载（HuggingFace 官方/镜像）、双阈值、写入模式、排序、前后缀、下划线替换、进度与取消。

## 预览区域

核心控件：

```text
DatasetPreviewPanel  （主窗口内嵌预览，数据集浏览器下方）
Form_preview         （独立缩放/平移预览窗口）
```

作用：

- `PreviewType=PreviewInMainWindow` 时，左侧底部内嵌面板是预览表面：展开状态持久化为 `Settings.DatasetPreviewExpanded`；「视图 → 显示预览」、其热键与面板头部箭头都经 `ToggleEmbeddedPreview` 切换；多选时可并列显示多张缩略图。
- `PreviewType=SeparateWindow` 时使用浮动 `Form_preview`：滚轮以光标为中心缩放、拖拽平移、双击在“适应窗口 ↔ 100%”间切换；跟随开关（isShowPreview）仅运行时有效。坐标映射数学在 `PreviewCanvasMath`（已链接进测试工程）。

## 菜单结构

主要菜单：

- File：加载文件夹、加载文件夹（自定义选项，可关预览图/从图片元数据读标签）、保存全部。
- View：显示预览、翻译 tags、显示 tag count、隐藏面板。
- Options：设置、语言切换。
- Tools：替换透明背景、视频格式转换、视频抽帧、ONNX 推标、背景移除、LLM 打标。
- LLM 设置（AiServerSet）：打开 `Form_AiServerSet`（端点、密钥、文本/视觉模型、LLM 并发）。
- 测试（Test）：打开 `Form_TestModule`（快速替换、角色标签审查入口）。
- Debug：调试入口。

菜单文本通过：

```text
I18n.GetText(key)
Languages/*.txt
```

## 右键 tag 操作

统一入口目前在 `MainForm` 中构建，不复用图片列表右键菜单。

覆盖范围：

- `gridViewTags`
- `gridViewAllTags`

当前动作：

- 查询 Danbooru Wiki。
- 重新翻译 tag。

Wiki 弹窗：

```text
Form_TagWikiPopup
DanbooruWikiClient
DanbooruDTextFormatter
```

后续扩展建议：

- 把右键动作抽为 `TagContextAction` 或独立 registry。
- 把 tag 提取逻辑从 `MainForm` 拆成 helper。
- 让每个动作声明可用条件、显示文本、执行函数。

## 翻译相关 UI

翻译开关在 View 菜单中。

核心类：

```text
TranslationManager
FallbackTranslator
AbstractTranslator
```

UI 行为：

- 开启翻译模式后显示 Translation 列。
- 批量翻译在后台执行，失败的 tag 留空。
- 右键“重新翻译”会强制刷新自动缓存，但不会覆盖手动翻译。
- Wiki 浮窗“翻译 Wiki”只翻译正文。
- 设置 → 翻译 中的「翻译前优先使用 danbooru-0-zh.csv」（默认开启）会在联网翻译前先查本地词表。

## 中文 tag 查找数据流

数据文件：

```text
BooruDatasetTagManager/Data/danbooru-0-zh.csv
```

加载位置：

```text
Program.ChineseTagLookup
ChineseTagLookupService
```

启用条件：

```text
Program.Settings.Language == "zh-CN"
```

输入路径：

- `Form_addTag.tagTextBox`
- `Form_replaceAll` 的新 tag 输入框

行为：

- 补全列表显示中文 alias。
- 候选选中后返回英文 tag。
- 用户直接输入完整中文并确认时，精确命中则转换成英文 tag。
- 未命中时保留原输入。

## 重构建议

优先级建议：

1. 把 `MainForm` 中的命令事件拆成 action/service。
2. 把 DataGridView tag 提取、选择、刷新封装到独立 helper。
3. 把右键 tag 菜单改为 action registry。
4. 把翻译、Wiki、中文 tag lookup 保持为无 UI 服务。
5. 最后再替换视觉布局或引入新的 UI 框架。

这样可以先稳定行为，再重做界面，避免 UI 重构时同时移动业务逻辑。
