using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
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

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private List<(string Name, int Category)> labels = new List<(string, int)>();
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

            labels = LoadLabels(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "selected_tags.csv"));
            LoadThresholds(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "thresholds.csv"));
            LoadPreprocess(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "preprocess.json"));
            session = CreateSession(HuggingFaceModelDownloader.GetLocalPath(ModelRepo, "model.onnx"));
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(string imagePath, double generalThreshold, double characterThreshold)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            // ImageLoader (ImageSharp) handles formats GDI+ cannot decode, e.g. WebP.
            using Image image = ImageLoader.GetImageFromFile(imagePath)
                ?? throw new InvalidOperationException(I18n.GetText("TaggerImageLoadFailed"));
            return TagImage(image, generalThreshold, characterThreshold);
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double generalThreshold, double characterThreshold)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            DenseTensor<float> input = PixAiOnnxImagePreprocessor.CreateInputTensor(image, inputWidth, inputHeight);
            string inputName = session.InputMetadata.Keys.First();
            string outputName = session.OutputMetadata.Keys.First();
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
            float[] output = results.First().AsEnumerable<float>().ToArray();

            var items = new List<AutoTagProviderItem>();
            int count = Math.Min(labels.Count, output.Length);
            for (int i = 0; i < count; i++)
            {
                (string name, int category) = labels[i];
                double threshold = category == 4 ? characterThreshold : generalThreshold;
                if (output[i] >= threshold)
                    items.Add(new AutoTagProviderItem { Tag = name, Confidence = output[i] });
            }

            return items;
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

        private static InferenceSession CreateSession(string modelPath)
        {
            var options = new SessionOptions();
            try
            {
                options.AppendExecutionProvider_DML(0);
            }
            catch
            {
                options.AppendExecutionProvider_CPU();
            }

            return new InferenceSession(HuggingFaceModelDownloader.NormalizePathForOnnx(modelPath), options);
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

        private static List<(string Name, int Category)> LoadLabels(string labelsPath)
        {
            var result = new List<(string, int)>();
            foreach (string line in File.ReadAllLines(labelsPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("name", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int category))
                    continue;

                result.Add((parts[0], category));
            }

            return result;
        }
    }

    internal static class PixAiOnnxImagePreprocessor
    {
        public static DenseTensor<float> CreateInputTensor(Image source, int width, int height)
        {
            using Bitmap resized = Resize(source, width, height);
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = resized.GetPixel(x, y);
                    tensor[0, 0, y, x] = (pixel.R / 255f - 0.5f) / 0.5f;
                    tensor[0, 1, y, x] = (pixel.G / 255f - 0.5f) / 0.5f;
                    tensor[0, 2, y, x] = (pixel.B / 255f - 0.5f) / 0.5f;
                }
            }

            return tensor;
        }

        private static Bitmap Resize(Image source, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, 0, 0, width, height);
            }

            return bitmap;
        }
    }
}
