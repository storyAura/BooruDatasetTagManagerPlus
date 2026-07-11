using System.Text;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class SafeFileTests : IDisposable
{
    private readonly string tempDir;

    public SafeFileTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "SafeFileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch (IOException)
        {
        }
    }

    private string PathFor(string name) => Path.Combine(tempDir, name);

    [Fact]
    public void WriteAllText_creates_new_file_without_leftover_temp()
    {
        string target = PathFor("new.txt");

        SafeFile.WriteAllText(target, "hello");

        Assert.Equal("hello", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public void WriteAllText_replaces_existing_content_atomically()
    {
        string target = PathFor("existing.txt");
        File.WriteAllText(target, "old");

        SafeFile.WriteAllText(target, "new");

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public void WriteAllText_writes_utf8_without_bom_like_file_writealltext()
    {
        string target = PathFor("encoding.txt");

        SafeFile.WriteAllText(target, "标签");

        byte[] bytes = File.ReadAllBytes(target);
        Assert.Equal(Encoding.UTF8.GetBytes("标签"), bytes);
    }

    [Fact]
    public void WriteAllTextWithBackup_keeps_previous_content_in_bak()
    {
        string target = PathFor("settings.json");
        File.WriteAllText(target, "v1");

        SafeFile.WriteAllTextWithBackup(target, "v2");

        Assert.Equal("v2", File.ReadAllText(target));
        Assert.Equal("v1", File.ReadAllText(target + ".bak"));
    }

    [Fact]
    public void WriteAllBytes_roundtrips_binary_content()
    {
        string target = PathFor("image.png");
        byte[] payload = { 1, 2, 3, 4, 5 };

        SafeFile.WriteAllBytes(target, payload);

        Assert.Equal(payload, File.ReadAllBytes(target));
    }

    [Fact]
    public void Locked_destination_throws_but_original_content_survives()
    {
        string target = PathFor("locked.txt");
        File.WriteAllText(target, "precious");

        using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // File.Replace reports IOException; the File.Move fallback reports
            // UnauthorizedAccessException. Either way the write must fail loudly.
            Exception ex = Record.Exception(() => SafeFile.WriteAllText(target, "overwrite"));
            Assert.True(ex is IOException || ex is UnauthorizedAccessException,
                $"Expected IOException or UnauthorizedAccessException, got {ex?.GetType().Name ?? "none"}");
        }

        // The whole point of the atomic writer: a failed write never truncates
        // the destination (File.WriteAllText used to leave a 0-byte file).
        Assert.Equal("precious", File.ReadAllText(target));
    }
}
