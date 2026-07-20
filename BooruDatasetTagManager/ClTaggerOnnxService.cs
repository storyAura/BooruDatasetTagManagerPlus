using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Preprocessing recipes of the cl_tagger family (verified against the
    /// author's onnx_predict.py and the cl_tagger_v2 model card).
    /// </summary>
    public enum ClTaggerPreprocess
    {
        /// <summary>v1 (WD EVA02 fine-tune): pad to a white square, resize,
        /// scale to [0,1], flip RGB→BGR, normalize mean=std=0.5, NCHW.</summary>
        V1PadSquareBgr,
        /// <summary>v2 (SigLIP2): direct resize (no padding), keep RGB,
        /// scale to [0,1], normalize mean=std=0.5, NCHW.</summary>
        V2ResizeRgb
    }

    public sealed class ClTaggerModelDefinition
    {
        public string Id { get; init; }
        public string Repo { get; init; }
        public string ShortName { get; init; }
        public string ModelFile { get; init; }
        public string LabelsFile { get; init; }
        /// <summary>Sidecar external-weights file (model.onnx.data) or null.</summary>
        public string ExternalDataFile { get; init; }
        public ClTaggerPreprocess Preprocess { get; init; }
        public int FallbackInputSize { get; init; } = 448;
        public double DefaultThreshold { get; init; }
        public double DefaultCharacterThreshold { get; init; }
        /// <summary>Gated HuggingFace repo: requires accepting the author's
        /// license on the model page and an access token to download.</summary>
        public bool IsGated { get; init; }
        public string RepoUrl => "https://huggingface.co/" + Repo;

        public IEnumerable<string> AllFiles()
        {
            yield return ModelFile;
            if (!string.IsNullOrEmpty(ExternalDataFile))
                yield return ExternalDataFile;
            yield return LabelsFile;
        }
    }

    /// <summary>
    /// ONNX inference for the cl_tagger family. v1 comes from the public
    /// Nonene/cl_tagger mirror; v2 (cella110n/cl_tagger_v2) is a gated repo
    /// whose license forbids redistribution — the app never bundles it, the
    /// user downloads it with their own HuggingFace access after accepting
    /// the license.
    /// </summary>
    public sealed class ClTaggerOnnxService : IDisposable
    {
        public static IReadOnlyList<ClTaggerModelDefinition> Models { get; } = new[]
        {
            new ClTaggerModelDefinition
            {
                Id = "cl:Nonene/cl_tagger:1_02",
                Repo = "Nonene/cl_tagger",
                ShortName = "v1.02",
                ModelFile = "cl_tagger_1_02/model.onnx",
                LabelsFile = "cl_tagger_1_02/tag_mapping.json",
                Preprocess = ClTaggerPreprocess.V1PadSquareBgr,
                FallbackInputSize = 448,
                DefaultThreshold = 0.45,
                DefaultCharacterThreshold = 0.45,
                IsGated = false
            },
            new ClTaggerModelDefinition
            {
                Id = "cl:cella110n/cl_tagger_v2:v2_00",
                Repo = "cella110n/cl_tagger_v2",
                ShortName = "v2.00",
                ModelFile = "v2_00/model.onnx",
                ExternalDataFile = "v2_00/model.onnx.data",
                LabelsFile = "v2_00/model_vocabulary.json",
                Preprocess = ClTaggerPreprocess.V2ResizeRgb,
                FallbackInputSize = 384,
                DefaultThreshold = 0.55,
                DefaultCharacterThreshold = 0.55,
                IsGated = true
            },
            new ClTaggerModelDefinition
            {
                Id = "cl:cella110n/cl_tagger_v2:v2_01a",
                Repo = "cella110n/cl_tagger_v2",
                ShortName = "v2.01a",
                ModelFile = "v2_01a/model.onnx",
                ExternalDataFile = "v2_01a/model.onnx.data",
                LabelsFile = "v2_01a/model_vocabulary.json",
                Preprocess = ClTaggerPreprocess.V2ResizeRgb,
                FallbackInputSize = 384,
                DefaultThreshold = 0.55,
                DefaultCharacterThreshold = 0.55,
                IsGated = true
            }
        };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private string inputName;
        private string outputName;
        private string loadedModelPath;
        private List<(string Name, string Category)> labels = new List<(string, string)>();
        private ClTaggerModelDefinition loadedModel;

        public bool IsLoaded => session != null;

        public static ClTaggerModelDefinition GetById(string id)
        {
            return Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Models[0];
        }

        public bool IsModelReady(ClTaggerModelDefinition model)
        {
            return model.AllFiles().All(file => downloader.IsFileCached(model.Repo, file));
        }

        public IReadOnlyList<string> GetRequiredFiles(ClTaggerModelDefinition model)
        {
            return model.AllFiles().Where(file => !downloader.IsFileCached(model.Repo, file)).ToList();
        }

        public async Task DownloadModelAsync(
            ClTaggerModelDefinition model,
            HuggingFaceDownloadSource source,
            string authToken,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            foreach (string file in model.AllFiles())
            {
                if (downloader.IsFileCached(model.Repo, file))
                    continue;

                await downloader.DownloadFileAsync(source, model.Repo, file, authToken, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public void LoadModel(ClTaggerModelDefinition model)
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
                // Loading labels + session is the integrity check: a corrupt
                // JSON or truncated ONNX throws here. Purge the whole model so
                // the next attempt re-downloads clean files.
                labels = LoadLabels(labelsPath, model.Preprocess);
                loadedModelPath = modelPath;
                session = CreateSession(modelPath);
                (inputName, outputName) = Wd14OnnxTaggerService.ResolveSessionMetadata(session);
            }
            catch (Exception ex) when (ex is not FileNotFoundException and not DllNotFoundException)
            {
                Unload();
                ClearModelCache(model);
                throw new ModelCorruptedException(I18n.GetText("TaggerModelCorruptCleared"), ex);
            }
            loadedModel = model;
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

        public IReadOnlyList<AutoTagProviderItem> TagImage(Image image, double generalThreshold, double characterThreshold)
        {
            if (session == null || loadedModel == null)
                throw new InvalidOperationException("Model is not loaded.");

            int targetSize = ResolveInputSize(session, loadedModel.FallbackInputSize);
            DenseTensor<float> input = ClTaggerImagePreprocessor.CreateInputTensor(image, targetSize, loadedModel.Preprocess);
            float[] logits = RunPrediction(session, input);

            var items = new List<AutoTagProviderItem>();
            int count = Math.Min(labels.Count, logits.Length);
            for (int i = 0; i < count; i++)
            {
                (string name, string category) = labels[i];
                if (name == null)
                    continue;

                double threshold;
                if (string.Equals(category, "General", StringComparison.OrdinalIgnoreCase))
                    threshold = generalThreshold;
                else if (string.Equals(category, "Character", StringComparison.OrdinalIgnoreCase))
                    threshold = characterThreshold;
                else
                    continue; // Rating/Quality/Copyright/Artist/Meta/Model are skipped like WD14.

                double probability = Sigmoid(logits[i]);
                if (probability >= threshold)
                    items.Add(new AutoTagProviderItem { Tag = name, Confidence = (float)probability });
            }

            return items;
        }

        internal static double Sigmoid(double x)
        {
            // Numerically stable for large |x| (the model outputs raw logits).
            if (x >= 0)
                return 1.0 / (1.0 + Math.Exp(-x));
            double e = Math.Exp(x);
            return e / (1.0 + e);
        }

        internal static int ResolveInputSize(InferenceSession session, int fallback)
        {
            int resolved = Wd14OnnxTaggerService.ResolveInputSize(session.InputMetadata.Values.First().Dimensions);
            return resolved > 0 ? resolved : fallback;
        }

        public void Unload()
        {
            session?.Dispose();
            session = null;
            loadedModel = null;
            loadedModelPath = null;
            labels.Clear();
        }

        public void ClearModelCache(ClTaggerModelDefinition model)
        {
            foreach (string file in model.AllFiles())
                downloader.DeleteCachedFile(model.Repo, file);
        }

        public void Dispose()
        {
            Unload();
        }

        /// <summary>
        /// v1 tag_mapping.json: { "0": { "tag": "...", "category": "General" }, ... }.
        /// v2 model_vocabulary.json: { "idx_to_tag": { "0": "..." }, "tag_to_category": { "tag": "General" } }.
        /// Returns a list aligned to the model output index (gaps stay null).
        /// </summary>
        internal static List<(string Name, string Category)> LoadLabels(string labelsPath, ClTaggerPreprocess preprocess)
        {
            using var reader = new StreamReader(labelsPath);
            using var jsonReader = new Newtonsoft.Json.JsonTextReader(reader);
            JObject root = JObject.Load(jsonReader);
            return preprocess == ClTaggerPreprocess.V2ResizeRgb
                ? ParseV2Vocabulary(root)
                : ParseV1TagMapping(root);
        }

        internal static List<(string Name, string Category)> ParseV1TagMapping(JObject root)
        {
            var byIndex = new SortedDictionary<int, (string, string)>();
            foreach (JProperty property in root.Properties())
            {
                if (!int.TryParse(property.Name, out int index) || property.Value is not JObject entry)
                    continue;
                string tag = (string)entry["tag"];
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                byIndex[index] = (tag, (string)entry["category"] ?? string.Empty);
            }

            return BuildAlignedList(byIndex);
        }

        internal static List<(string Name, string Category)> ParseV2Vocabulary(JObject root)
        {
            if (root["idx_to_tag"] is not JObject idxToTag)
                throw new InvalidDataException("model_vocabulary.json is missing idx_to_tag.");
            var tagToCategory = root["tag_to_category"] as JObject;

            var byIndex = new SortedDictionary<int, (string, string)>();
            foreach (JProperty property in idxToTag.Properties())
            {
                if (!int.TryParse(property.Name, out int index))
                    continue;
                string tag = (string)property.Value;
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                string category = tagToCategory?[tag]?.ToString() ?? string.Empty;
                byIndex[index] = (tag, category);
            }

            return BuildAlignedList(byIndex);
        }

        private static List<(string Name, string Category)> BuildAlignedList(SortedDictionary<int, (string, string)> byIndex)
        {
            if (byIndex.Count == 0)
                throw new InvalidDataException("The tag mapping file contains no tags.");

            var result = new List<(string, string)>(new (string, string)[byIndex.Keys.Max() + 1]);
            foreach (KeyValuePair<int, (string, string)> pair in byIndex)
                result[pair.Key] = pair.Value;
            return result;
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
                (inputName, outputName) = Wd14OnnxTaggerService.ResolveSessionMetadata(session);
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
            return Wd14OnnxTaggerService.ExtractFloatVector(results.First());
        }

        private InferenceSession CreateSession(string modelPath, bool forceCpu = false)
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            usesDirectMlProvider = false;
            if (forceCpu)
            {
                options.AppendExecutionProvider_CPU();
            }
            else
            {
                try
                {
                    options.AppendExecutionProvider_DML(0);
                    usesDirectMlProvider = true;
                }
                catch
                {
                    options.AppendExecutionProvider_CPU();
                }
            }

            return new InferenceSession(HuggingFaceModelDownloader.NormalizePathForOnnx(modelPath), options);
        }
    }

    internal static class ClTaggerImagePreprocessor
    {
        /// <summary>
        /// Both recipes: white-composited RGB, scale to [0,1], normalize
        /// mean=std=0.5, NCHW float32. v1 additionally pads to a white square
        /// before resizing and flips channels to BGR; v2 resizes directly and
        /// stays RGB.
        /// </summary>
        public static DenseTensor<float> CreateInputTensor(Image source, int targetSize, ClTaggerPreprocess mode)
        {
            using Bitmap prepared = PrepareBitmap(source, targetSize, mode);
            int width = prepared.Width;
            int height = prepared.Height;
            bool bgr = mode == ClTaggerPreprocess.V1PadSquareBgr;
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
                        // GDI byte order is BGR in memory.
                        float b = Normalize(buffer[offset + 0]);
                        float g = Normalize(buffer[offset + 1]);
                        float r = Normalize(buffer[offset + 2]);
                        if (bgr)
                        {
                            tensor[0, 0, y, x] = b;
                            tensor[0, 1, y, x] = g;
                            tensor[0, 2, y, x] = r;
                        }
                        else
                        {
                            tensor[0, 0, y, x] = r;
                            tensor[0, 1, y, x] = g;
                            tensor[0, 2, y, x] = b;
                        }
                    }
                }
            }
            finally
            {
                prepared.UnlockBits(data);
            }

            return tensor;
        }

        internal static float Normalize(byte value)
        {
            return (value / 255f - 0.5f) / 0.5f;
        }

        private static Bitmap PrepareBitmap(Image source, int targetSize, ClTaggerPreprocess mode)
        {
            using Bitmap rgb = EnsureRgbOnWhite(source);
            if (mode == ClTaggerPreprocess.V2ResizeRgb)
                return Resize(rgb, targetSize);

            using Bitmap squared = PadSquare(rgb);
            return Resize(squared, targetSize);
        }

        private static Bitmap EnsureRgbOnWhite(Image source)
        {
            var rgba = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(rgba))
            {
                graphics.Clear(Color.White);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rgb = new Bitmap(rgba.Width, rgba.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(rgb))
            {
                graphics.DrawImage(rgba, 0, 0, rgba.Width, rgba.Height);
            }

            rgba.Dispose();
            return rgb;
        }

        private static Bitmap PadSquare(Bitmap source)
        {
            int width = source.Width;
            int height = source.Height;
            if (width == height)
                return new Bitmap(source);

            int size = Math.Max(width, height);
            var square = new Bitmap(size, size, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(square))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
            }

            return square;
        }

        private static Bitmap Resize(Bitmap source, int targetSize)
        {
            if (source.Width == targetSize && source.Height == targetSize)
                return new Bitmap(source);

            var resized = new Bitmap(targetSize, targetSize, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, targetSize, targetSize);
            }

            return resized;
        }
    }
}
