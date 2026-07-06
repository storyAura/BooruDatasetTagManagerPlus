using System;
using System.Collections.Generic;
using System.Drawing;
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
            new Wd14ModelDefinition { Repo = "SmilingWolf/wd-eva02-large-tagger-v3", DefaultThreshold = 0.52, ShortName = "eva02-large v3" },
        };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private List<(string Name, int Category)> labels = new List<(string, int)>();
        private string loadedRepo;

        public string LoadedRepo => loadedRepo;
        public bool IsLoaded => session != null;

        public static Wd14ModelDefinition GetModel(string repo)
        {
            return Models.FirstOrDefault(model => string.Equals(model.Repo, repo, StringComparison.OrdinalIgnoreCase))
                ?? Models[^1];
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

        public void LoadModel(string repo)
        {
            if (string.Equals(loadedRepo, repo, StringComparison.OrdinalIgnoreCase) && session != null)
                return;

            Unload();
            string modelPath = HuggingFaceModelDownloader.GetLocalPath(repo, ModelFileName);
            string labelsPath = HuggingFaceModelDownloader.GetLocalPath(repo, LabelsFileName);
            if (!File.Exists(modelPath) || !File.Exists(labelsPath))
                throw new FileNotFoundException(I18n.GetText("TaggerModelMissing"));

            labels = LoadLabels(labelsPath);
            session = CreateSession(modelPath);
            loadedRepo = repo;
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

        public IReadOnlyList<AutoTagProviderItem> TagImage(string imagePath, double threshold)
        {
            return TagImage(imagePath, threshold, 0.85);
        }

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double generalThreshold, double characterThreshold)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            int targetSize = GetInputSize(session);
            DenseTensor<float> input = Wd14OnnxImagePreprocessor.CreateInputTensor(image, targetSize);
            string inputName = session.InputMetadata.Keys.First();
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
            float[] output = results.First().AsEnumerable<float>().ToArray();

            var items = new List<AutoTagProviderItem>();
            int count = Math.Min(labels.Count, output.Length);
            for (int i = 0; i < count; i++)
            {
                (string name, int category) = labels[i];
                double threshold = category switch
                {
                    0 => generalThreshold,
                    4 => characterThreshold,
                    _ => double.PositiveInfinity
                };

                if (category is not (0 or 4))
                    continue;

                if (output[i] >= threshold)
                    items.Add(new AutoTagProviderItem { Tag = name, Confidence = output[i] });
            }

            return items;
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

        private static int GetInputSize(InferenceSession session)
        {
            var dims = session.InputMetadata.Values.First().Dimensions;
            // NHWC: [batch, height, width, channels]
            if (dims.Length >= 4 && dims[1] > 0)
                return dims[1];
            if (dims.Length >= 3 && dims[0] > 0 && dims[0] != 3)
                return dims[0];
            return 448;
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
                        tensor[0, y, x, 0] = buffer[offset + 0];
                        tensor[0, y, x, 1] = buffer[offset + 1];
                        tensor[0, y, x, 2] = buffer[offset + 2];
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
            using Bitmap rgb = EnsureRgbOnWhite(source);
            using Bitmap squared = PadSquare(rgb);
            return ResizeIfNeeded(squared, targetSize);
        }

        private static Bitmap EnsureRgbOnWhite(Image source)
        {
            var rgba = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(rgba))
            {
                graphics.Clear(Color.White);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rgb = new Bitmap(rgba.Width, rgba.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(rgb))
            {
                graphics.DrawImage(rgba, 0, 0, rgba.Width, rgba.Height);
            }

            return rgb;
        }

        private static Bitmap PadSquare(Bitmap source)
        {
            int width = source.Width;
            int height = source.Height;
            int maxDim = Math.Max(width, height);
            if (width == maxDim && height == maxDim)
                return new Bitmap(source);

            var square = new Bitmap(maxDim, maxDim, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(square))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(source, (maxDim - width) / 2, (maxDim - height) / 2, width, height);
            }

            return square;
        }

        private static Bitmap ResizeIfNeeded(Bitmap source, int targetSize)
        {
            if (source.Width == targetSize && source.Height == targetSize)
                return new Bitmap(source);

            var resized = new Bitmap(targetSize, targetSize, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, targetSize, targetSize);
            }

            return resized;
        }
    }
}
