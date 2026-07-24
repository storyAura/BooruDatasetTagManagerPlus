using System.Text;
using System.Windows.Forms;
using Xunit;

namespace BooruDatasetTagManager.Tests;

/// <summary>
/// Regression tests for the issues found by the 2026-07-13 I/O audit
/// (IO_BUG_AUDIT.md). Each test class targets one audit finding.
/// </summary>
public sealed class TransactionRecoveryTrustBoundaryTests : IDisposable
{
    private readonly string datasetRoot;
    private readonly string outsideDir;

    public TransactionRecoveryTrustBoundaryTests()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "BDTM-io01-" + Guid.NewGuid().ToString("N"));
        datasetRoot = Path.Combine(baseDir, "dataset");
        outsideDir = Path.Combine(baseDir, "outside");
        Directory.CreateDirectory(datasetRoot);
        Directory.CreateDirectory(outsideDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(datasetRoot)!, true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateTransaction(string manifestJson, params (string name, string content)[] files)
    {
        string txnDir = Path.Combine(datasetRoot, CharacterTagFileTransaction.DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(txnDir);
        File.WriteAllText(Path.Combine(txnDir, "manifest.json"), manifestJson);
        foreach ((string name, string content) in files)
            File.WriteAllText(Path.Combine(txnDir, name), content);
        return txnDir;
    }

    private static string ManifestFor(string targetPath, bool existed, string stagedFile = "new-0.txt", string backupFile = "")
    {
        string escapedTarget = targetPath.Replace("\\", "\\\\");
        string escapedBackup = backupFile.Replace("\\", "\\\\");
        return "{\"Entries\":[{\"TargetPath\":\"" + escapedTarget + "\",\"Existed\":" + (existed ? "true" : "false") +
               ",\"StagedFile\":\"" + stagedFile + "\",\"BackupFile\":\"" + escapedBackup + "\",\"Applied\":false}]}";
    }

    [Fact]
    public async Task Recovery_refuses_to_delete_file_outside_dataset_root()
    {
        string victim = Path.Combine(outsideDir, "victim.txt");
        File.WriteAllText(victim, "precious");
        CreateTransaction(ManifestFor(victim, existed: false));

        await CharacterTagFileTransaction.RecoverIncompleteAsync(datasetRoot);

        Assert.True(File.Exists(victim));
        Assert.Equal("precious", File.ReadAllText(victim));
        Assert.Empty(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.DirectoryPrefix + "*"));
        Assert.Single(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.QuarantinePrefix + "*"));
    }

    [Fact]
    public async Task Recovery_refuses_to_overwrite_file_outside_dataset_root()
    {
        string victim = Path.Combine(outsideDir, "victim.txt");
        File.WriteAllText(victim, "precious");
        CreateTransaction(
            ManifestFor(victim, existed: true, backupFile: "backup-0.txt"),
            ("backup-0.txt", "attacker content"));

        await CharacterTagFileTransaction.RecoverIncompleteAsync(datasetRoot);

        Assert.Equal("precious", File.ReadAllText(victim));
        Assert.Single(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.QuarantinePrefix + "*"));
    }

    [Fact]
    public async Task Recovery_refuses_backup_file_names_with_path_segments()
    {
        string target = Path.Combine(datasetRoot, "a.txt");
        File.WriteAllText(target, "old");
        string escapingBackup = "..\\..\\outside\\victim.txt";
        CreateTransaction(ManifestFor(target, existed: true, backupFile: escapingBackup));

        await CharacterTagFileTransaction.RecoverIncompleteAsync(datasetRoot);

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Single(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.QuarantinePrefix + "*"));
    }

    [Fact]
    public async Task Recovery_quarantines_corrupted_manifest_instead_of_throwing()
    {
        CreateTransaction("{ this is not valid json !!");

        await CharacterTagFileTransaction.RecoverIncompleteAsync(datasetRoot);

        Assert.Empty(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.DirectoryPrefix + "*"));
        Assert.Single(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.QuarantinePrefix + "*"));
    }

    [Fact]
    public async Task Recovery_still_restores_valid_in_root_transaction()
    {
        string target = Path.Combine(datasetRoot, "a.txt");
        File.WriteAllText(target, "half-written");
        CreateTransaction(
            ManifestFor(target, existed: true, backupFile: "backup-0.txt"),
            ("backup-0.txt", "old"),
            ("new-0.txt", "new"));

        await CharacterTagFileTransaction.RecoverIncompleteAsync(datasetRoot);

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Empty(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.DirectoryPrefix + "*"));
        Assert.Empty(Directory.GetDirectories(datasetRoot, CharacterTagFileTransaction.QuarantinePrefix + "*"));
    }
}

public sealed class SafeFileConcurrencyTests : IDisposable
{
    private readonly string tempDir;

    public SafeFileConcurrencyTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-io04-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Concurrent_writers_to_same_target_never_corrupt_or_throw()
    {
        string target = Path.Combine(tempDir, "contested.txt");
        string contentA = new string('A', 4096);
        string contentB = new string('B', 4096);

        var tasks = new List<Task>();
        for (int i = 0; i < 25; i++)
        {
            tasks.Add(Task.Run(() => SafeFile.WriteAllText(target, contentA)));
            tasks.Add(Task.Run(() => SafeFile.WriteAllText(target, contentB)));
        }
        await Task.WhenAll(tasks);

        string final = File.ReadAllText(target);
        Assert.True(final == contentA || final == contentB,
            "Final content must be exactly one writer's payload, not interleaved.");
        Assert.Empty(Directory.GetFiles(tempDir, "*.tmp"));
    }

    [Fact]
    public void Failed_replace_leaves_no_temp_file_behind()
    {
        string target = Path.Combine(tempDir, "locked.txt");
        File.WriteAllText(target, "precious");

        using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Exception ex = Record.Exception(() => SafeFile.WriteAllText(target, "overwrite"));
            Assert.True(ex is IOException || ex is UnauthorizedAccessException);
        }

        Assert.Equal("precious", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(tempDir, "*.tmp"));
    }
}

public sealed class ImageFileDeleterTests : IDisposable
{
    private readonly string tempDir;

    public ImageFileDeleterTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-io05-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            ClearReadOnlyRecursive(tempDir);
            Directory.Delete(tempDir, true);
        }
        catch (IOException)
        {
        }
    }

    private static void ClearReadOnlyRecursive(string dir)
    {
        foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
    }

    [Fact]
    public void Deletes_image_and_tag_file_together()
    {
        string image = Path.Combine(tempDir, "a.png");
        string tags = Path.Combine(tempDir, "a.txt");
        File.WriteAllBytes(image, new byte[] { 1, 2, 3 });
        File.WriteAllText(tags, "1girl");

        bool result = ImageFileDeleter.DeleteImageWithTags(image, tags, out string error);

        Assert.True(result, error);
        Assert.False(File.Exists(image));
        Assert.False(File.Exists(tags));
        Assert.False(Directory.Exists(Path.Combine(tempDir, ImageFileDeleter.TrashDirectoryName)));
    }

    [Fact]
    public void Locked_tag_file_restores_the_image_instead_of_half_deleting()
    {
        string image = Path.Combine(tempDir, "a.png");
        string tags = Path.Combine(tempDir, "a.txt");
        File.WriteAllBytes(image, new byte[] { 1, 2, 3 });
        File.WriteAllText(tags, "1girl");

        bool result;
        string error;
        using (new FileStream(tags, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = ImageFileDeleter.DeleteImageWithTags(image, tags, out error);
        }

        Assert.False(result);
        Assert.NotNull(error);
        // The audit's IO-05 scenario: previously the image was already gone
        // while the item was reported as "failed to delete".
        Assert.True(File.Exists(image), "Image must be restored when the tag file cannot be deleted.");
        Assert.True(File.Exists(tags));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(image));
    }

    [Fact]
    public void Image_without_tag_file_is_deleted()
    {
        string image = Path.Combine(tempDir, "b.png");
        File.WriteAllBytes(image, new byte[] { 9 });

        bool result = ImageFileDeleter.DeleteImageWithTags(image, Path.Combine(tempDir, "b.txt"), out string error);

        Assert.True(result, error);
        Assert.False(File.Exists(image));
    }
}

public sealed class TranslationCachePersistenceTests : IDisposable
{
    private readonly string tempDir;

    public TranslationCachePersistenceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-io06-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Concurrent_appends_and_rewrites_lose_no_entries_and_never_throw()
    {
        using var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, tempDir, new NullTranslator());
        manager.LoadTranslations();

        var tasks = new List<Task>();
        for (int i = 0; i < 40; i++)
        {
            int n = i;
            // New entries append; repeated manual updates of tag0 force full
            // rewrites racing against those appends.
            tasks.Add(manager.AddTranslationAsync($"tag{n}", $"trans{n}", isManual: false));
            tasks.Add(manager.AddTranslationAsync("tag0", $"manual{n}", isManual: true));
        }
        await Task.WhenAll(tasks);

        using var reloaded = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, tempDir, new NullTranslator());
        reloaded.LoadTranslations();
        for (int i = 1; i < 40; i++)
        {
            Assert.True(reloaded.Contains($"tag{i}"), $"tag{i} was lost from the persisted cache.");
            Assert.Equal($"trans{i}", reloaded.GetTranslation($"tag{i}"));
        }
        Assert.True(reloaded.Contains("tag0"));
    }

    private sealed class NullTranslator : AbstractTranslator
    {
        public NullTranslator() : base(TranslationService.GoogleTranslate)
        {
        }

        public override Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }

        public override void Dispose()
        {
        }
    }
}

public sealed class ImageSorterPathSafetyTests : IDisposable
{
    private readonly string tempDir;

    public ImageSorterPathSafetyTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-io07-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("cats", true)]
    [InlineData("black dress", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a\\b", false)]
    [InlineData("a/b", false)]
    [InlineData("C:", false)]
    [InlineData("con|cat", false)]
    public void IsValidCategoryName_accepts_only_plain_folder_names(string name, bool expected)
    {
        Assert.Equal(expected, ImageSorter.IsValidCategoryName(name));
    }

    [Fact]
    public async Task StartCopy_refuses_destinations_escaping_the_root()
    {
        string rootDir = Path.Combine(tempDir, "root");
        string outsideDir = Path.Combine(tempDir, "escaped");
        Directory.CreateDirectory(rootDir);
        string sourceImage = Path.Combine(tempDir, "src.png");
        File.WriteAllBytes(sourceImage, new byte[] { 1 });

        var sorter = new ImageSorter(rootDir);
        var rootNode = new TreeNode("Root") { Name = "Root" };
        rootNode.Nodes.Add("..\\escaped", "..\\escaped");
        sorter.CreateFromTreeNode(rootNode);
        sorter.AddFileQueue("..\\escaped", sourceImage);

        await sorter.StartCopyAsync();

        Assert.Single(sorter.LastCopyErrors);
        Assert.False(Directory.Exists(outsideDir), "No directory may be created outside the sorter root.");
        Assert.Empty(Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Equals(sourceImage, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task StartCopy_copies_into_valid_category_inside_root()
    {
        string rootDir = Path.Combine(tempDir, "root");
        Directory.CreateDirectory(rootDir);
        string sourceImage = Path.Combine(tempDir, "src.png");
        File.WriteAllBytes(sourceImage, new byte[] { 1 });

        var sorter = new ImageSorter(rootDir);
        var rootNode = new TreeNode("Root") { Name = "Root" };
        rootNode.Nodes.Add("cats", "cats");
        sorter.CreateFromTreeNode(rootNode);
        sorter.AddFileQueue("cats", sourceImage);

        await sorter.StartCopyAsync();

        Assert.Empty(sorter.LastCopyErrors);
        Assert.Single(Directory.GetFiles(Path.Combine(rootDir, "cats")));
    }
}

public sealed class TolerantFileEnumeratorTests : IDisposable
{
    private readonly string tempDir;

    public TolerantFileEnumeratorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-io03-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Skips_app_internal_bdtm_directories()
    {
        File.WriteAllText(Path.Combine(tempDir, "a.png"), "x");
        string trash = Path.Combine(tempDir, ".bdtm-trash", "abc");
        Directory.CreateDirectory(trash);
        File.WriteAllText(Path.Combine(trash, "staged.png"), "x");

        var errors = new List<string>();
        var files = TolerantFileEnumerator.GetFiles(tempDir, errors);

        Assert.Single(files);
        Assert.EndsWith("a.png", files[0]);
        Assert.Empty(errors);
    }

    [Fact]
    public void Missing_root_reports_error_instead_of_throwing()
    {
        var errors = new List<string>();
        var files = TolerantFileEnumerator.GetFiles(Path.Combine(tempDir, "does-not-exist"), errors);

        Assert.Empty(files);
        Assert.Single(errors);
    }

    [Fact]
    public void Walks_nested_directories()
    {
        Directory.CreateDirectory(Path.Combine(tempDir, "sub", "subsub"));
        File.WriteAllText(Path.Combine(tempDir, "a.png"), "x");
        File.WriteAllText(Path.Combine(tempDir, "sub", "b.png"), "x");
        File.WriteAllText(Path.Combine(tempDir, "sub", "subsub", "c.png"), "x");

        var files = TolerantFileEnumerator.GetFiles(tempDir, new List<string>());

        Assert.Equal(3, files.Count);
    }
}

public sealed class DatasetLoadIsolationTests : IDisposable
{
    private readonly string tempDir;

    public DatasetLoadIsolationTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-io03b-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Locked_tag_file_fails_only_that_item_not_the_whole_load()
    {
        File.WriteAllBytes(Path.Combine(tempDir, "ok.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(tempDir, "ok.txt"), "1girl");
        File.WriteAllBytes(Path.Combine(tempDir, "locked.png"), new byte[] { 1 });
        string lockedTags = Path.Combine(tempDir, "locked.txt");
        File.WriteAllText(lockedTags, "solo");

        using var manager = new DatasetManager();
        bool loaded;
        using (new FileStream(lockedTags, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            loaded = manager.LoadFromFolder(tempDir, loadPreviewImages: false, readMetadata: false);
        }

        Assert.True(loaded, "One locked tag file must not fail the whole dataset load.");
        Assert.Single(manager.DataSet);
        Assert.Single(manager.LastLoadErrors);
        Assert.Contains("locked.png", manager.LastLoadErrors[0]);
    }
}

/// <summary>
/// Source-level assertions for UI wiring that is not compiled into the test
/// project (Form1) — same pattern as the existing audit integration tests.
/// </summary>
public sealed class IoAuditSourceWiringTests
{
    [Fact]
    public void FormClosing_cancels_exit_when_save_reported_errors()
    {
        string source = ReadMainSource("Form1.cs");
        int closing = source.IndexOf("private void Form1_FormClosing", StringComparison.Ordinal);
        Assert.True(closing >= 0);
        string body = source.Substring(closing, 1200);
        Assert.Contains("if (ReportSaveErrorsIfAny())", body);
        Assert.Contains("e.Cancel = true;", body);
    }

    [Fact]
    public void Dataset_switch_aborts_on_save_errors_and_on_cancel()
    {
        string source = ReadMainSource("Form1.cs");
        int method = source.IndexOf("private async Task LoadFromFolderAsync", StringComparison.Ordinal);
        Assert.True(method >= 0);
        string body = source.Substring(method, 2000);
        Assert.Contains("if (ReportSaveErrorsIfAny())", body);
        Assert.Contains("else if (result == DialogResult.Cancel)", body);
    }

    [Fact]
    public void New_dataset_loads_into_candidate_before_old_one_is_disposed()
    {
        string source = ReadMainSource("Form1.cs");
        int candidate = source.IndexOf("DatasetManager candidateManager = new DatasetManager();", StringComparison.Ordinal);
        int replace = source.IndexOf("Program.DataManager = candidateManager;", StringComparison.Ordinal);
        int dispose = source.IndexOf("oldDataManager.Dispose();", StringComparison.Ordinal);
        Assert.True(candidate >= 0, "Load must go through a candidate manager.");
        Assert.True(replace > candidate, "The global manager is only replaced after a successful load.");
        Assert.True(dispose > replace, "The old manager is only disposed after the swap.");
    }

    [Fact]
    public void DeleteImage_uses_the_transactional_deleter()
    {
        string source = ReadMainSource("Form1.cs");
        Assert.Contains("ImageFileDeleter.DeleteImageWithTags(", source);
    }

    [Fact]
    public void Language_loading_is_fully_guarded_before_the_message_loop()
    {
        string languageManager = ReadMainSource("LanguageManager.cs");
        Assert.Contains("failed to enumerate language files", languageManager);
        string i18n = ReadMainSource("I18n.cs");
        Assert.Contains("CreateLanguageManagerSafe", i18n);
    }

    [Fact]
    public void All_language_files_contain_the_new_error_keys()
    {
        string languagesDir = Path.Combine(RepoRoot(), "BooruDatasetTagManager", "Languages");
        foreach (string file in Directory.GetFiles(languagesDir, "*.txt"))
        {
            string content = File.ReadAllText(file, Encoding.UTF8);
            Assert.Contains("TipLoadErrors=", content);
            Assert.Contains("TipSorterInvalidCategoryName=", content);
            Assert.Contains("TipSorterInvalidIndex=", content);
        }
    }

    private static string ReadMainSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(RepoRoot(), "BooruDatasetTagManager", fileName));
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "BooruDatasetTagManager.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
