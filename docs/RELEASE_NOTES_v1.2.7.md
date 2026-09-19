<!-- lang:zh-CN -->
# BooruDatasetTagManager+ v1.2.7（中文）

**新功能**

- **Tag 推标接入 OppaiOracle**：[Grio43/OppaiOracle](https://huggingface.co/Grio43/OppaiOracle) 作为新的本地 ONNX 家族出现在模型下拉中。Apache-2.0，非 gated，词表约 1.9 万 general 标签（不含角色）。两个检查点：
  - **V1 (320)**：原生 320×320，默认阈值 0.55（略低于官方 P=R）。
  - **V1.1 (448)**：448×448 微调，默认阈值 0.65（与官方 Space 一致）。
- 预处理按官方 Space：灰边 letterbox（RGB 114，不放大）、`pixel_values` + `padding_mask`、图内已做 sigmoid。ORT 会话用 BASIC / 关闭图优化（全量优化会在 bool mask 上失败）。透明底合成后会丢掉 `gray_background`；`rating:*` 与 PAD/UNK 不写入。
- 角色阈值对该家族隐藏。GUI / CLI `onnx-tag --model oo:Grio43/OppaiOracle:v1.1` 与 YOLO 检测「在 Tag 推标中打开」共用同一套服务。

## 其他

- **修复多重切割 YOLO 小框**：检测框扩比例后长边小于勾选档位时，按 64 对齐的原尺寸写出，不再整批当成「没有检测到目标」。框对齐后不足 64px 才跳过，并单独提示。
- **角色标签审查流程重构**：
  - `character-tag-auditor/SKILL.md` 重写为「对每个标签依次执行」的 8 步流程（分类 → 归属 → 存在性 → 颜色绑定 → 容器词与同物配对 → 风格修剪 → 发色守则 → 排序），去掉了四段互相重复的散文；无颜色的穿戴标签要么替换为 `color item`，要么理由以 `color unverifiable:` 开头，`reason` 必须写出参考图上的证据，「核心标记」之类套话视为未审查。`prompt-pyramid` 的 character-audit 段只保留排序职责。
  - 视觉阶段提示词点名本次必须解决的无颜色穿戴标签，并列出**同槽位簇**（如 `[bow, hair ribbon, hairband]`），要求每簇只留视觉上互不相同的物品。
  - 新增一次**定向解析请求**（不带 SKILL，只带参考图）：对视觉复核后仍无颜色的穿戴标签问颜色，对仍有 ≥2 个成员的簇问「是否同一件、最佳标签是什么」；代码只接受词表内的颜色词和簇内（或颜色+簇成员）的标签，失败静默保留视觉结果。每个角色最多 3 次请求。
  - 随包新增 `Data/danbooru_tag_near_synonyms.csv`（danbooru 相关标签图，7,467 行）驱动成簇；`Data/danbooru_dataset_general.csv` 的 `parent_tag` 列驱动确定性上位词折叠（`jewelry` → `earrings`；两个具体成员并存时删除大类词）与颜色/类型兄弟合并（`black gloves` + `elbow gloves`：合成词在词表中才合成，否则保留颜色标签）；CSV 缺失的发饰 → `hair ornament` 包含关系内置补齐。复核网格里同簇标签加同色底纹并在悬停时列出簇成员。
  - **精确标签优先**：替换目标必须是词表内标签或「颜色 + 词表内标签」；自造合成词（`frilled black dress`、`black elbow gloves`）折回已有的精确彩色标签。同物只留一个标签。成簇按去色基底计算，所以上色后的 `black hairband` 与 `black hair ribbon` 仍会问「是否同一件」。模型把容器词删掉时，若只剩一个已确认的具体标签，改写成替换（`dress` → `black dress`），好让只有大类词的图片也能拿到精确标签；裸装饰词（`frills` 等）旁边已有服装时删除。
- 测试套件从 787 增长到 **830**（OppaiOracle 目录条目、三列 CSV、灰边 letterbox / mask、`rating` / `gray_background` 过滤、下拉全名、YOLO 小框原尺寸回退、parent_tag 解析与家族折叠、近义词簇、解析请求、精确标签优先 / 自造合成词折回、被删容器词改替换、裸装饰词删除、SKILL 结构守卫）。

<!-- lang:en -->
# BooruDatasetTagManager+ v1.2.7 (English)

**New**

- **Tag tagger adds OppaiOracle**: [Grio43/OppaiOracle](https://huggingface.co/Grio43/OppaiOracle) is a new local ONNX family in the model dropdown. Apache-2.0, not gated, ~19k general-only tags (no character head). Two checkpoints:
  - **V1 (320)**: native 320×320, default threshold 0.55 (a touch below the published P=R point).
  - **V1.1 (448)**: 448×448 fine-tune, default threshold 0.65 (same as the official Space).
- Preprocess matches the official Space: gray letterbox (RGB 114, no upscale), `pixel_values` + `padding_mask`, sigmoid already inside the graph. The ORT session uses BASIC / disabled graph opt (full opt fails on the bool mask). After an alpha composite, `gray_background` is dropped; `rating:*` and PAD/UNK are not written.
- The character-threshold control is hidden for this family. GUI, CLI `onnx-tag --model oo:Grio43/OppaiOracle:v1.1`, and YOLO detect's "open in Tag tagger" share the same service.

## Other

- **Fix multi-crop YOLO small boxes**: when an expanded detect box is smaller than the selected gear, write it at the 64-aligned native size instead of reporting “nothing detected”. Boxes under 64px after alignment are skipped with a separate message.
- **Character tag audit pipeline rework**:
  - `character-tag-auditor/SKILL.md` is rewritten as an 8-step per-tag procedure (category → attribution → existence → color binding → container tags and same-item pairs → style pruning → hair colors → order), dropping four overlapping prose sections. A color-less wearable is either replaced by `color item` or kept with a reason starting `color unverifiable:`; every `reason` must cite what is visible in the reference, and stock phrases like “core tag” count as no review. The `prompt-pyramid` character-audit section now only orders.
  - The visual-stage prompt names the color-less wearable tags that must be resolved and lists **same-slot clusters** (e.g. `[bow, hair ribbon, hairband]`), asking to keep only visibly distinct items per cluster.
  - A new **targeted resolution request** (reference image only, no skills) asks the color of wearables still color-less after visual review and, for clusters that still have ≥2 members, whether they are one item and which tag is best. Code accepts only allowed color words and cluster members (or color + member); any failure silently keeps the visual result. At most 3 requests per character.
  - Ships `Data/danbooru_tag_near_synonyms.csv` (danbooru related-tag graph, 7,467 rows) for clustering; the `parent_tag` column of `Data/danbooru_dataset_general.csv` drives deterministic hypernym collapse (`jewelry` → `earrings`; a container next to two members is deleted) and color/type sibling merge (`black gloves` + `elbow gloves`: combined only when the combined tag exists in the vocabulary, otherwise the color tag is kept); hair accessory → `hair ornament` implications missing from the CSV are built in. The review grid tints clustered tags and lists the cluster on hover.
  - **Known tags win**: a replacement target must be a vocabulary tag or `color` + a vocabulary tag; invented composites (`frilled black dress`, `black elbow gloves`) fold back onto the known precise colored tag. One item, one tag. Clusters are computed on color-stripped bases so `black hairband` and `black hair ribbon` are still asked whether they are the same piece. When the model deletes a container that has exactly one confirmed specific tag, that delete is rewritten as a replace (`dress` → `black dress`) so images that only carry the generic word still receive the precise tag. Bare decoration words (`frills` and the like) are deleted when a garment is already confirmed.
- The test suite grows from 787 to **830** (OppaiOracle catalog entries, three-column CSV, gray letterbox / mask, `rating` / `gray_background` filters, dropdown full names, YOLO native-size fallback, parent_tag parsing and family collapse, near-synonym clusters, resolution pass, known-tag precedence / composite fold-back, deleted-container revive, bare-decoration drop, skill structure guards).
