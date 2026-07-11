using System;
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
        public static void WriteAllText(string path, string contents)
        {
            // Match File.WriteAllText's default: UTF-8 without BOM.
            WriteAllText(path, contents, new UTF8Encoding(false));
        }

        public static void WriteAllText(string path, string contents, Encoding encoding)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents, encoding);
            ReplaceOrMove(tmp, path, null);
        }

        public static void WriteAllTextWithBackup(string path, string contents)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents, new UTF8Encoding(false));
            ReplaceOrMove(tmp, path, path + ".bak");
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            ReplaceOrMove(tmp, path, null);
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
