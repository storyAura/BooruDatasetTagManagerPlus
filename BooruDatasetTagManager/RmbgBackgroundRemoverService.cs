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

namespace BooruDatasetTagManager
{
    public sealed class RmbgModelDefinition
    {
        public string Id { get; init; }
        public string DisplayName { get; init; }
        public string Repo { get; init; }
        public string FileName { get; init; }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// In-process background removal using the official RMBG-1.4 ONNX weights,
    /// replacing the old dependency on the external AiApiServer/PyTorch service.
    /// RMBG-1.4 is used (not 2.0) because 2.0 is a gated repo that cannot be
    /// downloaded anonymously. Pre/post-processing mirrors BRIA's RMBG-1.4
    /// reference exactly: RGB, resize to 1024x1024 bilinear, /255, normalize with
    /// mean 0.5 / std 1.0; the output mask is min-max normalized, resized back,
    /// and used as the alpha channel.
    /// </summary>
    public sealed class RmbgBackgroundRemoverService : IDisposable
    {
        private const int InputSize = 1024;
        private static readonly float[] Mean = { 0.5f, 0.5f, 0.5f };
        private static readonly float[] Std = { 1.0f, 1.0f, 1.0f };

        // Only float32-input ONNX exports are listed. The fp16 export needs
        // Float16 tensors; full/quantized both keep float32 I/O.
        public static IReadOnlyList<RmbgModelDefinition> Models { get; } = new[]
        {
            new RmbgModelDefinition
            {
                Id = "rmbg14:full",
                DisplayName = "RMBG-1.4 (~176 MB)",
                Repo = "briaai/RMBG-1.4",
                FileName = "onnx/model.onnx"
            },
            new RmbgModelDefinition
            {
                Id = "rmbg14:quantized",
                DisplayName = "RMBG-1.4 (quantized, ~44 MB)",
                Repo = "briaai/RMBG-1.4",
                FileName = "onnx/model_quantized.onnx"
            },
        };

        private readonly HuggingFaceModelDownloader downloader = new HuggingFaceModelDownloader();
        private readonly object sync = new object();
        private InferenceSession session;
        private bool usesDirectMlProvider;
        private string loadedModelPath;
        private string loadedModelId;
        private string inputName;
        private string outputName;

        public bool IsLoaded => session != null;
        public string LoadedModelId => loadedModelId;

        public static RmbgModelDefinition GetModel(string id)
        {
            return Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Models[0];
        }

        public bool IsModelReady(RmbgModelDefinition model)
        {
            return model != null && downloader.IsFileCached(model.Repo, model.FileName);
        }

        public async Task DownloadModelAsync(
            RmbgModelDefinition model,
            HuggingFaceDownloadSource source,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (downloader.IsFileCached(model.Repo, model.FileName))
                return;
            await downloader.DownloadFileAsync(source, model.Repo, model.FileName, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        public void LoadModel(RmbgModelDefinition model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            lock (sync)
            {
                if (string.Equals(loadedModelId, model.Id, StringComparison.OrdinalIgnoreCase) && session != null)
                    return;

                UnloadUnderLock();
                string modelPath = HuggingFaceModelDownloader.GetLocalPath(model.Repo, model.FileName);
                if (!File.Exists(modelPath))
                    throw new FileNotFoundException(I18n.GetText("TaggerModelMissing"));

                loadedModelPath = modelPath;
                try
                {
                    // Loading the session is the integrity check: a corrupt or
                    // truncated ONNX file throws here. Delete it so the caller
                    // re-downloads a clean copy. FileNotFound / unsupported-input /
                    // missing-native-runtime are not corruption.
                    session = CreateSession(modelPath, forceCpu: false, out usesDirectMlProvider);
                    ResolveSessionMetadata(session);
                }
                catch (Exception ex) when (ex is not FileNotFoundException
                                           and not NotSupportedException
                                           and not DllNotFoundException)
                {
                    UnloadUnderLock();
                    downloader.DeleteCachedFile(model.Repo, model.FileName);
                    throw new ModelCorruptedException(I18n.GetText("TaggerModelCorruptCleared"), ex);
                }
                loadedModelId = model.Id;
            }
        }

        /// <summary>
        /// Removes the background from <paramref name="imagePath"/> and returns a
        /// PNG as a byte array. When <paramref name="fillColor"/> is null the PNG
        /// keeps a transparent background; otherwise the cut-out is composited
        /// over that solid color (opaque output). Thread-affine: callers should
        /// invoke it from a single worker at a time (the batch loop is sequential).
        /// </summary>
        public byte[] RemoveBackground(string imagePath, System.Drawing.Color? fillColor = null)
        {
            if (session == null)
                throw new InvalidOperationException("Model is not loaded.");

            using var original = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);

            DenseTensor<float> input = BuildInputTensor(original);
            float[] mask = RunWithFallback(input);

            ApplyMaskAsAlpha(original, mask, InputSize, InputSize);

            using var output = new MemoryStream();
            if (fillColor.HasValue)
            {
                var bg = new Rgba32(fillColor.Value.R, fillColor.Value.G, fillColor.Value.B, 255);
                using var flattened = new Image<Rgba32>(original.Width, original.Height, bg);
                // Alpha-composite the cut-out over the solid background.
                flattened.Mutate(ctx => ctx.DrawImage(original, 1f));
                flattened.SaveAsPng(output);
            }
            else
            {
                original.SaveAsPng(output);
            }
            return output.ToArray();
        }

        private DenseTensor<float> BuildInputTensor(Image<Rgba32> source)
        {
            using Image<Rgb24> resized = source.CloneAs<Rgb24>();
            resized.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                // Stretch to a square + Triangle (bilinear) mirror the Python
                // transforms.Resize((1024,1024)) preprocessing exactly.
                Mode = ResizeMode.Stretch,
                Size = new SixLabors.ImageSharp.Size(InputSize, InputSize),
                Sampler = KnownResamplers.Triangle
            }));

            var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
            resized.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb24> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        Rgb24 p = row[x];
                        tensor[0, 0, y, x] = (p.R / 255f - Mean[0]) / Std[0];
                        tensor[0, 1, y, x] = (p.G / 255f - Mean[1]) / Std[1];
                        tensor[0, 2, y, x] = (p.B / 255f - Mean[2]) / Std[2];
                    }
                }
            });
            return tensor;
        }

        private float[] RunWithFallback(DenseTensor<float> input)
        {
            try
            {
                return RunCore(session, input);
            }
            catch (OnnxRuntimeException) when (usesDirectMlProvider)
            {
                // DirectML runtime failure: rebuild on CPU and retry once (same
                // pattern as the WD14 tagger).
                lock (sync)
                {
                    session?.Dispose();
                    session = CreateSession(loadedModelPath, forceCpu: true, out usesDirectMlProvider);
                    ResolveSessionMetadata(session);
                }
                return RunCore(session, input);
            }
        }

        private float[] RunCore(InferenceSession activeSession, DenseTensor<float> input)
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = activeSession.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, input) },
                new[] { outputName });
            float[] raw = results.First().AsEnumerable<float>().ToArray();

            // Min-max normalize to [0,1] (BRIA RMBG-1.4 postprocess). This is
            // robust whether the export emits a sigmoid mask or raw logits;
            // guard against a uniform mask (all foreground / all background).
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] < min) min = raw[i];
                if (raw[i] > max) max = raw[i];
            }
            float range = max - min;
            if (range < 1e-6f)
            {
                for (int i = 0; i < raw.Length; i++)
                    raw[i] = Math.Clamp(raw[i], 0f, 1f);
            }
            else
            {
                for (int i = 0; i < raw.Length; i++)
                    raw[i] = (raw[i] - min) / range;
            }
            return raw;
        }

        private static void ApplyMaskAsAlpha(Image<Rgba32> image, float[] mask, int maskWidth, int maskHeight)
        {
            // Resize the mask back to the source resolution with bilinear
            // sampling, matching the Python PIL mask.resize(image.size).
            using var maskImage = new Image<L8>(maskWidth, maskHeight);
            maskImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<L8> row = accessor.GetRowSpan(y);
                    int baseIndex = y * maskWidth;
                    for (int x = 0; x < row.Length; x++)
                    {
                        float v = mask[baseIndex + x];
                        row[x] = new L8((byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255));
                    }
                }
            });
            // Triangle == bilinear in ImageSharp.
            maskImage.Mutate(ctx => ctx.Resize(image.Width, image.Height, KnownResamplers.Triangle));

            maskImage.ProcessPixelRows(image, (maskAccessor, imageAccessor) =>
            {
                for (int y = 0; y < imageAccessor.Height; y++)
                {
                    Span<L8> maskRow = maskAccessor.GetRowSpan(y);
                    Span<Rgba32> imageRow = imageAccessor.GetRowSpan(y);
                    for (int x = 0; x < imageRow.Length; x++)
                        imageRow[x].A = maskRow[x].PackedValue;
                }
            });
        }

        private void ResolveSessionMetadata(InferenceSession loadedSession)
        {
            inputName = loadedSession.InputMetadata.Keys.First();
            outputName = loadedSession.OutputMetadata.Keys.First();

            // This service feeds float32 tensors; the fp16 export would need
            // Float16 tensors. Fail with a clear message instead of a cryptic
            // ORT type error at inference time.
            Type inputType = loadedSession.InputMetadata[inputName].ElementType;
            if (inputType != typeof(float))
            {
                // Already holding sync (called from LoadModel / RunWithFallback).
                // NotSupportedException (not corruption) so LoadModel does not
                // delete a perfectly valid but wrong-precision model.
                UnloadUnderLock();
                throw new NotSupportedException(
                    string.Format(I18n.GetText("UIBGRemovalFormUnsupportedInput"), inputType?.Name));
            }
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

        private void UnloadUnderLock()
        {
            session?.Dispose();
            session = null;
            loadedModelId = null;
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
