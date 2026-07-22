using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// One selectable dataset sub-folder (typically a kohya repeat folder such
    /// as "character_webp/1_a"). <see cref="RelativePath"/> is normalized with
    /// '/' separators; an empty value means images directly under the root.
    /// </summary>
    public sealed class DatasetFolderEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public int ImageCount { get; set; }
        public string RepresentativeImagePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pure path math for grouping a flat dataset by sub-folder and testing
    /// folder membership. Kept out of Forms so it can be linked into the test
    /// project.
    /// </summary>
    public static class DatasetFolderIndex
    {
        /// <summary>
        /// Relative folder of <paramref name="imagePath"/> under
        /// <paramref name="datasetRoot"/>, normalized to '/' separators.
        /// Returns an empty string for images directly in the root and for
        /// paths outside the root (they belong to the implicit root group).
        /// </summary>
        public static string GetRelativeFolder(string imagePath, string datasetRoot)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(datasetRoot))
                return string.Empty;
            string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
            if (string.IsNullOrEmpty(directory))
                return string.Empty;
            string root = NormalizeAbsolute(datasetRoot);
            string normalizedDirectory = NormalizeAbsolute(directory);
            if (string.Equals(normalizedDirectory, root, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            if (!normalizedDirectory.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return normalizedDirectory.Substring(root.Length + 1);
        }

        /// <summary>
        /// True when <paramref name="imagePath"/> belongs to
        /// <paramref name="relativeFolder"/>. A null or empty folder means
        /// "all images". Matching is exact (no sub-folder inheritance) because
        /// every gallery entry is one concrete directory.
        /// </summary>
        public static bool IsInFolder(string imagePath, string datasetRoot, string relativeFolder)
        {
            if (string.IsNullOrEmpty(relativeFolder))
                return true;
            return string.Equals(
                GetRelativeFolder(imagePath, datasetRoot),
                NormalizeRelative(relativeFolder),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Groups image paths into folder entries ordered by name. The
        /// representative image is the naturally-first path of each group so
        /// the gallery thumbnail is stable across reloads.
        /// </summary>
        public static IReadOnlyList<DatasetFolderEntry> Create(IEnumerable<string> imagePaths, string datasetRoot)
        {
            if (imagePaths == null)
                throw new ArgumentNullException(nameof(imagePaths));

            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string folder = GetRelativeFolder(path, datasetRoot);
                if (!groups.TryGetValue(folder, out List<string> members))
                {
                    members = new List<string>();
                    groups.Add(folder, members);
                }
                members.Add(path);
            }

            return groups
                .Select(group => new DatasetFolderEntry
                {
                    RelativePath = group.Key,
                    ImageCount = group.Value.Count,
                    RepresentativeImagePath = group.Value
                        .OrderBy(path => path, new FileNamesComparer())
                        .First()
                })
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string NormalizeRelative(string relativeFolder)
        {
            return (relativeFolder ?? string.Empty)
                .Replace('\\', '/')
                .Trim('/');
        }

        private static string NormalizeAbsolute(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }
    }
}
