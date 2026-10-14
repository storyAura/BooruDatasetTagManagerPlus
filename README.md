<div align="center">

# BooruDatasetTagManager+ 1.2.8

面向 LoRA / 角色数据集的 Windows 打标工具

[English](README_en.md) · [Português do Brasil](docs/pt-BR/README_pt_BR.md)

[![Release](https://img.shields.io/github/v/release/storyAura/BooruDatasetTagManagerPlus?color=0078D7)](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D7) ![.NET](https://img.shields.io/badge/.NET-8-512BD4) [![License](https://img.shields.io/badge/license-MIT-2EA043)](LICENSE)

</div>

![BooruDatasetTagManager+](docs/images/promo.png)

每张图配一个同名 `.txt` 存放标签，加载文件夹就能改。也能用本地 Tag 推标或 LLM 自动打标、审查角色标签、用中文搜索 Danbooru 标签。界面默认简体中文。fork 自 [starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)。

[下载](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) · [功能详解](docs/FEATURES.md) · [发布说明](docs/RELEASE_NOTES_v1.2.8.md) · [问题反馈](https://github.com/storyAura/BooruDatasetTagManagerPlus/issues)

## 功能

- **标签编辑**：撤销 / 重做、权重、多选批量改；全部标签按次数统计，可用 AND / OR / NOT / XOR 筛图
- **中文搜标签**：内置约 10 万条通用标签和 33 万角色名单，输入「头发」就能找到 `long hair`、`black hair`；标签按类别着色，可筛选、排序
- **Tag 推标**：本地 ONNX 模型（WD14 / PixAI / CL / OppaiOracle），GPU 或 CPU 运行，图片不出本机
- **LLM 打标**：任意 OpenAI 兼容端点，输出标签或自然语言描述
- **角色标签审查**：AI 对照标准图删错标、补颜色，少标法 / 全标法，最多 4 个角色，逐条复核后才写入
- **图片处理**：图片编辑器、批量裁剪、多重切割（含 YOLO）、预分桶、背景移除
- **数据集整理**：kohya 文件夹分组浏览、相似图、坏图扫描、按标签分文件夹、视频抽帧
- **命令行**：同一个 exe 无窗口运行，可批量改标、推标、审查

## 快速开始

**环境要求**：Windows 10 / 11 x64。Release 包自带运行时，不用另装 .NET。只有 LLM 打标和角色标签审查需要 API，其余功能都能离线使用。

1. 从 [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) 下载 `BooruDatasetTagManagerPlus-*-win-x64.zip` 并解压
2. 运行 `BooruDatasetTagManagerPlus.exe`
3. **文件 → 加载文件夹**，选择你的数据集目录，就可以直接改标签了
4. 要用 LLM 功能时，先在顶栏 **LLM 设置** 填写端点、密钥和模型

| 想做什么 | 入口 |
| --- | --- |
| 自动打标 | 工具 → 标注工具 → Tag 推标 / LLM 打标 |
| 审查角色标签 | 工具 → 标注工具 → 角色标签审查 |
| 裁剪、抠图、分桶 | 工具 → 处理工具 / 预处理工具 |
| 写脚本批处理 | `BooruDatasetTagManagerPlus.exe help` |

每个功能的详细说明和截图见 **[功能详解](docs/FEATURES.md)**。

### 从源码构建

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
```

`quick_build.bat` 打包自包含版本到 `dist/`（首次会下载 FFmpeg）。

## 更新日志

- **1.2.8**（当前）— Tag 推标新增 PixAI Tagger v1.0 与 WD EVA02 2026 Canary，可选 GPU / CPU；图片标签查找浮窗（感谢 @furjifurji #1）；角色审查不再编造带颜色的标签，温度可调，网络错误自动重试。[发布说明](docs/RELEASE_NOTES_v1.2.8.md)
- **1.2.7** — Tag 推标新增 OppaiOracle；角色审查改为逐标签流程；修复多重切割 YOLO 漏报小框。[发布说明](docs/RELEASE_NOTES_v1.2.7.md)
- **1.2.6** — ONNX 推标改名 Tag 推标；工具菜单分组；Win10 原生库加载与大批量推标内存加固。[发布说明](docs/RELEASE_NOTES_v1.2.6.md)

更早的版本见 [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) 和 [docs](docs/) 下的发布说明。

## 常见问题

- **图片会上传吗？** Tag 推标、背景移除、视频处理都在本机完成。只有 LLM 打标和角色标签审查会把图片发到你配置的端点。
- **设置存在哪？** `文档\BooruDatasetTagManagerPlus\settings.json`，API 密钥用 DPAPI 加密。程序目录下的 `Models/`、`Cache/` 可以随时删，模型能在软件里重新下载。
- **遇到问题怎么反馈？** 在 设置 → 常规 打开调试模式，复现后把程序目录的 `debug.log`（崩溃时还有 `crash.log`）附到 [Issues](https://github.com/storyAura/BooruDatasetTagManagerPlus/issues)。

## 参与贡献

欢迎提 Issue 和 Pull Request。提交前请先跑通 `dotnet test`；新增的界面文字要同时加进 `BooruDatasetTagManager/Languages/` 下的 5 个语言文件。

## 致谢与许可

- **[starik222](https://github.com/starik222)** — [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) 原作者，本项目在其基础上开发
- **[FFmpeg](https://ffmpeg.org/)** — 视频处理（GPL 组件，随 Release 打包）
- 本项目遵循 [MIT License](LICENSE)；分发修改版构建时请保留原作者版权声明
