using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Size = System.Drawing.Size;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// YOLOv8 anime detector (person / face / head). Default weights:
    /// HuggingFace <c>deepghs/anime_person_detection</c> /
    /// <c>person_detect_v1.1_s/model.onnx</c> (MIT, not gated). Catalog:
    /// <see cref="YoloDetectorCatalog"/>.
    /// </summary>
    public sealed class YoloPersonDetectorService : IDisposable
    {
        public const string DefaultRepo = "deepghs/anime_person_detection";
        public const string DefaultFileName = "person_detect_v1.1_s/model.onnx";
        public const string DefaultModelId = YoloDetectorCatalog.DefaultId;
        public const string ImportRepo = "yolo-import";
        public const float DefaultConfidence = 0.3f;

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private readonly object sync = new object();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private string loadedModelPath;
        private string inputName;
        private int inputSize = YoloDetectionMath.DefaultInputSize;

        public bool IsLoaded => session != null;

        public string DefaultLocalPath => HuggingFaceModelDownloader.GetLocalPath(DefaultRepo, DefaultFileName);

        public bool IsDefaultModelReady()
        {
            return downloader.IsFileCached(DefaultRepo, DefaultFileName);
        }

        public bool IsModelReady(string importPath)
        {
            if (!string.IsNullOrWhiteSpace(importPath) && File.Exists(importPath))
                return true;
            return IsDefaultModelReady();
        }

        public bool IsModelReady(YoloDetectorModelEntry entry, string importPath)
        {
            if (entry == null)
                return false;
            if (entry.Kind == YoloDetectorKind.Import)
                return !string.IsNullOrWhiteSpace(importPath) && File.Exists(importPath);
            return downloader.IsFileCached(entry.Repo, entry.FileName);
        }

        public string ResolveModelPath(YoloDetectorModelEntry entry, string importPath)
        {
            if (entry == null || entry.Kind == YoloDetectorKind.Import)
            {
                if (!string.IsNullOrWhiteSpace(importPath) && File.Exists(importPath))
                    return Path.GetFullPath(importPath);
                return null;
            }

            return YoloDetectorCatalog.GetLocalPath(entry);
        }

        public async Task DownloadDefaultModelAsync(
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            await DownloadModelAsync(YoloDetectorCatalog.Default, source, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task DownloadModelAsync(
            YoloDetectorModelEntry entry,
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            if (entry == null || entry.Kind == YoloDetectorKind.Import)
                return;
            if (downloader.IsFileCached(entry.Repo, entry.FileName))
                return;
            await downloader.DownloadFileAsync(source, entry.Repo, entry.FileName, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Copies an ONNX file into <c>Models/yolo-import/{filename}</c> (contained).
        /// </summary>
        public static string ImportOnnx(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException(sourcePath);
            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName)
                || !fileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected an .onnx file.");
            }

            string dest = HuggingFaceModelDownloader.GetLocalPath(ImportRepo, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? Program.AppPath);
            File.Copy(sourcePath, dest, overwrite: true);
            return dest;
        }

        public void LoadModel(string modelPath = null)
        {
            string path = string.IsNullOrWhiteSpace(modelPath) ? DefaultLocalPath : Path.GetFullPath(modelPath);
            lock (sync)
            {
                if (string.Equals(loadedModelPath, path, StringComparison.OrdinalIgnoreCase) && session != null)
                    return;

                UnloadUnderLock();
                if (!File.Exists(path))
                    throw new FileNotFoundException(I18n.GetText("YoloDetectNoModel"));

                loadedModelPath = path;
                try
                {
                    OnnxModelIntegrity.RunWithTransientLockRetry(() =>
                    {
                        try
                        {
                            session = CreateSession(path, forceCpu: false, out usesDirectMlProvider);
                            ResolveSessionMetadata(session);
                        }
                        catch (Exception ex) when (ex is not DllNotFoundException && usesDirectMlProvider)
                        {
                            session?.Dispose();
                            session = CreateSession(path, forceCpu: true, out usesDirectMlProvider);
                            ResolveSessionMetadata(session);
                        }
                    });
                }
                catch (Exception ex) when (ex is not FileNotFoundException
                                           and not NotSupportedException)
                {
                    YoloDetectorModelEntry cached = YoloDetectorCatalog.FindByLocalPath(path);
                    UnloadUnderLock();
                    if (cached != null
                        && cached.Kind != YoloDetectorKind.Import
                        && OnnxModelIntegrity.ShouldClearCachedModel(ex))
                    {
                        downloader.DeleteCachedFile(cached.Repo, cached.FileName);
                        throw new ModelCorruptedException(I18n.GetText("TaggerModelCorruptCleared"), ex);
                    }

                    throw;
                }
            }
        }

        public List<(System.Drawing.Rectangle Box, float Score)> Detect(
            string imagePath,
            float confThreshold = DefaultConfidence,
            float iouThreshold = YoloDetectionMath.DefaultIou)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return new List<(System.Drawing.Rectangle, float)>();

            using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(imagePath);
            image.Mutate(context => context.AutoOrient());
            var size = new Size(image.Width, image.Height);
            YoloLetterbox map = YoloDetectionMath.ComputeLetterbox(image.Width, image.Height, inputSize);
            DenseTensor<float> input = BuildInputTensor(image, map);
            float[] output = RunWithFallback(input, out int[] dimensions);
            List<(System.Drawing.Rectangle Box, float Score)> raw =
                YoloDetectionMath.ParseYoloOutput(output, dimensions, confThreshold, map, size);
            return YoloDetectionMath.NonMaxSuppression(raw, iouThreshold);
        }

        private DenseTensor<float> BuildInputTensor(Image<Rgb24> image, YoloLetterbox map)
        {
            int size = map.InputSize;
            var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
            float fill = YoloDetectionMath.LetterboxFill / 255f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tensor[0, 0, y, x] = fill;
                    tensor[0, 1, y, x] = fill;
                    tensor[0, 2, y, x] = fill;
                }
            }

            using var resized = image.Clone(context => context.Resize(
                new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(map.NewWidth, map.NewHeight),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Triangle
                }));

            resized.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb24> row = accessor.GetRowSpan(y);
                    int destY = y + map.PadY;
                    for (int x = 0; x < row.Length; x++)
                    {
                        int destX = x + map.PadX;
                        Rgb24 pixel = row[x];
                        tensor[0, 0, destY, destX] = pixel.R / 255f;
                        tensor[0, 1, destY, destX] = pixel.G / 255f;
                        tensor[0, 2, destY, destX] = pixel.B / 255f;
                    }
                }
            });

            return tensor;
        }

        private float[] RunWithFallback(DenseTensor<float> input, out int[] dimensions)
        {
            try
            {
                return Run(input, out dimensions);
            }
            catch (Exception ex) when (ex is not DllNotFoundException && usesDirectMlProvider)
            {
                lock (sync)
                {
                    session?.Dispose();
                    session = CreateSession(loadedModelPath, forceCpu: true, out usesDirectMlProvider);
                    ResolveSessionMetadata(session);
                }
                return Run(input, out dimensions);
            }
        }

        private float[] Run(DenseTensor<float> input, out int[] dimensions)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, input)
            };
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
            DisposableNamedOnnxValue first = results.First();
            var tensor = first.AsTensor<float>();
            dimensions = tensor.Dimensions.ToArray();
            return tensor.ToArray();
        }

        private void ResolveSessionMetadata(InferenceSession loadedSession)
        {
            inputName = loadedSession.InputMetadata.Keys.First();
            int[] dims = loadedSession.InputMetadata[inputName].Dimensions;
            if (dims != null && dims.Length >= 4 && dims[2] > 0)
                inputSize = dims[2];
            else
                inputSize = YoloDetectionMath.DefaultInputSize;

            Type inputType = loadedSession.InputMetadata[inputName].ElementType;
            if (inputType != typeof(float))
            {
                UnloadUnderLock();
                throw new NotSupportedException(
                    string.Format(I18n.GetText("UIBGRemovalFormUnsupportedInput"), inputType?.Name));
            }
        }

        private static InferenceSession CreateSession(string modelPath, bool forceCpu, out bool usesDirectMl)
        {
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

        private void UnloadUnderLock()
        {
            session?.Dispose();
            session = null;
            loadedModelPath = null;
        }

        public void Dispose()
        {
            lock (sync)
            {
                UnloadUnderLock();
            }
        }
    }
}
