# BooruDatasetTagManager+ 1.2.0

[English](README_en.md) | [Português do Brasil](docs/pt-BR/README_pt_BR.md)

面向 LoRA / 角色数据集的 Windows 标签管理工具,fork 自 **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**:保留原版「加载文件夹 → 编辑同名 `.txt`」核心流程,新增 LLM 打标(标签 / 自然语言)、角色标签审查、本地 ONNX 推标与中文标签工作流。**首次启动默认为简体中文界面**,遵循 [MIT License](LICENSE)。

![主界面](docs/images/main-window-dataset-browser.png)

## 更新日志

- **1.2.0**(当前)— 数据集面板整体重构:统一文件夹分组浏览器(搜索、折叠、批量重命名、右键快捷推标)+ 内嵌多图预览;标签语义浅色着色与类别排序;Danbooru 角色名单匹配(着色 + 译名);翻译、Wiki 弹窗与角色审查向导多项修复;全库审计后的发布与数据安全加固(批量改名失败回滚、HF 令牌只发官方站、隔离打包、LLM 保存门禁、视频替换防覆盖、配置容错启动)。[发布说明](docs/RELEASE_NOTES_v1.2.0.md)
- **1.1.3** — 文件 I/O 与数据安全专项加固(修复内部审计确认的 8 项风险:保存失败不丢编辑、删除事务化、并发写盘安全等);新增图片编辑器、CL 系列 ONNX 模型、标签中文字典搜索与全部标签双击快速操作。[发布说明](docs/RELEASE_NOTES_v1.1.3.md)
- **1.1.2** — 统一 LLM 打标窗口(标签 / 自然语言两种模式);背景移除本地化(RMBG-1.4);崩溃兜底、原子写盘、密钥加密等稳固与安全加固。[发布说明](docs/RELEASE_NOTES_v1.1.2.md)
- **1.1.1** — 角色标签审查「应用并保存」提速;统一「裁剪图片」对话框。[发布说明](docs/RELEASE_NOTES_v1.1.1.md)
- **1.1** — WD14 全系列、按模型阈值、PixAI 修复。[发布说明](docs/RELEASE_NOTES_v1.1.md)
- **1.0.5** — 统一 ONNX 推标、视频工具。[发布说明](docs/RELEASE_NOTES_v1.0.5.md)

## 快速开始

从 [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) 下载 `BooruDatasetTagManagerPlus-*-win-x64.zip`,解压后运行 `BooruDatasetTagManagerPlus.exe`(自包含,无需安装 .NET)。

1. **文件 → 加载文件夹**;「加载文件夹(自定义选项)…」还可以关闭预览图(大数据集更快)或从图片元数据读取初始标签(适合刚出图、还没写 `.txt` 的数据集)
2. 直接编辑标签:「全部标签」与「图片标签」搜索框支持中文字典(输入「头发」即可定位 long hair、black hair 等);双击「全部标签」行可执行快速操作(默认打开「全部替换」,可在设置更换);遇到生词可开 Danbooru Wiki
3. 使用 LLM 功能前,先在 **LLM 设置** 配置 OpenAI 兼容端点与模型
4. 按需运行 **工具 → LLM 打标 / ONNX 推标 / 背景移除 / 视频工具**,或 **测试 → 打开角色标签审查**

### 从源码构建

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — 启动 Release(或 Debug)
- `quick_build.bat` — 本地快速打包至 `dist/`(首次构建自动下载 FFmpeg)

运行后程序目录会生成 **Models/**(下载的 ONNX 模型)、**Cache/**(缓存)、**settings.json**(API 与偏好设置)。均为本地自动生成数据,可随时删除——设置恢复默认,模型可在应用内重新下载。

## 功能一览

| 模块 | 说明 |
| --- | --- |
| **数据集浏览** | 文件夹分组浏览器(搜索、折叠、重命名 / 批量重命名、右键快捷推标);内嵌预览(多选并排);行内显示格式·像素·大小 |
| **标签语义** | 18 类浅色分类着色与类别排序;内置 Danbooru 角色名单(精确识别 + 「译名 (作品)」翻译) |
| **LLM 打标** | 标签 / 标签→自然语言两种模式;OpenAI 兼容端点;提示词模板;LLM 并发 1–100 |
| **角色标签审查** | 触发词 + 标准图 + 全库标签清单;AI 两阶段审查;单 / 双角色;事务化写回 |
| **ONNX 推标** | WD14 全系列 + PixAI + CL 系列本地推标;各模型阈值独立记忆;HuggingFace 下载 |
| **背景移除** | 内置 RMBG-1.4 ONNX 本地抠图,无需外部服务;透明或纯色背景 |
| **图片编辑器** | 画笔 / 橡皮擦 / 吸管 / 裁切 / 旋转翻转,PS 式快捷键;另有独立多重裁剪对话框 |
| **视频处理** | 格式转换;全部帧 / 按 FPS / 指定帧抽帧;内置 FFmpeg |
| **标签编辑** | 中文字典搜索、全部标签双击快速操作、多选校对(Shift+T)、Danbooru Wiki |

## 功能详解

### 数据集浏览与预览

数据集面板是一个统一浏览器:顶部搜索框同时过滤文件夹与文件名;kohya 重复文件夹显示为可折叠分组(多文件夹默认全部折叠,搜索框右侧有全部展开 / 全部折叠按钮),点击文件夹名即可把数据集范围限定到该文件夹(全部标签计数、批量操作与审查向导同步跟随);图片行显示缩略图、文件名与「格式 · 像素 · 大小」,选择操作与文件管理器一致(Ctrl / Shift / Ctrl+A / 方向键 / 右键菜单 / Delete)。

- **文件夹右键**:重命名文件夹(磁盘与内存同步改名,未保存的编辑不丢失);批量重命名图片(前缀 + 数字 / 字母 / 保留原名 + 后缀,实时预览,`.txt` 同步改名);ONNX / LLM 打标此文件夹
- **内嵌预览**:数据集底部可折叠预览面板(视图 → 显示预览,状态记忆);多选时并排显示前 4 张,双击任一格在独立窗口打开;独立预览窗支持光标缩放、拖拽平移、双击适应 ↔ 100%、Ctrl+0 / Ctrl+1
- **标签着色与类别排序**:两个标签面板按 18 类语义浅色着色(角色 / 作品 / 发型 / 眼睛 / 服装…);图片标签工具栏的「类别排序」按分类分组排序并遵守「不排序前 N 行」;全部标签面板的类别排序默认关闭,可在其工具栏勾选
- **角色名单**:内置约 33 万条 Danbooru 角色标签(`Data/danbooru_character_tags.csv`),角色标签精确识别着色,翻译显示为「译名 (作品)」;可在 设置 → 翻译 关闭

### LLM 打标

入口:**工具 → LLM 打标…**、数据集右键,或标签工具栏「自动生成标签」。使用前先在 **LLM 设置** 配置 OpenAI 兼容端点、文本 / 视觉模型与全局 LLM 并发(默认 5,范围 1–100)。

![LLM 设置](docs/images/llm-settings.png)

![LLM 打标](docs/images/llm-tagger.png)

- **标签模式**:图片 → 标签,按写入模式(全部替换 / 追加 / 跳过已有)直接写回数据集,支持排序、前后缀与下划线后处理;提示词模板内置 4 套(Danbooru Tag / 自然语言 / 混合模式 / 自然语言2),可自定义并导出 JSON(不含凭据)
- **标签→自然语言模式**(原 TAG2NL):标签 + 图片 → 自然语言描述;输出格式可选 **标签+自然语言 / 仅自然语言**;默认另存副本到 `数据集目录_captioned/`(源 `.txt` 只读、可跳过已有),也可就地写回原图 `.txt`
- **无标注先 ONNX**:勾选后,对没有标签的图片先用本地 ONNX 推标,再交给 LLM 生成描述——标签→自然语言的自动流水线

### 角色标签审查

入口:**测试 → 打开角色标签审查…**。设定锁定触发词(强制保留)、标签方法(**少标法**只留核心特征 / **全标法**保留全部正确细节)、最小出现次数与标准图后,AI 先文本初筛、再视觉复核(流程不可回退,改参数请取消后重开);最后逐条复核(保留 / 删除 / 替换 / 不确定)、预览最终角色提示词,**应用并保存**为事务写盘、失败自动回滚。

支持**双角色**数据集:为角色 A / B 各自设定触发词、标准图与性别后,图片按触发词、再按文件夹自动归属,双人同图自动补齐 `2girls` 等主体数标签,AI 审查、逐条复核与应用均逐角色进行。

![审查向导 · 复核](docs/images/character-tag-audit-review.png)

### ONNX 推标

入口:**工具 → ONNX 推标…**,或数据集右键 **ONNX 重新推标**(自动开始);文件夹右键 **ONNX 打标此文件夹…** 会预选「当前文件夹」来源,确认设置后再开始。

![ONNX 推标](docs/images/onnx-tagger.png)

- 模型:WD14 全系列 12 款 + PixAI 0.9 + CL 系列(cl_tagger v1.02、cl_tagger_v2 v2.00 / v2.01a 🔒);各模型阈值与设置独立保存;HuggingFace 官方或镜像下载
- cl_tagger_v2 为**受限(gated)仓库**,作者许可禁止再分发与捆绑分发——软件不附带模型;下载前弹出许可提示,需自行在 HuggingFace 申请访问并填入 Access Token(DPAPI 加密保存),或手动下载放入 `Models` 目录
- 写入模式(全部替换 / 追加 / 跳过已有)、可选排序、下划线→空格、前后缀标签;批量推标带进度条

### 背景移除

入口:**工具 → 背景移除**,或数据集右键 **移除背景**。内置 RMBG-1.4 ONNX 本地抠图,**无需外部服务**;首次使用一键下载模型(约 176 MB,或量化版约 44 MB,官方 / 镜像源)。

![背景移除](docs/images/background-removal.png)

- 范围:全部图片或仅所选;背景:**透明**或**纯色**(默认白,带取色器);「移除测试」可先对单张预览
- 输出:**替换原图**或**另存 `_nobg.png` 副本**(选项记忆);完成后自动刷新缩略图或导入副本

### 图片编辑器

入口:数据集右键 **编辑图片**。仿 Photoshop 布局:左侧紧凑工具箱、顶部选项栏、底部状态栏。

![图片编辑器](docs/images/image-editor.png)

- 快捷键与 PS 一致:**B** 画笔、**E** 橡皮擦、**I** 吸管、**C** 裁切、**H** 抓手(或按住**空格**)、`[`/`]` 笔刷大小、**Alt+点击** 临时取色、滚轮以光标为中心缩放、**Ctrl+0** 适应窗口、**Ctrl+1** 100%、**Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y** 撤销重做(整笔一步,最多 15 步)、**Enter** 应用裁切、**Ctrl+S** 保存
- 保存可**覆盖原图**(原子写,失败不损坏原文件)或**另存 `_edit` 副本**(自动克隆同名标签文件并导入数据集);默认方式在 设置 → 界面 选择
- 另有数据集右键 **裁剪图片**:一次画多个选区,导出同目录 `_r1/_r2…` 并自动导入数据集

![多重裁剪](docs/images/crop-image-multi-region.png)

### 视频处理

**工具 → 视频格式转换… / 视频抽帧…**。mp4 / mkv / avi / webm / mov / flv 互转(可替换原文件);全部帧 / 按 FPS / 原生 FPS / 指定帧号抽帧,带预览与锁定帧流程;结果自动导入数据集。Release 包内置 FFmpeg。

![视频抽帧](docs/images/video-frame-extraction.png)

### 多选标签校对

多选图片后按 **Shift+T** 打开可视化编辑器:左侧标签列表(含出现次数、按频次排序)点击切换校对目标;**绿框 = 已有该标签,红框 = 没有**,点击图片上的 是/否 即可增删;跨标签修改一次「保存」统一生效。

![多选标签编辑器](docs/images/multi-select-tag-editor.png)

### 数据与隐私

- **LLM 打标与角色标签审查会把图片发送到你配置的端点**;ONNX 推标、背景移除与视频处理全部在本机完成
- 设置(含 DPAPI 加密的 API 密钥)保存在本地 `settings.json`;标签保存为原子写,批量操作不破坏原图,删除图片事务化可回滚

## 致谢与许可

- **[starik222](https://github.com/starik222)** — [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) 原作者,本项目在其基础上开发
- **[FFmpeg](https://ffmpeg.org/)** — 视频处理(GPL 组件,随 Release 打包)
- 本项目遵循 [MIT License](LICENSE);分发修改版构建时请保留原作者版权声明
