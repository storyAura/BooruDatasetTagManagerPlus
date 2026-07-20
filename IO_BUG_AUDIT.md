# BooruDatasetTagManager+ I/O 与崩溃风险审查报告

审查日期：2026-07-13  
审查范围：`BooruDatasetTagManager/` 主程序及相关 xUnit 测试  
审查方式：静态数据流审查、异常传播分析、并发/事务边界分析、现有构建与测试验证。

## 1. 结论摘要

当前版本已经具备不少正确的 I/O 防护：主 UI 有全局异常兜底，数据集标签保存使用临时文件替换，批处理大多会隔离单文件错误，图片加载也会复制内容并及时释放源句柄。因此，普通坏图或单次写入失败通常不会直接令整个进程退出。

但本次仍确认到 **8 项值得修复的 I/O 风险**：

| 编号 | 严重度 | 类型 | 结论 |
|---|---|---|---|
| IO-01 | 高 | 越界写入/删除 | 字符标签事务恢复信任磁盘清单中的绝对目标路径，可覆盖或删除数据集外文件 |
| IO-02 | 高 | 静默数据丢失 | “保存后退出/切换数据集”即使保存失败也继续退出或销毁旧数据集 |
| IO-03 | 中高 | I/O 故障放大 | 加载新数据集前先销毁旧数据集；一个锁定/无权限标签文件会使整次加载失败 |
| IO-04 | 中 | 并发写冲突 | `SafeFile` 对同一目标固定使用同一个 `.tmp`，并发保存会互相争用或写错版本 |
| IO-05 | 中 | 部分删除 | 删除图片与标签边车文件不是事务；第二步失败时图片已无法恢复 |
| IO-06 | 中 | 缓存损坏/丢记录 | 翻译缓存的追加与全量重写未使用统一文件锁，也不是原子写入 |
| IO-07 | 中 | 目录越界/操作异常 | 图片分类器允许分类名逃逸根目录，目录扫描和空序列也缺少错误处理 |
| IO-08 | 低到中 | 启动终止 | 语言目录枚举位于异常边界之外；目录存在但不可读时仍可能在消息循环启动前退出 |

“严重度”表示潜在影响，不表示每次操作都能稳定触发。IO-01 需要被篡改的数据集事务目录；IO-04/IO-06 需要并发时序；IO-02、IO-03、IO-05 可由磁盘满、只读文件、独占锁或权限变化等常见 I/O 故障触发。

## 2. 详细问题

### IO-01：事务恢复可写入或删除数据集目录之外的文件

**严重度：高**  
**位置：** `BooruDatasetTagManager/CharacterTagFileTransaction.cs:93-134`

提交新事务时，代码会调用 `EnsureWithinRoot` 校验目标：

```csharp
string target = Path.GetFullPath(change.TargetPath);
EnsureWithinRoot(root, target);
```

但恢复事务时，`manifest.json` 中的 `TargetPath` 被直接使用，没有再次校验它仍位于当前数据集根目录内：

```csharp
TransactionManifest manifest = JsonConvert.DeserializeObject<TransactionManifest>(
    await File.ReadAllTextAsync(manifestPath, cancellationToken)) ?? new TransactionManifest();
foreach (TransactionEntry entry in manifest.Entries.AsEnumerable().Reverse())
{
    if (entry.Existed)
    {
        string backup = Path.Combine(transactionDirectory, entry.BackupFile);
        if (File.Exists(backup))
        {
            string targetDirectory = Path.GetDirectoryName(entry.TargetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
                Directory.CreateDirectory(targetDirectory);
            File.Copy(backup, entry.TargetPath, true);
        }
    }
    else if (File.Exists(entry.TargetPath))
    {
        File.Delete(entry.TargetPath);
    }
}
```

而恢复是在每次加载数据集时自动发生的：

```csharp
CharacterTagFileTransaction.RecoverIncompleteAsync(folder).GetAwaiter().GetResult();
```

#### 触发条件

数据集内存在名称匹配 `.bdtm-character-tag-txn-*` 的目录，其 `manifest.json` 被篡改、损坏后被错误修复，或来自不可信压缩包/共享目录。`TargetPath` 可以是数据集之外的绝对路径。

#### 影响

- `Existed=true` 时可用事务目录里的备份内容覆盖任意当前用户可写文件。
- `Existed=false` 时可删除任意当前用户可删除文件。
- 该操作发生在“打开数据集”阶段，用户没有针对这些外部目标的确认。

#### 根因

提交路径执行了信任边界校验，恢复路径却将持久化清单当作可信内存状态。磁盘清单必须视为外部输入。

#### 修复建议

1. 将 `datasetRoot` 传入 `RecoverTransactionAsync`，对每个 `entry.TargetPath` 执行 `Path.GetFullPath` 与 `EnsureWithinRoot`。
2. 校验 `StagedFile`/`BackupFile` 只能是简单文件名，不能包含目录分隔符、`..` 或根路径。
3. 对无效清单停止恢复并隔离事务目录，不应继续删除或覆盖。
4. 增加“清单目标位于根目录之外时拒绝恢复”的单元测试。

---

### IO-02：保存失败后仍退出或切换数据集，导致未保存编辑丢失

**严重度：高**  
**位置：** `BooruDatasetTagManager/Form1.cs:734-744`、`1812-1823`

关闭主窗口时，用户选择“是”后会调用 `SaveAll()`，但无论是否有写入失败都不会取消关闭：

```csharp
if (result == DialogResult.Yes)
{
    Program.DataManager.SaveAll();
    ReportSaveErrorsIfAny();
}
else if (result == DialogResult.Cancel)
    e.Cancel = true;
```

切换数据集也有相同问题：

```csharp
if (result == DialogResult.Yes)
{
    Program.DataManager.SaveAll();
    ReportSaveErrorsIfAny();
}
// 随后继续选择和加载新目录
```

`DatasetManager.SaveAll()` 确实会收集失败项并保持其 `IsModified` 状态，但调用方只显示错误，没有把失败当作阻止退出/切换的条件：

```csharp
catch (Exception ex)
{
    LastSaveErrors.Add($"{item.Value.TextFilePath}: {ex.Message}");
}
```

#### 触发条件

任一标签文件只读、被其他程序独占、目录权限被撤销、磁盘已满、网络共享断开等；用户在关闭或切换时选择“保存”。

#### 影响

用户已经明确选择保存，但程序显示错误后仍关闭，内存中的标签编辑随进程退出而丢失。切换数据集时旧 `DatasetManager` 随后会被释放，效果相同。

#### 修复建议

- `SaveAll()` 应返回明确结果，例如 `SaveResult { SavedCount, Errors }`，不要用“至少成功一个文件”的 `bool` 表达整体成功。
- 关闭事件中只要 `LastSaveErrors.Count > 0` 就设置 `e.Cancel = true`。
- 切换数据集时保存失败应直接 `return`，允许用户修复权限、另存或选择明确的“放弃更改”。
- 增加只读/独占锁标签文件的 UI 逻辑测试或将“是否允许关闭”提取为可单测的纯逻辑。

---

### IO-03：新数据集加载失败会提前丢弃旧数据集；单文件错误会拖垮整次加载

**严重度：中高**  
**位置：** `BooruDatasetTagManager/Form1.cs:765-789`、`DatasetManager.cs:462-495`、`711-735`

主窗体先替换全局管理器并释放旧管理器，然后才真正读取新目录：

```csharp
DatasetManager oldDataManager = Program.DataManager;
Program.DataManager = new DatasetManager();
if (oldDataManager != null)
{
    gridViewDS.DataSource = null;
    oldDataManager.Dispose();
}

if (!await Program.DataManager.LoadFromFolderAsync(...))
{
    SetStatus(I18n.GetText("TipFolderWrong"));
    return;
}
```

加载本身使用 `Directory.GetFiles(..., AllDirectories)` 和 PLINQ。任一 `DataItem.LoadData` 抛出的异常会终止整次并行查询：

```csharp
string[] imgs = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);

imgs.AsParallel()
    .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount / 2))
    .ForAll(x =>
{
    var dt = new DataItem();
    dt.LoadData(x, loadPreviewImages ? imgSize : 0, readMetadata);
    DataSet.TryAdd(dt.ImageFilePath, dt);
});
```

其中标签读取没有逐文件异常隔离：

```csharp
if (File.Exists(TextFilePath))
{
    TagsModifyTime = File.GetLastWriteTime(TextFilePath);
    string text = File.ReadAllText(TextFilePath);
    var temp_tags = PromptParser.ParsePrompt(text, ...);
    Tags.LoadFromPromptParserData(temp_tags);
}
```

#### 触发条件

- 子目录无访问权限或遍历期间被删除。
- 任一 `.txt` 标签文件被独占锁定。
- 元数据读取器遇到异常文件。
- 网络盘/移动盘在扫描期间断开。

#### 影响

程序通常不会进程级崩溃，因为外层已有 `catch`；但整个新数据集加载失败，并且旧数据集视图和内存对象已经被销毁。新管理器还可能保留并行加载成功的部分项，形成不完整状态。

#### 修复建议

1. 在局部变量 `candidateManager` 中完成新数据集加载，成功后再原子替换 `Program.DataManager` 并释放旧对象。
2. 使用可容错枚举，逐目录捕获 `UnauthorizedAccessException`、`DirectoryNotFoundException`、`IOException`。
3. 在单个 `DataItem` 粒度收集错误并继续加载其他文件；最终向用户显示“成功 N、失败 M”。
4. 若策略要求全有或全无，失败时必须释放候选管理器并完整保留旧管理器。

---

### IO-04：`SafeFile` 固定 `.tmp` 文件名，不支持同目标并发写

**严重度：中**  
**位置：** `BooruDatasetTagManager/SafeFile.cs:16-40`

三个写入方法都由目标路径直接拼接同一个 `.tmp`：

```csharp
public static void WriteAllText(string path, string contents, Encoding encoding)
{
    string tmp = path + ".tmp";
    File.WriteAllText(tmp, contents, encoding);
    ReplaceOrMove(tmp, path, null);
}

public static void WriteAllBytes(string path, byte[] bytes)
{
    string tmp = path + ".tmp";
    File.WriteAllBytes(tmp, bytes);
    ReplaceOrMove(tmp, path, null);
}
```

#### 触发条件

两个任务同时保存同一标签/设置/图片目标。例如主窗体手动保存与 LLM/ONNX 窗口后台 `SaveAll()` 重叠，或重复触发相同输出任务。

#### 可能时序

1. A 写入 `x.txt.tmp`。
2. B 截断并写入同一个 `x.txt.tmp`，或因共享模式获得 `IOException`。
3. A 移动 B 的内容到 `x.txt`。
4. B 再移动时发现临时文件不存在。

最终可能出现保存失败、最后写入者语义被破坏，或调用方认为 A 成功但落盘的是 B 的内容。

此外，写入临时文件成功、替换目标失败时没有 `finally` 清理；`.tmp` 会残留。后续写入可覆盖它，但诊断和并发行为会更混乱。

#### 修复建议

- 使用同目录唯一临时名，例如 `path + "." + Guid.NewGuid().ToString("N") + ".tmp"`。
- 用 `try/finally` 删除本次调用创建的临时文件。
- 如业务要求严格的最后写入者语义，对规范化目标路径使用按路径 `SemaphoreSlim`。
- 增加两个任务并发写同一目标及替换失败后无残留的测试。

---

### IO-05：删除图片与标签文件不是一个事务，失败后出现“半删除”

**严重度：中**  
**位置：** `BooruDatasetTagManager/Form1.cs:2213-2231`

```csharp
try
{
    if (File.Exists(file))
        File.Delete(file);
    if (File.Exists(tagFile))
        File.Delete(tagFile);
    deletedPaths.Add(file);
    deletedPathSet.Add(file);
}
catch (Exception ex)
{
    Trace.WriteLine($"Failed to delete '{file}': {ex}");
    failedPaths.Add(file);
}
```

#### 触发条件

图片可删除，但标签文件只读、被锁或删除标签时磁盘/共享发生错误。

#### 影响

图片已经永久删除，异常处理却把该项归为“删除失败”并保留在内存数据集中。UI 中仍有数据项和标签，但图片路径已经不存在。反向顺序只会把问题变成“标签已删、图片还在”，不能解决事务性。

#### 修复建议

- 优先移入同卷的应用回收/暂存目录，两个文件都成功移动后再从数据集移除；失败则移回。
- 或使用 Windows 回收站 API，让用户仍可恢复。
- 无论采用何种策略，都应根据每个文件的实际结果更新数据集状态，而不是只用一个 `try` 表示二者共同成功。
- 增加“第二个文件删除失败”的故障注入测试。

---

### IO-06：翻译缓存追加与重写缺少统一持久化锁和原子替换

**严重度：中**  
**位置：** `BooruDatasetTagManager/TranslationManager.cs:172-214`

内存字典通过 `cacheSync` 保护，但锁在文件 I/O 之前已经释放：

```csharp
lock (cacheSync)
{
    // 修改 translationCache / Translations
}

if (rewriteFile)
    await RewriteTranslationsFileAsync();
else
    await File.AppendAllTextAsync(translationFilePath,
        $"{(isManual ? "*" : "")}{orig}={trans}\r\n", Encoding.UTF8);
```

重写直接截断目标文件：

```csharp
await File.WriteAllTextAsync(
    translationFilePath,
    builder.ToString(),
    Encoding.UTF8);
```

`translationLocker` 只包围 `TranslateAsync`，公开的 `AddTranslationAsync` 不获取该锁。因此追加与重写可以并发发生。

#### 影响

- 两次追加可能发生共享冲突并抛出 `IOException`。
- 全量重写可覆盖刚完成的追加，造成缓存记录只存在于内存、重启后消失。
- 重写期间崩溃、磁盘满或进程终止会留下截断文件。

#### 修复建议

使用独立的 `persistenceLocker` 序列化所有翻译文件操作；在锁内根据最新内存快照通过 `SafeFile`/唯一临时文件原子重写。若保留追加模式，也必须确保追加与重写共享同一把锁。

---

### IO-07：图片分类器的目录名可逃逸目标根目录，扫描操作也容易抛异常

**严重度：中**  
**位置：** `BooruDatasetTagManager/ImageSorter.cs:39-50`、`99-130`，`Form_ImageSorterSettings.cs:27-52`、`92-105`，`Form_ImageSorter.cs:109-138`

分类节点名称直接来自文本框：

```csharp
selectedNode.Nodes.Add(textBoxNodeName.Text, textBoxNodeName.Text);
```

随后名称被拼成相对路径，但没有验证 `..`、根路径或非法字符：

```csharp
return string.Join("\\", tempLst);

string dstDir = Path.Combine(RootDir, indexedSortItems[item.Key].Path);
Directory.CreateDirectory(dstDir);
File.Copy(file, GetDstFile(file, dstDir));
```

输入 `..\\outside` 或根路径可以使文件被复制到所选 `RootDir` 之外。虽然目标文件名由数字生成且 `File.Copy` 默认不覆盖，但仍违反了 UI 所声明的根目录边界。

另外，拖入目录时下列异常没有局部捕获：

```csharp
FileAttributes attr = File.GetAttributes(file);
string[] imgs = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
```

“计算下一个索引”在没有数字文件名时还会对空列表调用 `Max()`：

```csharp
textBoxIndex.Text = (indexes.Max() + 1).ToString();
```

全局 WinForms 异常处理通常能避免进程退出，但操作会中断并弹出通用未处理错误。

#### 修复建议

- 分类名只允许单个合法目录段：拒绝空值、`.`、`..`、分隔符、根路径和 `Path.GetInvalidFileNameChars()`。
- 创建目录前对 `Path.GetFullPath(Path.Combine(root, relative))` 执行根目录包含校验。
- 目录枚举改为逐目录容错并显示失败清单。
- 空索引时使用配置起始值或 `DefaultIfEmpty(0).Max() + 1`。

---

### IO-08：语言目录枚举异常可在启动消息循环之前逃逸

**严重度：低到中**  
**位置：** `BooruDatasetTagManager/LanguageManager.cs:17-38`、`I18n.cs:18-42`、`Program.cs:52`

`LanguageManager` 只处理“目录不存在”，但目录枚举本身在 `try` 外：

```csharp
if (!Directory.Exists(langDir))
{
    Trace.WriteLine($"LanguageManager: directory not found: {langDir}");
    return;
}
string[] files = Directory.GetFiles(langDir, "*.txt", SearchOption.TopDirectoryOnly);
foreach (string file in files)
{
    try
    {
        LoadLanguageFromFile(file);
    }
    catch (Exception ex)
    {
        Trace.WriteLine($"LanguageManager: failed to load '{file}': {ex}");
    }
}
```

`I18n.Initialize` 的注释称“每一步都回退”，但 `new LanguageManager()` 并未置于异常处理内：

```csharp
if (langManager == null)
{
    langManager = new LanguageManager();
}
```

它由 `Program.Main` 在 `Application.Run` 之前调用。若 `Languages` 目录存在但 ACL 禁止读取、目录正被重命名、网络安装目录断开，`UnauthorizedAccessException`/`IOException` 可在主消息循环开始前终止启动。`AppDomain.UnhandledException` 最多记录崩溃，不能恢复程序。

#### 修复建议

将目录检查、枚举和 `FixOldLangFilesFromDefault` 一并放入异常边界；`I18n.Initialize` 也应对 `LanguageManager` 构造失败回退到空字典，并向用户显示一次可理解的启动警告。

## 3. 已有防护与非问题

本次没有把以下代码误报为缺陷：

- `ImageLoader` 使用 ImageSharp 加载后转换为独立 `Bitmap`，不会长期锁住原图片文件。
- `DatasetManager.SaveAll` 对单个标签写失败会继续处理其他文件，并保留失败项的修改标记。
- `Program` 注册了 `Application.ThreadException`、`AppDomain.UnhandledException` 和 `TaskScheduler.UnobservedTaskException`，能覆盖大量未处理异常。
- 背景替换、背景移除、模型下载、更新包下载已经采用临时文件或部分文件，明显降低了截断正式文件的风险。
- `CharacterTagFileTransaction.CommitAsync` 在正常提交入口会检查目标位于数据集根目录；问题仅在恢复入口没有重复验证不可信清单。

全局异常处理只能防止部分进程级退出，不能恢复已经发生的删除、覆盖、旧管理器释放或内存编辑丢失，因此不能替代局部事务和错误返回值。

## 4. 测试与构建结果

执行命令：

```powershell
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj --no-restore
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows --no-restore
```

结果：

- 测试：**264 通过，0 失败，0 跳过**。
- 构建：**成功，0 错误，37 警告**。
- 警告主要包括旧包目标框架兼容性、nullable 注解上下文、`Equals`/`GetHashCode` 配对和 WebP 原生指针恒假比较；本报告未把这些编译警告直接认定为 I/O 缺陷。

现有测试已覆盖 `SafeFile` 的单线程正常写、替换、备份和锁定目标不截断，也覆盖字符标签事务的正常提交/失败恢复。尚缺以下关键故障注入：

1. 恶意或损坏事务清单中的数据集外目标。
2. 保存失败时是否取消关闭/切换。
3. 同一目标的两个并发 `SafeFile` 写入。
4. 单个标签文件被独占锁定时的数据集加载策略。
5. 图片删除成功但标签删除失败。
6. 翻译缓存追加与重写并发。
7. 图片分类名中的 `..`、根路径和非法字符。

## 5. 建议修复顺序

1. **立即修复 IO-01**：恢复前重新校验所有目标和事务内文件名。
2. **随后修复 IO-02**：任何保存失败都必须阻止默认关闭/切换。
3. **一起处理 IO-03**：候选数据集加载成功后再替换旧管理器，并决定逐文件容错策略。
4. **修复 IO-04、IO-06**：统一唯一临时文件与按目标/按缓存文件的持久化锁。
5. **修复 IO-05、IO-07、IO-08**：完善删除事务、分类器路径约束和启动容错。

推荐每项先加入可稳定失败的回归测试，再修改实现，避免将“隐藏异常”误当作“修复数据一致性”。
