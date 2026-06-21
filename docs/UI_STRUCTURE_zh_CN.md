# BooruDatasetTagManagerPlus 当前 UI 结构说明

这份文档记录当前 WinForms UI 的主要结构，供后续重构前梳理边界使用。当前项目仍以 `MainForm` 为中心，很多交互直接写在窗体事件中，重构时可以优先拆出 UI action、数据服务和视图模型。

## 主窗口

主窗口类：

```text
BooruDatasetTagManager/Form1.cs
BooruDatasetTagManager/Form1.Designer.cs
```

窗体标题为 `BooruDatasetTagManagerPlus`。

主窗口由 `ToolStripContainer`、`SplitContainer`、`DataGridView`、`MenuStrip`、`StatusStrip` 组成，整体布局是三栏加底部状态栏：

- 左侧：数据集图片列表。
- 中间：当前图片 tags。
- 右侧：All/Common tags、AutoTagger preview、Preview 等 tab。
- 顶部：菜单、图片列表工具栏、tag 工具栏、All tags 工具栏、AutoTagger 工具栏。
- 底部：状态栏。

## 左侧数据集区域

核心控件：

```text
gridViewDS
```

作用：

- 展示数据集图片。
- 支持单选、多选。
- 当前选择会驱动中间 tag 列表和预览图更新。
- 相关操作包括加载文件夹、保存全部、过滤、删除图片和 tags、打开图片所在目录。

主要数据来源：

```text
Program.DataManager
DatasetManager
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
- 上移/下移 tag。
- 复制/粘贴 tags。
- 设置当前 tag 列表到所有图片。
- 权重调整。
- prompt 排序。
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
- All tags 右键菜单。

替换 tag 弹窗：

```text
Form_replaceAll
AutoCompleteTextBox
```

源 tag 保持下拉选择；新 tag 使用自动补全输入框。简体中文界面下，可以输入中文查找英文 tag。

## 右侧 AutoTagger preview 区域

核心控件：

```text
gridViewAutoTags
tabAutoTags
```

作用：

- 调用 AiApiServer 或 OpenAI-compatible endpoint 为当前图片生成 tags。
- 展示候选 tag 与置信度。
- 允许把选中的自动 tag 添加到当前图片 tags。
- 支持 AutoTagger 预览 tag 右键菜单。

相关配置窗口：

```text
Form_AutoTaggerSettings
Form_AutoTaggerOpenAiSettings
```

## Preview 区域

核心控件：

```text
tabPreview
pictureBox / preview controls
```

作用：

- 展示当前图片预览。
- 支持根据设置在主窗口或独立预览窗口显示。

相关窗口：

```text
Form_preview
```

## 菜单结构

主要菜单：

- File：加载文件夹、带附加设置加载、保存全部。
- View：显示预览、翻译 tags、显示 tag count、隐藏面板。
- Options：设置、AutoTagger 设置、语言切换。
- Tools：透明背景替换、批量生成 tags、裁剪、背景移除。
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
- `gridViewAutoTags`

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
