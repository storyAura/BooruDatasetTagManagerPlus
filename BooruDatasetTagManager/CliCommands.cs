using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Headless command-line interface for automation (CI, batch scripts,
    /// future services). Pure static logic over caption files — no Program.*
    /// or WinForms references, so the class links into the test project.
    /// Program.Main routes here when the first argument is a known verb;
    /// with no arguments the GUI starts exactly as before.
    /// </summary>
    public static class CliCommands
    {
        public const int ExitOk = 0;
        public const int ExitError = 1;
        public const int ExitUsage = 2;

        private static readonly string[] KnownVerbs =
        {
            "help", "version", "stats", "list-images", "list-tags", "classify-tags",
            "add-tags", "remove-tags", "replace-tag", "export", "fix-tags",
            "onnx-models", "onnx-tag", "audit"
        };

        // Verbs that need the full app runtime (ONNX sessions, the LLM client,
        // app settings). Their implementation lives in CliAiCommands, which is
        // deliberately not linked into the test project; Program.Main installs
        // the hook, and without it these verbs fail with a clean message.
        private static readonly string[] AiVerbs = { "onnx-models", "onnx-tag", "audit" };

        /// <summary>Runs the AI verbs; installed by Program.Main.</summary>
        public static Func<string[], TextWriter, TextWriter, int> AiRunner;

        /// <summary>
        /// Optional general-tag L1/L2 catalog. Null or empty keeps the
        /// heuristic <see cref="TagSemanticClassifier"/> path used by tests.
        /// </summary>
        public static GeneralTagCategoryCatalog GeneralCategoryCatalog;

        private static readonly string[] HelpFlags = { "--help", "-h", "/?" };
        private static readonly string[] VersionFlags = { "--version", "-v" };

        /// <summary>True when the arguments select the headless CLI path.
        /// Unknown first arguments fall through to the GUI, so double-click
        /// and existing shortcuts keep working.</summary>
        public static bool IsCliInvocation(string[] args)
        {
            if (args == null || args.Length == 0)
                return false;
            string first = args[0].ToLowerInvariant();
            return KnownVerbs.Contains(first)
                || HelpFlags.Contains(first)
                || VersionFlags.Contains(first);
        }

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            try
            {
                return RunCore(args, output, error);
            }
            catch (CliUsageException ex)
            {
                error.WriteLine("Error: " + ex.Message);
                error.WriteLine("Run 'help' for usage.");
                return ExitUsage;
            }
            catch (Exception ex)
            {
                error.WriteLine("Error: " + ex.Message);
                return ExitError;
            }
        }

        private static int RunCore(string[] args, TextWriter output, TextWriter error)
        {
            string verb = args[0].ToLowerInvariant();
            if (HelpFlags.Contains(verb) || verb == "help")
            {
                PrintUsage(output);
                return ExitOk;
            }
            if (VersionFlags.Contains(verb) || verb == "version")
            {
                output.WriteLine(typeof(CliCommands).Assembly.GetName().Version?.ToString(3) ?? "unknown");
                return ExitOk;
            }

            if (AiVerbs.Contains(verb))
            {
                if (AiRunner == null)
                {
                    error.WriteLine("Error: AI commands (ONNX tagging, LLM audit) are not available in this build.");
                    return ExitError;
                }
                return AiRunner(args, output, error);
            }

            CliOptions options = CliOptions.Parse(args);
            CliDataset dataset = CliDataset.Load(options, error);
            switch (verb)
            {
                case "stats": return RunStats(dataset, output);
                case "list-images": return RunListImages(dataset, options, output);
                case "list-tags": return RunListTags(dataset, options, output);
                case "classify-tags": return RunClassifyTags(dataset, output);
                case "add-tags": return RunAddTags(dataset, options, output);
                case "remove-tags": return RunRemoveTags(dataset, options, output);
                case "replace-tag": return RunReplaceTag(dataset, options, output);
                case "fix-tags": return RunFixTags(dataset, options, output);
                case "export": return RunExport(dataset, options, output);
                default: throw new CliUsageException($"Unknown command '{verb}'.");
            }
        }

        private static void PrintUsage(TextWriter output)
        {
            output.WriteLine("BooruDatasetTagManagerPlus command line");
            output.WriteLine();
            output.WriteLine("Usage: BooruDatasetTagManagerPlus.exe <command> <folder> [options]");
            output.WriteLine();
            output.WriteLine("Commands:");
            output.WriteLine("  stats <folder>                      dataset statistics");
            output.WriteLine("  list-images <folder>                image paths (relative to <folder>)");
            output.WriteLine("      [--tags \"a,b\" --match any|all|none] [--untagged]");
            output.WriteLine("  list-tags <folder>                  tag<TAB>count, most frequent first");
            output.WriteLine("      [--category NAME] [--min-count N]");
            output.WriteLine("  classify-tags <folder>              tag<TAB>category<TAB>count");
            output.WriteLine("                                      (with catalog: tag<TAB>L1<TAB>L2<TAB>count)");
            output.WriteLine("  add-tags <folder> --tags \"a,b\"      add tags to caption files");
            output.WriteLine("      [--position start|end] [--if-tags \"x,y\" --match any|all|none]");
            output.WriteLine("      [--only-untagged]");
            output.WriteLine("  remove-tags <folder> --tags \"a,b\"   remove tags from caption files");
            output.WriteLine("  replace-tag <folder> --from X --to Y  replace a tag everywhere");
            output.WriteLine("  fix-tags <folder>                   fix inconsistent tags (subject-count");
            output.WriteLine("      conflicts, solo on multi-subject images, character parent/child dupes;");
            output.WriteLine("      child variants rarer than the threshold fold into their parent)");
            output.WriteLine("      [--child-threshold N (default 0 = off; e.g. 30 to enable)] [--catalog FILE]");
            output.WriteLine("  export <folder> [--out FILE]        JSON {image: [tags]} to file or stdout");
            output.WriteLine("  onnx-models                         local ONNX tagger models and their status");
            output.WriteLine("  onnx-tag <folder>                   auto-tag images with a local ONNX model");
            output.WriteLine("      [--model ID] [--threshold X] [--character-threshold X]");
            output.WriteLine("      [--write-mode skip|append|replace]  (default skip: only untagged images)");
            output.WriteLine("      [--sort none|confidence|alphabetical] [--no-download]");
            output.WriteLine("      [--download-source NAME] [--hf-token TOKEN]");
            output.WriteLine("  audit <folder> --trigger TAG --reference IMG   LLM character-tag audit");
            output.WriteLine("      [--gender girl|boy] [--style sparse|dense] [--min-count N]");
            output.WriteLine("      [--model NAME] [--report FILE]");
            output.WriteLine("  help | version");
            output.WriteLine();
            output.WriteLine("Common options:");
            output.WriteLine("  --separator S   tag separator on load (default \",\"; \",\" is written back as \", \")");
            output.WriteLine("  --ext E         extension for newly created caption files (default txt)");
            output.WriteLine("  --dry-run       report changes without writing anything");
            output.WriteLine();
            output.WriteLine("Exit codes: 0 ok, 1 error, 2 usage error.");
        }

        // ---- commands ---------------------------------------------------

        private static int RunStats(CliDataset dataset, TextWriter output)
        {
            var counts = dataset.CountTags();
            int tagged = dataset.Items.Count(item => item.Tags.Count > 0);
            output.WriteLine($"images: {dataset.Items.Count}");
            output.WriteLine($"tagged: {tagged}");
            output.WriteLine($"untagged: {dataset.Items.Count - tagged}");
            output.WriteLine($"unique-tags: {counts.Count}");
            output.WriteLine($"tag-instances: {counts.Values.Sum()}");
            return ExitOk;
        }

        private static int RunListImages(CliDataset dataset, CliOptions options, TextWriter output)
        {
            IReadOnlyList<string> filterTags = options.GetTagList("tags");
            foreach (CliItem item in dataset.Items)
            {
                if (options.HasFlag("untagged") && item.Tags.Count > 0)
                    continue;
                if (filterTags.Count > 0 && !MatchesTags(item.Tags, filterTags, options.Match))
                    continue;
                output.WriteLine(item.RelativePath);
            }
            return ExitOk;
        }

        private static bool HasCategoryCatalog =>
            GeneralCategoryCatalog != null && GeneralCategoryCatalog.Count > 0;

        private static int RunListTags(CliDataset dataset, CliOptions options, TextWriter output)
        {
            string categoryName = options.GetValue("category");
            TagCategoryPath? csvFilter = null;
            TagSemanticCategory? heuristicFilter = null;
            if (categoryName != null)
            {
                if (HasCategoryCatalog)
                {
                    if (!TagCategoryTaxonomy.TryParseFilter(categoryName, out TagCategoryPath parsed))
                    {
                        throw new CliUsageException($"Unknown category '{categoryName}'. Valid: "
                            + string.Join(", ", TagCategoryTaxonomy.PrimaryOrder)
                            + " (or Hair, Eyes, …)");
                    }
                    csvFilter = parsed;
                }
                else if (!Enum.TryParse(categoryName, ignoreCase: true, out TagSemanticCategory parsed))
                {
                    throw new CliUsageException($"Unknown category '{categoryName}'. Valid: "
                        + string.Join(", ", Enum.GetNames<TagSemanticCategory>()));
                }
                else
                {
                    heuristicFilter = parsed;
                }
            }
            int minCount = options.GetInt("min-count", 1);
            foreach (var pair in dataset.CountTags()
                .Where(pair => pair.Value >= minCount)
                .Where(pair => MatchesListCategory(pair.Key, csvFilter, heuristicFilter))
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                output.WriteLine($"{pair.Key}\t{pair.Value}");
            }
            return ExitOk;
        }

        private static bool MatchesListCategory(
            string tag, TagCategoryPath? csvFilter, TagSemanticCategory? heuristicFilter)
        {
            if (csvFilter == null && heuristicFilter == null)
                return true;
            if (csvFilter != null)
            {
                return TagCategoryTaxonomy.Classify(tag, -1, GeneralCategoryCatalog, null)
                    .Matches(csvFilter.Value);
            }
            return TagSemanticClassifier.Classify(tag, -1) == heuristicFilter.Value;
        }

        private static int RunClassifyTags(CliDataset dataset, TextWriter output)
        {
            foreach (var pair in dataset.CountTags()
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (HasCategoryCatalog)
                {
                    TagCategoryPath path = TagCategoryTaxonomy.Classify(
                        pair.Key, -1, GeneralCategoryCatalog, null);
                    output.WriteLine($"{pair.Key}\t{path.L1}\t{path.L2}\t{pair.Value}");
                }
                else
                {
                    output.WriteLine($"{pair.Key}\t{TagSemanticClassifier.Classify(pair.Key, -1)}\t{pair.Value}");
                }
            }
            return ExitOk;
        }

        private static int RunAddTags(CliDataset dataset, CliOptions options, TextWriter output)
        {
            IReadOnlyList<string> tags = options.RequireTagList("tags");
            IReadOnlyList<string> ifTags = options.GetTagList("if-tags");
            bool atStart = string.Equals(options.GetValue("position"), "start", StringComparison.OrdinalIgnoreCase);
            int changed = 0;
            foreach (CliItem item in dataset.Items)
            {
                if (options.HasFlag("only-untagged") && item.Tags.Count > 0)
                    continue;
                if (ifTags.Count > 0 && !MatchesTags(item.Tags, ifTags, options.Match))
                    continue;
                List<string> updated = new List<string>(item.Tags);
                List<string> missing = tags.Where(tag => !updated.Contains(tag)).ToList();
                if (missing.Count == 0)
                    continue;
                if (atStart)
                    updated.InsertRange(0, missing);
                else
                    updated.AddRange(missing);
                dataset.Write(item, updated, options.DryRun);
                changed++;
            }
            output.WriteLine($"{(options.DryRun ? "would modify" : "modified")}: {changed}");
            return ExitOk;
        }

        private static int RunRemoveTags(CliDataset dataset, CliOptions options, TextWriter output)
        {
            IReadOnlyList<string> tags = options.RequireTagList("tags");
            int changed = 0;
            foreach (CliItem item in dataset.Items)
            {
                List<string> updated = item.Tags.Where(tag => !tags.Contains(tag)).ToList();
                if (updated.Count == item.Tags.Count)
                    continue;
                dataset.Write(item, updated, options.DryRun);
                changed++;
            }
            output.WriteLine($"{(options.DryRun ? "would modify" : "modified")}: {changed}");
            return ExitOk;
        }

        private static int RunReplaceTag(CliDataset dataset, CliOptions options, TextWriter output)
        {
            string from = NormalizeTag(options.RequireValue("from"));
            string to = NormalizeTag(options.RequireValue("to"));
            if (to.Length == 0)
                throw new CliUsageException("--to must not be empty (use remove-tags to delete).");
            int changed = 0;
            foreach (CliItem item in dataset.Items)
            {
                int index = item.Tags.IndexOf(from);
                if (index < 0)
                    continue;
                List<string> updated = new List<string>(item.Tags);
                if (updated.Contains(to))
                    updated.RemoveAt(index);
                else
                    updated[index] = to;
                dataset.Write(item, updated, options.DryRun);
                changed++;
            }
            output.WriteLine($"{(options.DryRun ? "would modify" : "modified")}: {changed}");
            return ExitOk;
        }

        /// <summary>
        /// Headless twin of the GUI's 测试 → 错误标签修复: subject-count
        /// conflicts and solo removals, plus character parent/child family
        /// resolution driven by the character catalog's relation data (the
        /// deployed Data\danbooru_character_tags.csv by default, --catalog to
        /// override; without a catalog only the subject-count rules run).
        /// </summary>
        private static int RunFixTags(CliDataset dataset, CliOptions options, TextWriter output)
        {
            int childThreshold = options.GetInt("child-threshold", 0);
            if (childThreshold < 0)
                throw new CliUsageException("--child-threshold must be zero or positive.");
            string catalogPath = options.GetValue("catalog")
                ?? Path.Combine(AppContext.BaseDirectory, "Data", "danbooru_character_tags.csv");
            CharacterTagCatalog catalog = CharacterTagCatalog.LoadFromFile(catalogPath);
            Func<string, bool> isCharacterTag = catalog.Count > 0 ? catalog.Contains : _ => false;
            Func<string, string> getParentTag = catalog.Count > 0 ? catalog.GetParentTag : null;
            output.WriteLine(catalog.Count > 0
                ? $"character catalog: {catalog.Count} tags"
                : "character catalog: not found, only subject-count rules apply");

            IReadOnlyList<TagConsistencyIssue> issues = TagConsistencyPlanner.Plan(
                dataset.Items.Select(item => (item.ImagePath, (IReadOnlyList<string>)item.Tags)),
                isCharacterTag,
                dataset.CountTags(),
                getParentTag,
                childThreshold);
            if (issues.Count == 0)
            {
                output.WriteLine("no inconsistent tags found");
                return ExitOk;
            }

            var itemsByPath = dataset.Items.ToDictionary(
                item => item.ImagePath, StringComparer.OrdinalIgnoreCase);
            int modified = 0;
            foreach (IGrouping<string, TagConsistencyIssue> group in
                issues.GroupBy(issue => issue.ImagePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!itemsByPath.TryGetValue(group.Key, out CliItem item))
                    continue;
                var updated = new List<string>(item.Tags);
                foreach (TagConsistencyIssue issue in group)
                {
                    bool fold = issue.Reason == TagConsistencyReason.ChildBelowThreshold;
                    output.WriteLine($"{(fold ? "fold" : "remove")}\t{item.RelativePath}\t{issue.RemoveTag}\t{issue.KeptTag}");
                    int index = updated.IndexOf(issue.RemoveTag);
                    if (index < 0)
                        continue;
                    // A fold becomes the kept tag in place; when the kept tag
                    // is already present, folding collapses to a removal.
                    if (fold && !updated.Contains(issue.KeptTag))
                        updated[index] = issue.KeptTag;
                    else
                        updated.RemoveAt(index);
                }
                dataset.Write(item, updated, options.DryRun);
                modified++;
            }
            output.WriteLine($"{(options.DryRun ? "would modify" : "modified")}: {modified}");
            return ExitOk;
        }

        private static int RunExport(CliDataset dataset, CliOptions options, TextWriter output)
        {
            var map = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (CliItem item in dataset.Items)
                map[item.RelativePath] = new List<string>(item.Tags);
            string json = JsonConvert.SerializeObject(map, Formatting.Indented);
            string outFile = options.GetValue("out");
            if (outFile == null)
            {
                output.WriteLine(json);
            }
            else
            {
                SafeFile.WriteAllText(Path.GetFullPath(outFile), json);
                output.WriteLine($"exported: {map.Count} -> {outFile}");
            }
            return ExitOk;
        }

        private static bool MatchesTags(IReadOnlyList<string> tags, IReadOnlyList<string> filter, string match)
        {
            switch (match)
            {
                case "all": return filter.All(tags.Contains);
                case "none": return !filter.Any(tags.Contains);
                case "any": return filter.Any(tags.Contains);
                default: throw new CliUsageException($"Unknown --match '{match}' (any|all|none).");
            }
        }

        internal static string NormalizeTag(string tag)
        {
            return (tag ?? string.Empty).Trim().ToLowerInvariant();
        }

        internal sealed class CliUsageException : Exception
        {
            public CliUsageException(string message) : base(message) { }
        }

        // ---- argument parsing -------------------------------------------

        /// <summary>Parsed command line: one positional folder plus
        /// case-insensitive "--name value" options and "--name" flags.</summary>
        internal sealed class CliOptions
        {
            private static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "dry-run", "untagged", "only-untagged", "no-download"
            };

            private readonly Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public string Folder { get; private set; }
            public string Separator => GetValue("separator") ?? ",";
            public string NewCaptionExtension => (GetValue("ext") ?? "txt").TrimStart('.');
            public string Match => (GetValue("match") ?? "any").ToLowerInvariant();
            public bool DryRun => HasFlag("dry-run");

            public static CliOptions Parse(string[] args)
            {
                var options = new CliOptions();
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        string name = arg.Substring(2);
                        if (Flags.Contains(name))
                        {
                            options.flags.Add(name);
                        }
                        else
                        {
                            if (i + 1 >= args.Length)
                                throw new CliUsageException($"Option --{name} needs a value.");
                            options.values[name] = args[++i];
                        }
                    }
                    else if (options.Folder == null)
                    {
                        options.Folder = arg;
                    }
                    else
                    {
                        throw new CliUsageException($"Unexpected argument '{arg}'.");
                    }
                }
                if (options.Folder == null)
                    throw new CliUsageException("Missing dataset folder argument.");
                return options;
            }

            public string GetValue(string name)
            {
                return values.TryGetValue(name, out string value) ? value : null;
            }

            public string RequireValue(string name)
            {
                return GetValue(name) ?? throw new CliUsageException($"Missing required option --{name}.");
            }

            public bool HasFlag(string name)
            {
                return flags.Contains(name);
            }

            public int GetInt(string name, int fallback)
            {
                string raw = GetValue(name);
                if (raw == null)
                    return fallback;
                if (!int.TryParse(raw, out int parsed))
                    throw new CliUsageException($"--{name} expects a number, got '{raw}'.");
                return parsed;
            }

            public double? GetDouble(string name)
            {
                string raw = GetValue(name);
                if (raw == null)
                    return null;
                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                {
                    throw new CliUsageException($"--{name} expects a number, got '{raw}'.");
                }
                return parsed;
            }

            /// <summary>Tags from a list-valued option, normalized like the app
            /// (trim + lowercase, deduplicated, order kept). Empty when absent.</summary>
            public IReadOnlyList<string> GetTagList(string name)
            {
                string raw = GetValue(name);
                if (raw == null)
                    return Array.Empty<string>();
                return PromptParser.ParsePrompt(raw, false, Separator)
                    .Select(item => item.Text)
                    .ToList();
            }

            public IReadOnlyList<string> RequireTagList(string name)
            {
                IReadOnlyList<string> tags = GetTagList(name);
                if (tags.Count == 0)
                    throw new CliUsageException($"--{name} needs at least one tag.");
                return tags;
            }
        }

        // ---- dataset access ---------------------------------------------

        internal sealed class CliItem
        {
            public string ImagePath;
            public string RelativePath;
            public string CaptionPath;   // null while no caption file exists
            public List<string> Tags;
        }

        /// <summary>
        /// Caption-file view of a dataset folder: every image/video found by
        /// the recursive tolerant walk plus its parsed sidecar tags. Loads no
        /// pixels, so it stays fast and headless.
        /// </summary>
        internal sealed class CliDataset
        {
            public string Root;
            public List<CliItem> Items;
            private string separator;
            private string newCaptionExtension;

            public static CliDataset Load(CliOptions options, TextWriter error)
            {
                string root = Path.GetFullPath(options.Folder);
                if (!Directory.Exists(root))
                    throw new DirectoryNotFoundException($"Dataset folder not found: {root}");
                var walkErrors = new List<string>();
                var items = new List<CliItem>();
                var captionExtensions = new[] { options.NewCaptionExtension, "txt", "caption" }
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (string path in TolerantFileEnumerator.GetFiles(root, walkErrors)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (!Extensions.ImageExtensions.Contains(extension)
                        && !Extensions.VideoExtensions.Contains(extension))
                    {
                        continue;
                    }
                    string captionPath = ImageEditorSaveService.FindExistingCaptionPath(path, captionExtensions);
                    var tags = new List<string>();
                    if (captionPath != null)
                    {
                        tags = PromptParser.ParsePrompt(File.ReadAllText(captionPath), false, options.Separator)
                            .Select(item => item.Text)
                            .ToList();
                    }
                    items.Add(new CliItem
                    {
                        ImagePath = path,
                        RelativePath = Path.GetRelativePath(root, path).Replace('\\', '/'),
                        CaptionPath = captionPath,
                        Tags = tags
                    });
                }
                foreach (string walkError in walkErrors)
                    error.WriteLine("Warning: " + walkError);
                return new CliDataset
                {
                    Root = root,
                    Items = items,
                    separator = options.Separator,
                    newCaptionExtension = options.NewCaptionExtension
                };
            }

            public Dictionary<string, int> CountTags()
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (CliItem item in Items)
                {
                    foreach (string tag in item.Tags)
                        counts[tag] = counts.TryGetValue(tag, out int count) ? count + 1 : 1;
                }
                return counts;
            }

            /// <summary>Writes the new tag list durably (temp + atomic replace)
            /// and updates the in-memory item; dry runs only update memory-free
            /// state, i.e. nothing at all.</summary>
            public void Write(CliItem item, List<string> tags, bool dryRun)
            {
                if (dryRun)
                    return;
                string target = item.CaptionPath ?? Path.Combine(
                    Path.GetDirectoryName(item.ImagePath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(item.ImagePath) + "." + newCaptionExtension);
                // The bare "," load separator is written back in the app's
                // human-friendly ", " form; any custom separator round-trips.
                string joinWith = separator == "," ? ", " : separator;
                SafeFile.WriteAllText(target, string.Join(joinWith, tags));
                item.CaptionPath = target;
                item.Tags = tags;
            }
        }
    }
}
