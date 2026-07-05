# BooruDatasetTagManager+ 1.0.4

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
| **中文工作流** | Danbooru 中文映射、Wiki 弹窗、审查理由翻译 |

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

## 其他

- **裁剪 / 去背景**：本地 AiApiServer（`http://127.0.0.1:50051`），与 LLM 链路独立
- **隐私**：`settings.json` 本地保存；打标/TAG2NL/审查均向所配端点发送图片

## 致谢

- **[starik222](https://github.com/starik222)** — [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) 原作者

## 安装

**推荐：** 从 [Releases](https://github.com/storyAura/BooruDatasetTagManagerPlus/releases) 下载 `BooruDatasetTagManagerPlus-*-win-x64.zip`，解压后运行 `BooruDatasetTagManagerPlus.exe`（自包含，无需单独安装 .NET）。

从源码构建：

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `test_start.bat` — 启动 Release（或 Debug）
- `quick_build.bat` — 本地发布至 `dist/`（产物不入库，请上传至 Releases）
