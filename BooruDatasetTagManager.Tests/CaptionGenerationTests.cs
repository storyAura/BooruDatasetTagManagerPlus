using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class CaptionGenerationTests
{
    [Fact]
    public void PromptBuilderAddsCleanReferenceTagsAndDropsQualityTags()
    {
        string prompt = CaptionPromptBuilder.BuildUserPrompt(
            new[] { "masterpiece", "1girl", "blue_hair", "black_shoes", "best quality" },
            string.Empty,
            new CaptionGenerationOptions());

        Assert.Contains("Reference tags:", prompt);
        Assert.Contains("1girl", prompt);
        Assert.Contains("blue hair", prompt);
        Assert.Contains("black shoes", prompt);
        Assert.DoesNotContain("masterpiece", prompt);
        Assert.DoesNotContain("best quality", prompt);
        Assert.Contains("coherent natural language paragraph", prompt);
        Assert.Contains("Do NOT output comma-separated tags", prompt);
    }

    [Fact]
    public void TaskPromptsKeepAutoTaggingAndLlmT2NlSeparate()
    {
        Assert.NotEqual(
            AiPromptTemplateCatalog.AutoTaggingSystemPrompt,
            AiPromptTemplateCatalog.LlmT2NlSystemPrompt);
        Assert.Contains("Danbooru-style tags", AiPromptTemplateCatalog.AutoTaggingSystemPrompt);
        Assert.Contains("natural language paragraph", AiPromptTemplateCatalog.LlmT2NlSystemPrompt);
        Assert.Contains("Do not output a comma-separated tag list", AiPromptTemplateCatalog.LlmT2NlSystemPrompt);
    }

    [Fact]
    public void OutputFormatterRemovesThinkBlocksAndAppliesTemplate()
    {
        string output = CaptionOutputFormatter.Format(
            "{trigger_words}: {tags} => {caption}",
            "hero",
            "1girl, black shoes",
            "<think>hidden reasoning</think>A woman is standing.");

        Assert.Equal("hero: 1girl, black shoes => A woman is standing.", output);
    }

    [Fact]
    public void OriginalTagsAndCaptionPreserveTagTextWithExactlyOneBoundaryNewline()
    {
        string tags = "blue_hair,  1girl,solo  \r\n\r\n";

        string output = CaptionOutputFormatter.FormatOriginalTagsAndCaption(
            tags,
            "<think>hidden</think>A woman with blue hair.");

        Assert.Equal(
            "blue_hair,  1girl,solo  " + Environment.NewLine + "A woman with blue hair.",
            output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    public void OriginalTagsAndCaptionWriteOnlyCaptionWhenTagsAreMissing(string tags)
    {
        string output = CaptionOutputFormatter.FormatOriginalTagsAndCaption(tags, "A blue square.");

        Assert.Equal("A blue square.", output);
    }

    [Fact]
    public void OutputPathUsesSiblingCaptionedFolder()
    {
        string sourceDir = Path.Combine(Path.GetTempPath(), "bdtm-caption-tests");
        string imagePath = Path.Combine(sourceDir, "nested", "image.png");

        string output = CaptionGenerationService.GetOutputTextPath(sourceDir, imagePath, "_captioned");

        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "bdtm-caption-tests_captioned", "nested", "image.txt"),
            output);
    }

    [Fact]
    public async Task ScanCountsTotalPendingAndExistingOutputs()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        SaveImage(Path.Combine(source, "a.png"));
        SaveImage(Path.Combine(source, "nested", "b.jpg"));
        File.WriteAllText(Path.Combine(source, "ignored.txt"), "tag");
        string existing = CaptionGenerationService.GetOutputTextPath(source, Path.Combine(source, "a.png"), "_captioned");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "already done");

        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        Assert.Equal(2, scan.Total);
        Assert.Equal(1, scan.Pending);
        Assert.Equal(1, scan.Skipped);
        Assert.Equal(Path.Combine(temp.Path, "dataset_captioned"), scan.OutputRoot);
    }

    [Fact]
    public async Task ProcessingContinuesAfterSingleImageFailure()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        SaveImage(Path.Combine(source, "a.png"));
        SaveImage(Path.Combine(source, "b.png"));
        int calls = 0;
        var service = new CaptionGenerationService((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new CaptionModelResponse(string.Empty, "model failed")
                : new CaptionModelResponse("A blue square.", string.Empty));
        });
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        CaptionGenerationResult result = await service.ProcessAsync(
            scan,
            new CaptionGenerationOptions { MaxConcurrency = 1 });

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.False(result.Canceled);
        Assert.Single(result.Errors);
        Assert.True(File.Exists(CaptionGenerationService.GetOutputTextPath(source, Path.Combine(source, "b.png"), "_captioned")));
    }

    [Fact]
    public async Task CancellationStopsWithoutCountingFailure()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        SaveImage(Path.Combine(source, "a.png"));
        SaveImage(Path.Combine(source, "b.png"));
        using var cancellation = new CancellationTokenSource();
        var service = new CaptionGenerationService((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new CaptionModelResponse("A blue square.", string.Empty));
        });
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        CaptionGenerationResult result = await service.ProcessAsync(
            scan,
            new CaptionGenerationOptions { MaxConcurrency = 1 },
            cancellationToken: cancellation.Token);

        Assert.True(result.Canceled);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task ProcessingNeverChangesSourceImageOrTags()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        string image = Path.Combine(source, "a.png");
        string tags = Path.Combine(source, "a.txt");
        SaveImage(image);
        File.WriteAllText(tags, "masterpiece, blue_hair");
        byte[] originalImage = File.ReadAllBytes(image);
        string originalTags = File.ReadAllText(tags);
        var service = new CaptionGenerationService((_, _) =>
            Task.FromResult(new CaptionModelResponse("A person with blue hair.", string.Empty)));
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        CaptionGenerationResult result = await service.ProcessAsync(scan, new CaptionGenerationOptions());

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(originalImage, File.ReadAllBytes(image));
        Assert.Equal(originalTags, File.ReadAllText(tags));
        Assert.Equal(
            originalTags + Environment.NewLine + "A person with blue hair.",
            File.ReadAllText(CaptionGenerationService.GetOutputTextPath(source, image, "_captioned")));
    }

    [Fact]
    public async Task ProcessingCanReplaceExistingTextWithoutOverwritingAnyImage()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        string image = Path.Combine(source, "a.png");
        SaveImage(image);
        byte[] originalSource = File.ReadAllBytes(image);
        string outputText = CaptionGenerationService.GetOutputTextPath(source, image, "_captioned");
        string outputImage = CaptionGenerationService.GetOutputImagePath(source, image, "_captioned");
        Directory.CreateDirectory(Path.GetDirectoryName(outputText)!);
        File.WriteAllText(outputText, "1girl, blue_hair, solo");
        byte[] existingOutputImage = new byte[] { 4, 3, 2, 1 };
        File.WriteAllBytes(outputImage, existingOutputImage);
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");
        var service = new CaptionGenerationService((_, _) =>
            Task.FromResult(new CaptionModelResponse("A woman with blue hair stands alone.", string.Empty)));

        CaptionGenerationResult result = await service.ProcessAsync(
            scan,
            new CaptionGenerationOptions { SkipExisting = false });

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("A woman with blue hair stands alone.", File.ReadAllText(outputText));
        Assert.Equal(originalSource, File.ReadAllBytes(image));
        Assert.Equal(existingOutputImage, File.ReadAllBytes(outputImage));
    }

    [Fact]
    public async Task ProcessingHonorsConfiguredMaximumConcurrency()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        for (int index = 0; index < 8; index++)
            SaveImage(Path.Combine(source, $"{index}.png"));

        int active = 0;
        int maximumActive = 0;
        var service = new CaptionGenerationService(async (_, cancellationToken) =>
        {
            int current = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = maximumActive;
                if (observed >= current)
                    break;
            }
            while (Interlocked.CompareExchange(ref maximumActive, current, observed) != observed);

            try
            {
                await Task.Delay(80, cancellationToken);
                return new CaptionModelResponse("A blue square.", string.Empty);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        CaptionGenerationResult result = await service.ProcessAsync(
            scan,
            new CaptionGenerationOptions { MaxConcurrency = 3 });

        Assert.Equal(8, result.Succeeded);
        Assert.Equal(3, maximumActive);
    }

    [Fact]
    public void CaptionGenerationDefaultsToFiveConcurrentRequests()
    {
        Assert.Equal(5, new CaptionGenerationOptions().MaxConcurrency);
    }

    [Fact]
    public async Task CaptionSinkReceivesCleanedCaptionAndSkipsFileOutput()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        string image = Path.Combine(source, "a.png");
        SaveImage(image);
        File.WriteAllText(Path.Combine(source, "a.txt"), "1girl, solo");
        var captured = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var service = new CaptionGenerationService((_, _) =>
            Task.FromResult(new CaptionModelResponse("<think>reasoning</think>A girl stands alone.", string.Empty)));
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        CaptionGenerationResult result = await service.ProcessAsync(
            scan,
            new CaptionGenerationOptions { IncludeOriginalTags = false, CaptionSink = (path, caption) => captured[path] = caption });

        Assert.Equal(1, result.Succeeded);
        // NL-only format: think block stripped, source tags NOT prepended.
        Assert.Single(captured);
        Assert.Equal("A girl stands alone.", captured.Values.Single());
        // With a sink provided, the "_captioned" side file is never written.
        Assert.False(File.Exists(CaptionGenerationService.GetOutputTextPath(source, image, "_captioned")));
    }

    [Fact]
    public async Task CaptionSinkPrependsOriginalTagsWhenTagsPlusNlFormat()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        string image = Path.Combine(source, "a.png");
        SaveImage(image);
        File.WriteAllText(Path.Combine(source, "a.txt"), "1girl, solo");
        var captured = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var service = new CaptionGenerationService((_, _) =>
            Task.FromResult(new CaptionModelResponse("A girl stands alone.", string.Empty)));
        CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(source, "_captioned");

        CaptionGenerationResult result = await service.ProcessAsync(
            scan,
            new CaptionGenerationOptions { IncludeOriginalTags = true, CaptionSink = (path, caption) => captured[path] = caption });

        Assert.Equal(1, result.Succeeded);
        // Tags + natural language: original tags, one newline, then the caption.
        Assert.Equal("1girl, solo" + Environment.NewLine + "A girl stands alone.", captured.Values.Single());
    }

    private static void SaveImage(string path)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(20, 80, 160));
        image.Save(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-caption-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
