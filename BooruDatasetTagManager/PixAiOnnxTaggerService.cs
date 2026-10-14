using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;

namespace BooruDatasetTagManager
{
    public enum PixAiPreprocessKind
    {
        StretchV09,
        LetterboxRgbV10
    }

    public sealed class PixAiModelDefinition
    {
        public string Id { get; init; }
        public string Repo { get; init; }
        public string ShortName { get; init; }
        public PixAiPreprocessKind Preprocess { get; init; }
        public string ModelFile { get; init; } = "model.onnx";
        public string LabelsFile { get; init; }
        public string ExternalDataFile { get; init; }
        public string PreprocessFile { get; init; }
        public string ThresholdsFile { get; init; }
        public string CategoriesFile { get; init; }
        public int InputSize { get; init; } = 448;
        public double DefaultGeneralThreshold { get; init; }
        public double DefaultCharacterThreshold { get; init; }

        public IEnumerable<string> AllFiles()
        {
            yield return ModelFile;
            if (!string.IsNullOrEmpty(ExternalDataFile))
                yield return ExternalDataFile;
            yield return LabelsFile;
            if (!string.IsNullOrEmpty(PreprocessFile))
                yield return PreprocessFile;
            if (!string.IsNullOrEmpty(ThresholdsFile))
                yield return ThresholdsFile;
            if (!string.IsNullOrEmpty(CategoriesFile))
                yield return CategoriesFile;
        }
    }

    public sealed class PixAiOnnxTaggerService : IDisposable
    {
        public const string ModelRepo = "deepghs/pixai-tagger-v0.9-onnx";
        public const string V09Id = "pixai:v0.9";
        public const string V10Id = "pixai:v1.0";
        public const string V10Repo = "noaione/pixai-tagger-v1.0-onnx";

        public static IReadOnlyList<PixAiModelDefinition> Models { get; } = new[]
        {
            new PixAiModelDefinition
            {
                Id = V09Id,
                Repo = ModelRepo,
                ShortName = "pixai-tagger v0.9",
                Preprocess = PixAiPreprocessKind.StretchV09,
                LabelsFile = "selected_tags.csv",
                PreprocessFile = "preprocess.json",
                ThresholdsFile = "thresholds.csv",
                CategoriesFile = "categories.json",
                InputSize = 448,
                DefaultGeneralThreshold = 0.3,
                DefaultCharacterThreshold = 0.85
            },
            new PixAiModelDefinition
            {
                Id = V10Id,
                Repo = V10Repo,
                ShortName = "pixai-tagger v1.0",
                Preprocess = PixAiPreprocessKind.LetterboxRgbV10,
                LabelsFile = "tags.json",
                ExternalDataFile = "model.onnx.data",
                InputSize = 1008,
                DefaultGeneralThreshold = 0.17,
                DefaultCharacterThreshold = 0.27
            }
        };

        public static IReadOnlyList<string> RequiredFiles { get; } = Models[0].AllFiles().ToArray();

        private static readonly string[] PreferredOutputNames = { "prediction", "logits" };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private bool loadedForceCpu;
        private List<(string Name, int Category)> labels = new List<(string, int)>();
        private string inputName = "input";
        private string outputName = "prediction";
        private bool outputRequiresSigmoid;
        private string loadedModelPath;
        private PixAiModelDefinition loadedDefinition;
        private int inputWidth = 448;
        private int inputHeight = 448;
        private double defaultGeneralThreshold = 0.3;
        private double defaultCharacterThreshold = 0.85;

        public bool IsLoaded => session != null;

        public static PixAiModelDefinition GetById(string id)
        {
            return Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Models[0];
        }

        public bool IsModelReady(PixAiModelDefinition model)
        {
            if (model == null)
                return false;

            return model.AllFiles().All(file => downloader.IsFileCached(model.Repo, file));
        }

        public IReadOnlyList<string> GetMissingFiles(PixAiModelDefinition model)
        {
            if (model == null)
                return Array.Empty<string>();

            return model.AllFiles().Where(file => !downloader.IsFileCached(model.Repo, file)).ToList();
        }

        public async Task DownloadModelAsync(
            PixAiModelDefinition model,
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            foreach (string file in model.AllFiles())
            {
                if (downloader.IsFileCached(model.Repo, file))
                    continue;

                await downloader.DownloadFileAsync(source, model.Repo, file, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public void LoadModel(PixAiModelDefinition model, bool forceCpu = false)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (loadedDefinition != null
                && string.Equals(loadedDefinition.Id, model.Id, StringComparison.OrdinalIgnoreCase)
                && session != null
                && loadedForceCpu == forceCpu)
            {
                return;
            }

            Unload();
            foreach (string file in model.AllFiles())
            {
                if (!downloader.IsFileCached(model.Repo, file))
                    throw new FileNotFoundException(I18n.GetText("TaggerModelMissing"), file);
            }

            try
            {
                // Loading the csv/json metadata + session is the integrity check:
                // a corrupt file throws here. Purge only genuine parse failures.
                OnnxModelIntegrity.RunWithTransientLockRetry(() =>
                {
                    string labelsPath = HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.LabelsFile);
                    labels = model.Preprocess == PixAiPreprocessKind.LetterboxRgbV10
                        ? PixAiTagsJsonLoader.Load(labelsPath)
                        : PixAiSelectedTagsCsvLoader.Load(labelsPath);
                    if (!string.IsNullOrEmpty(model.ThresholdsFile))
                        LoadThresholds(HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.ThresholdsFile));
                    if (!string.IsNullOrEmpty(model.PreprocessFile))
                        LoadPreprocess(HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.PreprocessFile));
                    else
                    {
                        inputWidth = model.InputSize;
                        inputHeight = model.InputSize;
                    }

                    string modelPath = HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.ModelFile);
                    loadedModelPath = modelPath;
                    // The v1.0 SAM3 graph is about 2 GB. Full ORT optimization
                    // rewrites it on the CPU and then DirectML spends the run
                    // copying those nodes back. Keep the exported graph.
                    bool lightGraph = model.Preprocess == PixAiPreprocessKind.LetterboxRgbV10;
                    try
                    {
                        session = CreateSession(modelPath, forceCpu, lightGraph);
                    }
                    catch (Exception ex) when (ex is not DllNotFoundException && usesDirectMlProvider)
                    {
                        // A DirectML session can fail for device/driver reasons that
                        // say nothing about the file on disk: retry on CPU before
                        // the outer handler declares the model corrupt and purges
                        // the cached download.
                        session = CreateSession(modelPath, forceCpu: true, lightGraph: lightGraph);
                    }
                    ConfigureSessionMetadata(session);
                    loadedDefinition = model;
                    loadedForceCpu = !usesDirectMlProvider;
                });
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                Unload();
                if (OnnxModelIntegrity.ShouldClearCachedModel(ex))
                {
                    ClearModelCache(model);
                    throw new ModelCorruptedException(I18n.GetText("TaggerModelCorruptCleared"), ex);
                }

                throw;
            }
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
            int inferenceSize = Math.Max(inputWidth, inputHeight);
            using Image image = ImageLoader.GetImageForInference(imagePath, inferenceSize)
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

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double generalThreshold, double characterThreshold)
        {
            return TagImage(image, TaggerProFilter.Basic(generalThreshold, characterThreshold));
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, TaggerProFilter filter)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            DenseTensor<float> input = loadedDefinition?.Preprocess == PixAiPreprocessKind.LetterboxRgbV10
                ? PixAiV1OnnxImagePreprocessor.CreateInputTensor(image, inputWidth)
                : PixAiOnnxImagePreprocessor.CreateInputTensor(image, inputWidth, inputHeight);
            float[] output = RunPrediction(session, input);
            return BuildTagItems(output, filter ?? TaggerProFilter.Basic(0.3, 0.85));
        }

        public IReadOnlyList<OnnxTagResult> TagImageBatch(IReadOnlyList<string> paths, TaggerProFilter filter, int batchSize)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            filter ??= TaggerProFilter.Basic(0.3, 0.85);
            int inferenceSize = Math.Max(inputWidth, inputHeight);
            return OnnxInferenceBatch.Execute(
                paths,
                batchSize,
                session.InputMetadata[inputName].Dimensions,
                path => EncodePath(path, inferenceSize),
                tensor => RunPrediction(session, tensor),
                row => BuildTagItems(row, filter));
        }

        private (DenseTensor<float> Tensor, string Error) EncodePath(string path, int inferenceSize)
        {
            try
            {
                using Image image = ImageLoader.GetImageForInference(path, inferenceSize)
                    ?? throw new InvalidOperationException(I18n.GetText("TaggerImageLoadFailed"));
                DenseTensor<float> tensor = loadedDefinition?.Preprocess == PixAiPreprocessKind.LetterboxRgbV10
                    ? PixAiV1OnnxImagePreprocessor.CreateInputTensor(image, inputWidth)
                    : PixAiOnnxImagePreprocessor.CreateInputTensor(image, inputWidth, inputHeight);
                return (tensor, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public void Unload()
        {
            session?.Dispose();
            session = null;
            loadedDefinition = null;
            loadedModelPath = null;
            loadedForceCpu = false;
            labels.Clear();
        }

        public void ClearModelCache(PixAiModelDefinition model)
        {
            if (model == null)
                return;

            foreach (string file in model.AllFiles())
                downloader.DeleteCachedFile(model.Repo, file);
        }

        public void Dispose()
        {
            Unload();
        }

        internal static (string InputName, string OutputName, bool RequiresSigmoid) ResolveSessionMetadata(InferenceSession session)
        {
            return ResolveSessionMetadata(session.InputMetadata.Keys, session.OutputMetadata.Keys);
        }

        internal static (string InputName, string OutputName, bool RequiresSigmoid) ResolveSessionMetadata(
            IEnumerable<string> inputNames,
            IEnumerable<string> outputNames)
        {
            string resolvedInput = inputNames.FirstOrDefault(name =>
                string.Equals(name, "input", StringComparison.OrdinalIgnoreCase))
                ?? inputNames.First();

            foreach (string preferred in PreferredOutputNames)
            {
                // Return the model's real key, not the preferred constant: ORT
                // name lookup is case-sensitive.
                string match = outputNames.FirstOrDefault(name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    continue;

                return (resolvedInput, match, string.Equals(preferred, "logits", StringComparison.OrdinalIgnoreCase));
            }

            string available = string.Join(", ", outputNames);
            throw new InvalidOperationException($"PixAI ONNX model is missing a supported output tensor. Available outputs: {available}");
        }

        internal static float[] ExtractFloatVector(NamedOnnxValue result)
        {
            if (result.Value is DenseTensor<float> denseTensor)
                return denseTensor.ToArray();

            if (result.Value is Tensor<float> tensor)
                return tensor.ToArray();

            return result.AsEnumerable<float>().ToArray();
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
            (inputName, outputName, outputRequiresSigmoid) = ResolveSessionMetadata(loadedSession);
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
                session = CreateSession(
                    loadedModelPath,
                    forceCpu: true,
                    lightGraph: loadedDefinition?.Preprocess == PixAiPreprocessKind.LetterboxRgbV10);
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
            // v0.9 "prediction" is already a probability. v1.0 is a logits
            // export (output name "logits"); sigmoid once either way.
            if (outputRequiresSigmoid
                || loadedDefinition?.Preprocess == PixAiPreprocessKind.LetterboxRgbV10)
            {
                output = ApplySigmoid(output);
            }
            return output;
        }

        private List<AutoTagProviderItem> BuildTagItems(float[] output, TaggerProFilter filter)
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

        private static InferenceSession CreateSession(string modelPath, bool forceCpu, bool lightGraph, out bool usesDirectMl)
        {
            // Full optimization of the v1.0 graph is a CPU rewrite of a 2 GB
            // SAM3 model. DirectML then copies those rewritten nodes back to
            // the CPU on every image. BASIC leaves the exported graph on the GPU.
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = lightGraph
                    ? GraphOptimizationLevel.ORT_ENABLE_BASIC
                    : GraphOptimizationLevel.ORT_ENABLE_ALL,
                EnableMemoryPattern = !lightGraph
            };
            if (lightGraph)
                options.EnableCpuMemArena = false;

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

        private InferenceSession CreateSession(string modelPath, bool forceCpu = false, bool lightGraph = false)
        {
            return CreateSession(modelPath, forceCpu, lightGraph, out usesDirectMlProvider);
        }

        private void LoadPreprocess(string preprocessPath)
        {
            JObject json = JObject.Parse(File.ReadAllText(preprocessPath));
            JArray stages = json["stages"] as JArray;
            if (stages == null)
                return;

            foreach (JToken stage in stages)
            {
                if (!string.Equals(stage["type"]?.ToString(), "resize", StringComparison.OrdinalIgnoreCase))
                    continue;

                JArray size = stage["size"] as JArray;
                if (size == null || size.Count < 2)
                    continue;

                inputWidth = size[0].Value<int>();
                inputHeight = size[1].Value<int>();
                break;
            }
        }

        private void LoadThresholds(string thresholdsPath)
        {
            foreach (string line in File.ReadAllLines(thresholdsPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("category", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 3)
                    continue;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int category))
                    continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold))
                    continue;

                if (category == 0)
                    defaultGeneralThreshold = threshold;
                else if (category == 4)
                    defaultCharacterThreshold = threshold;
            }
        }
    }

    internal static class PixAiSelectedTagsCsvLoader
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
                if (parts.Length < 2 || IsHeader(parts))
                    continue;

                if (TryParsePixAiRow(parts, out string name, out int category))
                {
                    result.Add((name, category));
                    continue;
                }

                if (TryParseLegacyRow(parts, out name, out category))
                    result.Add((name, category));
            }

            return result;
        }

        private static bool IsHeader(string[] parts)
        {
            return parts[0].Equals("id", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("name", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParsePixAiRow(string[] parts, out string name, out int category)
        {
            name = string.Empty;
            category = 0;
            if (parts.Length < 4)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return false;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out category))
                return false;

            name = parts[2];
            return !string.IsNullOrWhiteSpace(name);
        }

        private static bool TryParseLegacyRow(string[] parts, out string name, out int category)
        {
            name = parts[0];
            return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out category)
                && !string.IsNullOrWhiteSpace(name);
        }
    }

    internal static class PixAiOnnxImagePreprocessor
    {
        public static DenseTensor<float> CreateInputTensor(Image source, int width, int height)
        {
            // Resize() clears the canvas white first, so alpha composites
            // correctly without a separate full-size RGB pass.
            using Bitmap resized = Resize(source, width, height);
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            BitmapData data = resized.LockBits(
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
                        tensor[0, 0, y, x] = (buffer[offset + 0] / 255f - 0.5f) / 0.5f;
                        tensor[0, 1, y, x] = (buffer[offset + 1] / 255f - 0.5f) / 0.5f;
                        tensor[0, 2, y, x] = (buffer[offset + 2] / 255f - 0.5f) / 0.5f;
                    }
                }
            }
            finally
            {
                resized.UnlockBits(data);
            }

            return tensor;
        }

        private static Bitmap Resize(Image source, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            return bitmap;
        }
    }

    internal static class PixAiTagsJsonLoader
    {
        public static List<(string Name, int Category)> Load(string labelsPath)
        {
            return Parse(File.ReadAllText(labelsPath));
        }

        internal static List<(string Name, int Category)> Parse(string json)
        {
            JObject root = JObject.Parse(json);
            JArray categories = root["categories"] as JArray
                ?? throw new InvalidOperationException("PixAI tags.json is missing categories.");

            int total = root["num_classes"]?.Value<int>() ?? 0;
            if (total <= 0)
            {
                foreach (JToken category in categories)
                {
                    int end = category["offset"].Value<int>() + category["count"].Value<int>();
                    if (end > total)
                        total = end;
                }
            }

            var slots = new (string Name, int Category)[total];
            var filled = new bool[total];
            foreach (JToken category in categories)
            {
                string name = category["name"]?.ToString() ?? string.Empty;
                int offset = category["offset"].Value<int>();
                int count = category["count"].Value<int>();
                int mapped = MapCategory(name);
                JArray tags = category["tags"] as JArray;
                if (tags == null || tags.Count != count)
                    throw new InvalidOperationException($"PixAI tags.json category '{name}' has an invalid count.");

                for (int i = 0; i < count; i++)
                {
                    int index = offset + i;
                    if ((uint)index >= (uint)total)
                        throw new InvalidOperationException($"PixAI tags.json index {index} is outside num_classes.");

                    slots[index] = (tags[i].ToString(), mapped);
                    filled[index] = true;
                }
            }

            var result = new List<(string, int)>(total);
            for (int i = 0; i < total; i++)
            {
                if (!filled[i])
                    throw new InvalidOperationException($"PixAI tags.json is missing tag index {i}.");

                result.Add(slots[i]);
            }

            return result;
        }

        internal static int MapCategory(string name)
        {
            return name switch
            {
                "general" => TaggerCategoryFilter.General,
                "character" => TaggerCategoryFilter.Character,
                "copyright" => TaggerCategoryFilter.Copyright,
                "style" => TaggerCategoryFilter.Style,
                "meta" => TaggerCategoryFilter.Meta,
                "rating" => TaggerCategoryFilter.Rating,
                _ => -1
            };
        }
    }

    internal static class PixAiV1OnnxImagePreprocessor
    {
        public static DenseTensor<float> CreateInputTensor(Image source, int size)
        {
            using Bitmap prepared = Letterbox(source, size);
            var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
            BitmapData data = prepared.LockBits(
                new Rectangle(0, 0, size, size),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * size;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int y = 0; y < size; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < size; x++)
                    {
                        int offset = row + (x * 3);
                        // GDI memory is B,G,R. v1.0 wants RGB, then (x/255 - 0.5) / 0.5.
                        float red = buffer[offset + 2] / 255f;
                        float green = buffer[offset + 1] / 255f;
                        float blue = buffer[offset + 0] / 255f;
                        tensor[0, 0, y, x] = (red - 0.5f) / 0.5f;
                        tensor[0, 1, y, x] = (green - 0.5f) / 0.5f;
                        tensor[0, 2, y, x] = (blue - 0.5f) / 0.5f;
                    }
                }
            }
            finally
            {
                prepared.UnlockBits(data);
            }

            return tensor;
        }

        private static Bitmap Letterbox(Image source, int size)
        {
            // Match run_onnx.py: scale with truncation (small images scale up),
            // flatten alpha onto white, then center on a black square.
            double scale = Math.Min(size / (double)source.Height, size / (double)source.Width);
            int scaledWidth = Math.Max(1, (int)(source.Width * scale));
            int scaledHeight = Math.Max(1, (int)(source.Height * scale));
            scaledWidth = Math.Min(size, scaledWidth);
            scaledHeight = Math.Min(size, scaledHeight);

            using var fitted = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(fitted))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, 0, 0, scaledWidth, scaledHeight);
            }

            var canvas = new Bitmap(size, size, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                graphics.DrawImage(fitted, (size - scaledWidth) / 2, (size - scaledHeight) / 2, scaledWidth, scaledHeight);
            }

            return canvas;
        }
    }
}
