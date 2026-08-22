using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooruDatasetTagManager.AiApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Headless implementations of the CLI verbs that need the full app
    /// runtime: local ONNX auto-tagging and the LLM character-tag audit.
    /// Deliberately NOT linked into the test project (it wires Program.* and
    /// the AiApi client); CliCommands dispatches here through the AiRunner
    /// hook that Program.Main installs. The orchestration mirrors
    /// Form_OnnxTagger / Form_CharacterTagAuditWizard: same services, same
    /// write-mode semantics, same transactional caption writes.
    /// </summary>
    internal static class CliAiCommands
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            EnsureRuntime();
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "onnx-models":
                        return RunOnnxModels(output);
                    case "onnx-tag":
                        return RunOnnxTagAsync(
                            CliCommands.CliOptions.Parse(args), output, error, cancellation.Token)
                            .GetAwaiter().GetResult();
                    case "audit":
                        return RunAuditAsync(
                            CliCommands.CliOptions.Parse(args), output, error, cancellation.Token)
                            .GetAwaiter().GetResult();
                    default:
                        throw new CliCommands.CliUsageException($"Unknown AI command '{args[0]}'.");
                }
            }
            catch (OperationCanceledException)
            {
                error.WriteLine("Cancelled.");
                return CliCommands.ExitError;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }

        /// <summary>
        /// The GUI initializes Program.* during startup; a headless run must
        /// provide the same statics the shared services read (settings,
        /// lockers) before any dataset or tag-list code executes.
        /// </summary>
        private static void EnsureRuntime()
        {
            Program.AppPath ??= Application.StartupPath;
            Program.Settings ??= new AppSettings(AppSettings.ResolveUserSettingsDirectory(Application.StartupPath));
            Program.EditableTagListLocker ??= new SemaphoreSlim(1, 1);
            Program.ListChangeLocker ??= new object();
            DebugLog.Enabled = Program.Settings.DebugMode;
        }

        private sealed class SimpleProgress<T> : IProgress<T>
        {
            private readonly Action<T> handler;

            public SimpleProgress(Action<T> handler)
            {
                this.handler = handler;
            }

            public void Report(T value)
            {
                handler(value);
            }
        }

        // ---- onnx-models ------------------------------------------------

        private static int RunOnnxModels(TextWriter output)
        {
            using var wd14 = new Wd14OnnxTaggerService();
            using var pixai = new PixAiOnnxTaggerService();
            using var cl = new ClTaggerOnnxService();
            foreach (OnnxTaggerModelEntry entry in OnnxTaggerCatalog.AllModels)
            {
                bool ready = entry.Kind switch
                {
                    OnnxTaggerModelKind.Wd14 => wd14.IsModelReady(entry.Repo),
                    OnnxTaggerModelKind.PixAi => pixai.IsModelReady(),
                    _ => cl.IsModelReady(entry.ClModel)
                };
                string thresholds = "threshold=" + entry.DefaultThreshold.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                    + (entry.DefaultCharacterThreshold.HasValue
                        ? " character=" + entry.DefaultCharacterThreshold.Value.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty);
                output.WriteLine($"{entry.Id}\t{(ready ? "ready" : "not-downloaded")}\t{entry.DisplayName}\t{thresholds}");
            }
            return CliCommands.ExitOk;
        }

        // ---- onnx-tag ---------------------------------------------------

        private static async Task<int> RunOnnxTagAsync(
            CliCommands.CliOptions options, TextWriter output, TextWriter error, CancellationToken token)
        {
            OnnxTaggerModelEntry entry = ResolveModel(options.GetValue("model"), output);
            NetworkResultSetMode setMode = ParseWriteMode(options.GetValue("write-mode"));
            using DatasetManager dataset = LoadDataset(options.Folder, error);

            List<DataItem> inputs = dataset.DataSet.Values
                .Where(item => Extensions.ImageExtensions.Contains(
                    Path.GetExtension(item.ImageFilePath).ToLowerInvariant()))
                .ToList();
            inputs.Sort((a, b) => FileNamesComparer.StrCmpLogicalW(a.Name, b.Name));

            // Skip-existing never modifies already-tagged images: filter them
            // out BEFORE inference so no compute is wasted (same P0 contract
            // as Form_OnnxTagger.RunJobCoreAsync).
            int skippedExisting = 0;
            if (setMode == NetworkResultSetMode.SkipExistTagList)
            {
                int before = inputs.Count;
                inputs = inputs.Where(item => item.Tags.Count == 0).ToList();
                skippedExisting = before - inputs.Count;
            }
            if (inputs.Count == 0)
            {
                output.WriteLine($"nothing to tag (skipped-existing: {skippedExisting})");
                return CliCommands.ExitOk;
            }

            (double threshold, double characterThreshold) = ResolveThresholds(entry, options);
            output.WriteLine($"model: {entry.Id}, images: {inputs.Count}, threshold: {threshold}, "
                + $"character-threshold: {characterThreshold}, write-mode: {WriteModeName(setMode)}");

            using var wd14 = new Wd14OnnxTaggerService();
            using var pixai = new PixAiOnnxTaggerService();
            using var cl = new ClTaggerOnnxService();
            await EnsureModelDownloadedAsync(entry, options, wd14, pixai, cl, output, token).ConfigureAwait(false);

            Func<string, OnnxTagResult> tagImage;
            switch (entry.Kind)
            {
                case OnnxTaggerModelKind.PixAi:
                    pixai.LoadModel();
                    tagImage = path => pixai.TagImageWithTiming(path, threshold, characterThreshold);
                    break;
                case OnnxTaggerModelKind.ClTagger:
                    cl.LoadModel(entry.ClModel);
                    tagImage = path => cl.TagImageWithTiming(path, threshold, characterThreshold);
                    break;
                default:
                    wd14.LoadModel(entry.Repo);
                    tagImage = path => wd14.TagImageWithTiming(path, threshold, characterThreshold);
                    break;
            }

            var results = new List<(DataItem Item, OnnxTagResult Result)>(inputs.Count);
            var fileErrors = new List<string>();
            bool cancelled = false;
            for (int i = 0; i < inputs.Count; i++)
            {
                // Stop instead of throwing so results computed so far still get
                // applied and saved, matching the GUI's cancel behavior.
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                DataItem item = inputs[i];
                try
                {
                    OnnxTagResult result = await Task.Run(() => tagImage(item.ImageFilePath), CancellationToken.None)
                        .ConfigureAwait(false);
                    results.Add((item, result));
                    output.WriteLine($"[{i + 1}/{inputs.Count}] {item.Name}: {result.Tags.Count} tags");
                }
                catch (Exception ex)
                {
                    fileErrors.Add(item.ImageFilePath + ": " + ex.Message);
                    error.WriteLine($"[{i + 1}/{inputs.Count}] {item.Name}: {ex.Message}");
                }
            }

            TaggerSettings writeSettings = entry.Kind == OnnxTaggerModelKind.PixAi
                ? Program.Settings.PixAiTagger
                : Program.Settings.Wd14Tagger;
            // Runtime-only overrides: the CLI never calls SaveSettings, so the
            // user's stored GUI preferences stay untouched on disk.
            writeSettings.SetMode = setMode;
            AutoTaggerSort? sort = ParseSort(options.GetValue("sort"));
            if (sort.HasValue)
                writeSettings.SortMode = sort.Value;

            dataset.ExecuteBulkMutation(() =>
            {
                foreach ((DataItem item, OnnxTagResult result) in results)
                    TagWriteService.ApplyTags(item, result.Tags, writeSettings);
            });

            int modified = dataset.DataSet.Values.Count(item => item.IsModified);
            if (options.DryRun)
            {
                output.WriteLine($"would modify: {modified} (skipped-existing: {skippedExisting}, errors: {fileErrors.Count})");
            }
            else
            {
                dataset.SaveAll();
                fileErrors.AddRange(dataset.LastSaveErrors);
                output.WriteLine($"tagged: {results.Count}, modified: {modified}, "
                    + $"skipped-existing: {skippedExisting}, errors: {fileErrors.Count}");
            }
            if (cancelled)
                output.WriteLine("cancelled: remaining images were not processed");
            return fileErrors.Count > 0 ? CliCommands.ExitError : CliCommands.ExitOk;
        }

        private static OnnxTaggerModelEntry ResolveModel(string requestedId, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(requestedId))
            {
                OnnxTaggerModelEntry fallback = OnnxTaggerCatalog.GetById(OnnxTaggerCatalog.ResolveInitialModelId(
                    Program.Settings.OnnxTaggerLastModelId, Program.Settings.Wd14Tagger.SelectedModelRepo));
                output.WriteLine($"model not given, using {fallback.Id} (list ids with 'onnx-models')");
                return fallback;
            }
            OnnxTaggerModelEntry entry = OnnxTaggerCatalog.AllModels.FirstOrDefault(model =>
                string.Equals(model.Id, requestedId, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new CliCommands.CliUsageException($"Unknown model '{requestedId}'. Run 'onnx-models' for the list.");
            return entry;
        }

        private static string ThresholdKey(OnnxTaggerModelEntry entry)
        {
            return entry.Kind == OnnxTaggerModelKind.ClTagger ? entry.Id : entry.Repo;
        }

        private static (double Threshold, double CharacterThreshold) ResolveThresholds(
            OnnxTaggerModelEntry entry, CliCommands.CliOptions options)
        {
            double threshold;
            double characterThreshold;
            if (entry.Kind == OnnxTaggerModelKind.PixAi)
            {
                threshold = Program.Settings.PixAiTagger.GeneralThreshold;
                characterThreshold = Program.Settings.PixAiTagger.CharacterThreshold;
            }
            else
            {
                Wd14TaggerSettings stored = Program.Settings.Wd14Tagger;
                string key = ThresholdKey(entry);
                (threshold, characterThreshold) = stored.GetThresholdsForRepo(key);
                if (entry.Kind == OnnxTaggerModelKind.ClTagger && !stored.HasThresholdsForRepo(key))
                {
                    // First use of a CL model: the WD fallback defaults do not
                    // apply, take the catalog defaults (same as the GUI).
                    threshold = entry.DefaultThreshold;
                    characterThreshold = entry.DefaultCharacterThreshold ?? threshold;
                }
            }
            threshold = options.GetDouble("threshold") ?? threshold;
            characterThreshold = options.GetDouble("character-threshold") ?? characterThreshold;
            return (threshold, characterThreshold);
        }

        private static async Task EnsureModelDownloadedAsync(
            OnnxTaggerModelEntry entry,
            CliCommands.CliOptions options,
            Wd14OnnxTaggerService wd14,
            PixAiOnnxTaggerService pixai,
            ClTaggerOnnxService cl,
            TextWriter output,
            CancellationToken token)
        {
            bool ready = entry.Kind switch
            {
                OnnxTaggerModelKind.Wd14 => wd14.IsModelReady(entry.Repo),
                OnnxTaggerModelKind.PixAi => pixai.IsModelReady(),
                _ => cl.IsModelReady(entry.ClModel)
            };
            if (ready)
                return;
            if (options.HasFlag("no-download"))
            {
                throw new InvalidOperationException(
                    $"Model '{entry.Id}' is not downloaded and --no-download was given.");
            }

            HuggingFaceDownloadSource source = ResolveDownloadSource(entry, options);
            IProgress<(string file, long downloaded, long? total)> progress = CreateDownloadProgress(output);
            output.WriteLine($"downloading model {entry.Id} from {source}...");
            if (entry.Kind == OnnxTaggerModelKind.PixAi)
            {
                await pixai.DownloadModelAsync(source, progress, token).ConfigureAwait(false);
            }
            else if (entry.Kind == OnnxTaggerModelKind.ClTagger)
            {
                string hfToken = options.GetValue("hf-token") ?? Program.Settings.HuggingFaceToken;
                if (entry.ClModel.IsGated && string.IsNullOrWhiteSpace(hfToken))
                {
                    throw new CliCommands.CliUsageException(
                        $"Model '{entry.Id}' is a gated repo: pass --hf-token or store a HuggingFace token once via the GUI.");
                }
                await cl.DownloadModelAsync(entry.ClModel, source, hfToken, progress, token).ConfigureAwait(false);
            }
            else
            {
                await wd14.DownloadModelAsync(entry.Repo, source, progress, token).ConfigureAwait(false);
            }
            output.WriteLine("download complete");
        }

        private static HuggingFaceDownloadSource ResolveDownloadSource(
            OnnxTaggerModelEntry entry, CliCommands.CliOptions options)
        {
            string requested = options.GetValue("download-source");
            if (!string.IsNullOrWhiteSpace(requested))
            {
                if (!Enum.TryParse(requested, ignoreCase: true, out HuggingFaceDownloadSource parsed))
                {
                    throw new CliCommands.CliUsageException($"Unknown --download-source '{requested}'. Valid: "
                        + string.Join(", ", Enum.GetNames<HuggingFaceDownloadSource>()));
                }
                return parsed;
            }
            return entry.Kind == OnnxTaggerModelKind.PixAi
                ? Program.Settings.PixAiTagger.DownloadSource
                : Program.Settings.Wd14Tagger.DownloadSource;
        }

        private static IProgress<(string file, long downloaded, long? total)> CreateDownloadProgress(TextWriter output)
        {
            var lastBucket = new Dictionary<string, int>(StringComparer.Ordinal);
            return new SimpleProgress<(string file, long downloaded, long? total)>(update =>
            {
                (string file, long downloaded, long? total) = update;
                // One line per 10% (or per 32 MB when the size is unknown), not
                // one per chunk — automation logs stay readable.
                int bucket = total is > 0
                    ? (int)(downloaded * 10 / total.Value)
                    : (int)(downloaded / (32L * 1024 * 1024));
                lock (lastBucket)
                {
                    if (lastBucket.TryGetValue(file, out int last) && last == bucket)
                        return;
                    lastBucket[file] = bucket;
                }
                output.WriteLine(total is > 0
                    ? $"downloading {file}: {downloaded * 100 / total.Value}%"
                    : $"downloading {file}: {downloaded / (1024 * 1024)} MB");
            });
        }

        private static NetworkResultSetMode ParseWriteMode(string raw)
        {
            switch ((raw ?? "skip").ToLowerInvariant())
            {
                case "skip": return NetworkResultSetMode.SkipExistTagList;
                case "append": return NetworkResultSetMode.OnlyNewWithAddition;
                case "replace": return NetworkResultSetMode.AllWithReplacement;
                default: throw new CliCommands.CliUsageException($"Unknown --write-mode '{raw}' (skip|append|replace).");
            }
        }

        private static string WriteModeName(NetworkResultSetMode mode)
        {
            return mode switch
            {
                NetworkResultSetMode.SkipExistTagList => "skip",
                NetworkResultSetMode.OnlyNewWithAddition => "append",
                _ => "replace"
            };
        }

        private static AutoTaggerSort? ParseSort(string raw)
        {
            if (raw == null)
                return null;
            switch (raw.ToLowerInvariant())
            {
                case "none": return AutoTaggerSort.None;
                case "confidence": return AutoTaggerSort.Confidence;
                case "alphabetical": return AutoTaggerSort.Alphabetical;
                default: throw new CliCommands.CliUsageException($"Unknown --sort '{raw}' (none|confidence|alphabetical).");
            }
        }

        // ---- audit ------------------------------------------------------

        private static async Task<int> RunAuditAsync(
            CliCommands.CliOptions options, TextWriter output, TextWriter error, CancellationToken token)
        {
            string trigger = options.RequireValue("trigger").Trim();
            if (trigger.Length == 0)
                throw new CliCommands.CliUsageException("--trigger must not be empty.");
            string reference = Path.GetFullPath(options.RequireValue("reference"));
            if (!File.Exists(reference))
                throw new FileNotFoundException("Reference image not found: " + reference);
            EnsureReferenceDecodes(reference);

            if (string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ConnectionAddress))
            {
                throw new CliCommands.CliUsageException(
                    "No LLM API is configured. Set the endpoint, key and audit model once in the GUI (LLM settings).");
            }
            Program.OpenAiAutoTagger ??= AiOpenAiClient.CreateFromSettings(Program.Settings);
            string model = options.GetValue("model");
            if (string.IsNullOrWhiteSpace(model))
                model = Program.Settings.CharacterTagAuditModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new CliCommands.CliUsageException(
                    "No audit model configured: pass --model or pick one once in the GUI (LLM settings).");
            }
            CharacterTagAuditStyle style = ParseStyle(options.GetValue("style"));
            int minimumCount = options.GetInt("min-count", Program.Settings.CharacterTagAuditMinimumCount);
            CharacterGender gender = ParseGender(options.GetValue("gender"));

            using DatasetManager dataset = LoadDataset(options.Folder, error);
            CharacterTagInventory inventory = CharacterTagInventory.Create(
                dataset.DataSet.Values.Select(item => item.Tags.TextTags.AsEnumerable()));
            if (!inventory.Tags.Any(item => string.Equals(item.Tag, trigger, StringComparison.Ordinal)))
            {
                string candidates = string.Join(", ", CharacterTagTriggerCandidates.Create(inventory)
                    .Take(5).Select(candidate => candidate.Tag));
                throw new CliCommands.CliUsageException(
                    $"Trigger '{trigger}' does not appear in the dataset's tags. Most frequent tags: {candidates}");
            }

            CharacterTagSkillBundle skills = CharacterTagSkillLoader.Load(AppContext.BaseDirectory);
            var auditOptions = new CharacterTagAuditOptions
            {
                Inventory = inventory,
                TriggerWord = trigger,
                Style = style,
                MinimumCount = minimumCount,
                Model = model,
                ReferenceImagePath = reference,
                CharacterAuditorSkill = skills.CharacterAuditor,
                PromptPyramidSkill = skills.PromptPyramid
            };

            // Same request bridge as the wizard: audit-service request → the
            // OpenAI-compatible client (multi-key rotation included).
            var service = new CharacterTagAuditService(async (request, requestToken) =>
            {
                var openAiRequest = new OpenAiRequest
                {
                    Model = request.Model,
                    SystemPrompt = request.SystemPrompt,
                    UserPrompt = request.UserPrompt
                };
                openAiRequest.ImagePath.AddRange(request.ImagePaths);
                OpenAiDetailedResponse response = await Program.OpenAiAutoTagger
                    .SendDetailedRequestAsync(openAiRequest, requestToken).ConfigureAwait(false);
                CharacterTagTokenUsage usage = response.TotalTokens.HasValue
                    ? new CharacterTagTokenUsage(response.InputTokens ?? 0, response.OutputTokens ?? 0, response.TotalTokens.Value)
                    : null;
                return new CharacterTagModelResponse(response.Result, response.ErrMessage, usage);
            });

            output.WriteLine($"audit: trigger='{trigger}', model={model}, style={style}, "
                + $"min-count={minimumCount}, tags={inventory.Tags.Count}, 2 model requests");
            var progress = new SimpleProgress<CharacterTagAuditProgress>(update =>
                output.WriteLine($"stage: {update.Stage} ({update.CompletedSteps}/{update.TotalSteps})"));
            CharacterTagAuditResult result = await service.ExecuteAsync(auditOptions, progress, token).ConfigureAwait(false);

            foreach (CharacterTagAuditItem item in result.Items)
            {
                if (item.ShouldDelete)
                    output.WriteLine($"delete: {item.Tag}");
                else if (item.ShouldReplace)
                    output.WriteLine($"replace: {item.Tag} -> {item.ReplacementTag}");
            }
            int deletions = result.Items.Count(item => item.ShouldDelete);
            int replacements = result.Items.Count(item => item.ShouldReplace);
            string finalPrompt = CharacterPromptSubjectNormalizer.NormalizeToSingle(result.FinalPrompt, gender);
            output.WriteLine($"decisions: keep {result.Items.Count - deletions - replacements}, "
                + $"delete {deletions}, replace {replacements} (excluded below min-count: {result.ExcludedItems.Count})");
            output.WriteLine("final-prompt: " + finalPrompt);

            // Apply with per-tag weights preserved (clone + rename, exactly the
            // wizard's transform), then commit through the same transactional
            // caption writer the GUI uses.
            string separator = Program.Settings.SeparatorOnSave.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            var changes = new List<CharacterTagFileChange>();
            foreach (DataItem item in dataset.DataSet.Values)
            {
                IReadOnlyList<EditableTag> transformed = TransformEditableTags(item.Tags, result.Items);
                if (item.Tags.TextTags.SequenceEqual(transformed.Select(tag => tag.Tag), StringComparer.Ordinal))
                    continue;
                changes.Add(new CharacterTagFileChange(
                    item.TextFilePath,
                    string.Join(separator, transformed.Select(tag => tag.ToString()))));
            }

            if (options.DryRun)
            {
                output.WriteLine($"would modify: {changes.Count} caption files (dry run, nothing written)");
            }
            else if (changes.Count == 0)
            {
                output.WriteLine("no caption files need changes");
            }
            else
            {
                await CharacterTagFileTransaction.CommitAsync(dataset.DatasetRoot, changes).ConfigureAwait(false);
                output.WriteLine($"modified: {changes.Count} caption files");
            }

            string reportPath = options.GetValue("report");
            if (reportPath != null)
            {
                var report = new
                {
                    folder = dataset.DatasetRoot,
                    trigger,
                    model,
                    style = style.ToString(),
                    minimumCount,
                    gender = gender.ToString(),
                    dryRun = options.DryRun,
                    finalPrompt = result.FinalPrompt,
                    normalizedFinalPrompt = finalPrompt,
                    decisions = result.Items,
                    excluded = result.ExcludedItems,
                    changedFiles = changes.Select(change => change.TargetPath).ToList(),
                    metrics = result.Metrics
                };
                SafeFile.WriteAllText(Path.GetFullPath(reportPath),
                    JsonConvert.SerializeObject(report, Formatting.Indented, new StringEnumConverter()));
                output.WriteLine("report: " + reportPath);
            }
            return CliCommands.ExitOk;
        }

        /// <summary>Same clone-and-rename transform as the wizard: weights and
        /// order survive, deletions drop the tag, replacements dedup.</summary>
        private static IReadOnlyList<EditableTag> TransformEditableTags(
            EditableTagList originalTags,
            IEnumerable<CharacterTagAuditItem> decisions)
        {
            var byTag = decisions.ToDictionary(item => item.Tag, StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<EditableTag>();
            foreach (EditableTag original in originalTags)
            {
                if (byTag.TryGetValue(original.Tag, out CharacterTagAuditItem decision) && decision.ShouldDelete)
                    continue;
                EditableTag transformed = (EditableTag)original.Clone();
                if (byTag.TryGetValue(original.Tag, out decision) && decision.ShouldReplace)
                    transformed.Tag = decision.ReplacementTag;
                if (emitted.Add(transformed.Tag))
                    result.Add(transformed);
            }
            return result;
        }

        private static void EnsureReferenceDecodes(string path)
        {
            // Fail before the paid model call, not after — same guarantee the
            // wizard gives for its reference gallery.
            using System.Drawing.Image decoded = ImageLoader.GetImageFromFile(path);
            if (decoded == null)
                throw new InvalidOperationException("Reference image could not be decoded: " + path);
        }

        private static CharacterTagAuditStyle ParseStyle(string raw)
        {
            if (raw == null)
                return Program.Settings.CharacterTagAuditStyle;
            if (!Enum.TryParse(raw, ignoreCase: true, out CharacterTagAuditStyle parsed))
                throw new CliCommands.CliUsageException($"Unknown --style '{raw}' (sparse|dense).");
            return parsed;
        }

        private static CharacterGender ParseGender(string raw)
        {
            switch ((raw ?? "girl").ToLowerInvariant())
            {
                case "girl": return CharacterGender.Girl;
                case "boy": return CharacterGender.Boy;
                default: throw new CliCommands.CliUsageException($"Unknown --gender '{raw}' (girl|boy).");
            }
        }

        // ---- shared -----------------------------------------------------

        private static DatasetManager LoadDataset(string folder, TextWriter error)
        {
            string root = Path.GetFullPath(folder);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("Dataset folder not found: " + root);
            var dataset = new DatasetManager();
            if (!dataset.LoadFromFolder(root, loadPreviewImages: false, readMetadata: false))
                throw new InvalidOperationException("No images found in " + root);
            foreach (string loadError in dataset.LastLoadErrors)
                error.WriteLine("Warning: " + loadError);
            return dataset;
        }
    }
}
