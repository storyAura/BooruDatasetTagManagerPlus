# BooruDatasetTagManager+ 1.1.2

[English](README_en.md) | [Português do Brasil](docs/pt-BR/README_pt_BR.md)

面向 LoRA / 角色数据集的 Windows 标签管理工具。保留原版「加载文件夹 → 编辑同名 `.txt`」流程，并集成 LLM 打标（标签 / 自然语言两种模式）、角色标签审查与中文标签工作流。**首次启动默认为简体中文界面**。

![主界面](docs/images/main-window-wiki.png)

## 项目渊源

本仓库 fork 自 **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**，保留原版「加载文件夹 → 编辑同名 `.txt`」核心流程，并在此基础上扩展 LLM 打标（标签 / 自然语言）、角色标签审查与中文工作流。

遵循 [MIT License](LICENSE)。分发修改版构建时请保留原作者版权声明。

## 核心能力

| 模块 | 功能 |
| --- | --- |
| **LLM 设置** | OpenAI 兼容端点；文本 / 打标视觉 / 审查视觉模型分离；LLM 并发与固定提示词 |
| **LLM 打标** | 统一运行窗口（仿 ONNX 界面）：输入来源、**标签 / 标签→自然语言两种模式**、提示词模板、视觉模型、写入模式、LLM 并发。标签模式直接写回数据集；自然语言模式（原 TAG2NL）可选 **标签+NL / 仅 NL** 内容格式、另存 `_captioned` 或就地写回，并可在无标注时**先用 ONNX 推标** |
| **角色标签审查** | 触发词 + 标准图 + 全库标签清单，AI 两阶段审查后写回数据集 |
| **ONNX 推标** | WD14 + PixAI 统一界面；HuggingFace 下载；双阈值；写入模式；前后缀标签 |
| **视频处理** | 格式转换（mp4/mkv/avi/webm 等）；全部帧 / 按 FPS / 指定帧抽帧；自带 FFmpeg |
| **背景移除** | 工具菜单与数据集右键；客户端内置 RMBG-1.4 ONNX 本地抠图，**无需外部服务**；首次使用一键下载模型（约 176 MB，或量化版约 44 MB）；可选透明或纯色背景（默认白）、替换原图或另存 `_nobg.png` 副本 |
| **裁剪图片** | 单次或多重裁切；导出至原图同目录 `_r1/_r2`；完成后自动导入数据集 |
| **多选标签校对** | 多选图片后 Shift+T 打开可视化编辑器；左侧标签列表（含出现次数）自由切换；绿框=有、红框=无，点击增删，跨标签一次保存 |

## 1.1.2 更新内容

安全性、稳定性与易用性全面加固；打标工作流统一为单一 **LLM 打标** 窗口。

- **统一 LLM 打标窗口**：标签 / 标签→自然语言（原 TAG2NL）两种模式；输出格式可选（标签+自然语言 / 仅自然语言）、另存 `_captioned` 或就地写回、无标注时先 ONNX 推标；提示词模板与打标设置并入窗口，「AI 视觉打标」统一改名「LLM 打标」，并发升级为全局 LLM 并发
- **背景移除本地化**：客户端内置 RMBG-1.4 ONNX，无需外部服务；透明 / 纯色背景、替换原图或另存 `_nobg.png` 副本
- **稳固与数据安全**：全局崩溃兜底写 `crash.log`；标签保存原子写零丢失；批量处理不破坏原图；任务运行中关窗安全收尾；模型下载抗中断 + 加载前完整性检测
- **安全**：API 密钥 DPAPI 加密；AI 服务端鉴权与访问控制；弃用 `BinaryFormatter`
- **易用**：设置内一键检查更新；多选标签校对（Shift+T）新增左侧标签列表；翻译前优先本地 CSV（默认开启）
- 移除「AutoTagger 预览窗口」与独立 TAG2NL 菜单；单元测试 264 项全部通过

完整明细（添加 / 优化 / 删除 / 修复）见 **[v1.1.2 发布说明](docs/RELEASE_NOTES_v1.1.2.md)**。

## 1.1.1 更新内容

角色标签审查「应用并保存」提速；「裁剪图片」统一为单一对话框（多选区、同目录 `_r1/_r2` 导出、自动导入数据集）。

完整明细见 **[v1.1.1 发布说明](docs/RELEASE_NOTES_v1.1.1.md)**；更早版本：[v1.1](docs/RELEASE_NOTES_v1.1.md)（WD14 全系列、PixAI 修复）· [v1.0.5](docs/RELEASE_NOTES_v1.0.5.md)（统一 ONNX 推标、视频工具）。

## 与原版差异

- **打标**：OpenAI 兼容 LLM 替代单一本地 interrogator 路径；四套内置提示词模板 + 自定义导入导出
- **描述生成**：LLM 打标「自然语言」模式（原 TAG2NL）批处理（LLM 并发 1–100），非外部脚本
- **角色数据集**：三步审查向导（少标法 / 全标法），本地规范化兜底
- **安全**：源数据只读；`_captioned` 与审查写盘均支持取消、单文件失败隔离、事务回滚

## 工作流

1. **文件 → 加载文件夹**
2. 编辑标签；遇到生词可开 **Danbooru Wiki**
3. **LLM 设置** 配置端点与模型
4. 按需：**工具 → LLM 打标…**（标签 / 标签→自然语言模式）/ **测试 → 打开角色标签审查...**

## LLM 设置

![LLM 设置](docs/images/llm-settings.png)

- **LLM 连接**：地址、密钥、超时、文本模型、连接/刷新、测速
- **视觉模型**：自动打标视觉模型、角色审查模型（建议 Gemini 等视觉模型）
- **LLM 并发**：外接 LLM 打标（标签与自然语言）统一并发；默认 5，范围 1–100
- **自然语言提示词（固定）**：只读，与打标模板独立

## 自动打标模板

![自动打标提示词模板](docs/images/auto-tag-prompt-templates.png)

内置 Danbooru Tag、自然语言、混合模式、自然语言2；可自定义、导出 JSON（不含凭据）。**LLM 打标**（标签模式）使用当前选中模板。

## LLM 打标

![LLM 打标](docs/images/llm-tagger.png)

- 入口：**工具 → LLM 打标…**、数据集右键 **LLM 打标**，或标签工具栏「自动生成标签」按钮
- 通用：输入来源（选中 / 全部）、视觉模型、**提示词模板**下拉、LLM 并发；**打标设置…** 打开完整提示词/参数，**LLM 设置…** 配置端点与模型
- **标签模式**：图片 → 标签，按写入模式（全部替换 / 追加 / 跳过已有）、排序、前后缀、下划线后处理，直接写回数据集
- **标签→自然语言模式（原 TAG2NL）**：标签 + 图片 → 自然语言描述
  - **输出格式**：**标签+自然语言**（默认，原 TAG2NL 格式）/ **仅自然语言**
  - **自然语言输出**：**另存副本**（默认）输出到 `数据集目录_captioned/`、源 `.txt` 只读、可跳过已有；**就地写回** 把结果写回原图 `.txt`（经数据集管理器，内存与磁盘一致）
  - **无标注先 ONNX**：勾选后，对没有标签的图片先用本地 WD14 ONNX 推标（模型未下载会提示下载），再交给 LLM 生成描述——标签→自然语言的自动流水线

## 角色 LoRA 标签审查

入口：**测试 → 打开角色标签审查...**

### 1. 参数与标准图

![审查向导 · 参数](docs/images/character-tag-audit-setup.png)

| 选项 | 说明 |
| --- | --- |
| 锁定触发词 | 角色 / LoRA trigger，强制保留 |
| 标签方法 | **少标法**：核心特征；**全标法**：全部正确细节 |
| 执行模式 | 复核并编辑 / 汇总并应用 |
| 最小出现次数 | 低于阈值标签不参与审查 |
| 标准图 | 视觉复核参考图 |

### 2. AI 审查

文本初筛 → 视觉复核；进度条按阶段更新。流程**不可回退**，改参数请取消后重开。

### 3. 复核并应用

![审查向导 · 复核](docs/images/character-tag-audit-review.png)

- 逐条判定：保留 / 删除 / 替换 / 不确定
- 理由可译中文；可换标准图 **重新视觉审查**
- 底部预览最终角色提示词（经 prompt-pyramid 规范化）
- **应用并保存**：事务写盘，失败回滚

**少标法**本地额外删除非核心项（细碎刘海、泛化头饰、轻微面部特征等），并在视觉已确认颜色时归一泛化服饰标签；**全标法**保留真实细节。

## ONNX 推标

入口：**工具 → ONNX 推标...**，或数据集右键 **ONNX 重新推标**。

![工具菜单](docs/images/tools-menu.png)

![ONNX 推标](docs/images/onnx-tagger.png)

![右键 ONNX 重新推标](docs/images/context-menu-onnx-retag.png)

- 模型选择：WD14 全系列 12 款 + PixAI 0.9；各模型阈值独立保存
- HuggingFace 官方或镜像下载；各模型设置自动保存
- 写入模式（全部替换 / 追加 / 跳过已有）与可选排序
- 后处理：下划线→空格（仅 ONNX 推理）、前缀/后缀标签
- 批量推标显示进度条；右键推标自动打开对话框并开始

## 视频处理

**工具 → 视频格式转换...** / **视频抽帧...**

![视频格式转换](docs/images/video-format-conversion.png)

![视频抽帧](docs/images/video-frame-extraction.png)

- 支持 mp4、mkv、avi、webm、mov、flv 等格式互转；可选替换原文件
- 全部帧 / 按 FPS / 原生 FPS / 指定帧号抽帧；带预览与锁定帧流程
- 抽帧结果自动导入数据集；Release 包内置 FFmpeg

## 背景移除

入口：**工具 → 背景移除**，或数据集右键 **移除背景**。

![背景移除](docs/images/background-removal.png)

- 客户端内置 RMBG-1.4 ONNX 本地抠图，**无需外部服务**；首次使用点击「下载并加载模型」一键下载（约 176 MB，或量化版约 44 MB，官方源 / 国内镜像），已下载则打开即自动加载
- 移除模式：全部图片 / 仅所选图片；背景：**透明**或**纯色**（默认白色、带取色器）
- 输出方式：**替换原图**或**另存 `_nobg.png` 副本**（选项记忆）；「移除测试」可先对单张预览效果
- 完成后自动刷新缩略图与预览（替换模式）或把副本导入数据集（另存模式）

## 多选标签校对

多选图片后按 **Shift+T** 打开可视化编辑器。

![多选标签编辑器](docs/images/multi-select-tag-editor.png)

- 左侧标签列表（含出现次数、按频次排序），点击即切换校对目标
- **绿框 = 已有该标签，红框 = 没有**；点击图片上的 是/否 即可增删
- 跨多个标签的修改，一次「保存」统一生效；右键图片可打开预览

## 其他

- **裁剪图片**：数据集右键 **裁剪图片** — 可画一个或多个选区，导出为同目录 `{文件名}_r1/_r2` …，并自动导入数据集
- **本地处理**：背景移除（RMBG-1.4 ONNX）与视频处理（内置 FFmpeg）均在本机完成，与 LLM 链路独立
- **隐私**：`settings.json` 本地保存；LLM 打标（含标签→自然语言）与角色审查会向所配端点发送图片

## 致谢

- **[starik222](https://github.com/starik222)** — [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) 原作者
- **[FFmpeg](https://ffmpeg.org/)** — 视频处理（GPL 组件，随 Release 打包）

## 安装

**推荐：** 从 [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) 下载 `BooruDatasetTagManagerPlus-*-win-x64.zip`，解压后运行 `BooruDatasetTagManagerPlus.exe`（自包含，无需单独安装 .NET）。

从源码构建：

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — 启动 Release（或 Debug）
- `quick_build.bat` — 本地快速打包至 `dist/`（产物不入库；首次构建会自动下载 FFmpeg）

本地运行后，程序目录下会生成 **Models/**（下载的 ONNX 模型）、**Cache/**（如视频缩略图缓存）、**settings.json**（API 与偏好设置）。这些均为本地运行时数据，**不会也不应提交到 Git**；ONNX 模型请在应用内下载。
