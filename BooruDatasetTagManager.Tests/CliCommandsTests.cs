using System.Text;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class CliCommandsTests : IDisposable
{
    private readonly string root;

    public CliCommandsTests()
    {
        root = Path.Combine(Path.GetTempPath(), "bdtm-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        // Fake media: the CLI never decodes pixels, extension is enough.
        File.WriteAllText(Path.Combine(root, "a.png"), "x");
        File.WriteAllText(Path.Combine(root, "a.txt"), "White Hair, smile");
        File.WriteAllText(Path.Combine(root, "sub", "b.jpg"), "x");
        File.WriteAllText(Path.Combine(root, "sub", "b.caption"), "smile, holding sword");
        File.WriteAllText(Path.Combine(root, "sub", "c.webp"), "x");
        File.WriteAllText(Path.Combine(root, "notes.md"), "not media");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter(new StringBuilder());
        var error = new StringWriter(new StringBuilder());
        int code = CliCommands.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void InvocationDetectionOnlyClaimsKnownVerbs()
    {
        Assert.True(CliCommands.IsCliInvocation(new[] { "stats", "folder" }));
        Assert.True(CliCommands.IsCliInvocation(new[] { "--help" }));
        Assert.True(CliCommands.IsCliInvocation(new[] { "VERSION" }));
        Assert.False(CliCommands.IsCliInvocation(Array.Empty<string>()));
        Assert.False(CliCommands.IsCliInvocation(null!));
        Assert.False(CliCommands.IsCliInvocation(new[] { "some-random-arg" }));
    }

    [Fact]
    public void AiVerbsAreRecognizedButNeedTheAppRuntime()
    {
        Assert.True(CliCommands.IsCliInvocation(new[] { "onnx-tag", root }));
        Assert.True(CliCommands.IsCliInvocation(new[] { "onnx-models" }));
        Assert.True(CliCommands.IsCliInvocation(new[] { "audit", root }));

        // The test build never installs CliCommands.AiRunner (CliAiCommands is
        // main-project-only), so the AI verbs must fail with a clean message
        // instead of crashing or silently succeeding.
        (int code, _, string err) = Run("onnx-tag", root);
        Assert.Equal(CliCommands.ExitError, code);
        Assert.Contains("not available", err);
    }

    [Fact]
    public void HelpPrintsUsageAndUsageErrorsExitWithTwo()
    {
        (int helpCode, string helpOut, _) = Run("help");
        Assert.Equal(CliCommands.ExitOk, helpCode);
        Assert.Contains("Usage:", helpOut);
        Assert.Contains("onnx-tag", helpOut);
        Assert.Contains("audit", helpOut);

        (int noFolder, _, string noFolderErr) = Run("stats");
        Assert.Equal(CliCommands.ExitUsage, noFolder);
        Assert.Contains("folder", noFolderErr, StringComparison.OrdinalIgnoreCase);

        (int badMatch, _, _) = Run("list-images", root, "--tags", "smile", "--match", "sometimes");
        Assert.Equal(CliCommands.ExitUsage, badMatch);

        (int missingFolder, _, _) = Run("stats", Path.Combine(root, "no-such-dir"));
        Assert.Equal(CliCommands.ExitError, missingFolder);
    }

    [Fact]
    public void StatsAndListTagsCountAcrossSubfolders()
    {
        (int code, string output, _) = Run("stats", root);
        Assert.Equal(CliCommands.ExitOk, code);
        Assert.Contains("images: 3", output);
        Assert.Contains("tagged: 2", output);
        Assert.Contains("untagged: 1", output);
        Assert.Contains("unique-tags: 3", output);
        Assert.Contains("tag-instances: 4", output);

        (_, string tags, _) = Run("list-tags", root);
        // Loading lowercases like the app; "smile" counts twice.
        Assert.StartsWith("smile\t2", tags);
        Assert.Contains("white hair\t1", tags);

        (_, string frequent, _) = Run("list-tags", root, "--min-count", "2");
        Assert.Contains("smile", frequent);
        Assert.DoesNotContain("white hair", frequent);

        (_, string hairOnly, _) = Run("list-tags", root, "--category", "hair");
        Assert.Contains("white hair", hairOnly);
        Assert.DoesNotContain("smile", hairOnly);

        (_, string classified, _) = Run("classify-tags", root);
        Assert.Contains("white hair\tHair\t1", classified);
        Assert.Contains("smile\tExpression\t2", classified);
    }

    [Fact]
    public void ListImagesSupportsTagAndUntaggedFilters()
    {
        (_, string all, _) = Run("list-images", root);
        Assert.Contains("a.png", all);
        Assert.Contains("sub/b.jpg", all);
        Assert.Contains("sub/c.webp", all);

        (_, string withHair, _) = Run("list-images", root, "--tags", "white hair");
        Assert.Contains("a.png", withHair);
        Assert.DoesNotContain("b.jpg", withHair);

        (_, string none, _) = Run("list-images", root, "--tags", "smile", "--match", "none");
        Assert.DoesNotContain("a.png", none);
        Assert.Contains("sub/c.webp", none);

        (_, string both, _) = Run("list-images", root, "--tags", "smile,white hair", "--match", "all");
        Assert.Contains("a.png", both);
        Assert.DoesNotContain("b.jpg", both);

        (_, string untagged, _) = Run("list-images", root, "--untagged");
        Assert.Equal("sub/c.webp", untagged.Trim());
    }

    [Fact]
    public void AddTagsAppendsCreatesAndRespectsConditions()
    {
        (int code, string output, _) = Run("add-tags", root, "--tags", "1girl, Smile");
        Assert.Equal(CliCommands.ExitOk, code);
        Assert.Contains("modified: 3", output);
        // Existing caption: dedup keeps the one "smile", appends "1girl".
        Assert.Equal("white hair, smile, 1girl", File.ReadAllText(Path.Combine(root, "a.txt")));
        // No caption existed: a new .txt is created.
        Assert.Equal("1girl, smile", File.ReadAllText(Path.Combine(root, "sub", "c.txt")));

        (_, string start, _) = Run("add-tags", root, "--tags", "solo", "--position", "start",
            "--if-tags", "white hair");
        Assert.Contains("modified: 1", start);
        Assert.Equal("solo, white hair, smile, 1girl", File.ReadAllText(Path.Combine(root, "a.txt")));

        (_, string onlyUntagged, _) = Run("add-tags", root, "--tags", "extra", "--only-untagged");
        Assert.Contains("modified: 0", onlyUntagged);
    }

    [Fact]
    public void RemoveAndReplaceRewriteCaptionsWithDedup()
    {
        (_, string removed, _) = Run("remove-tags", root, "--tags", "smile");
        Assert.Contains("modified: 2", removed);
        Assert.Equal("white hair", File.ReadAllText(Path.Combine(root, "a.txt")));
        Assert.Equal("holding sword", File.ReadAllText(Path.Combine(root, "sub", "b.caption")));

        (_, string replaced, _) = Run("replace-tag", root, "--from", "white hair", "--to", "silver hair");
        Assert.Contains("modified: 1", replaced);
        Assert.Equal("silver hair", File.ReadAllText(Path.Combine(root, "a.txt")));

        // Replacing into an already-present tag collapses to one copy.
        File.WriteAllText(Path.Combine(root, "a.txt"), "silver hair, smile");
        (_, string merged, _) = Run("replace-tag", root, "--from", "silver hair", "--to", "smile");
        Assert.Contains("modified: 1", merged);
        Assert.Equal("smile", File.ReadAllText(Path.Combine(root, "a.txt")));
    }

    [Fact]
    public void DryRunReportsWithoutWriting()
    {
        (_, string output, _) = Run("add-tags", root, "--tags", "1girl", "--dry-run");
        Assert.Contains("would modify: 3", output);
        Assert.Equal("White Hair, smile", File.ReadAllText(Path.Combine(root, "a.txt")));
        Assert.False(File.Exists(Path.Combine(root, "sub", "c.txt")));
    }

    [Fact]
    public void FixTagsRemovesConflictsAndFoldsRareChildren()
    {
        string folder = Path.Combine(root, "fix");
        Directory.CreateDirectory(folder);
        string catalogPath = Path.Combine(folder, "relations.csv");
        File.WriteAllText(catalogPath,
            "character_tag,other_names,copyright,parent_tag,post_count\n"
            + "hatsune_miku,,vocaloid,,100\n"
            + "racing_miku,,vocaloid,hatsune_miku,50\n");
        File.WriteAllText(Path.Combine(folder, "d.png"), "x");
        File.WriteAllText(Path.Combine(folder, "d.txt"), "1boy, 2boys, solo, racing miku, hatsune miku");

        // Dry run reports without writing.
        (int dryCode, string dryOut, _) = Run("fix-tags", folder, "--catalog", catalogPath, "--child-threshold", "30", "--dry-run");
        Assert.Equal(CliCommands.ExitOk, dryCode);
        Assert.Contains("would modify: 1", dryOut);
        Assert.Equal("1boy, 2boys, solo, racing miku, hatsune miku",
            File.ReadAllText(Path.Combine(folder, "d.txt")));

        (int code, string output, _) = Run("fix-tags", folder, "--catalog", catalogPath, "--child-threshold", "30");
        Assert.Equal(CliCommands.ExitOk, code);
        Assert.Contains("remove\td.png\t1boy\t2boys", output);
        Assert.Contains("remove\td.png\tsolo\t2boys", output);
        // racing miku appears once (< 30): folded into the present parent.
        Assert.Contains("fold\td.png\tracing miku\thatsune miku", output);
        Assert.Equal("2boys, hatsune miku", File.ReadAllText(Path.Combine(folder, "d.txt")));

        // The fixer converges: a second run finds nothing.
        (_, string second, _) = Run("fix-tags", folder, "--catalog", catalogPath, "--child-threshold", "30");
        Assert.Contains("no inconsistent tags found", second);

        // Without a catalog only the subject-count rules apply.
        (_, string noCatalog, _) = Run("fix-tags", folder);
        Assert.Contains("only subject-count rules apply", noCatalog);

        (_, string helpOut, _) = Run("help");
        Assert.Contains("fix-tags", helpOut);
        Assert.Contains("default 0", helpOut);
    }

    [Fact]
    public void FixTagsDoesNotFoldRareChildrenByDefault()
    {
        string folder = Path.Combine(root, "fix-default");
        Directory.CreateDirectory(folder);
        string catalogPath = Path.Combine(folder, "relations.csv");
        File.WriteAllText(catalogPath,
            "character_tag,other_names,copyright,parent_tag,post_count\n"
            + "kayoko_(blue_archive),,blue archive,,100\n"
            + "kayoko_(dress)_(blue_archive),,blue archive,kayoko_(blue_archive),10\n");
        File.WriteAllText(Path.Combine(folder, "d.png"), "x");
        File.WriteAllText(Path.Combine(folder, "d.txt"), "kayoko (dress) (blue archive)");

        (int code, string output, _) = Run("fix-tags", folder, "--catalog", catalogPath);
        Assert.Equal(CliCommands.ExitOk, code);
        Assert.Contains("no inconsistent tags found", output);
        Assert.Equal("kayoko (dress) (blue archive)", File.ReadAllText(Path.Combine(folder, "d.txt")));
    }

    [Fact]
    public void ExportEmitsJsonToStdoutAndFile()
    {
        (int code, string output, _) = Run("export", root);
        Assert.Equal(CliCommands.ExitOk, code);
        var map = Newtonsoft.Json.JsonConvert
            .DeserializeObject<Dictionary<string, List<string>>>(output)!;
        Assert.Equal(3, map.Count);
        Assert.Equal(new List<string> { "white hair", "smile" }, map["a.png"]);
        Assert.Empty(map["sub/c.webp"]);

        string outFile = Path.Combine(root, "export.json");
        (_, string fileRun, _) = Run("export", root, "--out", outFile);
        Assert.Contains("exported: 3", fileRun);
        Assert.True(File.Exists(outFile));
    }

    [Fact]
    public void CustomSeparatorRoundTrips()
    {
        string folder = Path.Combine(root, "sep");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "d.png"), "x");
        File.WriteAllText(Path.Combine(folder, "d.txt"), "white hair|smile");

        (_, string tags, _) = Run("list-tags", folder, "--separator", "|");
        Assert.Contains("white hair\t1", tags);

        Run("add-tags", folder, "--tags", "1girl", "--separator", "|");
        Assert.Equal("white hair|smile|1girl", File.ReadAllText(Path.Combine(folder, "d.txt")));
    }
}
