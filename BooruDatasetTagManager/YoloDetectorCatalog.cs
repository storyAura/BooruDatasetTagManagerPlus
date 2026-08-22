using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    public enum YoloDetectorKind
    {
        Person,
        Face,
        Head,
        Import
    }

    public sealed class YoloDetectorModelEntry
    {
        public string Id { get; init; }
        public YoloDetectorKind Kind { get; init; }
        public string DisplayName { get; init; }
        public string Repo { get; init; }
        public string FileName { get; init; }
        public float DefaultConfidence { get; init; } = 0.30f;

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>
    /// Curated anime YOLO detectors (deepghs, MIT, not gated). Standard
    /// YOLOv8 ONNX exports only — yolo11 / RT-DETR variants use a different
    /// head and would need a separate decoder.
    /// </summary>
    public static class YoloDetectorCatalog
    {
        public const string DefaultId = "deepghs:person_detect_v1.1_s";
        public const string ImportId = "yolo-import:custom";

        public static IReadOnlyList<YoloDetectorModelEntry> AllModels { get; }

        public static YoloDetectorModelEntry Default => GetById(DefaultId);

        static YoloDetectorCatalog()
        {
            AllModels = new[]
            {
                Person("person_detect_v1.1_n", "v1.1 nano", 0.33f),
                Person("person_detect_v1.1_s", "v1.1 small", 0.30f),
                Person("person_detect_v1.1_m", "v1.1 medium", 0.35f),
                Person("person_detect_v1.2_s", "v1.2 small", 0.30f),
                Person("person_detect_v1.3_s", "v1.3 small", 0.32f),
                Face("face_detect_v1.3_s", "v1.3 small", 0.26f),
                Face("face_detect_v1.4_n", "v1.4 nano", 0.28f),
                Face("face_detect_v1.4_s", "v1.4 small", 0.31f),
                Head("head_detect_v1.6_s", "v1.6 small", 0.30f),
                Head("head_detect_v2.0_n", "v2.0 nano", 0.30f),
                Head("head_detect_v2.0_s", "v2.0 small", 0.30f),
                new YoloDetectorModelEntry
                {
                    Id = ImportId,
                    Kind = YoloDetectorKind.Import,
                    DisplayName = "[Import] custom ONNX",
                    Repo = YoloPersonDetectorService.ImportRepo,
                    FileName = string.Empty,
                    DefaultConfidence = 0.30f
                }
            };
        }

        public static YoloDetectorModelEntry GetById(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                YoloDetectorModelEntry match = AllModels.FirstOrDefault(model =>
                    string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return AllModels.First(model => model.Id == DefaultId);
        }

        public static YoloDetectorModelEntry ResolveInitial(string savedId, string importPath)
        {
            if (!string.IsNullOrWhiteSpace(savedId)
                && AllModels.Any(model => string.Equals(model.Id, savedId, StringComparison.OrdinalIgnoreCase)))
            {
                return GetById(savedId);
            }

            // Pre-catalog installs had no model id; they preferred an imported
            // ONNX whenever one was on disk.
            if (!string.IsNullOrWhiteSpace(importPath) && File.Exists(importPath))
                return GetById(ImportId);

            return Default;
        }

        public static string GetLocalPath(YoloDetectorModelEntry entry)
        {
            if (entry == null || entry.Kind == YoloDetectorKind.Import)
                return null;
            return HuggingFaceModelDownloader.GetLocalPath(entry.Repo, entry.FileName);
        }

        public static YoloDetectorModelEntry FindByLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string full = Path.GetFullPath(path);
            foreach (YoloDetectorModelEntry entry in AllModels)
            {
                if (entry.Kind == YoloDetectorKind.Import)
                    continue;
                string local = GetLocalPath(entry);
                if (string.Equals(local, full, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        private static YoloDetectorModelEntry Person(string folder, string shortName, float confidence)
        {
            return new YoloDetectorModelEntry
            {
                Id = "deepghs:" + folder,
                Kind = YoloDetectorKind.Person,
                DisplayName = "[Person] " + shortName,
                Repo = "deepghs/anime_person_detection",
                FileName = folder + "/model.onnx",
                DefaultConfidence = confidence
            };
        }

        private static YoloDetectorModelEntry Face(string folder, string shortName, float confidence)
        {
            return new YoloDetectorModelEntry
            {
                Id = "deepghs:" + folder,
                Kind = YoloDetectorKind.Face,
                DisplayName = "[Face] " + shortName,
                Repo = "deepghs/anime_face_detection",
                FileName = folder + "/model.onnx",
                DefaultConfidence = confidence
            };
        }

        private static YoloDetectorModelEntry Head(string folder, string shortName, float confidence)
        {
            return new YoloDetectorModelEntry
            {
                Id = "deepghs:" + folder,
                Kind = YoloDetectorKind.Head,
                DisplayName = "[Head] " + shortName,
                Repo = "deepghs/anime_head_detection",
                FileName = folder + "/model.onnx",
                DefaultConfidence = confidence
            };
        }
    }
}
