using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BooruDatasetTagManager
{
    public sealed class OppaiOracleModelDefinition
    {
        public string Id { get; init; }
        public string Repo { get; init; }
        public string ShortName { get; init; }
        public string ModelFile { get; init; }
        public string LabelsFile { get; init; }
        public int ImageSize { get; init; }
        public double DefaultThreshold { get; init; }
        public string RepoUrl => "https://huggingface.co/" + Repo;

        public IEnumerable<string> AllFiles()
        {
            yield return ModelFile;
            yield return LabelsFile;
        }
    }

    /// <summary>
    /// ONNX inference for Grio43/OppaiOracle. Preprocessing and the ORT
    /// graph-opt fallback match the official Space (letterbox + gray pad +
    /// padding_mask; ORT_ENABLE_ALL synthesizes a MemcpyToHost on the bool
    /// mask that some CPU builds reject).
    /// </summary>
    public sealed class OppaiOracleOnnxService : IDisposable
    {
        public const string ModelRepo = "Grio43/OppaiOracle";
        public const string V1Id = "oo:Grio43/OppaiOracle:v1";
        public const string V11Id = "oo:Grio43/OppaiOracle:v1.1";

        public static IReadOnlyList<OppaiOracleModelDefinition> Models { get; } = new[]
        {
            new OppaiOracleModelDefinition
            {
                Id = V1Id,
                Repo = ModelRepo,
                ShortName = "OppaiOracle V1 (320)",
                ModelFile = "V1_onnx/model.onnx",
                LabelsFile = "V1_onnx/selected_tags.csv",
                ImageSize = 320,
                DefaultThreshold = 0.55
            },
            new OppaiOracleModelDefinition
            {
                Id = V11Id,
                Repo = ModelRepo,
                ShortName = "OppaiOracle V1.1 (448)",
                ModelFile = "V1.1_onnx/model.onnx",
                LabelsFile = "V1.1_onnx/selected_tags.csv",
                ImageSize = 448,
                DefaultThreshold = 0.65
            }
        };

        private static readonly GraphOptimizationLevel[] SessionOptLevels =
        {
            GraphOptimizationLevel.ORT_ENABLE_BASIC,
            GraphOptimizationLevel.ORT_DISABLE_ALL
        };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private string pixelValuesName;
        private string paddingMaskName;
        private string outputName;
        private string loadedModelPath;
        private List<string> labels = new List<string>();
        private OppaiOracleModelDefinition loadedModel;

        public bool IsLoaded => session != null;

        public static OppaiOracleModelDefinition GetById(string id)
        {
            return Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Models[^1];
        }

        public bool IsModelReady(OppaiOracleModelDefinition model)
        {
            return model.AllFiles().All(file => downloader.IsFileCached(model.Repo, file));
        }

        public IReadOnlyList<string> GetRequiredFiles(OppaiOracleModelDefinition model)
        {
            return model.AllFiles().Where(file => !downloader.IsFileCached(model.Repo, file)).ToList();
        }

        public async Task DownloadModelAsync(
            OppaiOracleModelDefinition model,
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            foreach (string file in model.AllFiles())
            {
                if (downloader.IsFileCached(model.Repo, file))
                    continue;

                await downloader.DownloadFileAsync(source, model.Repo, file, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public void LoadModel(OppaiOracleModelDefinition model)
        {
            if (loadedModel != null
                && string.Equals(loadedModel.Id, model.Id, StringComparison.OrdinalIgnoreCase)
                && session != null)
            {
                return;
            }

            Unload();
            string modelPath = HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.ModelFile);
            string labelsPath = HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.LabelsFile);
            if (!File.Exists(modelPath) || !File.Exists(labelsPath))
                throw new FileNotFoundException(I18n.GetText("TaggerModelMissing"));

            try
            {
                OnnxModelIntegrity.RunWithTransientLockRetry(() =>
                {
                    labels = OppaiOracleSelectedTagsCsvLoader.Load(labelsPath);
                    loadedModelPath = modelPath;
                    try
                    {
                        session = CreateSession(modelPath);
                    }
                    catch (Exception ex) when (ex is not DllNotFoundException && usesDirectMlProvider)
                    {
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
                    ClearModelCache(model);
                    throw new ModelCorruptedException(I18n.GetText("TaggerModelCorruptCleared"), ex);
                }

                throw;
            }

            loadedModel = model;
        }

        public OnnxTagResult TagImageWithTiming(string imagePath, double threshold)
        {
            if (session == null || loadedModel == null)
                throw new InvalidOperationException("Model is not loaded.");

            var stopwatch = Stopwatch.StartNew();
            int inferenceSize = ResolveInputSize();
            using Image image = ImageLoader.GetImageForInference(imagePath, inferenceSize)
                ?? throw new InvalidOperationException(I18n.GetText("TaggerImageLoadFailed"));
            IReadOnlyList<AutoTagProviderItem> tags = TagImage(image, threshold);
            stopwatch.Stop();
            return new OnnxTagResult
            {
                Tags = tags,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double threshold)
        {
            if (session == null || loadedModel == null)
                throw new InvalidOperationException("Model is not loaded.");

            int targetSize = ResolveInputSize();
            OppaiOraclePreprocessResult prepared = OppaiOracleImagePreprocessor.Create(image, targetSize);
            float[] probabilities = RunPrediction(prepared.PixelValues, prepared.PaddingMask);
            return CollectTags(labels, probabilities, threshold, prepared.WasComposited);
        }

        internal static IReadOnlyList<AutoTagProviderItem> CollectTags(
            IReadOnlyList<string> names,
            float[] probabilities,
            double threshold,
            bool wasComposited)
        {
            var items = new List<AutoTagProviderItem>();
            int count = Math.Min(names.Count, probabilities.Length);
            for (int i = 0; i < count; i++)
            {
                string name = names[i];
                if (!ShouldEmitTag(name, wasComposited))
                    continue;
                if (probabilities[i] >= threshold)
                    items.Add(new AutoTagProviderItem { Tag = name, Confidence = probabilities[i] });
            }

            return TagWriteService.OrderByConfidenceDescending(items);
        }

        internal static bool ShouldEmitTag(string name, bool wasComposited)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (name.Equals("<PAD>", StringComparison.OrdinalIgnoreCase)
                || name.Equals("<UNK>", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (name.StartsWith("rating:", StringComparison.OrdinalIgnoreCase))
                return false;
            if (wasComposited && string.Equals(name, "gray_background", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        internal static (string PixelValues, string PaddingMask, string Output) ResolveIoNames(
            IEnumerable<string> inputNames,
            IEnumerable<string> outputNames)
        {
            List<string> inputs = inputNames.ToList();
            List<string> outputs = outputNames.ToList();
            string pixel = inputs.FirstOrDefault(name =>
                string.Equals(name, "pixel_values", StringComparison.OrdinalIgnoreCase))
                ?? inputs[0];
            string mask = inputs.FirstOrDefault(name =>
                string.Equals(name, "padding_mask", StringComparison.OrdinalIgnoreCase));
            string output = outputs.FirstOrDefault(name =>
                string.Equals(name, "probabilities", StringComparison.OrdinalIgnoreCase))
                ?? outputs[0];
            return (pixel, mask, output);
        }

        public void Unload()
        {
            session?.Dispose();
            session = null;
            loadedModel = null;
            loadedModelPath = null;
            pixelValuesName = null;
            paddingMaskName = null;
            outputName = null;
            labels.Clear();
        }

        public void ClearModelCache(OppaiOracleModelDefinition model)
        {
            foreach (string file in model.AllFiles())
                downloader.DeleteCachedFile(model.Repo, file);
        }

        public void Dispose()
        {
            Unload();
        }

        private int ResolveInputSize()
        {
            if (session != null
                && session.InputMetadata.TryGetValue(pixelValuesName ?? string.Empty, out NodeMetadata metadata))
            {
                int resolved = Wd14OnnxTaggerService.ResolveInputSize(metadata.Dimensions);
                if (resolved > 0)
                    return resolved;
            }

            return loadedModel?.ImageSize ?? 448;
        }

        private void ConfigureSessionMetadata(InferenceSession loadedSession)
        {
            (pixelValuesName, paddingMaskName, outputName) = ResolveIoNames(
                loadedSession.InputMetadata.Keys,
                loadedSession.OutputMetadata.Keys);
        }

        private float[] RunPrediction(DenseTensor<float> pixelValues, DenseTensor<bool> paddingMask)
        {
            try
            {
                return RunPredictionCore(session, pixelValues, paddingMask);
            }
            catch (OnnxRuntimeException ex) when (usesDirectMlProvider)
            {
                session.Dispose();
                session = CreateSession(loadedModelPath, forceCpu: true);
                ConfigureSessionMetadata(session);
                try
                {
                    return RunPredictionCore(session, pixelValues, paddingMask);
                }
                catch (Exception retryEx)
                {
                    throw new InvalidOperationException(ex.Message, retryEx);
                }
            }
        }

        private float[] RunPredictionCore(
            InferenceSession activeSession,
            DenseTensor<float> pixelValues,
            DenseTensor<bool> paddingMask)
        {
            var feeds = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(pixelValuesName, pixelValues)
            };
            if (!string.IsNullOrEmpty(paddingMaskName))
                feeds.Add(NamedOnnxValue.CreateFromTensor(paddingMaskName, paddingMask));

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = activeSession.Run(
                feeds,
                new[] { outputName });
            return Wd14OnnxTaggerService.ExtractFloatVector(results.First());
        }

        private InferenceSession CreateSession(string modelPath, bool forceCpu = false)
        {
            Exception lastError = null;
            foreach (GraphOptimizationLevel level in SessionOptLevels)
            {
                try
                {
                    InferenceSession created = CreateSessionCore(modelPath, forceCpu, level, out bool usesDirectMl);
                    usesDirectMlProvider = usesDirectMl;
                    return created;
                }
                catch (Exception ex) when (ex is not DllNotFoundException)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("Failed to create the OppaiOracle ONNX session.");
        }

        private static InferenceSession CreateSessionCore(
            string modelPath,
            bool forceCpu,
            GraphOptimizationLevel optimizationLevel,
            out bool usesDirectMl)
        {
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = optimizationLevel
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
    }

    internal static class OppaiOracleSelectedTagsCsvLoader
    {
        public static List<string> Load(string labelsPath)
        {
            return ParseLines(File.ReadAllLines(labelsPath));
        }

        internal static List<string> ParseLines(IEnumerable<string> lines)
        {
            var byIndex = new SortedDictionary<int, string>();
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 2)
                    continue;
                if (IsHeader(parts))
                    continue;
                if (!TryParseRow(parts, out int index, out string name))
                    continue;

                byIndex[index] = name;
            }

            if (byIndex.Count == 0)
                throw new InvalidDataException("The OppaiOracle tag CSV contains no tags.");

            var result = new List<string>(new string[byIndex.Keys.Max() + 1]);
            foreach (KeyValuePair<int, string> pair in byIndex)
                result[pair.Key] = pair.Value;
            return result;
        }

        private static bool IsHeader(string[] parts)
        {
            return parts[0].Equals("tag_id", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("name", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseRow(string[] parts, out int index, out string name)
        {
            index = 0;
            name = null;
            if (parts.Length < 2)
                return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return false;
            if (index < 0)
                return false;

            name = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = null;
            return true;
        }
    }

    internal readonly struct OppaiOraclePreprocessResult
    {
        public DenseTensor<float> PixelValues { get; init; }
        public DenseTensor<bool> PaddingMask { get; init; }
        public bool WasComposited { get; init; }
    }

    internal static class OppaiOracleImagePreprocessor
    {
        public static readonly Color PadColor = Color.FromArgb(114, 114, 114);

        public static OppaiOraclePreprocessResult Create(Image source, int targetSize)
        {
            bool wasComposited = HasAnyTransparency(source);
            (int left, int top, int contentWidth, int contentHeight) = LayoutLetterbox(
                source.Width, source.Height, targetSize);

            using var canvas = new Bitmap(targetSize, targetSize, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(PadColor);
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(source, left, top, contentWidth, contentHeight);
            }

            var pixelValues = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
            var mask = new DenseTensor<bool>(new[] { 1, targetSize, targetSize });
            BitmapData data = canvas.LockBits(
                new Rectangle(0, 0, targetSize, targetSize),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                byte[] buffer = new byte[Math.Abs(stride) * targetSize];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                int contentRight = left + contentWidth;
                int contentBottom = top + contentHeight;
                for (int y = 0; y < targetSize; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < targetSize; x++)
                    {
                        bool padded = x < left || x >= contentRight || y < top || y >= contentBottom;
                        mask[0, y, x] = padded;
                        int offset = row + (x * 3);
                        pixelValues[0, 0, y, x] = Normalize(buffer[offset + 2]);
                        pixelValues[0, 1, y, x] = Normalize(buffer[offset + 1]);
                        pixelValues[0, 2, y, x] = Normalize(buffer[offset + 0]);
                    }
                }
            }
            finally
            {
                canvas.UnlockBits(data);
            }

            return new OppaiOraclePreprocessResult
            {
                PixelValues = pixelValues,
                PaddingMask = mask,
                WasComposited = wasComposited
            };
        }

        internal static float Normalize(byte value)
        {
            return (value / 255f - 0.5f) / 0.5f;
        }

        internal static (int Left, int Top, int Width, int Height) LayoutLetterbox(
            int sourceWidth, int sourceHeight, int targetSize)
        {
            double scale = Math.Min(Math.Min(targetSize / (double)sourceWidth, targetSize / (double)sourceHeight), 1.0);
            int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            return ((targetSize - width) / 2, (targetSize - height) / 2, width, height);
        }

        internal static bool HasAnyTransparency(Image source)
        {
            if (source is not Bitmap bitmap || !Image.IsAlphaPixelFormat(bitmap.PixelFormat))
                return false;

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buffer = new byte[Math.Abs(stride) * bitmap.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (buffer[row + (x * 4) + 3] < 255)
                            return true;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return false;
        }
    }
}
