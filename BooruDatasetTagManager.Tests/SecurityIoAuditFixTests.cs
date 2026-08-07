using BooruDatasetTagManager;
using Newtonsoft.Json;
using Xunit;

namespace BooruDatasetTagManager.Tests;

/// <summary>
/// Regression coverage for the 2026-08 security / I/O audit fixes
/// (docs/SECURITY_IO_AUDIT_2026-08.md).
/// </summary>
public sealed class SecurityIoAuditFixTests
{
    [Theory]
    [InlineData("foo/../../outside")]
    [InlineData("../outside")]
    [InlineData("a/./b")]
    public void IsSafeRelativeFolder_rejects_dot_segments(string relative)
    {
        Assert.False(DatasetFolderIndex.IsSafeRelativeFolder(relative));
    }

    [Fact]
    public void RenameFolder_rejects_dot_dot_relative_paths()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "1_alpha"));
        File.WriteAllBytes(Path.Combine(temp.Path, "1_alpha", "a.png"), new byte[] { 1, 2, 3 });
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        Assert.Throws<ArgumentException>(() => manager.RenameFolder("1_alpha/../../outside", "x"));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "1_alpha")));
    }

    [Fact]
    public void ResolveFileUnderDirectory_strips_path_segments()
    {
        using var temp = new TemporaryDirectory();
        string target = DatasetFolderIndex.ResolveFileUnderDirectory(temp.Path, @"..\..\evil\payload.zip");
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, "payload.zip")), target);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public void ResolveFileUnderDirectory_rejects_empty_and_dot_names(string name)
    {
        using var temp = new TemporaryDirectory();
        Assert.ThrowsAny<Exception>(() => DatasetFolderIndex.ResolveFileUnderDirectory(temp.Path, name));
    }

    [Fact]
    public void GetOutputImagePath_rejects_image_outside_source_root()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "dataset");
        Directory.CreateDirectory(source);
        string outside = Path.Combine(temp.Path, "outside.png");
        File.WriteAllBytes(outside, new byte[] { 1 });

        Assert.Throws<ArgumentException>(() =>
            CaptionGenerationService.GetOutputImagePath(source, outside, "_captioned"));
    }

    [Fact]
    public void Parser_uses_local_category_when_model_mislabels_protected_tag()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "sitting" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[]
            {
                new
                {
                    tag = "sitting",
                    decision = "delete",
                    category = "clothing",
                    reason = "model lied"
                }
            }
        });

        CharacterTagAuditItem result = Assert.Single(
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));

        Assert.Equal(CharacterTagCategory.Action, result.Category);
        Assert.Equal(CharacterTagDecision.Keep, result.FinalDecision);
        Assert.False(result.CanDelete);
        Assert.False(result.ShouldDelete);
    }

    [Fact]
    public void BoundedStringLog_retains_only_trailing_chars()
    {
        var log = new VideoProcessingService.BoundedStringLog(32);
        log.AppendLine(new string('A', 100));
        log.AppendLine("TAIL");
        string text = log.ToString();
        Assert.Equal(32, text.Length);
        Assert.EndsWith("TAIL" + Environment.NewLine, text);
    }
}
