# BooruDatasetTagManager+ 1.1.1

[English](README.md)

面向 LoRA / 角色数据集的 Windows 标签管理工具。保留原版「加载文件夹 → 编辑同名 `.txt`」流程，并集成 LLM 视觉打标、TAG2NL、角色标签审查与中文标签工作流。**首次启动默认为简体中文界面**。

![主界面](docs/images/main-window-wiki.png)

## 项目渊源

本仓库 fork 自 **[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)**，保留原版「加载文件夹 → 编辑同名 `.txt`」核心流程，并在此基础上扩展 LLM 打标、TAG2NL、角色标签审查与中文工作流。

遵循 [MIT License](LICENSE)。分发修改版构建时请保留原作者版权声明。

## 核心能力

| 模块 | 功能 |
| --- | --- |
| **LLM 设置** | OpenAI 兼容端点；文本 / 打标视觉 / 审查视觉模型分离；TAG2NL 并发与固定提示词 |
| **LLM 打标** | 对全部图片批量 AI 视觉打标 |
| **AI 视觉打标** | 单图或选中图片打标，可选提示词模板 |
| **TAG2NL** | 标签 + 图片 → 自然语言描述，输出至同级 `_captioned` 目录 |
| **角色标签审查** | 触发词 + 标准图 + 全库 inventory，AI 两阶段审查后写回数据集 |
| **ONNX 推标** | WD14 + PixAI 统一界面；HuggingFace 下载；双阈值；写入模式；前后缀标签 |
| **视频处理** | 格式转换（mp4/mkv/avi/webm 等）；全部帧 / 按 FPS / 指定帧抽帧；自带 FFmpeg |
| **背景移除** | 工具菜单与数据集右键；需本地 AiApiServer + rmbg2 模型 |
| **裁剪图片** | 单次或多重裁切；导出至原图同目录 `_r1/_r2`；完成后自动导入数据集 |

## 1.1.1 更新内容

- **角色标签审查保存加速** — 减少 manifest 重复写入；AllTags 批量更新；保存进度提示；轻量刷新
- **统一裁剪图片** — 单次与多重裁切合并为同一对话框；右侧实时预览；Delete 删除选区
- **同目录导出** — `{basename}_r1.png`、`_r2.png` … 与原图同文件夹；不覆盖原图
- **自动导入** — 裁切完成后新图片立即出现在数据集列表中

上一版本：[v1.1](docs/RELEASE_NOTES_v1.1.md)（WD14 全系列、PixAI 修复、ONNX 批量推标优化）。

## 1.1 更新内容

- **WD14 全系列模型** — 模型列表含 12 款 WD14（vit-large v3、eva02-large v3 等）
- **PixAI v0.9 修复** — 正确读取 prediction/logits 输出；v0.9 CSV；WebP 加载；DirectML 失败回退 CPU
- **WD14 按模型独立阈值** — vit-large 默认 0.26，不再误用 eva02 的 0.52
- **大模型稳定性** — WD14 DirectML 挂起时自动回退 CPU；显式 output 名；NHWC/NCHW 输入尺寸
- **批量推标修复** — 推理与写标签分离，修复中途 BindingSource 循环引用错误
- **进度与耗时** — 进度条显示上次/平均/剩余/已用推理时间
- **下载体验** — 模型下载完成提示，状态恢复为可推标
- **减少界面卡顿** — 推标结束后后台保存；单图选中时同步刷新标签列表

上一版本：[v1.0.5](docs/RELEASE_NOTES_v1.0.5.md)（统一 ONNX 推标、视频工具、内置 FFmpeg）。

## 1.0.5 更新内容

- **统一 ONNX 推标** — WD14（eva02-large v3）与 PixAI 0.9 合并为同一对话框；支持 HuggingFace / 镜像下载；通用阈值与角色阈值分离
- **推理修复** — WebP 通过 ImageLoader 加载；修复 v3 `selected_tags.csv` 解析；预处理对齐官方 wd-tagger
- **标签后处理** — 可选下划线→空格（仅 ONNX）；前缀/后缀标签适用于全部写入路径
- **右键 ONNX 重新推标** — 打开推标对话框并显示进度条，自动对选中图片开始推标
- **视频工具** — 格式转换与视频抽帧（预览、锁定帧、原生 FPS）；Release 包内置 FFmpeg
- **界面精简** — 移除文件浏览输入源及 ONNX 设置中的无用提示文字

## 与原版差异

- **打标**：OpenAI 兼容 LLM 替代单一本地 interrogator 路径；四套内置提示词模板 + 自定义导入导出
- **描述生成**：原生 TAG2NL 批处理（并发 1–100），非外部脚本
- **角色数据集**：三步审查向导（少标法 / 全标法），本地规范化兜底
- **安全**：源数据只读；`_captioned` 与审查写盘均支持取消、单文件失败隔离、事务回滚

## 工作流

1. **文件 → 加载文件夹**
2. 编辑标签； unfamiliar 词可开 **Danbooru Wiki**
3. **LLM 设置** 配置端点与模型
4. 按需：**LLM 打标** / **TAG2NL** / **测试 → 角色标签审查**

## LLM 设置

![LLM 设置](docs/images/llm-settings.png)

- **LLM 连接**：地址、密钥、超时、文本模型、连接/刷新、测速
- **视觉模型**：自动打标视觉模型、角色审查模型（建议 Gemini 等视觉模型）
- **TAG2NL 并发数**：仅影响 TAG2NL；默认 5
- **TAG2NL 提示词**：固定只读，与打标模板独立

## 自动打标模板

![自动打标提示词模板](docs/images/auto-tag-prompt-templates.png)

内置 Danbooru Tag、自然语言、混合模式、自然语言 2；可自定义、导出 JSON（不含凭据）。**LLM 打标**与单图打标共用当前选中模板。

## TAG2NL

- 菜单：**工具 → TAG2NL**
- 输出：`数据集目录_captioned/`，源 `.txt` 只读
- 格式：`原始标签` + 换行 + `自然语言描述`
- 默认跳过已有文件；可勾选重新生成（原子替换）

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

## 其他

- **裁剪图片**：数据集右键 **裁剪图片** — 可画一个或多个选区，导出为同目录 `{文件名}_r1/_r2` …，并自动导入数据集
- **去背景 / 视频**：本地 AiApiServer 提供去背景（`http://127.0.0.1:50051`）；视频处理使用内置 FFmpeg，与 LLM 链路独立
- **隐私**：`settings.json` 本地保存；打标/TAG2NL/审查均向所配端点发送图片

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
