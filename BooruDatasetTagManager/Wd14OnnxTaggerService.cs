using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BooruDatasetTagManager
{
    public sealed class Wd14ModelDefinition
    {
        public string Repo { get; init; }
        public double DefaultThreshold { get; init; }
        public double DefaultCharacterThreshold { get; init; } = 0.85;
        public string ShortName { get; init; }
    }

    public sealed class Wd14OnnxTaggerService : IDisposable
    {
        public const string ModelFileName = "model.onnx";
        public const string LabelsFileName = "selected_tags.csv";
        public const string DefaultRepo = "SmilingWolf/wd-eva02-large-tagger-v3";
        public const string CanaryRepo = "Misaka41Z/wd-eva02-tagger-2026-canary-onnx-v2";

        public static IReadOnlyList<Wd14ModelDefinition> Models { get; } = new[]
        {
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-convnext-tagger", DefaultThreshold = 0.35, ShortName = "convnext v1" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-convnext-tagger-v2", DefaultThreshold = 0.35, ShortName = "convnext v2" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-convnextv2-tagger-v2", DefaultThreshold = 0.35, ShortName = "convnextv2 v2" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-swinv2-tagger-v2", DefaultThreshold = 0.35, ShortName = "swinv2 v2" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-vit-tagger", DefaultThreshold = 0.35, ShortName = "vit v1" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-vit-tagger-v2", DefaultThreshold = 0.35, ShortName = "vit v2" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-v1-4-moat-tagger-v2", DefaultThreshold = 0.35, ShortName = "moat v2" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-vit-tagger-v3", DefaultThreshold = 0.25, ShortName = "vit v3" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-swinv2-tagger-v3", DefaultThreshold = 0.25, ShortName = "swinv2 v3" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-convnext-tagger-v3", DefaultThreshold = 0.25, ShortName = "convnext v3" },
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-vit-large-tagger-v3", DefaultThreshold = 0.26, ShortName = "vit-large v3" },
            new Wd14ModelDefinition { Repo = DefaultRepo, DefaultThreshold = 0.52, ShortName = "eva02-large v3" },
            // Same eva02_large_patch14_448 head as v3, vocabulary extended to 2026-05.
            // The published P=R operating point is 0.6094 for every category.
            new Wd14ModelDefinition { Repo = CanaryRepo, DefaultThreshold = 0.61, DefaultCharacterThreshold = 0.61, ShortName = "eva02-large 2026 canary" },
        };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private bool loadedForceCpu;
        private string inputName;
        private string outputName;
        private bool outputRequiresSigmoid;
        private string loadedModelPath;
        private List<(string Name, int Category)> labels = new List<(string, int)>();
        private string loadedRepo;

        private static readonly string[] PreferredOutputNames = { "output", "predictions", "probs", "logits" };

        public string LoadedRepo => loadedRepo;
        public bool IsLoaded => session != null;

        public static Wd14ModelDefinition GetModel(string repo)
        {
            return Models.FirstOrDefault(model => string.Equals(model.Repo, repo, StringComparison.OrdinalIgnoreCase))
                ?? Models.First(model => string.Equals(model.Repo, DefaultRepo, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsModelReady(string repo)
        {
            return downloader.IsFileCached(repo, ModelFileName)
                && downloader.IsFileCached(repo, LabelsFileName);
        }

        public IReadOnlyList<string> GetRequiredFiles(string repo)
        {
            var missing = new List<string>();
            if (!downloader.IsFileCached(repo, ModelFileName))
                missing.Add(ModelFileName);
            if (!downloader.IsFileCached(repo, LabelsFileName))
                missing.Add(LabelsFileName);
            return missing;
        }

        public async Task DownloadModelAsync(
            string repo,
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            foreach (string file in new[] { ModelFileName, LabelsFileName })
            {
                if (downloader.IsFileCached(repo, file))
                    continue;

                await downloader.DownloadFileAsync(source, repo, file, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public void LoadModel(string repo, bool forceCpu = false)
        {
            if (string.Equals(loadedRepo, repo, StringComparison.OrdinalIgnoreCase)
                && session != null
                && loadedForceCpu == forceCpu)
            {
                return;
            }

            Unload();
            string modelPath = HuggingFaceModelDownloader.GetLocalPath(repo, ModelFileName);
            string labelsPath = HuggingFaceModelDownloader.GetLocalPath(repo, LabelsFileName);
            if (!File.Exists(modelPath) || !File.Exists(labelsPath))
                throw new FileNotFoundException(I18n.GetText("TaggerModelMissing"));

            try
            {
                // Loading labels + session is the integrity check: a corrupt CSV
                // or truncated ONNX throws here. Purge only genuine parse
                // failures — a just-downloaded file locked by Defender is not
                // corrupt, and neither is a missing native runtime.
                OnnxModelIntegrity.RunWithTransientLockRetry(() =>
                {
                    labels = LoadLabels(labelsPath);
                    loadedModelPath = modelPath;
                    try
                    {
                        session = CreateSession(modelPath, forceCpu);
                    }
                    catch (Exception ex) when (ex is not DllNotFoundException && usesDirectMlProvider)
                    {
                        // A DirectML session can fail for device/driver reasons that
                        // say nothing about the file on disk: retry on CPU before
                        // the outer handler declares the model corrupt and purges
                        // the cached download.
                        session = CreateSession(modelPath, forceCpu: true);
                    }
                    ConfigureSessionMetadata(session);
                });
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                Unload();
                if (OnnxModelIntegrity.ShouldClearCachedModel(ex))
                {
                    ClearModelCache(repo);
                    throw new ModelCorruptedException(I18n.GetText("TaggerModelCorruptCleared"), ex);
                }

                throw;
            }
            loadedRepo = repo;
            loadedForceCpu = !usesDirectMlProvider;
        }

        public OnnxTagResult TagImageWithTiming(string imagePath, double generalThreshold, double characterThreshold)
        {
            return TagImageWithTiming(imagePath, TaggerProFilter.Basic(generalThreshold, characterThreshold));
        }

        public OnnxTagResult TagImageWithTiming(string imagePath, TaggerProFilter filter)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            var stopwatch = Stopwatch.StartNew();
            // Shrink in ImageSharp first so a 4K source never becomes a
            // full-resolution GDI bitmap before the 448-ish letterbox.
            using Image image = ImageLoader.GetImageForInference(imagePath, GetInputSize(session))
                ?? throw new InvalidOperationException(I18n.GetText("TaggerImageLoadFailed"));
            IReadOnlyList<AutoTagProviderItem> tags = TagImage(image, filter);
            stopwatch.Stop();
            return new OnnxTagResult
            {
                Tags = tags,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(string imagePath, double generalThreshold, double characterThreshold)
        {
            return TagImageWithTiming(imagePath, generalThreshold, characterThreshold).Tags;
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(string imagePath, TaggerProFilter filter)
        {
            return TagImageWithTiming(imagePath, filter).Tags;
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(string imagePath, double threshold)
        {
            return TagImage(imagePath, threshold, 0.85);
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double generalThreshold, double characterThreshold)
        {
            return TagImage(image, TaggerProFilter.Basic(generalThreshold, characterThreshold));
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, TaggerProFilter filter)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            filter ??= TaggerProFilter.Basic(0.35, 0.85);
            IReadOnlyList<int> dims = session.InputMetadata[inputName].Dimensions;
            int targetSize = ResolveInputSize(dims);
            // SmilingWolf ONNX is NHWC. A torch export of the same architecture
            // (the 2026 canary) may be NCHW; the pixel values stay BGR 0-255.
            DenseTensor<float> input = IsNchwLayout(dims)
                ? Wd14OnnxImagePreprocessor.CreateInputTensorNchw(image, targetSize)
                : Wd14OnnxImagePreprocessor.CreateInputTensor(image, targetSize);
            float[] output = RunPrediction(session, input);
            return DecodeTags(output, filter);
        }

        public IReadOnlyList<OnnxTagResult> TagImageBatch(IReadOnlyList<string> paths, TaggerProFilter filter, int batchSize)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            filter ??= TaggerProFilter.Basic(0.35, 0.85);
            return OnnxInferenceBatch.Execute(
                paths,
                batchSize,
                session.InputMetadata[inputName].Dimensions,
                EncodePath,
                tensor => RunPrediction(session, tensor),
                row => DecodeTags(row, filter));
        }

        private (DenseTensor<float> Tensor, string Error) EncodePath(string path)
        {
            try
            {
                IReadOnlyList<int> dims = session.InputMetadata[inputName].Dimensions;
                int targetSize = ResolveInputSize(dims);
                using Image image = ImageLoader.GetImageForInference(path, targetSize)
                    ?? throw new InvalidOperationException(I18n.GetText("TaggerImageLoadFailed"));
                DenseTensor<float> tensor = IsNchwLayout(dims)
                    ? Wd14OnnxImagePreprocessor.CreateInputTensorNchw(image, targetSize)
                    : Wd14OnnxImagePreprocessor.CreateInputTensor(image, targetSize);
                return (tensor, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        private IReadOnlyList<AutoTagProviderItem> DecodeTags(float[] output, TaggerProFilter filter)
        {
            var items = new List<AutoTagProviderItem>();
            int count = Math.Min(labels.Count, output.Length);
            for (int i = 0; i < count; i++)
            {
                (string name, int category) = labels[i];
                if (TaggerCategoryFilter.TryAccept(name, category, output[i], filter, out AutoTagProviderItem item))
                    items.Add(item);
            }

            List<AutoTagProviderItem> ordered = TagWriteService.OrderByConfidenceDescending(items);
            return filter.CharacterFirst ? TagWriteService.PlaceCharactersFirst(ordered) : ordered;
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double threshold)
        {
            return TagImage(image, threshold, 0.85);
        }

        public void Unload()
        {
            session?.Dispose();
            session = null;
            loadedRepo = null;
            loadedModelPath = null;
            loadedForceCpu = false;
            labels.Clear();
        }

        public void ClearModelCache(string repo)
        {
            downloader.DeleteCachedFile(repo, ModelFileName);
            downloader.DeleteCachedFile(repo, LabelsFileName);
        }

        public void Dispose()
        {
            Unload();
        }

        internal static (string InputName, string OutputName) ResolveSessionMetadata(InferenceSession session)
        {
            return ResolveSessionMetadata(session.InputMetadata.Keys, session.OutputMetadata.Keys);
        }

        internal static (string InputName, string OutputName) ResolveSessionMetadata(
            IEnumerable<string> inputNames,
            IEnumerable<string> outputNames)
        {
            string resolvedInput = inputNames.FirstOrDefault(name =>
                string.Equals(name, "input", StringComparison.OrdinalIgnoreCase))
                ?? inputNames.First();

            foreach (string preferred in PreferredOutputNames)
            {
                // Return the model's real key, not the preferred constant: ORT
                // name lookup is case-sensitive, so answering "predictions" for
                // a model that declares "Predictions" would fail at Run().
                string match = outputNames.FirstOrDefault(name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return (resolvedInput, match);
            }

            string resolvedOutput = outputNames.First();
            return (resolvedInput, resolvedOutput);
        }

        internal static float[] ExtractFloatVector(NamedOnnxValue result)
        {
            if (result.Value is DenseTensor<float> denseTensor)
                return denseTensor.ToArray();

            if (result.Value is Tensor<float> tensor)
                return tensor.ToArray();

            return result.AsEnumerable<float>().ToArray();
        }

        internal static int ResolveInputSize(IReadOnlyList<int> dims)
        {
            if (dims == null || dims.Count == 0)
                return 448;

            // NHWC: [batch, height, width, channels]
            if (dims.Count >= 4 && dims[1] > 0 && dims[3] == 3)
                return dims[1];
            if (dims.Count >= 4 && dims[1] > 0 && dims[1] == dims[2])
                return dims[1];

            // NCHW: [batch, channels, height, width]
            if (dims.Count >= 4 && dims[1] == 3 && dims[2] > 0)
                return dims[2];

            if (dims.Count >= 3 && dims[0] > 0 && dims[0] != 3)
                return dims[0];

            return 448;
        }

        internal static bool IsNchwLayout(IReadOnlyList<int> dims)
        {
            if (dims == null || dims.Count < 4)
                return false;

            // NHWC stores channels last. Anything else with channels in dim 1 is NCHW.
            if (dims[3] == 3)
                return false;

            return dims[1] == 3 && dims[2] > 0;
        }

        internal static bool ValuesLookLikeLogits(IReadOnlyList<float> values)
        {
            if (values == null)
                return false;

            for (int i = 0; i < values.Count; i++)
            {
                float value = values[i];
                if (value < 0f || value > 1f)
                    return true;
            }

            return false;
        }

        internal static float[] ApplySigmoid(IReadOnlyList<float> values)
        {
            var output = new float[values.Count];
            for (int i = 0; i < values.Count; i++)
                output[i] = 1f / (1f + MathF.Exp(-values[i]));
            return output;
        }

        private void ConfigureSessionMetadata(InferenceSession loadedSession)
        {
            (inputName, outputName) = ResolveSessionMetadata(loadedSession);
            outputRequiresSigmoid = string.Equals(outputName, "logits", StringComparison.OrdinalIgnoreCase);
        }

        private float[] RunPrediction(InferenceSession activeSession, DenseTensor<float> input)
        {
            try
            {
                return RunPredictionCore(activeSession, input);
            }
            catch (OnnxRuntimeException ex) when (usesDirectMlProvider)
            {
                activeSession.Dispose();
                session = CreateSession(loadedModelPath, forceCpu: true);
                loadedForceCpu = true;
                ConfigureSessionMetadata(session);
                try
                {
                    return RunPredictionCore(session, input);
                }
                catch (Exception retryEx)
                {
                    throw new InvalidOperationException(ex.Message, retryEx);
                }
            }
        }

        private float[] RunPredictionCore(InferenceSession activeSession, DenseTensor<float> input)
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = activeSession.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, input) },
                new[] { outputName });
            float[] output = ExtractFloatVector(results.First());
            // SmilingWolf graphs already end in a sigmoid. A raw torch export
            // (output name "logits", or unbounded scores) still needs one.
            if (outputRequiresSigmoid || ValuesLookLikeLogits(output))
                output = ApplySigmoid(output);
            return output;
        }

        private InferenceSession CreateSession(string modelPath, bool forceCpu = false)
        {
            return CreateSession(modelPath, forceCpu, out usesDirectMlProvider);
        }

        private static InferenceSession CreateSession(string modelPath, bool forceCpu, out bool usesDirectMl)
        {
            // Disposed once the session is constructed: ORT copies what it
            // needs, and each leaked SessionOptions held a native handle.
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            usesDirectMl = false;
            if (forceCpu)
            {
                options.AppendExecutionProvider_CPU();
            }
            else
            {
                try
                {
                    options.AppendExecutionProvider_DML(0);
                    usesDirectMl = true;
                }
                catch
                {
                    options.AppendExecutionProvider_CPU();
                }
            }

            return new InferenceSession(HuggingFaceModelDownloader.NormalizePathForOnnx(modelPath), options);
        }

        private static int GetInputSize(InferenceSession session)
        {
            return ResolveInputSize(session.InputMetadata.Values.First().Dimensions);
        }

        private static List<(string Name, int Category)> LoadLabels(string labelsPath)
        {
            return SelectedTagsCsvLoader.Load(labelsPath);
        }
    }

    internal static class SelectedTagsCsvLoader
    {
        public static List<(string Name, int Category)> Load(string labelsPath)
        {
            return ParseLines(File.ReadAllLines(labelsPath));
        }

        internal static List<(string Name, int Category)> ParseLines(IEnumerable<string> lines)
        {
            var result = new List<(string, int)>();
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                if (IsHeader(parts))
                    continue;

                if (TryParseRow(parts, out string name, out int category))
                    result.Add((name, category));
            }

            return result;
        }

        private static bool IsHeader(string[] parts)
        {
            return parts[0].Equals("tag_id", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("name", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseRow(string[] parts, out string name, out int category)
        {
            name = string.Empty;
            category = 0;

            // v3: tag_id,name,category,count
            if (parts.Length >= 4
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out category))
            {
                name = parts[1];
                return !string.IsNullOrWhiteSpace(name);
            }

            // v1/v2: name,category[,count]
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out category))
            {
                name = parts[0];
                return !string.IsNullOrWhiteSpace(name);
            }

            return false;
        }
    }

    internal static class Wd14OnnxImagePreprocessor
    {
        public static DenseTensor<float> CreateInputTensor(Image source, int targetSize)
        {
            using Bitmap prepared = PrepareBitmap(source, targetSize);
            int width = prepared.Width;
            int height = prepared.Height;
            var tensor = new DenseTensor<float>(new[] { 1, height, width, 3 });
            BitmapData data = prepared.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = row + (x * 3);
                        // GDI Format24bppRgb memory is already B,G,R per pixel.
                        // WD14 ONNX expects BGR NHWC — write bytes as-is (do not
                        // swap again; that used to feed RGB and scramble hues).
                        tensor[0, y, x, 0] = buffer[offset + 0]; // B
                        tensor[0, y, x, 1] = buffer[offset + 1]; // G
                        tensor[0, y, x, 2] = buffer[offset + 2]; // R
                    }
                }
            }
            finally
            {
                prepared.UnlockBits(data);
            }

            return tensor;
        }

        public static DenseTensor<float> CreateInputTensorNchw(Image source, int targetSize)
        {
            using Bitmap prepared = PrepareBitmap(source, targetSize);
            int width = prepared.Width;
            int height = prepared.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            BitmapData data = prepared.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = row + (x * 3);
                        // Same BGR 0-255 values as the NHWC tensor, channels first.
                        tensor[0, 0, y, x] = buffer[offset + 0];
                        tensor[0, 1, y, x] = buffer[offset + 1];
                        tensor[0, 2, y, x] = buffer[offset + 2];
                    }
                }
            }
            finally
            {
                prepared.UnlockBits(data);
            }

            return tensor;
        }

        private static Bitmap PrepareBitmap(Image source, int targetSize)
        {
            // One white square canvas: composite alpha, scale (never upscale)
            // and center in a single draw. The old pad-to-longest-side-then-
            // resize path materialized a maxDim×maxDim intermediate (a
            // 10000px-wide image cost ~300MB) and leaked its RGBA temporary.
            double scale = Math.Min(1.0, targetSize / (double)Math.Max(source.Width, source.Height));
            int scaledWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

            var square = new Bitmap(targetSize, targetSize, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(square))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, (targetSize - scaledWidth) / 2, (targetSize - scaledHeight) / 2, scaledWidth, scaledHeight);
            }

            return square;
        }
    }
}
