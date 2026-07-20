# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

BooruDatasetTagManager+ is a .NET 8 Windows Forms tool for tagging LoRA/character image datasets (fork of starik222/BooruDatasetTagManager). The UI defaults to Simplified Chinese; user-facing docs are trilingual (zh / en / pt-BR). `AGENTS.md` holds the general contributor guide; this file adds the non-obvious traps and architecture.

## Commands

```powershell
# Build (repo root)
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows

# All tests
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj

# Single test class / method
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj --filter "FullyQualifiedName~AllTagsSearchTests"
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj --filter "FullyQualifiedName~ChineseTagLookupTests.FindEnglishTagsByChineseNameMatchesSynonymSubstrings"

# Run the app (note: assembly is BooruDatasetTagManagerPlus.exe, NOT the repo/project name)
BooruDatasetTagManager\bin\Debug\net8.0-windows\BooruDatasetTagManagerPlus.exe
test_start.bat          # launches Release, falls back to Debug, builds if missing

# Self-contained publish to dist/ (also downloads bundled ffmpeg on first run)
quick_build.bat [debug|release]

# Full release: publish + zip + GitHub release (needs gh auth; reads docs/RELEASE_NOTES_v<VERSION>.md)
publish_release.bat [version]
```

## Non-obvious conventions (each has broken a build/test before)

- **Test project links sources file-by-file.** `BooruDatasetTagManager.Tests.csproj` has no project reference; it lists each main-project `.cs` via `<Compile Include>`. A new class used by tests must also be added there, or the test build fails with missing types. Forms that reference `Program.*` generally cannot be linked in (Program drags the whole app).
- **Version lives in four places** and must be bumped together: the six fields in `BooruDatasetTagManager.csproj`, `Properties/AssemblyInfo.cs` (**this is the effective one** — the csproj sets `GenerateAssemblyInfo=false`), `publish_release.bat` (`VERSION=`), and the guard test `CharacterTagAuditIntegrationTests.ProjectPublishesAgentSkillsAndVersionIsXXX` (asserts the strings; its method name carries the version).
- **Release docs pattern:** each release bumps the version in the H1 of all three READMEs (`README.md` zh is primary, `README_en.md`, `docs/pt-BR/README_pt_BR.md`), prepends a one-bullet summary to their 更新日志 / Changelog / Histórico de versões section (details go in the notes file, not the README), and adds a `docs/RELEASE_NOTES_vx.y.z.md` (English body + 「更新摘要（中文）」 at the end). README structure (5 top-level sections, in order: 更新日志 / 快速开始 / 功能一览 / 功能详解 / 致谢与许可) is guarded by `AiServerSetMigrationAndDocumentationTests.BilingualReadmesContainFeatureSectionsAndImages`.
- **i18n:** 5 files in `BooruDatasetTagManager/Languages/` (`en-US`, `zh-CN`, `zh-TW`, `ru-RU`, `pt-BR`), UTF-8 no BOM, `Key=Value`, append new keys at the end of every file — `LocalizationAndImageLoaderTests.TranslationContainsEveryEnglishKey` fails if any en-US key is missing from any of the other four files. `I18n.GetText` returns the raw key on a miss and also when `Program.Settings` is null (it appends hotkey hints via `Program.Settings.Hotkeys` inside a catch-all).
- **Runtime-added WinForms controls skip DPI auto-scaling.** Anything added after `InitializeComponent()` must be positioned relative to already-scaled designer controls (or via `LogicalToDeviceUnits`), never with hard-coded pixels — hard-coded coordinates overlap rows on high-DPI displays (bit `Form_settings` and the Form1 search boxes).
- **Overwriting an image file** follows a fixed sequence (see `Form1.RemoveBackgrounds`): `SafeFile.WriteAllBytes` (atomic) → `Program.DataManager.RemoveFromCache(path)` → `Extensions.MakeThumb` → `RefreshDatasetGrid()`.
- Most dialogs are hand-coded (no `.Designer.cs`) — e.g. `Form_ImageEditor`, `Form_CharacterTagAuditWizard`; `Form1` and a few others are designer-based. Enums go in `GlobalEnums.cs`. `ColorSchemeManager` lives in the legacy namespace `UmaMusumeDBBrowser`.
- Durable writes anywhere in the app go through `SafeFile` (temp + `File.Replace`); batch image ops write a temp file and swap only on success; image deletion is transactional via `ImageFileDeleter` (stage → purge, rollback on failure); directory scans that must survive unreadable entries go through `TolerantFileEnumerator` (dataset load, image sorter, deleter). The 8 risks confirmed by the internal I/O audit (`IO_BUG_AUDIT.md`) are locked in by `IoBugAuditRegressionTests` — don't regress them.
- **The character-tag-audit policy gates the AI, not the user.** `CharacterTagAuditPolicy.CanDelete` is enforced at parse time (`CharacterTagAuditResponseParser` forces AI Delete/Replace on protected categories back to Keep), while the wizard's review grid deliberately lets the user override any non-trigger tag. `CharacterTagAuditItem.ShouldDelete`/`ShouldReplace` are therefore intentionally NOT gated by `CanDelete` — re-adding that gate silently discards manual review decisions.
- **Keep canvas/coordinate math out of Forms.** Forms referencing `Program.*` can't be linked into the test project, so mapping math lives in static helpers (`CropCanvasHelper`, `ImageEditorCanvasMath`) that are linked and unit-tested. The crop off-by-one bug shipped precisely because this math briefly lived inside `Form_ImageEditor`.

## Architecture

**Composition root:** `Program.cs` — an internal static class whose public static fields are the app's service locator: `Settings` (`AppSettings`, JSON at app dir), `DataManager` (`DatasetManager`), `TagsList` (`TagsDB`), `TransManager` (`TranslationManager`), `ChineseTagLookup`, `ColorManager`, `AppPath`. Startup order: settings → I18n → color scheme → wait-form async load of the tag DB and `Data/danbooru-0-zh.csv`. Global exception handlers write `crash.log`. Every step falls back instead of throwing (corrupt configs are backed up as `.corrupt` and defaulted).

**Main window:** `Form1.cs` (~4k lines) is the hub. Three panes — dataset grid `gridViewDS` (bound to a plain `List<DataItem>`; after removing rows you must `CurrencyManager.Refresh()`, plain `Refresh()` leaves stale rows that crash on paint), current-image tags `gridViewTags` (single mode: `EditableTagList`; multi-select mode: `MultiSelectDataTable`, where continuation rows carry their tag only via `MultiSelectDataRow.GetTagText()`), and all-tags `gridViewAllTags` (bound to `AllTagsList`, a hand-rolled `IBindingListView` with count aggregation, filter/sort, and prefix>substring>translation>alias search). Both tag grids have always-visible search boxes (prefix beats substring, wrap-around next-match on Enter, Chinese input resolves through `ChineseTagLookup`); the searches index `AllTagsList.List` directly, which matches grid row order 1:1 because `AddTag` inserts sorted. Double-clicking an all-tags row runs the configurable `Settings.AllTagsDoubleClickAction`. A significant part of the UI (menus, context menus, search boxes, settings rows) is built at runtime in constructors, not in the designer. `switchLanguage()` re-applies every string — new UI text needs an I18n key wired there.

**Data model:** `DatasetManager.DataSet` maps image path → `DataItem` (thumbnail `Img`, `EditableTagList Tags` with undo history, `IsModified` = exact text compare against a saved snapshot — never hashes). Tag edits propagate to `AllTagsList` counts via `AddTag`/`RemoveTag`.

**Chinese tag workflow:** `ChineseTagLookupService` (loaded from `Data/danbooru-0-zh.csv`, format `english_tag,中文名|同义词`) backs autocomplete aliases, input resolution (typing Chinese yields the English tag), and grid search; its methods gate on `IsSimplifiedChineseLanguage(language)`. `TranslationManager` consults this CSV before online translators (provider chain with fallback, cache in `Translations/`).

**Tagging providers:** `AutoTagProvider` adapters over (a) local ONNX — `Wd14OnnxTaggerService` / `PixAiOnnxTaggerService` / `ClTaggerOnnxService` (CL family), model list in `OnnxTaggerCatalog`, weights fetched by `HuggingFaceModelDownloader` (`.partial` + integrity check; a corrupt model throws `ModelCorruptedException` and is deleted for re-download; gated models like cl_tagger_v2 first show `Form_GatedModelNotice`, which collects a DPAPI-encrypted HF access token — their weights must never be bundled); (b) OpenAI-compatible LLMs via `AiApi/` + `CaptionGenerationService` (used by `Form_LlmTagger` for Tags and Natural-language modes); (c) `AiApiServer/` (Python) — legacy/optional, the client no longer depends on it. Background removal is in-client (`RmbgBackgroundRemoverService`, RMBG-1.4 ONNX). Video work goes through `VideoProcessingService` + bundled ffmpeg (`ThirdParty/ffmpeg`, fetched by `scripts/fetch_ffmpeg.ps1`).

**Image editor:** dataset-grid right-click → Edit image opens `Form_ImageEditor` (brush/eraser/crop/rotate/flip with Photoshop-style zoom, pan, and eyedropper). Deliberately layered: `ImageEditorDocument` (bitmap + bounded snapshot undo/redo), `ImageEditorCanvasMath` (pure screen↔image mapping), `ImageEditorSaveService` (encode by the original extension via ImageSharp — webp included, collision-free `_edit` naming, caption-file cloning) — all three are linked into the test project; the Form owns only interaction. Saving honors `Settings.ImageEditorSaveMode` (Ask / Overwrite / NewFile): overwrite follows the fixed overwrite sequence above, new files register via `DataManager.AddImages` (the cloned caption is picked up automatically).

**Images:** decoded with ImageSharp 3.x (WebP included), converted by `ImageLoader.ToDrawingImage` to 32bppArgb `System.Drawing` bitmaps for the UI. `ImageLruCache` is bounded and hands out clones — never hold the shared instance.

**Long-running windows** (ONNX tagger, video tools, LLM tagger, audit wizard) follow the pattern: cancel + deferred close while a job runs (closing mid-job used to crash the process), progress marshaled to the UI thread, UI locks released in `finally`.
