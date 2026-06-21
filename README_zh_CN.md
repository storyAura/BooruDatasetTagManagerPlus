<div align="center">

[English](./README.md) | **中文简体** | [Portugues do Brasil](./docs/pt-BR/README_pt_BR.md)

</div>

# BooruDatasetTagManagerPlus

BooruDatasetTagManagerPlus 是 [starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) 的 fork，当前仓库为 [storyAura/BooruDatasetTagManagerPlus](https://github.com/storyAura/BooruDatasetTagManagerPlus)。

它是一个面向 booru 风格数据集的标签编辑器，可用于编辑 LoRA、embedding、hypernetwork 等图像模型训练数据集的 caption/tag 文本文件，也可以从只有图片的文件夹开始新建标签文件。

这个 fork 保留原项目的数据集浏览、标签编辑、批量替换、自动标签、翻译、多语言界面等工作流，并加入了更适合中文用户的 tag 查询、翻译容错和 Danbooru Wiki 查询能力。

## 与上游项目的关系

- 上游项目：[starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)
- 当前 fork：[storyAura/BooruDatasetTagManagerPlus](https://github.com/storyAura/BooruDatasetTagManagerPlus)
- 开源协议：MIT。原项目的版权声明和许可文本保留在 [LICENSE](./LICENSE) 中。
- 本 fork 的目标是在保持上游数据集工作流兼容的基础上，增强中文用户体验、tag 查询效率和翻译稳定性。

## 与原仓库的主要区别

相比上游 BooruDatasetTagManager，本 fork 当前主要调整包括：

- 项目标识统一为 `BooruDatasetTagManagerPlus`，包括应用窗口标题、程序集产品名、输出 exe 名称和 README 文档。
- 增加翻译 fallback 链：多个免费翻译接口按顺序尝试，并支持单接口请求超时配置。
- 增加统一的 tag 右键操作入口，包括 Danbooru Wiki 查询和重新翻译 tag。
- 增加 Danbooru Wiki 浮窗：清理 Danbooru DText 正文、支持 Wiki 正文翻译，并保留浏览器打开兜底。
- 增加简体中文 tag 查找：当界面语言为 `zh-CN` 时，可在添加/替换标签输入框中用中文别名补全到英文 tag。
- 增加 `BooruDatasetTagManager/Data/danbooru-0-zh.csv` 作为中文到英文 tag 的查询数据源。
- 增加 [docs/UI_STRUCTURE_zh_CN.md](./docs/UI_STRUCTURE_zh_CN.md)，记录当前 WinForms UI 结构，方便后续重构。
- 增加本地快速启动/构建脚本：[test_start.bat](./test_start.bat)。

## 使用方式

准备一个包含图片和同名 `.txt` 标签文件的数据集文件夹。也可以只准备图片，BooruDatasetTagManagerPlus 会在保存时创建标签文件。

在程序中选择：

```text
File -> Load folder
```

然后选择数据集目录。左侧是图片列表，中间是当前图片 tags，右侧包含 All/Common tags 和 AutoTagger 预览。

编辑完成后选择：

```text
File -> Save all changes
```

可以一次选择多张图片，方便对相似图片批量编辑 tags。

## 标签翻译

在设置中选择翻译目标语言和翻译服务后，可以在下面的菜单中显示 tag 翻译列：

```text
View -> Translate tags
```

当前版本使用 fallback 翻译链，默认优先中文友好的接口，然后依次尝试 MyMemory、Google JSON API、旧 Google Mobile HTML。每个接口都有超时控制，默认 5 秒。

翻译缓存保存在 `Translations` 文件夹中。手动翻译可以用 `*` 标记，右键重新翻译不会覆盖手动翻译。

## 标签补全与中文查找

BooruDatasetTagManagerPlus 会从程序目录下的 `Tags` 文件夹加载 A1111 tag autocomplete 格式的 CSV 或逐行 tag TXT 文件，并生成内部缓存以加快启动。

简体中文界面下还会加载：

```text
BooruDatasetTagManager/Data/danbooru-0-zh.csv
```

这份文件用于“中文查找英文 tag”，不会作为普通 tag 库导入。格式为：

```csv
english_tag,中文名|中文别名
```

在添加标签或替换标签时：

- 输入中文会出现类似 `长发 -> long hair` 的候选。
- 选择候选后写入的是英文 tag。
- 如果直接输入完整中文并确认，精确匹配时会自动转成英文 tag。
- 如果没有匹配，就保留原输入，不强行替换。

## Danbooru Wiki 查询

在 tag 表格中右键 tag，选择“查询 Danbooru Wiki”即可打开浮窗。

浮窗会显示：

- tag 标题
- other names
- 更新时间
- 清理后的 Wiki 正文
- 翻译 Wiki
- 在浏览器打开

即使 Wiki 接口加载失败，“在浏览器打开”按钮也会保持可用。

## AutoTagger

BooruDatasetTagManagerPlus 可以通过内置的 AiApiServer 为图片生成 tags。进入 `AiApiServer` 目录后安装依赖：

```bash
pip install -r requirements.txt
```

启动服务：

```bash
python main.py
```

然后可以在程序的 `Tools` 菜单、tag 工具栏按钮或 AutoTagger 预览页中生成 tags。

## 当前 UI 结构

后续 UI 重构前，可以先阅读：

```text
docs/UI_STRUCTURE_zh_CN.md
```

这份文档记录了当前 WinForms 主界面、主要表单、数据流和右键菜单扩展点。

## 构建

需要安装 .NET 8 SDK。命令行构建：

```bash
dotnet build BooruDatasetTagManager/BooruDatasetTagManager.csproj -c Release
```

运行测试：

```bash
dotnet test BooruDatasetTagManager.sln -c Release
```

也可以直接双击根目录的 [test_start.bat](./test_start.bat)。当前输出程序名为 `BooruDatasetTagManagerPlus.exe`。
