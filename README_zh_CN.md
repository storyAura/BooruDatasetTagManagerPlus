# BooruDatasetTagManager+

[English](README.md)

BooruDatasetTagManager+ 是一款用于检查、检索、翻译和生成图像数据集标签的 Windows 桌面工具。它基于原版 BooruDatasetTagManager，在保留核心标签编辑流程的同时，加入统一的 OpenAI 兼容自动打标、原生 C# 描述生成、更可靠的批处理控制和更完整的中文工作流。

![功能总览](docs/images/feature-overview.png)

## 项目定位及上游关系

Plus 版仍以原版 WinForms 数据集编辑器为核心：打开数据集目录、查看图片并编辑同名 `.txt` 标签文件。源图片在浏览与标注流程中按只读数据处理；功能范围聚焦于数据集整理与标注，不包含图像生成能力。

本仓库是社区分支。重新分发修改后的构建前，请同时查看上游项目和本仓库的许可证。

## 与原版的区别

| 范围 | 原版流程 | Plus 版流程 |
| --- | --- | --- |
| 自动打标 | 以本地识别服务配置为主 | 一套 OpenAI 兼容连接和可选择的提示词模板 |
| 提示词管理 | 固定任务提示词 | 四个内置模板，以及可编辑、导出的自定义模板 |
| 描述转换 | 外部脚本或手动流程 | 原生 C# LLM-T2NL 批处理 |
| 描述文件 | 仅生成描述 | 源标签原文、一个换行、自然语言描述 |
| 批处理安全 | 取决于具体工具 | 同级 `_captioned` 输出、原子替换、可取消、单文件失败隔离 |
| 处理速度 | 顺序描述流程 | LLM-T2NL 并发可设 1–100，默认 5 |
| 中文数据集 | 基础标签编辑 | 中文别名查询、翻译回退和内置 Danbooru 标签数据 |
| 参考工具 | 本地编辑 | 集成 Danbooru Wiki 与改进后的快速替换/测试窗口 |
| 裁剪与去背景 | 本地 AiApiServer | 仍由专用本地 AiApiServer 链路提供 |

## 数据集工作流

1. 打开数据集目录。图片可以位于子目录中，同名 `.txt` 文件视为标签文件。
2. 选择图片后编辑、搜索、翻译、添加、删除或调整标签。
3. 切换数据集或启动批处理前保存修改。
4. 通过工具栏或菜单执行自动打标、Wiki 查询、快速替换、裁剪和去背景。
5. 需要为完整数据集生成自然语言训练描述时，使用“工具 → LLM-T2NL”。

![主界面](docs/images/main-window.png)

## AiServerSet

AiServerSet 是 OpenAI 兼容功能的统一配置窗口。填写 HTTP/HTTPS API 基础地址（通常以 `/v1` 结尾）、必要时填写 API 密钥，并设置超时和模型。“连接 / 刷新模型”会查询当前输入的端点；“测速”发送一次短文本请求并统计模型响应耗时，该操作与持久化保存相互独立，当前草稿配置保持未提交状态。

全新构建中的端点、密钥、模型、系统提示词和用户提示词均为空。端点按用户输入的原值使用，不执行地址推断或自动重写；网页响应、无效 JSON、认证失败、超时和网络错误会分类提示，刷新失败时保留既有模型配置。

LLM-T2NL 并发数在此设置，范围为 1–100。它只影响 LLM-T2NL；自动打标预览和测速仍是单请求。

![连接字段为空的 AiServerSet](docs/images/ai-server-set.png)

## 自动打标提示词模板

单图、选中图片和全数据集自动打标统一使用 AiServerSet 当前选择的模板。默认提供 Danbooru Tag、Natural Language、Mixed Mode 和 Natural Language 2 四个内置模板。

内置模板允许修改并保存内容，但不能改名或删除，并可恢复出厂文本。自定义模板可以新建、改名、编辑、保存和删除。名称与内容不能为空，名称忽略大小写后不得重复。“导出当前”可导出当前模板；“导出全部自定义”只导出自定义模板。版本化 JSON 仅包含模板元数据与内容，不包含端点、API 密钥或模型。

## 原生 LLM-T2NL

LLM-T2NL 使用配置好的 OpenAI 兼容视觉模型，为当前已加载的完整数据集生成自然语言描述。它始终使用独立、固定的自然语言任务提示词，不受自动打标模板选择影响。

执行前，程序会提示保存未提交的标签修改，并显示扫描摘要。默认跳过已有输出；勾选“重新生成已有输出”后会原子替换已生成文本。处理过程支持取消；单文件异常会被隔离并记录，其余任务继续执行。进度窗口会显示当前文件、成功、跳过和失败数量。

如果数据集位于 `D:\datasets\example`，输出目录就是 `D:\datasets\example_captioned`。处理采用源数据只读策略，所有生成内容均限定写入同级输出目录。每个生成的 `.txt` 采用以下格式：

```text
original_tag_text, 保持原样
根据图片和清理后参考标签生成的自然语言描述。
```

源标签的顺序、下划线、空格和分隔符均保持不变；拼接前仅移除末尾换行，并在标签与描述之间加入恰好一个换行。标签文件不存在或仅为空白时，只写自然语言描述。重新生成操作仅原子替换目标文本，输出目录中已复制的图片保持现状。

![LLM-T2NL 确认与进度界面](docs/images/llm-t2nl.png)

## 标签检索、翻译与 Wiki

- 搜索和筛选当前数据集标签，同时保持图片与标签导航同步。
- 通过内置 Danbooru 映射查询中文别名，保存时仍保留原始标签值。
- 本地没有直接匹配时使用翻译回退与翻译缓存。
- 在编辑流程中直接查询不熟悉标签的 Danbooru Wiki 说明。
- 使用支持 DPI 的“测试/快速替换”窗口配置替换规则和翻译 CSV 选项。

## 裁剪与去背景

自动裁剪与去背景使用配套的本地 AiApiServer，其默认地址为 `http://127.0.0.1:50051`。该服务具有独立的功能边界，专用于图像裁剪与去背景；OpenAI 兼容自动打标和 LLM-T2NL 由 AiServerSet 链路负责。

## 安装与启动

程序面向 Windows 和 .NET 8。使用打包版本时，解压自包含压缩包并运行 `BooruDatasetTagManagerPlus.exe`；`dist` 发布包不要求系统预装 .NET。裁剪/去背景需要配套 AiApiServer 依赖；OpenAI 兼容功能需要用户自行提供端点与模型。

`test_start.bat` 会优先启动 Release 输出，其次使用 Debug，均不存在时自动构建。`quick_build.bat` 会将自包含 Release 发布到 `dist`。

## 隐私与配置

- API 配置保存在本地 `settings.json`；填写凭据后不要共享该文件。
- 仓库默认设置和发布产物不包含 OpenAI 端点、API 密钥、模型或用户提示词。
- 自动打标和 LLM-T2NL 会把图片发送到你配置的端点；处理私有数据前请确认服务提供方的隐私政策。
- LLM-T2NL 的写入范围限定为同级 `_captioned` 目录，源标签文件在该流程中保持只读。

## 构建、测试与发布

在仓库根目录运行：

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```
