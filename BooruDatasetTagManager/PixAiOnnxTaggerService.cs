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
    public sealed class PixAiOnnxTaggerService : IDisposable
    {
        public const string ModelRepo = "deepghs/pixai-tagger-v0.9-onnx";
        public static readonly IReadOnlyList<string> RequiredFiles = new[]
        {
            "model.onnx",
            "selected_tags.csv",
            "categories.json",
            "preprocess.json",
            "thresholds.csv"
        };

        private static readonly string[] PreferredOutputNames = { "prediction", "logits" };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private List<(string Name, int Category)> labels = new List<(string, int)>();
        private string inputName = "input";
        private string outputName = "prediction";
        private bool outputRequiresSigmoid;
        private int inputWidth = 448;
        private int inputHeight = 448;
        private double defaultGeneralThreshold = 0.3;
        private double defaultCharacterThreshold = 0.85;

        public bool IsLoaded => session != null;

        public bool IsModelReady()
        {
            return RequiredFiles.All(file => downloader.IsFileCached(ModelRepo, file));
        }

        public IReadOnlyList<string> GetMissingFiles()
        {
            return RequiredFiles.Where(file => !downloader.IsFileCached(ModelRepo, file)).ToList();
        }

        public async Task DownloadModelAsync(
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            foreach (string file in RequiredFiles)
            {
                if (downloader.IsFileCached(ModelRepo, file))
                    continue;

                await downloader.DownloadFileAsync(source, ModelRepo, file, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public void LoadModel()
        {
            if (session != null)
                return;

            foreach (string file in RequiredFiles)
            {
                if (!downloader.IsFileCached(ModelRepo, file))
                    throw new FileNotFoundException(I18n.GetText("TaggerModelMissing"), file);
            }

            labels = PixAiSelectedTagsCsvLoader.Load(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "selected_tags.csv"));
            LoadThresholds(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "thresholds.csv"));
            LoadPreprocess(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "preprocess.json"));
            session = CreateSession(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "model.onnx"));
            ConfigureSessionMetadata(session);
        }

        public OnnxTagResult TagImageWithTiming(string imagePath, double generalThreshold, double characterThreshold)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            var stopwatch = Stopwatch.StartNew();
            // ImageLoader (ImageSharp) handles formats GDI+ cannot decode, e.g. WebP.
            using Image image = ImageLoader.GetImageFromFile(imagePath)
                ?? throw new InvalidOperationException(I18n.GetText("TaggerImageLoadFailed"));
            IReadOnlyList<AutoTagProviderItem> tags = TagImage(image, generalThreshold, characterThreshold);
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
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            DenseTensor<float> input = PixAiOnnxImagePreprocessor.CreateInputTensor(image, inputWidth, inputHeight);
            float[] output = RunPrediction(session, input);
            return BuildTagItems(output, generalThreshold, characterThreshold);
        }

        public void Unload()
        {
            session?.Dispose();
            session = null;
            labels.Clear();
        }

        public void ClearModelCache()
        {
            foreach (string file in RequiredFiles)
                downloader.DeleteCachedFile(ModelRepo, file);
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
                if (!outputNames.Any(name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)))
                    continue;

                return (resolvedInput, preferred, string.Equals(preferred, "logits", StringComparison.OrdinalIgnoreCase));
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
                session = CreateSession(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "model.onnx"), forceCpu: true);
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
            if (outputRequiresSigmoid)
                output = ApplySigmoid(output);
            return output;
        }

        private List<AutoTagProviderItem> BuildTagItems(float[] output, double generalThreshold, double characterThreshold)
        {
            var items = new List<AutoTagProviderItem>();
            int count = Math.Min(labels.Count, output.Length);
            for (int i = 0; i < count; i++)
            {
                (string name, int category) = labels[i];
                if (category is not (0 or 4))
                    continue;

                double threshold = category == 4 ? characterThreshold : generalThreshold;
                if (output[i] >= threshold)
                    items.Add(new AutoTagProviderItem { Tag = name, Confidence = output[i] });
            }

            return items;
        }

        private static InferenceSession CreateSession(string modelPath, bool forceCpu, out bool usesDirectMl)
        {
            var options = new SessionOptions
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

        private InferenceSession CreateSession(string modelPath, bool forceCpu = false)
        {
            return CreateSession(modelPath, forceCpu, out usesDirectMlProvider);
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
            using Bitmap rgb = EnsureRgbOnWhite(source);
            using Bitmap resized = Resize(rgb, width, height);
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

        private static Bitmap EnsureRgbOnWhite(Image source)
        {
            if (source.PixelFormat == PixelFormat.Format24bppRgb)
                return new Bitmap(source);

            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            return bitmap;
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
}
