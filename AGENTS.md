# Repository Guidelines

Guide for contributors working on BooruDatasetTagManager+, a .NET 8 Windows Forms tool for managing and auto-tagging image datasets.

## Project Structure & Module Organization

- `BooruDatasetTagManager/` - main WinForms application (entry point `Program.cs`, main UI `Form1.cs`/`Form1.Designer.cs`).
- `BooruDatasetTagManager/AiApi/` - AI interrogator and OpenAI client integration.
- `BooruDatasetTagManager/Diffusion.Scanner/` - metadata and ComfyUI parameter parsing.
- `BooruDatasetTagManager/Languages/` - i18n text files (`en-US.txt`, `zh-CN.txt`, `zh-TW.txt`, `ru-RU.txt`, `pt-BR.txt`). Keys are `Name=Value` pairs; every key in `en-US.txt` must exist in all language files.
- `BooruDatasetTagManager/Data/` - bundled data such as `danbooru-0-zh.csv`.
- `BooruDatasetTagManager/Translations/` - cached translation files written at runtime.
- `BooruDatasetTagManager.Tests/` - xUnit test project. Source files from the main project are linked via `<Compile Include="..\..." Link="..." />` in the test `.csproj`.
- `AiApiServer/` - companion AI API server (Python).
- `dist/` - self-contained publish output.

## Build, Test, and Development Commands

All commands run from the repository root.

```powershell
dotnet build BooruDatasetTagManager.sln -c Debug -f net8.0-windows
dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o dist
```

- `quick_build.bat` - publishes a self-contained Release build to `dist/`.
- `test_start.bat` - launches the app from `bin\Release\net8.0-windows\` (falls back to Debug, then builds if missing).

After publishing, verify both `dist/` and `bin/Release/net8.0-windows/` are updated, since `test_start.bat` launches from the latter.

## Coding Style & Naming Conventions

Enforced by `.editorconfig`:

- 4-space indentation, CRLF line endings.
- PascalCase for classes, methods, properties, events.
- `I` prefix for interfaces.
- Prefer `var` only when the type is apparent; otherwise use explicit types.
- Prefer braces, block-scoped namespaces, simple `using` statements.
- `OPENAI001` and `CA1416` diagnostics are suppressed.

## Testing Guidelines

- Framework: xunit (`[Fact]`, `[Theory]`).
- Test files live in `BooruDatasetTagManager.Tests/` (e.g. `BatchAndTranslationTests.cs`, `CaptionGenerationTests.cs`, `LocalizationAndImageLoaderTests.cs`).
- Run: `dotnet test BooruDatasetTagManager.Tests\BooruDatasetTagManager.Tests.csproj`.
- Language files are validated by `LocalizationAndImageLoaderTests` - every `en-US.txt` key must exist in `zh-CN.txt`, and critical keys must have distinct translations.
- New pure-logic services (e.g. `CaptionGenerationService`) should have unit tests covering edge cases like empty input, think-block stripping, and output path resolution.

## Commit & Pull Request Guidelines

- Write clear, descriptive commit messages in English.
- Reference issues or feature names when relevant.
- Ensure `dotnet build` and `dotnet test` pass before pushing.
- When adding UI features, update all five language files and verify no keys are missing.
- Keep changes scoped to the affected modules; avoid unrelated refactors.

## i18n & Configuration Notes

- When adding i18n keys, append to all five language files in `BooruDatasetTagManager/Languages/`. Do not reorder or rewrite existing files.
- `AppSettings.cs` holds all persisted settings (JSON). Add new settings there with sensible defaults and include them in the `LoadData` copy block.
- Caption generation outputs to a sibling `_captioned` folder and never overwrites original dataset tag files.
