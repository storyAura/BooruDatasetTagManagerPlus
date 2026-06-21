<div align="center">

**English** | [中文简体](./README_zh_CN.md) | [Portugues do Brasil](./docs/pt-BR/README_pt_BR.md)

</div>

# BooruDatasetTagManagerPlus

BooruDatasetTagManagerPlus is a fork of [starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager), maintained at [storyAura/BooruDatasetTagManagerPlus](https://github.com/storyAura/BooruDatasetTagManagerPlus).

It is a booru-style dataset tag editor for LoRA, embedding, hypernetwork, and other image model training datasets. It can edit existing caption/tag text files, manage tags across many images, and create tag files for image-only folders.

This fork keeps the original BooruDatasetTagManager workflow and adds quality-of-life features for translation fallback, Danbooru Wiki lookup, and Simplified Chinese tag search.

## Relationship to Upstream

- Upstream project: [starik222/BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager)
- Fork repository: [storyAura/BooruDatasetTagManagerPlus](https://github.com/storyAura/BooruDatasetTagManagerPlus)
- License: MIT. The original license and copyright notice are preserved in [LICENSE](./LICENSE).
- This fork is intended to remain compatible with the upstream dataset workflow while improving Chinese-language usability and tag lookup ergonomics.

## Differences from the Original Project

Compared with the upstream BooruDatasetTagManager, this fork currently focuses on:

- Project identity updated to `BooruDatasetTagManagerPlus`, including application title, assembly product name, output executable name, and documentation.
- Translation fallback chain with multiple free APIs and configurable per-request timeout.
- Unified tag right-click actions, including Danbooru Wiki lookup and tag retranslation.
- Danbooru Wiki popup with readable DText cleanup, body translation, and browser fallback.
- Simplified Chinese tag lookup in add/replace tag inputs. When the UI language is `zh-CN`, Chinese aliases from `BooruDatasetTagManager/Data/danbooru-0-zh.csv` can autocomplete to English tags.
- Local UI structure notes in [docs/UI_STRUCTURE_zh_CN.md](./docs/UI_STRUCTURE_zh_CN.md), useful before future WinForms refactoring.
- Quick local launch/build script: [test_start.bat](./test_start.bat).

## Using

Prepare a dataset folder containing images and matching `.txt` tag files. You can also load a folder with only images; tag files will be created when you save.

Open the app and choose:

```text
File -> Load folder
```

The left pane displays images from the dataset. The center pane displays tags for the selected image. The right pane contains All/Common tags and the AutoTagger preview.

After editing, choose:

```text
File -> Save all changes
```

You can select multiple images at once to edit tags across similar images.

## Tag Translation

Select the translation language and translation service in settings, then enable:

```text
View -> Translate tags
```

BooruDatasetTagManagerPlus uses a fallback translation chain. The default order prioritizes the existing Chinese-friendly service, then tries MyMemory, Google JSON API, and the older Google Mobile HTML parser. Each provider has a configurable timeout, defaulting to 5 seconds.

Translations are saved in the `Translations` folder. Manual translations can be marked with `*`; forced retranslation will not overwrite manual translations.

## Tag Autocomplete and Chinese Lookup

The app loads tag autocomplete data from the `Tags` folder. It supports CSV files compatible with A1111 booru tag autocomplete and plain `.txt` files with one tag per line.

For Simplified Chinese UI users, BooruDatasetTagManagerPlus also loads:

```text
BooruDatasetTagManager/Data/danbooru-0-zh.csv
```

This file is used only for Chinese-to-English tag lookup. It is not imported as the normal tag database.

Expected format:

```csv
english_tag,Chinese name|Chinese alias
```

In add/replace tag inputs:

- Chinese input can show candidates such as `长发 -> long hair`.
- Selecting a candidate writes the English tag.
- Confirming an exact Chinese input also resolves to the English tag.
- If no match exists, the original input is kept.

## Danbooru Wiki Lookup

Right-click a tag in the current tags table, All tags table, or AutoTagger preview table, then choose Danbooru Wiki lookup.

The popup shows the title, other names, updated time, cleaned Wiki body text, a Wiki translation button, and an Open in browser button. The browser button remains available even if the JSON request fails.

## AutoTagger

You can generate tags for images directly in the app by running the included AiApiServer.

Install dependencies:

```bash
cd AiApiServer
pip install -r requirements.txt
```

Start the service:

```bash
python main.py
```

After launching the service, generate tags from the Tools menu, the tag toolbar, or the AutoTagger preview tab.

## Weighted Tags

The editor supports weighted tags. When loading tags, brackets are converted to weights. Select a tag and move the weight slider to change its weight.

## Color Scheme

The app includes Classic and Dark color schemes. You can edit `ColorScheme.json` manually.

## Interface Translation

Language files are located in the `Languages` folder. Copy an existing `xx-XX.txt` file, rename it to your language code, then translate values after the `=` sign.

## Build

Install the .NET 8 SDK, then run:

```bash
dotnet build BooruDatasetTagManager/BooruDatasetTagManager.csproj -c Release
```

Run tests:

```bash
dotnet test BooruDatasetTagManager.sln -c Release
```

For local use, double-click [test_start.bat](./test_start.bat). The current output executable is `BooruDatasetTagManagerPlus.exe`.

## Dependencies

- [ScreenLister](https://github.com/starik222/ScreenLister) - Used to obtain images from videos.
