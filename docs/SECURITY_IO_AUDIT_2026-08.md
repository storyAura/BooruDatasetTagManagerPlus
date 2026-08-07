# BooruDatasetTagManager+ 安全 / I/O / 性能审查报告

- **审查日期**: 2026-08-03
- **审查范围**: 当前工作树（Assembly `1.2.3`；安全/I/O 修复归入 v1.2.3，相对 GitHub 已发布 `v1.2.1`）
- **审查方式**: 静态代码审阅 + 现有回归测试对照（`IoBugAuditRegressionTests` 等）
- **威胁模型**: 桌面本地 WinForms 应用。攻击面主要是「本机恶意/损坏文件」「被篡改的配置/事务清单」「用户配置的任意 HTTP 端点」「GitHub/HF 下载内容」，不是经典互联网暴露式 SSRF/RCE 服务。

### 修复状态（2026-08-03）

以下审计项已落地代码修复，并由 `SecretProtectorTests` / `SecurityIoAuditFixTests` 覆盖：

| ID | 状态 |
|----|------|
| H-1 DPAPI 明文回退 | 已修：Protect 失败抛错，`SaveSettings` 中止写入 |
| H-2 LLM WebP/视频帧未 Dispose | 已修 |
| M-1 RenameFolder `..` | 已修：`IsSafeRelativeFolder` + root containment |
| M-2 HF 下载竞态 | 已修：按路径 `SemaphoreSlim` |
| M-3 更新包 fileName | 已修：`ResolveDownloadTarget` |
| M-4 审计 category 信任模型 | 已修：本地 `TagSemanticClassifier` 门控 |
| M-5 Caption 输出逃逸 | 已修 |
| M-6 ffmpeg 日志无上限 | 已修：`BoundedStringLog` 64KB |
| L-1 主题/TagsDB 非原子写 | 已修：改 `SafeFile` |
| L-2 Explorer 拼参 / L-3 junction | 未改（低优先级） |
| 性能 P0–P2 | 未改（另排期） |

---

## 1. 结论摘要

项目在 **2026-07 I/O 审计** 后已经显著加固：事务恢复信任边界、`SafeFile` 并发写、图片+侧车删除事务、翻译持久化、图片分拣路径注入、容忍扫描、数据集切换保存失败中止等，均有回归测试锁死。

本次审查**未发现可远程无交互 RCE 的恶性漏洞**。下列 High/Medium 项已在同日修复（见上表）；性能侧（相似图 O(n²)、全分辨率缓存、AllTags 过滤二次方、数据集重复全表扫描）仍待另排期。

| 等级 | 数量 | 说明 |
|------|------|------|
| Critical（远程/无交互恶性） | 0 | 无 |
| High | 2 | 已修：GDI 泄漏；密钥 Protect 失败写明文 |
| Medium | 6 | 已修：路径穿越、下载竞态、更新包文件名、审计分类、输出逃逸、ffmpeg 日志 |
| Low | 3 | L-1 已修；Explorer 回退拼参、junction TOCTOU 仍待 |
| 优化机会 | 8 | 见第 5 节（未在本轮实现） |

---

## 2. 已修复项（勿回退）

来源：`BooruDatasetTagManager.Tests/IoBugAuditRegressionTests.cs`（对应已定位的 2026-07-13 I/O 审计）。

| # | 主题 | 现状 |
|---|------|------|
| 1 | 事务恢复信任边界 | 拒绝数据集外删除/覆盖；拒绝带路径段的 backup 文件名；损坏 manifest 隔离检疫 |
| 2 | `SafeFile` 并发写 | 同路径锁 + 唯一临时文件；失败不留 `.tmp`；最终内容为完整 writer 载荷 |
| 3 | 图片+侧车删除 | `ImageFileDeleter` 先 stage 再 purge；侧车失败会还原图片 |
| 4 | 翻译文件持久化 | 串行化写，条目不丢 |
| 5 | 图片分拣路径注入 | `ImageSorter.IsValidCategoryName` + 目标 containment |
| 6 | 容忍目录遍历 | 跳过 `.bdtm-*`；缺失/不可读根不崩 |
| 7 | 数据集加载 | 单个锁定 caption 失败不拖垮整库 |
| 8 | UI 接线 | 数据集切换经 `ConfirmDatasetSwitch`；媒体删除走事务删除 |

其它已验证的正向实践：

- ffmpeg：`UseShellExecute = false` + `ArgumentList`（无 shell 注入）
- HF 模型路径：`EnsureWithinRoot`；HF token 仅附到 `huggingface.co` HTTPS
- 设置反序列化：未使用 `TypeNameHandling` / `BinaryFormatter`
- 设置保存热路径：`SafeFile.WriteAllTextWithBackup`

---

## 3. 新发现问题（按优先级）

### H-1 【High】DPAPI 加密失败时仍把 API Key 以明文写入 `settings.json`

**位置**: `SecretProtector.cs`（`Protect` catch / 非 Windows 分支）、`AppSettings` / `LlmApiProfiles` 序列化属性

**现象**:

```csharp
catch (Exception ex)
{
    ProtectFailureOccurred = true;
    return plainText; // 随后被 SaveSettings 落盘
}
```

**影响**: 本机任意能读应用目录的进程/备份工具可直接拿到 LLM / HF token。虽有 `ProtectFailureOccurred` UI 警告，但**默认行为已泄密**。

**修复建议**:

1. 非空密钥 Protect 失败时 **fail-closed**：拒绝保存，或保留磁盘上旧的 `dpapi:` 值。
2. 禁止把明文回写进 `settings.json`；最多保留内存中的会话密钥。
3. 增加回归测试：模拟 Protect 抛错 → 磁盘文件不得出现明文 key。

---

### H-2 【High】LLM WebP / 视频帧转换后 GDI `Image` 未 Dispose

**位置**: `AiApi/AiOpenAiClient.cs`（约 250–264 行）

```csharp
request.ImageData.Add(Extensions.ImageToByteArray(Extensions.GetImageFromFile(imagePath)));
// ...
foreach (var item in images)
    request.ImageData.Add(Extensions.ImageToByteArray(item.Value));
```

`ImageToByteArray` 只保存到字节数组，**不 Dispose 入参**。批量 LLM 打标会持续泄漏 GDI 句柄与非托管内存，可表现为「打着打着红叉 / 进程退出」。

**修复建议**:

```csharp
using Image img = Extensions.GetImageFromFile(imagePath);
request.ImageData.Add(Extensions.ImageToByteArray(img));
// 视频帧同理：编码后立刻 Dispose
```

并补一条「N 张 WebP 批量打标后 GDI 对象数不增长」的测试或手动检查清单。

---

### M-1 【Medium】`RenameFolder` 未拒绝相对路径中的 `..`

**位置**:

- `DatasetFolders.cs` → `NormalizeRelative` 只做分隔符归一与 Trim
- `DatasetManager.RenameFolder` → `Path.Combine(DatasetRoot, normalized)` 后直接 `Directory.Move`

**现象**: `newLeafName` 已拒绝 `.`/`..`，但 `relativeFolder` 若含 `foo/../../outside`，`GetFullPath` 后可落到数据集根之外。

**现实利用条件**: 正常 UI 分组来自真实扫描路径，通常不含 `..`；风险来自未来 API/CLI/错误状态写入 `ActiveFolders`，或手改内存/测试夹具。属**缺失防御纵深**，不是当前 UI 的必现洞。

**修复建议**:

1. `NormalizeRelative` 拒绝空段、`.`、`..`、盘符/根路径。
2. Move 前断言：`oldAbsolute`/`newAbsolute` 均为 `DatasetRoot` 的严格子路径（对齐 `ImageSorter` / HF downloader 的 containment 模式）。

---

### M-2 【Medium】Hugging Face 模型下载共享固定 `.partial`，无互斥

**位置**: `HuggingFaceModelDownloader.DownloadFileAsync`

**现象**: 两个并发下载同一 `(repo, filename)` 会同时 Append/Create/Promote 同一个 `*.partial`。校验主要是尺寸/HTML 头启发式，**无加密哈希**，竞态下可能产出「看起来够大但损坏」的 ONNX，随后误伤删除或推理崩溃。

**修复建议**:

1. 进程内 `SemaphoreSlim`/锁按 `localPath` 键控。
2. 下载到唯一临时名（`Guid`），校验后再原子 `Move`。
3. 有官方 SHA 时做摘要校验（对齐 `UpdateChecker.VerifyDigest`）。

---

### M-3 【Medium】更新包下载信任远程 `fileName`

**位置**: `UpdateChecker.DownloadReleaseAssetAsync` ← `Form_settings` 传入 `check.ZipAssetName`

**现象**: `Path.Combine(targetDir, fileName)` 未强制 `Path.GetFileName`。若 GitHub Release 资产名被污染为带路径段的名字（账号失陷 / 中间人改写 API），可写到下载目录以外。

**缓解**: 资产筛选要求 `.zip` 且含 `win-x64`；有 digest 时会验 SHA-256。仍应做文件名归一。

**修复建议**: `fileName = Path.GetFileName(fileName)` + 解析后路径必须落在 `targetDir` 下；禁止空/`..`。

---

### M-4 【Medium】角色标签审查的删除门控信任模型返回的 `category`

**位置**: `CharacterTagAudit.cs` → `CharacterTagAuditResponseParser`（`ParseCategory` + `CanDelete`）

**现象**: 策略本意是「姿势/动作等受保护类不可被 AI 删」。但 `category` 直接来自模型 JSON。模型把受保护标签（如 `sitting`）标成 `clothing` 时，`CanDelete` 为 true，标签会被删除。未知类别 fail-closed 到 `Other`（安全），**已知错误类别则 fail-open**。

**影响**: 不是传统安全 CVE，但是**会静默破坏数据集**的恶性逻辑风险（付费调用后数据损坏）。

**修复建议**:

1. 用本地 `TagSemanticClassifier` / TagsDB 类型独立推导类别，模型 category 仅作参考。
2. 至少：本地判定为受保护时强制 Keep，忽略模型 category。
3. 补回归：`sitting` + 模型谎称 `clothing` + `delete` → 最终 Keep。

---

### M-5 【Medium】`CaptionGenerationService.GetOutputImagePath` 可逃逸输出根

**位置**: `CaptionGenerationService.cs`

```csharp
string relative = Path.GetRelativePath(fullRoot, fullImage);
return Path.Combine(outputRoot, relative);
```

当 `imagePath` 不在 `sourceRoot` 下时，`relative` 可为 `..\..\evil\...`，输出写到 sibling caption 目录之外。

**现实利用**: 当前扫描器通常只喂根内文件；公共静态 API 仍不安全。

**修复建议**: 若 `relative` 以 `..` 开头或为绝对路径则拒绝；或 `EnsureWithinRoot(outputRoot, result)`。

---

### M-6 【Medium】ffmpeg 标准输出/错误无限追加

**位置**: `VideoProcessingService` 进程封装（`StringBuilder` 累积 stdout/stderr）

**影响**: 异常冗长 ffmpeg 日志可造成内存膨胀（本地 DoS / 任务失败）。命令注入面已正确关闭。

**修复建议**: 环形缓冲，仅保留末尾 N KB 用于错误展示。

---

### L-1 【Low】主题 / TagsDB 缓存非原子写

**位置**: `ColorScheme.Save`、`TagsDB.SaveTags` 仍用 `File.WriteAllText`

中断可能导致截断；主题回退默认、Tags 缓存重建，可用性影响为主。

**修复**: 改用 `SafeFile.WriteAllText`（主题可带 `.bak`）。

---

### L-2 【Low】Explorer 回退路径字符串拼接

**位置**: `Form1` 打开资源管理器的 fallback（`Process.Start("explorer.exe", "/select,\"...")`）

主路径已用 `SHOpenFolderAndSelectItems`。fallback 在极端路径字符下不如 `ArgumentList` 稳妥。建议统一 `ProcessStartInfo.ArgumentList`。

---

### L-3 【Low / 待验证】事务目录与 junction/reparse 竞态

`CharacterTagFileTransaction` 用字符串前缀做 containment，不解析最终 reparse 目标。本地攻击者若能在数据集内抢建 junction，理论上可把 commit 重定向到根外。对「单用户信任自己磁盘」的桌面工具通常可接受；加固可在 replace 前 `FileInfo`/`GetFinalPathNameByHandle` 再验一次。

---

## 4. 可疑但属产品边界的项

| 项 | 说明 |
|----|------|
| 任意 OpenAI-compatible Base URL | 用户可控出站；UI 对外部 HTTP 有警告，CLI/已存配置可跳过确认。应文档化「图片与 Key 会发往该端点」。 |
| `AiOpenAiClient` 整图读入内存 | 大图 × 高并发 → 内存峰值；建议缩放/字节预算（亦是性能项）。 |
| `settings.json` 首次创建用非原子写 | `LoadData` 冷启动/损坏恢复路径仍 `File.WriteAllText`；热路径已 SafeFile。建议统一。 |

---

## 5. 性能与架构优化（按收益排序）

| 优先级 | 项 | 证据位置 | 建议 |
|--------|----|----------|------|
| P0 | 相似图 O(n²) Hamming | `SimilarImageFinder` | 大数据集改桶/BK-tree；小数据集保留现状 |
| P0 | 全分辨率 LRU 缓存克隆 | `DatasetManager.GetImageFromFileWithCache` | 缓存有界预览；按像素/字节预算淘汰 |
| P0 | AllTags 过滤二次方 | `AllTagsList.UpdateFilter`（Contains/Remove/有序插入） | 一次重建可见列表 + 单次 Reset |
| P1 | 过滤/浏览器重复全表扫描 | `DatasetManager` ScopedItems / 文件夹聚合 | 维护 folder→items / tag→items 索引，结构变更时失效 |
| P1 | LLM 请求前图像无上限 | `AiOpenAiClient` | 最长边缩放 + 单请求字节上限 |
| P2 | TagsDB 每次全量读哈希 | `TagsDB.IsNeedUpdate` | `(len, LastWriteTimeUtc)` 快路径；变更文件才哈希 |
| P2 | 浏览器绘制时同步 `Image.Identify` | `DatasetBrowserView` | 异步加载尺寸；跨 refresh 保留元数据缓存 |
| P2 | 大库物化枚举 | `TolerantFileEnumerator.GetFiles` | CLI/统计改为流式 `IEnumerable` |

---

## 6. 建议修复顺序（落地清单）

### 本迭代（建议直接修）

1. **H-2** Dispose LLM WebP/视频帧 `Image`
2. **H-1** SecretProtector fail-closed，禁止明文回写
3. **M-1** `RenameFolder` / `NormalizeRelative` containment
4. **M-3** 更新包 `Path.GetFileName` + 目录校验
5. **M-2** 模型下载 per-path 锁 + 唯一 partial

### 下一迭代

6. **M-4** 审计类别改本地分类器门控  
7. **M-5** Caption 输出路径 containment  
8. **M-6** ffmpeg 日志环形缓冲  
9. **L-1** ColorScheme / TagsDB 改 SafeFile  
10. AllTags 过滤重建 + TagsDB 元数据快路径（性价比高）

### 中期

11. 相似图索引结构  
12. 数据集索引快照，消除重复扫描  
13. 图像缓存改为预览预算模型  

---

## 7. 建议新增的回归测试

```text
SecretProtector_ProtectFailure_DoesNotPersistPlaintext
RenameFolder_RejectsDotDotSegments_AndOutsideRoot
UpdateChecker_SanitizesAssetFileName
HuggingFaceDownloader_SerializesSameTargetDownloads
CaptionGeneration_RejectsImageOutsideSourceRoot
CharacterTagAudit_LocalProtectedCategoryOverridesModelLie
AiOpenAiClient_DisposesConvertedWebpImages  (或静态分析/代码契约测试)
```

现有 `IoBugAuditRegressionTests` 应继续保留；新 I/O 修复请按同类模式追加，避免「修了又回退」。

---

## 8. 附录：关键代码索引

| 组件 | 路径 |
|------|------|
| 原子写 | `BooruDatasetTagManager/SafeFile.cs` |
| 密钥 | `BooruDatasetTagManager/SecretProtector.cs` |
| 删除事务 | `BooruDatasetTagManager/ImageFileDeleter.cs` |
| 标签事务 | `BooruDatasetTagManager/CharacterTagFileTransaction.cs` |
| 模型下载 | `BooruDatasetTagManager/HuggingFaceModelDownloader.cs` |
| 更新下载 | `BooruDatasetTagManager/UpdateChecker.cs` |
| 文件夹重命名 | `BooruDatasetTagManager/DatasetManager.cs`, `DatasetFolders.cs` |
| 审查解析 | `BooruDatasetTagManager/CharacterTagAudit.cs` |
| LLM 客户端 | `BooruDatasetTagManager/AiApi/AiOpenAiClient.cs` |
| I/O 回归 | `BooruDatasetTagManager.Tests/IoBugAuditRegressionTests.cs` |

---

*本报告仅基于静态审查与既有测试对照，未做动态模糊或对抗性渗透。若需要，可在下一轮针对 H-1/H-2/M-1/M-2 直接提交补丁并补测试。*
