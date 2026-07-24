using System;
using System.Collections.Generic;
using System.IO;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Recursive file listing that isolates unreadable subdirectories instead
    /// of aborting the whole walk. <see cref="Directory.GetFiles(string, string, SearchOption)"/>
    /// with <see cref="SearchOption.AllDirectories"/> throws on the first
    /// protected/vanished directory and loses everything already found.
    /// </summary>
    public static class TolerantFileEnumerator
    {
        /// <summary>
        /// Prefix of app-internal folders (transactions, trash, quarantine)
        /// that must never be treated as dataset content.
        /// </summary>
        public const string InternalDirectoryPrefix = ".bdtm-";

        /// <summary>
        /// Lists all files under <paramref name="root"/>. Directories that fail
        /// to enumerate are reported into <paramref name="errors"/> and skipped;
        /// subdirectories whose name starts with <see cref="InternalDirectoryPrefix"/>
        /// are ignored entirely.
        /// </summary>
        public static List<string> GetFiles(string root, ICollection<string> errors)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                try
                {
                    files.AddRange(Directory.GetFiles(directory));
                    foreach (string subDirectory in Directory.GetDirectories(directory))
                    {
                        string name = Path.GetFileName(subDirectory);
                        if (name.StartsWith(InternalDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Don't follow junctions/symlinks: a reparse point can aim
                        // outside the dataset root, so recursing into it would pull
                        // in (and later let writes/deletes touch) unrelated files.
                        if ((File.GetAttributes(subDirectory) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                            continue;
                        pending.Push(subDirectory);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    errors?.Add($"{directory}: {ex.Message}");
                }
            }
            return files;
        }
    }
}
