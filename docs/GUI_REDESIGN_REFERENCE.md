# BooruDatasetTagManager+ GUI 重设计参考

> 版本基准：v1.2.3 · 面向「全部功能模块 / 全部接口 / 全部功能实现」的完整清单，供创作新 GUI 时对照行为与信息架构，而不是照搬 WinForms 控件布局。  
> 相关旧文档：[`UI_STRUCTURE_zh_CN.md`](UI_STRUCTURE_zh_CN.md)（主窗口结构摘要，内容已部分过时，以本文为准）。

---

## 0. 产品一句话与核心循环

**面向 LoRA / 角色图像数据集的 Windows 标签管理工具**：加载文件夹 → 编辑每张图同名 `.txt`（或 `.caption`）标签 → 可选本地 ONNX / 云端 LLM 打标 → 审查与清理 → 保存。

```text
加载数据集
  → 浏览 / 筛选图片（文件夹范围 + 标签筛选）
  → 编辑单图 / 多图标签（撤销、批量、语义分类）
  → 工具流水线（ONNX / LLM / 抠图 / 视频 / 相似图 / 坏图 / 审查）
  → 保存写回 caption 文件
```

同一 `BooruDatasetTagManagerPlus.exe` 兼作 **GUI** 与 **无窗口 CLI**（已知动词走 CLI，未知/空参数走 GUI）。

---

## 1. 信息架构总览（新 GUI 应保留的功能域）

| 域 | 用户目标 | 当前入口 |
| --- | --- | --- |
| A. 数据集 | 加载、浏览、范围、重命名、删除、预览 | 文件菜单 + 左栏浏览器 |
| B. 单图标签 | 增删改排、撤销、权重、搜索、类别 | 中栏 `gridViewTags` |
| C. 全库标签 | 计数、批量改标、筛选数据集、双击快操作 | 右栏 `gridViewAllTags` |
| D. 自动打标 | 本地 ONNX / 云端 LLM / 自然语言 caption | 工具菜单 / 右键 / CLI |
| E. 角色审查 | 触发词锁定、少标/全标、多角色、复核应用 | 测试模块 → 向导 / CLI `audit` |
| F. 质量清理 | 错误标签修复、相似图、坏图、透明底替换 | 测试 / 工具菜单 |
| G. 图像处理 | 编辑器、多重裁剪、背景移除 | 数据集右键 / 工具 |
| H. 视频 | 格式转换、抽帧 | 工具菜单 / 视频右键 |
| I. 配置 | 应用设置、LLM 档案、热键、语言、主题 | 设置 / LLM 设置 |
| J. 自动化 | CLI 全套动词 | exe 首参 |

---

## 2. 主窗口布局契约（行为层，非像素层）

当前实现：三栏 + 顶栏菜单/工具条 + 底栏状态。新 GUI 可重组视觉，但下列 **状态与数据流** 应等价。

### 2.1 三栏职责

| 区域 | 权威数据 | 必须暴露的能力 |
| --- | --- | --- |
| **数据集** | `DatasetManager` + 隐藏 `gridViewDS`（选择权威）+ 可见 `DatasetBrowserView` | 缩略图列表、文件夹分组/平铺、搜索、多选、文件夹范围、内嵌/独立预览、缩放行高 |
| **图片标签** | 单选：`EditableTagList`；多选：`MultiSelectDataTable` | 增删改排、撤销重做、复制粘贴、权重、搜索、类别筛选/排序、「应用到全部」、多选校对入口 |
| **全部标签** | `AllTagsList`（全局计数） | All/Common 切换、按名/次数排序、文本筛选、类别筛选、AND/OR/NOT/XOR 数据集筛选、批量增删替 |

### 2.2 选择镜像规则（重设计陷阱）

- 可见浏览器与隐藏网格必须 **双向同步选中行**。
- 任何锁定 UI 的长任务须同时禁用浏览器（现 `LockEdit`）。
- 重绑数据集数据源后必须刷新浏览器（现 `ApplyDataSetGridStyle` + `RefreshDatasetFolderList`），否则筛选结果看起来「无效」。
- 数据集网格无选中时，必须清空图片标签绑定（否则会显示上一数据集已释放的标签）。

### 2.3 文件夹范围（Scope）

- 多文件夹数据集默认按 kohya 重复文件夹分组。
- 点击组头 → `SetActiveFolder`：全部标签计数、批量操作、审查向导、相似图/坏图扫描均跟随范围。
- Ctrl/Shift 点组头 → `SetActiveFolders`（并集）；根目录哨兵键 `"."`（`DatasetFolderIndex.RootFolderKey`）。
- 平铺视图（`DatasetBrowserFlatView`）：忽略分组，展开为当前范围 + 标签筛选后的扁平列表；进入平铺时若有文件夹范围则扩回「全部」。

### 2.4 预览

| 模式 (`ImagePreviewType`) | 表面 |
| --- | --- |
| `PreviewInMainWindow` | 数据集下方可折叠 `DatasetPreviewPanel`（展开状态持久化） |
| `SeparateWindow` | 浮动 `Form_preview`（owned、非 TopMost；滚轮缩放、拖拽平移、双击适应↔100%） |

`IsPreviewFollowActive` 是所有选择/刷新路径上的唯一门控。多选内嵌预览并排最多约 4 张。

### 2.5 状态栏

- 通用状态文案（复制成功、保存中、翻译进度、筛选提示等）。
- 显示计数：`Showing {shown}/{total}`。

---

## 3. 全部窗体 / 对话框清单

### 3.1 主窗口与配置

| 窗体 | 用途 | 打开方式 |
| --- | --- | --- |
| `MainForm` (`Form1`) | 应用枢纽 | 启动（非 CLI） |
| `Form_settings` | 应用设置（常规/界面/翻译/热键） | 设置 → 应用设置 |
| `Form_AiServerSet` | LLM 站点档案（端点、多 Key、模型、并发、测速） | 顶栏「LLM 设置」 |
| `Form_AutoTaggerOpenAiSettings` | LLM 打标高级参数（提示词、温度、写入模式等） | LLM 打标窗「打标设置…」 |
| `Form_LoadingSettings` | 加载选项（预览、元数据、预览尺寸） | 文件 → 加载文件夹（自定义选项） |
| `Form_UpdateInfo` | 更新说明 | 设置内检查更新 |
| `Form_GatedModelNotice` | 受限 ONNX 模型许可 + HF Token | ONNX 下载 gated 模型时 |
| `Form_TestModule` | 测试入口：快速替换 / 角色审查 / 错误标签修复 | 顶栏「测试」 |

### 3.2 标签编辑辅助

| 窗体 | 用途 | 打开方式 |
| --- | --- | --- |
| `Form_addTag` | 添加标签（位置、跳过已有） | 图片标签「添加」 |
| `Form_replaceAll` | 全库替换标签 | 「替换」/ 全部标签双击（默认） |
| `Form_filter` | 全部标签文本过滤 | 「过滤」按钮 |
| `Form_TagImagesGrid` | 多选可视化校对墙（绿有/红无） | Shift+T / 按钮 |
| `Form_TagWikiPopup` | Danbooru Wiki | 标签右键 |

### 3.3 打标 / Caption

| 窗体 | 用途 | 打开方式 |
| --- | --- | --- |
| `Form_OnnxTagger` | 本地 ONNX 推标 | 工具 / 右键 / 文件夹右键 |
| `Form_LlmTagger` | LLM 标签 / 标签→自然语言 | 工具 / 右键 / 文件夹右键 |

### 3.4 角色审查

| 窗体 | 用途 | 打开方式 |
| --- | --- | --- |
| `Form_CharacterTagAuditWizard` | 三步向导：设置 → AI 进度 → 复核应用 | 测试模块 |

### 3.5 图像 / 视频工具

| 窗体 | 用途 | 打开方式 |
| --- | --- | --- |
| `Form_ImageEditor` | 画笔/橡皮/裁切/旋转/翻转/吸管 | 数据集右键「编辑图片」 |
| `Form_ImageEditorSavePrompt` | Ask 模式下的保存选择 | 编辑器保存 |
| `Form_ImageCrop` | 多选区裁剪导出 `_r1/_r2…` | 数据集右键「裁剪图片」 |
| `Form_manualCrop` | 旧单区裁剪（遗留） | 旧入口（若仍挂接） |
| `Form_preview` | 独立预览 | 双击 / 显示预览 |
| `Form_BGRemover` | RMBG 抠图 | 工具 / 右键 |
| `Form_backgroundReplace` | 透明底换纯色/随机色 | 工具 → 替换透明背景（选中）；文件夹右键 → 当前文件夹 / 全部图片 |
| `Form_SimilarImages` | 相似图分组审查墙 | 工具 → 查找相似图片 |
| `Form_CorruptedImages` | 坏图审查墙 | 工具 → 扫描坏图 |
| `Form_VideoConvert` | 视频格式转换 | 工具 / 视频右键 |
| `Form_VideoTools` | 抽帧（含预览播放） | 工具 / 视频右键 |
| `Form_ImageSorter` + `Form_ImageSorterSettings` | 拖放分类拷贝 | （分类器流程） |

### 3.6 遗留 / 空壳

| 窗体 | 说明 |
| --- | --- |
| `Form_Edit` | 几乎空壳，可忽略 |
| Designer 中若干未挂接 AutoTagger 菜单项 | 旧单提供方时代遗留，新 GUI 勿复活「静默预览丢结果」行为 |

---

## 4. 菜单与命令完整表

### 4.1 文件 (`MenuLabelFile`)

| 命令 | i18n 键（示意） | 行为要点 |
| --- | --- | --- |
| 加载文件夹 | `MenuItemLoadFolder` | 经 `ConfirmDatasetSwitch` → `LoadDatasetCoreAsync` |
| 加载文件夹（自定义选项） | `MenuItemLoadFolderWithSettings` | 同上，先弹 `Form_LoadingSettings` |
| 重新导入当前数据集 | `MenuItemReloadDataset` | F5；预览策略跟随当前数据集状态 |
| 保存更改 | `MenuItemSaveChanges` | Ctrl+S；`DatasetManager.SaveAll` |

**契约**：一切替换数据集的入口必须先 `ConfirmDatasetSwitch()`（保存 / 丢弃 / 取消；保存失败则中止）。

### 4.2 视图 (`MenuLabelView`)

| 命令 | 行为 |
| --- | --- |
| 显示预览 | 切换内嵌/跟随预览；Ctrl+P |
| 翻译标签 | 开/关翻译列与后台翻译 |
| 显示标签计数 | 全部标签 Count 列 |
| 隐藏全部标签 / 图片标签 / 数据集 | Ctrl+J / K / L 面板显隐 |

### 4.3 设置 (`MenuLabelOptions`)

| 命令 | 行为 |
| --- | --- |
| 应用设置 | `Form_settings` |
| 语言 | 运行时子菜单：`en-US` / `zh-CN` / `zh-TW` / `ru-RU` / `pt-BR` |

### 4.4 工具 (`MenuTools`)

| 命令 | 目标窗体 |
| --- | --- |
| 替换透明背景（选中图片） | `Form_backgroundReplace` |
| 视频格式转换 | `Form_VideoConvert` |
| 视频抽帧 | `Form_VideoTools` |
| ONNX 推标 | `Form_OnnxTagger` |
| 背景移除 | `Form_BGRemover` |
| LLM 打标 | `Form_LlmTagger` |
| 查找相似图片 | `Form_SimilarImages` |
| 扫描坏图 | `Form_CorruptedImages` |

### 4.5 顶栏运行时菜单

| 命令 | 目标 |
| --- | --- |
| LLM 设置 | `Form_AiServerSet` |
| 测试 | `Form_TestModule` |
| Debug（仅 `DebugMode`） | 打开 `debug.log` |

### 4.6 上下文菜单

**数据集图片右键**

- 打开所在文件夹  
- 删除图片和标签（事务化 `ImageFileDeleter`）  
- 移除背景  
- 裁剪图片 / 编辑图片  
- ONNX 重新推标 / LLM 打标  
- 视频工具…（仅视频扩展名）

**文件夹组头右键**

- 重命名文件夹（`DatasetManager.RenameFolder`，路径必须 `IsSafeRelativeFolder` + `IsUnderRoot`）  
- 批量重命名图片（`BatchRenamePlanner` + `RenameImages` 两阶段）  
- 对此文件夹 ONNX / LLM 打标（预选「当前文件夹」来源）  
- 替换透明背景（当前文件夹）——右键点中的文件夹（多选=并集），「全部」行上不可用  
- 替换透明背景（全部图片）——整个数据集；两者都先跑 `TransparentImageScanner` 预扫描（只留真正带透明像素的 PNG / WebP / GIF），再确认覆盖数量，跳过视频

**标签网格右键**（图片标签 / 全部标签共用）

- 查询 Danbooru Wiki  
- 重新翻译标签（强制刷新自动缓存，不覆盖手动译）

**数据集列表头右键**

- 列显隐（至少保留一列；持久化 `DatasetHiddenColumns`）

---

## 5. 工具栏操作清单（按面板）

### 5.1 图片标签工具栏

| 操作 | 默认热键 | 说明 |
| --- | --- | --- |
| 添加 | Ctrl+E | → `Form_addTag` |
| 删除 | Ctrl+D | |
| 撤销 / 重做 | Ctrl+Z / Ctrl+Shift+Z | `EditableTagList` 历史 |
| 复制 / 粘贴 / 从剪贴板粘贴 | | |
| 设置到全部图片 | | 当前列表覆盖全部 |
| 显示格式化文本 | | |
| 上移 / 下移 | Ctrl+PageUp/Down | |
| 在全部标签中查找 | Ctrl+F | |
| 多选校对墙 | Shift+T | `Form_TagImagesGrid` |
| Prompt 排序 | Ctrl+Q | 尊重「不排序前 N 行」 |
| 类别排序开关 | | 持久化 `ImageTagsCategorySort` |
| 类别筛选下拉 | | 仅控制行 `Visible`，不改绑定列表 |
| 搜索框 | | 前缀 > 子串 > 翻译 > 中文字典；Enter 下一个；Esc 清除 |
| 权重 | | |

### 5.2 全部标签工具栏

| 操作 | 默认热键 | 说明 |
| --- | --- | --- |
| All / Common 切换 | | Common = 所有图共有标签 |
| 添加到全部 / 从全部删除 | Ctrl+W / Ctrl+R | |
| 添加到选中 / 从选中删除 | Shift+W / Shift+R | |
| 添加到筛选结果 / 从筛选结果删除 | Ctrl+Shift+W / Ctrl+Shift+R | |
| 替换 | Ctrl+G | |
| **筛选模式下拉** AND/OR/NOT/XOR | Ctrl+Y | **禁止**做成「点一下切下一模式」；图标=当前模式；再点当前模式 = 取消筛选 |
| 按标签筛选数据集 / 退出 | Ctrl+Shift+F / Ctrl+Shift+G | 无选中标签时状态栏提示，勿静默 |
| 全部标签文本过滤进出 | Shift+F / Shift+G | |
| 排序：名升 / 次数升 / 次数降 | | |
| 类别排序 / 类别筛选 | | 与文本、次数过滤在 `CheckCurrentFilter` 中 AND |
| 搜索框 | | 与图片标签同一匹配优先级 |
| 双击行 | | `AllTagsDoubleClickAction` 八种快操作之一 |

**筛选模式语义**

| `FilterType` | 含义 |
| --- | --- |
| And | 同时含所有选中标签 |
| Or | 含任一选中标签 |
| Not | 不含这些标签（反选） |
| Xor | 异或语义 |

筛选激活期间，改变全部标签选中应 **立刻重跑** `SetFilter`。

### 5.3 数据集工具条

- 缩放滑块：同时驱动隐藏网格行高与浏览器缩略图高度。  
- 选择模式切换、显示计数。

---

## 6. 功能模块详解（实现契约）

### 6.1 数据集加载与保存

**实现**

- `DatasetManager.LoadFromFolder(folder, loadPreviewImages, readMetadata)`
- `TolerantFileEnumerator`：跳过不可读项与 `.bdtm-*` 内部目录
- `DataItem`：路径、缩略图、`EditableTagList`、`IsModified`（与已保存快照文本精确比较，非哈希）
- `SaveAll()`：写回各图 caption；耐久写盘走 `SafeFile`（temp + `File.Replace`）

**加载选项**

- 是否加载预览图（大数据集可关）
- 是否从图片元数据读初始标签
- 预览尺寸

**删除**

- `ImageFileDeleter.DeleteImageWithTags`：暂存 → 清除，失败回滚；caption 扩展名经 `ImageEditorSaveService.FindExistingCaptionPath` 解析，勿假定 `.txt`

**覆盖图片文件固定序列**（抠图/编辑覆盖）

1. `SafeFile.WriteAllBytes`  
2. `DataManager.RemoveFromCache(path)`  
3. `Extensions.MakeThumb`  
4. `RefreshDatasetGrid()`

### 6.2 标签编辑核心模型

```text
EditableTagList  ──(每图)──► EditableTag (Tag, Weight, IsManual, Order)
       │                      IEditableObject 单元格事务
       ├── TextTags 镜像 (_tags)  ← 事务中禁止静默失步
       └── History 撤销栈

AllTagsList ── 全局计数 / 筛选 / 排序 / 类别
MultiSelectDataTable ── 多选时「标签 × 图片」交叉编辑
```

**重设计必须保留的行为**

- 重绑网格前刷新悬挂的 `EndEdit()`，否则程序化 `Tag=` 跳过镜像。  
- 检测到镜像失步时 **自愈**（重建镜像 + `ErrorData.json`），不可抛异常（抛出会在 `RemoveAt` 路径二次损坏列表）。  
- 程序化 setter 须 `CaptureBackupOutsideTransaction()`，否则 Ctrl+Z 会还原空标签并崩。  
- `ReplaceTag` / `RemoveTags` 先 `EndEdit()`。

**添加位置** `DatasetManager.AddingType`：`Top` / `Center` / `Down` / `Custom`。

### 6.3 标签语义分类（UI 着色 / 排序 / 筛选）

`TagSemanticClassifier.Classify` → `TagSemanticCategory`（枚举顺序 = 排序秩）：

| 类别 | 用途 |
| --- | --- |
| Character, Copyright, Artist | Danbooru type 精确桶 |
| SubjectCount | 1girl / 2boys 等 |
| Hair, Eyes, Body, Expression, Clothing, Accessory | 启发式 |
| Object, Animal, Food, Action, Composition, Background, Style | 启发式 |
| General, Meta | 兜底 / meta |

- 行浅色着色：`GetAccent` + `ApplyTint`  
- 图片标签类别排序：遵守「不排序前 N 行」  
- Danbooru 角色精确匹配依赖 `CharacterTagCatalog`（`MatchCharacterTags` 开关）

### 6.4 中文标签工作流

| 组件 | 数据 | 行为 |
| --- | --- | --- |
| `ChineseTagLookupService` | `Data/danbooru-0-zh.csv` (`en,中文\|同义词`) | 输入中文 → 英文；搜索/补全；仅简中语言启用 |
| `CharacterTagCatalog` | `Data/danbooru_character_tags.csv` (~33 万) | 角色着色、「译名 (作品)」翻译、父子关系、补全 |
| `TranslationManager` | 手动缓存 > 角色表 > zh CSV > 在线链 | `Translations/<lang>.txt` |

补全模式 `AutocompleteMode`：关闭 / 前缀 / 前缀+包含 / 含翻译变体等。zh 模式下空 `Tags/` 时从字典回填英文补全。

### 6.5 ONNX 本地推标

**入口**：工具菜单、数据集右键（自动开始）、文件夹右键（预选当前文件夹）、CLI `onnx-tag`。

**模型目录** `OnnxTaggerCatalog`：

- WD14 全系列（`wd:<repo>`）  
- PixAI v0.9（`pixai:v0.9`）  
- CL 系列（含 gated `cl_tagger_v2*` 🔒）

**窗体控件组**

1. 输入来源：选中 / 全部 / 当前文件夹  
2. 模型 + 下载源（HF / 镜像）+ 通用/角色阈值  
3. 写入模式 + 排序 + 前后缀 + 下划线→空格  
4. Run / Cancel / 进度（最近/平均耗时、ETA）

**写入模式** `NetworkResultSetMode`

| 值 | UI 语义 | 实现要点 |
| --- | --- | --- |
| `AllWithReplacement` | 全部替换 | 覆盖 |
| `OnlyNewWithAddition` | 追加 | 去重且 **不重排** 已有标签 |
| `SkipExistTagList` | 跳过已有标签列表 | **推理前**预过滤已标注图；报告跳过数；禁止「推理后丢弃」 |

**服务**：`Wd14OnnxTaggerService` / `PixAiOnnxTaggerService` / `ClTaggerOnnxService` + `TagPostProcessor` + `TagWriteService`。  
下载：`HuggingFaceModelDownloader`（`.partial`、路径 containment、每目标 `SemaphoreSlim`）。  
Gated：`Form_GatedModelNotice` + DPAPI HF Token。

### 6.6 LLM 打标 / Caption

**前置**：`Form_AiServerSet` 配置 OpenAI 兼容端点；档案镜像到扁平字段（运行时权威仍是扁平 `OpenAiAutoTagger.*`）。客户端必须 `AiOpenAiClient.CreateFromSettings`（多 Key 轮询），禁止单 Key 构造。

**模式** `LlmTaggerMode`

| 模式 | 输入 → 输出 | 写回 |
| --- | --- | --- |
| Tags | 图 → booru 标签 | 同 ONNX 写入模式进数据集 |
| NaturalLanguage | 图(+标签) → 自然语言 | 见下 |

**自然语言输出**

| 维度 | 选项 |
| --- | --- |
| `LlmCaptionFormat` | TagsAndNaturalLanguage / NaturalLanguageOnly |
| `LlmCaptionOutputTarget` | SeparateFolder（`_captioned` 旁路） / InPlace（写回原 `.txt`） |

其它：并发 1–100、提示词模板（内置多套 + 自定义导出 JSON 不含凭据）、无标注先 ONNX、`reprocess-existing`、视频帧数/缩放（高级设置）。

**服务**：`AiOpenAiClient`、`CaptionGenerationService`、`OpenAiCompatibleAutoTagProvider`（`IAutoTagProvider`）。  
WebP / 视频帧转 PNG 时 **必须 Dispose Image**（防 GDI 泄漏）。

### 6.7 角色标签审查

**入口**：测试模块 → 向导；CLI `audit`。

**三步向导**

1. **设置**：模型、少标/全标（Sparse/Full）、执行模式（复核编辑 / 摘要应用）、最小出现次数、主体模式 Single/Dual/Quad（最多 4 角色槽；空触发词跳过）、每槽触发词/性别/标准图/文件夹  
2. **AI 进度**：文本初筛 → 视觉复核；展示耗时与 token；可重做视觉复核；多角色失败可只重试失败角色（`resumeFrom` 断点）  
3. **复核应用**：决策 Keep/Delete/Replace/Uncertain、替换词、类别、原因；搜索/仅看变更；复制最终提示词；事务化应用保存

**策略（不可在新 GUI 里「简化掉」）**

- `CharacterTagAuditPolicy.CanDelete` 在 **解析 AI 响应时** 强制：受保护类别的 Delete/Replace → Keep；用户在复核网格仍可覆盖非触发词。  
- `ShouldDelete`/`ShouldReplace` **不要**再套一层 `CanDelete`（会丢掉人工复核）。  
- 类别门控用 `ResolveCategoryForPolicy`（本地分类优先于模型 category）。  
- 禁止「具体发色 → colored hair / multicolored hair」类泛化替换（`IsForbiddenGenericHairReplacement`）。  
- 运行时读入 `Agent/skills/character-tag-auditor/SKILL.md` + `prompt-pyramid/SKILL.md`（非文档，是提示词）。

**多角色**（`CharacterTagDualAuditService`, MaxProfiles=4）

- 归属：触发词优先，再文件夹  
- 同图主体数注入：`1girl…6+girls` / `multiple girls` 等；去掉 `solo` 与更低人数，不碰更高人数  
- 冲突替换合并：冲突时保留原标签  
- 应用：`TransformEditableTagsDualForItem` / 事务写盘

### 6.8 错误标签修复（Tag Consistency）

**入口**：测试模块；CLI `fix-tags`。

`TagConsistencyPlanner.Plan` → 预览表 → 用户确认后应用（**不自动保存**，可撤销）：

| 原因 | 行为 |
| --- | --- |
| SubjectCountConflict | 同性别保留最高人数标签 |
| SoloWithMultipleSubjects | 人数和 ≥2 时删 `solo`（永不碰 `solo focus`） |
| CharacterVariantConflict | 同角色家族按全库出现量投票 |
| ChildBelowThreshold | 子级出现 < 阈值（默认 30）则 **替换** 为可信祖先 |

家族：有 catalog 时走父链根；否则回退去括号基名启发式。

### 6.9 快速替换

测试模块：阈值阈值 + 运行 → `QuickTagReplaceService.GetReplacementSourceTags` 建议同类别低频标签源。

### 6.10 相似图片

`SimilarImageFinder`：64-bit dHash + Hamming + 贪婪星形聚类。  
UI：四档阈值 → 分组墙 → 绿留红删 →「每组保留最大文件」→ 事务删除后重扫。  
**陷阱**：墙共享 `DataItem.Img` 时，删除前必须 `ClearResults()` 再 `DeleteDatasetMediaFiles`；哈希只在 UI 线程读共享位图。

### 6.11 扫描坏图

`CorruptedImageScanner.Inspect`：missing / empty / decode / invalid_size。  
墙 **自有** X 占位图（不共享数据集 Img）；默认全红；删除流程同相似图。跳过视频；尊重文件夹范围。

### 6.12 背景移除

`RmbgBackgroundRemoverService`（RMBG-1.4 ONNX）：范围、透明/纯色、替换原图或 `_nobg.png`、单张测试。完成后刷新缩略图或 `AddImages` 导入副本。

### 6.13 图片编辑器

分层（便于测试、新 GUI 应继续拆开）：

| 层 | 职责 |
| --- | --- |
| `ImageEditorDocument` | 位图 + 有界撤销（15 步 / 512MB） |
| `ImageEditorCanvasMath` | 屏↔图像坐标（纯函数） |
| `ImageEditorSaveService` | ImageSharp 按原扩展名编码、`_edit` 命名、克隆 caption |
| Form | 仅交互 |

工具：B 画笔 / E 橡皮 / I 吸管 / C 裁切 / H 抓手 / 旋转翻转；保存模式 Ask / Overwrite / NewFile。

**多重裁剪** `Form_ImageCrop`：多矩形 → `_r1/_r2…` 导出并导入数据集。

### 6.14 视频

`VideoProcessingService` + 捆绑 ffmpeg：

- 转换：选中/全部、可替换原文件、输出扩展名  
- 抽帧：All / ByFps / NativeFps / Specific；锁定帧列表；播放控件；结果导入数据集  

注意：替换原视频成功后会删源文件。

### 6.15 翻译与 Wiki

- 提供方：`AbstractTranslator` 工厂 + `FallbackTranslator`（超时必须把 `CancellationToken` 传到 HTTP）  
- Wiki：`DanbooruWikiClient` + `DanbooruDTextFormatter` → `Form_TagWikiPopup`（示例图、翻译正文、浏览器打开）

### 6.16 更新检查

`UpdateChecker`：GitHub Release；源码检出可 git pull；zip 下载经 `ResolveFileUnderDirectory` 消毒文件名；`Form_UpdateInfo` 展示本地化说明（`ReleaseNotesLocalizer` 按 UI 语言切 `<!-- lang:zh-CN -->` / `<!-- lang:en -->`）。

---

## 7. 设置项分组（新设置页信息架构）

### 7.1 常规

- 自动补全模式 / 字体 / 排序 / 触发字符数  
- 加载/保存分隔符、默认 caption 扩展名、caption 扩展名列表  
- 退出询问保存、加载时修复标签、选中变化时自动排序  
- 全部标签双击快操作（8 种 `AllTagsQuickAction`）  
- 调试模式（Debug 菜单 + `debug.log`）  
- 检查更新  

### 7.2 界面

- 预览尺寸、语言、配色方案  
- 预览位置 / 预览类型（主窗内嵌 / 独立窗）  
- 标签行高、字体  
- 缓存已打开图片  
- 图片编辑器默认保存动作  

### 7.3 翻译

- 目标语言、服务、超时  
- 仅手动译进入补全  
- 优先使用 `danbooru-0-zh.csv`  
- 匹配 Danbooru 角色标签（`MatchCharacterTags`）  

### 7.4 热键

- 可重绑；保存配置叠加在 `HotkeyData.InitDefault` 上（新默认热键自动到达老用户）  
- **禁止**直接设 `ShortcutKeys` 绕过系统  

### 7.5 LLM 档案（独立窗，非 settings 页签）

每站点：名称、Endpoint、Tokens（DPAPI，UI 只显示掩码尾）、Timeout、文本模型、视觉模型、角色审查模型、并发、测速。  
`LlmApiProfileLogic.ApplyActiveProfile` 镜像到扁平字段；`TokensProtected` 需 `ObjectCreationHandling.Replace`。

### 7.6 分散在其它窗的持久设置

| 键组 | 示例 |
| --- | --- |
| ONNX | 上次模型、各模型阈值、下载源 |
| LLM 打标 | 模式、caption 目标/格式、是否重处理、无标注先 ONNX |
| 审查 | 模型、风格、执行模式、最小次数、主体模式、性别数组 |
| 抠图 | 模型、填色、是否替换原图 |
| 浏览器 | 平铺、预览展开、隐藏列、类别排序开关 |
| 修复 | `TagFixChildThreshold`, `QuickReplaceThreshold` |
| 安全 | HF Token / LLM Tokens（DPAPI；`Protect` 失败必须抛错中止保存） |

---

## 8. 热键默认表

| Id | 默认 | 动作 |
| --- | --- | --- |
| DatasetFocus | Ctrl+1 | 焦点：数据集 |
| TagsFocus | Ctrl+2 | 焦点：图片标签 |
| AllTagsFocus | Ctrl+3 | 焦点：全部标签 |
| PreviewTabFocus | Ctrl+4 | 焦点：预览 |
| MenuItemSaveChanges | Ctrl+S | 保存 |
| MenuItemShowPreview | Ctrl+P | 预览 |
| MenuItemReloadDataset | F5 | 重新导入 |
| MenuHideAllTags | Ctrl+J | 显隐全部标签 |
| MenuHideTags | Ctrl+K | 显隐图片标签 |
| MenuHideDataset | Ctrl+L | 显隐数据集 |
| BtnTagAdd | Ctrl+E | 添加标签 |
| BtnTagDelete | Ctrl+D | 删除标签 |
| BtnTagUndo / Redo | Ctrl+Z / Ctrl+Shift+Z | 撤销/重做 |
| BtnTagUp / Down | Ctrl+PageUp/Down | 移动 |
| BtnTagFindInAll | Ctrl+F | 在全部中定位 |
| BtnTagAddToAll / Selected / Filtered | Ctrl+W / Shift+W / Ctrl+Shift+W | 批量添加 |
| BtnTagDeleteForAll / Selected / Filtered | Ctrl+R / Shift+R / Ctrl+Shift+R | 批量删除 |
| BtnTagReplace | Ctrl+G | 替换 |
| BtnImageFilter / Exit | Ctrl+Shift+F / Ctrl+Shift+G | 数据集筛选 |
| BtnTagMultiModeSwitch | Ctrl+Y | 打开筛选模式下拉（非循环） |
| BtnTagFilter / Exit | Shift+F / Shift+G | 全部标签文本过滤 |
| toolStripPromptSortBtn | Ctrl+Q | 排序 |
| BtnTagImageChecker | Shift+T | 多选校对墙 |

新热键四触点：`InitDefault` + `ChangeLanguage`(+5 语言 `HK*`) + `InitHotkeyCommands` +（若 Id=菜单 i18n 键则菜单自动附快捷键提示）。

---

## 9. CLI 接口（与 GUI 语义对齐）

```text
BooruDatasetTagManagerPlus.exe <command> <folder> [options]
退出码：0 成功 / 1 错误 / 2 用法错误
公共：--separator / --ext / --dry-run
```

| 动词 | 作用 |
| --- | --- |
| `help` / `version` | 用法 / 版本 |
| `stats` | 图数、已标/未标、唯一标签、实例数 |
| `list-images` | 路径；`--tags`+`--match any\|all\|none`；`--untagged` |
| `list-tags` | tag\\tcount；`--category`；`--min-count` |
| `classify-tags` | tag\\t语义类\\tcount |
| `add-tags` | `--tags`；`--position start\|end`；条件 `--if-tags`；`--only-untagged` |
| `remove-tags` | |
| `replace-tag` | `--from` / `--to` |
| `fix-tags` | 同 GUI 一致性修复；`--child-threshold`；`--catalog` |
| `export` | JSON `{image:[tags]}` |
| `onnx-models` | 本地模型状态（AI） |
| `onnx-tag` | ONNX 推标（AI）；`--write-mode skip\|append\|replace` 等 |
| `audit` | LLM 角色审查（AI）；`--trigger`+`--reference`；`--report` |

架构：`CliCommands`（可测试、无 Program）+ `CliAiCommands`（经 `AiRunner` 钩子，不链入测试工程）。

---

## 10. 服务层 / 接口 API（新 GUI 应调用的后端）

### 10.1 抽象接口

| 接口 | 职责 |
| --- | --- |
| `IAutoTagProvider` | `ConnectAsync` / `GetModelsAsync` / `GetModelParametersAsync` / `GenerateAsync`；能力标志 Images/Video/MultipleModels/DynamicParameters |
| `AbstractTranslator` | `TranslateAsync(..., CancellationToken)`；工厂 `Create(service)` |
| `IBindingList` / `IBindingListView` | `EditableTagList` / `AllTagsList` 网格绑定 |

### 10.2 核心服务（按域）

| 服务 | UI 主要调用 |
| --- | --- |
| `DatasetManager` | Load/Save/Filter/Rename/AddTagToAll/ReplaceTagInAll/SetActiveFolder(s) |
| `TagWriteService` | ONNX/LLM 结果写入 DataItem |
| `TagPostProcessor` | 下划线、前后缀、颜文字 |
| `*OnnxTaggerService` + `OnnxTaggerCatalog` | 下载、加载、推理 |
| `AiOpenAiClient` / `CaptionGenerationService` | LLM 请求、caption 目录扫描与写出 |
| `CharacterTagAuditService` / `CharacterTagDualAuditService` | 审查执行与断点 |
| `TagConsistencyPlanner` / `QuickTagReplaceService` | 修复计划 / 快替建议 |
| `SimilarImageFinder` / `CorruptedImageScanner` | 纯算法，Form 只做墙 |
| `RmbgBackgroundRemoverService` | 抠图 |
| `VideoProcessingService` | 转码/抽帧 |
| `TranslationManager` / `ChineseTagLookupService` / `CharacterTagCatalog` | 译与中文/角色 |
| `ImageEditorDocument` / `SaveService` / `CanvasMath` | 编辑器 |
| `SafeFile` / `SecretProtector` / `ImageFileDeleter` / `TolerantFileEnumerator` | I/O 与安全 |
| `HuggingFaceModelDownloader` / `UpdateChecker` | 下载与更新 |
| `LlmApiProfileLogic` | 档案 ↔ 扁平字段 |
| `ColorSchemeManager` | 主题 |
| `BatchRenamePlanner` / `DatasetFolderIndex` | 重命名与路径安全 |
| `TagSemanticClassifier` | 着色/排序/筛选 |

### 10.3 组合根（服务定位）

`Program` 静态字段：`Settings`、`DataManager`、`TagsList`、`TransManager`、`ChineseTagLookup`、`CharacterTagLookup`、`ColorManager`、`AppPath`。

**新 GUI 约定**：任何用户触发入口在 `DataManager == null` 时必须提示 `TipDatasetNoLoad` 并返回。

---

## 11. 枚举速查（控件绑定用）

```text
FilterType                 And | Or | Not | Xor
NetworkResultSetMode       AllWithReplacement | OnlyNewWithAddition | SkipExistTagList
LlmTaggerMode              Tags | NaturalLanguage
LlmCaptionOutputTarget     SeparateFolder | InPlace
LlmCaptionFormat           TagsAndNaturalLanguage | NaturalLanguageOnly
ImageEditorSaveMode        Ask | Overwrite | NewFile
ImagePreviewType           PreviewInMainWindow | SeparateWindow
AllTagsQuickAction         Replace / Add|Delete × (All|Selected|Filtered) / FilterByTag
CharacterTagDecision       Keep | Delete | Replace | Uncertain
CharacterTagAuditStyle     Sparse | Full（少标 / 全标）
CharacterTagAuditSubjectMode  Single | Dual | Quad
CharacterGender            Girl | Boy
HuggingFaceDownloadSource  HuggingFace | HfMirror
AutoTaggerSort             （置信度 / 字母等，与 ONNX/LLM 排序共用语境）
TagFilteringMode           None | Equal | Containing | NotEqual | NotContaining | Regex
AddingType                 Top | Center | Down | Custom
FrameExtractMode           All | ByFps | NativeFps | Specific
BatchRenameNumbering       Numeric | Letters | None
```

---

## 12. 持久化与本地文件地图

| 数据 | 路径 | 备注 |
| --- | --- | --- |
| 设置 | `settings.json` (+`.bak`/`.corrupt`) | `SafeFile`；密钥 DPAPI |
| Caption | 图旁 `.txt`/配置扩展名 | 保存写回 |
| 补全缓存 | `Tags/List.tdb` | `TagsDB.curVersion`  bump 重建 |
| 用户补全源 | `Tags/*.csv\|txt` | |
| 翻译缓存 | `Translations/<lang>.txt` | `*` 前缀=手动 |
| 主题 | `ColorScheme.json` | |
| 模型 | `Models/<repo>/` | `.partial` 暂存 |
| 日志 | `crash.log` / `debug.log` | |
| 删除暂存 | `.bdtm-trash/<guid>/` | |
| Caption 旁路输出 | `<folder>_captioned/` | |
| 捆绑数据 | `Data/*.csv`, `Languages/*.txt`, `Agent/skills/**/SKILL.md`, `ThirdParty/ffmpeg/` | |

---

## 13. 长任务窗体统一交互模式

ONNX / LLM / 视频 / 相似图 / 坏图 / 审查向导 共享：

```text
[来源选择] → [选项] → [Run | Cancel | Close] → [进度/状态]
关闭中：若任务在跑 → Cancel + 延迟 Close（finally 再真正关）
进度：必须 marshal 回 UI 线程
锁：UI Lock + finally 释放；数据集浏览器一并禁用
```

审查墙类（相似图 / 坏图 / 多选校对）共享语言：

```text
绿 = 保留 / 有标签
红 = 待删 / 无标签
左键切换 · 滑块缩放 · 批量标绿/红 · 删除红标后重扫
```

---

## 14. i18n 与无障碍契约

- 5 语言文件键集 **完全一致**（`LocalizationAndImageLoaderTests`）。  
- 新文案：`I18n.GetText` + 各窗 `switchLanguage`/`ApplyLanguage`。  
- 运行时添加的控件 **不做 DPI 硬编码像素**；手写 Form 必须 `AutoScaleMode.Dpi` + `AutoScaleDimensions=96F`。  
- 浮动工具窗：**Owned，禁止 TopMost**（否则盖住模态确认导致「假死」）。  
- 勿在 ImageList 句柄创建前 Dispose 已 Add 的 Image。

---

## 15. 新 GUI 建议功能树（信息架构草案）

可直接用作导航/IA，不必沿用当前菜单名：

```text
数据集
  ├─ 打开 / 打开选项 / 重新加载 / 保存
  ├─ 浏览（分组 | 平铺）· 搜索 · 范围
  ├─ 重命名文件夹 · 批量重命名图片
  ├─ 删除媒体（事务）
  └─ 预览（内嵌 | 窗口）

标签
  ├─ 当前图编辑（含撤销、权重、类别）
  ├─ 全库标签（计数、排序、类别）
  ├─ 按标签筛选数据集（AND/OR/NOT/XOR）
  ├─ 批量增删替（全部 / 选中 / 筛选）
  ├─ 多选校对墙
  ├─ 中文输入与补全 · Wiki · 翻译
  └─ 错误标签修复 · 快速替换

自动标注
  ├─ 本地 ONNX（模型库 · 阈值 · 写入模式）
  ├─ LLM 标签
  └─ LLM 自然语言 Caption（旁路 | 就地）

角色工作流
  └─ 标签审查向导（1–4 人 · 少标/全标 · 复核）

图像工具
  ├─ 编辑器 · 多重裁剪
  ├─ 背景移除 · 透明底替换
  ├─ 相似图清理
  └─ 坏图扫描

视频
  ├─ 格式转换
  └─ 抽帧

设置
  ├─ 常规 / 外观 / 翻译 / 热键
  ├─ LLM 连接档案
  └─ 更新 · 调试日志

自动化
  └─ CLI（与上列写盘语义 1:1）
```

---

## 16. 用户旅程（验收对照）

### J1 首次打标

1. 加载文件夹（可关预览）  
2. LLM 设置填端点与 Key  
3. ONNX 或 LLM 对未标注图写入（Skip 模式应跳过已有）  
4. 全部标签面板浏览计数，双击替换错标  
5. 保存  

### J2 角色 LoRA 清洗

1. 加载多文件夹数据集，点角色文件夹设范围  
2. 测试 → 角色审查：触发词 + 标准图 + 少标法  
3. 复核删除泛化/错误特征，应用保存  
4. 可选：错误标签修复清人数/父子冲突  

### J3 去重与质检

1. 相似图扫描 → 每组留最大 → 删红  
2. 坏图扫描 → 删红  
3. F5 重新导入确认磁盘一致  

### J4 视频素材入库

1. 抽帧 → 自动进口  
2. ONNX 批量 → 人工修  
3. 保存  

---

## 17. 实现类索引（按文件）

| 域 | 主要文件 |
| --- | --- |
| 主窗 | `Form1.cs`, `Form1.Designer.cs`, `DatasetBrowserView*`, `DatasetPreviewPanel*` |
| 数据 | `DatasetManager.cs`, `DatasetFolders.cs`, `EditableTagList.cs`, `AllTagsList.cs`, `MultiSelectDataTable.cs` |
| 设置 | `AppSettings.cs`, `Form_settings.cs`, `LlmApiProfiles.cs`, `HotkeyData` |
| ONNX | `Form_OnnxTagger.cs`, `*OnnxTaggerService.cs`, `OnnxTaggerCatalog.cs`, `TagWriteService.cs` |
| LLM | `Form_LlmTagger.cs`, `Form_AiServerSet.cs`, `AiApi/AiOpenAiClient.cs`, `CaptionGenerationService.cs` |
| 审查 | `Form_CharacterTagAuditWizard.cs`, `CharacterTagAudit.cs`, `CharacterTagAuditMulti.cs` |
| 清理 | `TagConsistencyPlanner`, `Form_SimilarImages`, `SimilarImageFinder`, `Form_CorruptedImages`, `CorruptedImageScanner` |
| 图像 | `Form_ImageEditor.cs`, `ImageEditor*.cs`, `Form_ImageCrop.cs`, `Form_BGRemover.cs` |
| 视频 | `Form_Video*.cs`, `VideoProcessingService.cs` |
| 译/中文 | `TranslationManager`, `ChineseTagLookupService`, `CharacterTagCatalog`, `TagsDB` |
| CLI | `CliCommands.cs`, `CliAiCommands.cs` |
| 安全 I/O | `SafeFile`, `SecretProtector`, `ImageFileDeleter`, `HuggingFaceModelDownloader` |
| i18n | `Languages/*.txt`, `I18n` |

---

## 18. 新 GUI 明确禁止回归的行为

1. 「跳过已有」却仍推理后丢弃结果（计费/耗时假成功）。  
2. 筛选模式做成点击循环（「点 NOT 得 OR」）。  
3. `SecretProtector.Protect` 失败回落明文。  
4. 文件夹重命名允许 `..` / 越根。  
5. TopMost 工具窗盖住 MessageBox。  
6. 审查删除门控只信模型 category。  
7. 未加载数据集时按钮 NRE。  
8. 换数据集不经 `ConfirmDatasetSwitch`。  
9. 标签列表失步时抛异常而非自愈。  
10. LLM 编码图像不 Dispose。

---

## 19. 与旧文档关系

| 文档 | 用途 |
| --- | --- |
| **本文** `GUI_REDESIGN_REFERENCE.md` | 全量功能/接口/实现契约，供新 GUI |
| `UI_STRUCTURE_zh_CN.md` | 旧主窗控件级笔记（缺 v1.2.2+ 多项） |
| `README.md` | 用户向功能说明与截图 |
| `SECURITY_IO_AUDIT_2026-08.md` | I/O 与安全加固细节 |
| `RELEASE_NOTES_v*.md` | 版本变更 |

---

*生成说明：基于 v1.2.3 代码与 README 盘点；若后续版本增删菜单/动词，请同步更新第 3–9 节与第 15 节功能树。*
