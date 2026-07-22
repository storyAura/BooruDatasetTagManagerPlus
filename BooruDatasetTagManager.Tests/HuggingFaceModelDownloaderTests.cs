using System;
using System.IO;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class HuggingFaceModelDownloaderTests
{
    [Fact]
    public void BuildDownloadUrl_uses_huggingface_for_official_source()
    {
        string url = HuggingFaceModelDownloader.BuildDownloadUrl(
            HuggingFaceDownloadSource.HuggingFace,
            "SmilingWolf/wd-vit-tagger-v3",
            "model.onnx");

        Assert.Equal("https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx", url);
    }

    [Fact]
    public void BuildDownloadUrl_uses_mirror_for_hf_mirror_source()
    {
        string url = HuggingFaceModelDownloader.BuildDownloadUrl(
            HuggingFaceDownloadSource.HfMirror,
            "deepghs/pixai-tagger-v0.9-onnx",
            "selected_tags.csv");

        Assert.Equal("https://hf-mirror.com/deepghs/pixai-tagger-v0.9-onnx/resolve/main/selected_tags.csv", url);
    }

    [Fact]
    public void GetLocalPath_places_files_under_models_directory()
    {
        string path = HuggingFaceModelDownloader.GetLocalPath("SmilingWolf/wd-vit-tagger-v3", "model.onnx");

        Assert.EndsWith("Models\\SmilingWolf\\wd-vit-tagger-v3\\model.onnx", path.Replace('/', '\\'));
    }

    [Fact]
    public void ValidateCachedFile_accepts_graph_only_onnx_smaller_than_the_size_floor()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bdtm-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // cl_tagger_v2 ships a ~773KB graph-only model.onnx whose weights
            // live in model.onnx.data; the 1MB floor alone must not reject it.
            string graphOnnx = Path.Combine(dir, "model.onnx");
            File.WriteAllBytes(graphOnnx, new byte[] { 0x08, 0x0A, 0x12, 0x00 });
            Assert.True(HuggingFaceModelDownloader.ValidateCachedFile(graphOnnx, "v2_01a/model.onnx"));

            string jsonOnnx = Path.Combine(dir, "error.onnx");
            File.WriteAllText(jsonOnnx, "{\"error\":\"denied\"}");
            Assert.False(HuggingFaceModelDownloader.ValidateCachedFile(jsonOnnx, "model.onnx"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateCachedFile_rejects_html_and_small_onnx_files()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bdtm-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string htmlOnnx = Path.Combine(dir, "model.onnx");
            File.WriteAllText(htmlOnnx, "<html>error</html>");
            Assert.False(HuggingFaceModelDownloader.ValidateCachedFile(htmlOnnx, "model.onnx"));

            string smallOnnx = Path.Combine(dir, "small.onnx");
            File.WriteAllBytes(smallOnnx, new byte[1024]);
            Assert.False(HuggingFaceModelDownloader.ValidateCachedFile(smallOnnx, "model.onnx"));

            string csvPath = Path.Combine(dir, "selected_tags.csv");
            File.WriteAllText(csvPath, "name,category" + Environment.NewLine + "1girl,0");
            Assert.True(HuggingFaceModelDownloader.ValidateCachedFile(csvPath, "selected_tags.csv"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("https://huggingface.co/org/repo/resolve/main/model.onnx", true)]
    [InlineData("https://cdn-lfs.huggingface.co/some/blob", true)]
    [InlineData("https://hf-mirror.com/org/repo/resolve/main/model.onnx", false)]
    [InlineData("http://huggingface.co/org/repo/resolve/main/model.onnx", false)]
    [InlineData("https://evilhuggingface.co/org/repo", false)]
    [InlineData("https://huggingface.co.evil.com/org/repo", false)]
    [InlineData("not a url", false)]
    public void ShouldAttachAuthToken_only_allows_https_huggingface_hosts(string url, bool expected)
    {
        // SEC-01: the user's gated-repo token must never reach a mirror host.
        Assert.Equal(expected, HuggingFaceModelDownloader.ShouldAttachAuthToken(url));
    }
}
