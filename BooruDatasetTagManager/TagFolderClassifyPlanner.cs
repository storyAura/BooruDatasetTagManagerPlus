using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    public sealed class TagFolderClassifyItem
    {
        public TagFolderClassifyItem(string sourcePath, IReadOnlyList<string> tags, string currentRelativeFolder)
        {
            SourcePath = sourcePath ?? string.Empty;
            Tags = tags ?? Array.Empty<string>();
            CurrentRelativeFolder = DatasetFolderIndex.NormalizeRelative(currentRelativeFolder);
        }

        public string SourcePath { get; }
        public IReadOnlyList<string> Tags { get; }
        public string CurrentRelativeFolder { get; }
    }

    public sealed class TagFolderMove
    {
        public TagFolderMove(string sourcePath, string destRelativeFolder, string destFileName)
        {
            SourcePath = sourcePath ?? string.Empty;
            DestRelativeFolder = destRelativeFolder ?? string.Empty;
            DestFileName = destFileName ?? string.Empty;
        }

        public string SourcePath { get; }
        public string DestRelativeFolder { get; }
        public string DestFileName { get; }
    }

    /// <summary>
    /// Pure plan for Tools → classify images into one user-named folder.
    /// Images that have every selected tag move there; the rest stay put.
    /// An empty / invalid name becomes <see cref="DefaultFolderName"/>.
    /// </summary>
    public static class TagFolderClassifyPlanner
    {
        public const string DefaultFolderName = "Mix";

        public static string SanitizeFolderName(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return string.Empty;
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = new char[tag.Length];
            int n = 0;
            foreach (char c in tag.Trim())
                chars[n++] = Array.IndexOf(invalid, c) >= 0 ? '_' : c;
            string name = new string(chars, 0, n).Trim().TrimEnd('.');
            if (name.Length == 0 || name == "." || name == "..")
                return string.Empty;
            return name;
        }

        public static string ResolveDestFolderName(string requested)
        {
            string name = SanitizeFolderName(requested);
            return name.Length == 0 ? DefaultFolderName : name;
        }

        /// <summary>
        /// If <c>Mix</c> already exists, the next folder is <c>Mix_2</c>, then
        /// <c>Mix_3</c>. The same suffixing applies to a custom name.
        /// </summary>
        public static string AllocateUniqueFolderName(
            string requested,
            IEnumerable<string> existingFolders)
        {
            string baseName = ResolveDestFolderName(requested);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingFolders != null)
            {
                foreach (string folder in existingFolders)
                {
                    string name = DatasetFolderIndex.NormalizeRelative(folder);
                    if (name.Length == 0 || name == DatasetFolderIndex.RootFolderKey)
                        continue;
                    taken.Add(name);
                }
            }

            if (!taken.Contains(baseName))
                return baseName;

            for (int suffix = 2; suffix < 10000; suffix++)
            {
                string candidate = baseName + "_" + suffix;
                if (!taken.Contains(candidate)
                    && DatasetFolderIndex.IsSafeRelativeFolder(candidate))
                {
                    return candidate;
                }
            }

            return baseName;
        }

        public static bool IsFolderNameFamily(string folder, string baseName)
        {
            string current = DatasetFolderIndex.NormalizeRelative(folder);
            string root = ResolveDestFolderName(baseName);
            if (current.Length == 0 || root.Length == 0)
                return false;
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                return true;
            string prefix = root + "_";
            if (!current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            string rest = current.Substring(prefix.Length);
            if (rest.Length == 0)
                return false;
            foreach (char c in rest)
            {
                if (c < '0' || c > '9')
                    return false;
            }
            return true;
        }

        public static IReadOnlyList<TagFolderMove> Plan(
            IEnumerable<TagFolderClassifyItem> images,
            IReadOnlyList<string> selectedTags,
            IEnumerable<string> occupiedDestKeys = null,
            string destFolderName = null,
            IEnumerable<string> existingFolders = null)
        {
            var selected = (selectedTags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (selected.Count == 0 || images == null)
                return Array.Empty<TagFolderMove>();

            string requested = ResolveDestFolderName(destFolderName);
            string destFolder = AllocateUniqueFolderName(requested, existingFolders);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (occupiedDestKeys != null)
            {
                foreach (string key in occupiedDestKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        reserved.Add(key.Replace('\\', '/'));
                }
            }

            var moves = new List<TagFolderMove>();
            foreach (TagFolderClassifyItem image in images)
            {
                if (image == null || string.IsNullOrWhiteSpace(image.SourcePath))
                    continue;
                if (!HasEverySelectedTag(image.Tags, selected))
                    continue;
                if (IsFolderNameFamily(image.CurrentRelativeFolder, requested))
                    continue;
                string destName = AllocateFileName(Path.GetFileName(image.SourcePath), destFolder, reserved);
                if (destName.Length == 0)
                    continue;
                moves.Add(new TagFolderMove(image.SourcePath, destFolder, destName));
            }
            return moves;
        }

        public static string DestKey(string destRelativeFolder, string destFileName)
        {
            string folder = DatasetFolderIndex.NormalizeRelative(destRelativeFolder);
            string name = Path.GetFileName(destFileName ?? string.Empty);
            return folder.Length == 0 ? name : folder + "/" + name;
        }

        private static bool HasEverySelectedTag(IReadOnlyList<string> imageTags, List<string> selected)
        {
            foreach (string tag in selected)
            {
                if (!imageTags.Contains(tag, StringComparer.Ordinal))
                    return false;
            }
            return true;
        }

        private static string AllocateFileName(string preferredName, string destFolder, HashSet<string> reserved)
        {
            string name = Path.GetFileName(preferredName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..")
                return string.Empty;
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            int suffix = 2;
            string candidate = name;
            while (reserved.Contains(DestKey(destFolder, candidate)))
            {
                candidate = stem + "_" + suffix + ext;
                suffix++;
            }
            reserved.Add(DestKey(destFolder, candidate));
            return candidate;
        }
    }
}
