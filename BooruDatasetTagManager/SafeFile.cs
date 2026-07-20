using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Write-to-temp + atomic-replace file helpers. A plain
    /// <see cref="File.WriteAllText(string,string)"/> truncates the destination
    /// before writing, so any failure mid-write (disk full, lock, crash) leaves
    /// the old content destroyed. These helpers never touch the destination
    /// until the new content is fully on disk.
    /// </summary>
    public static class SafeFile
    {
        // Concurrent saves of the same target (e.g. the main window and an
        // LLM/ONNX window both running SaveAll) used to share one fixed
        // "path.tmp" name and could move each other's half-written content
        // into place. A unique temp name per call plus a per-path lock keeps
        // last-writer-wins semantics intact.
        private static readonly ConcurrentDictionary<string, object> pathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static void WriteAllText(string path, string contents)
        {
            // Match File.WriteAllText's default: UTF-8 without BOM.
            WriteAllText(path, contents, new UTF8Encoding(false));
        }

        public static void WriteAllText(string path, string contents, Encoding encoding)
        {
            WriteCore(path, tmp => File.WriteAllText(tmp, contents, encoding), null);
        }

        public static void WriteAllTextWithBackup(string path, string contents)
        {
            WriteCore(path, tmp => File.WriteAllText(tmp, contents, new UTF8Encoding(false)), path + ".bak");
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            WriteCore(path, tmp => File.WriteAllBytes(tmp, bytes), null);
        }

        private static void WriteCore(string path, Action<string> writeTemp, string backupPath)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            lock (LockFor(path))
            {
                try
                {
                    writeTemp(tmp);
                    ReplaceOrMove(tmp, path, backupPath);
                }
                finally
                {
                    // A failed replace must not leave this call's temp file behind.
                    TryDelete(tmp);
                }
            }
        }

        private static object LockFor(string path)
        {
            string key;
            try
            {
                key = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                key = path;
            }
            return pathLocks.GetOrAdd(key, _ => new object());
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup only; the write outcome was already decided.
            }
        }

        /// <summary>
        /// Moves an already fully-written temp file over the destination as
        /// atomically as the filesystem allows.
        /// </summary>
        public static void ReplaceOrMove(string tempPath, string destinationPath, string backupPath)
        {
            if (File.Exists(destinationPath))
            {
                try
                {
                    File.Replace(tempPath, destinationPath, backupPath, ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Fall through to copy/move below.
                }
                catch (IOException)
                {
                    // File.Replace can fail on some filesystems (network shares,
                    // FAT). Fall back to a best-effort non-atomic swap.
                }
                if (backupPath != null)
                {
                    try { File.Copy(destinationPath, backupPath, true); } catch { }
                }
                File.Move(tempPath, destinationPath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }
        }
    }
}
