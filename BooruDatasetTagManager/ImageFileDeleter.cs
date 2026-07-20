using System;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Deletes an image and its tag sidecar as one unit. Both files are first
    /// moved into a per-call trash folder next to the image (moves are
    /// reversible); only when every move succeeded is the trash purged. If the
    /// tag file cannot be moved the image is restored, so a failure can never
    /// leave a half-deleted pair on disk while the UI still shows both.
    /// </summary>
    public static class ImageFileDeleter
    {
        /// <summary>
        /// Trash folder name. Starts with <see cref="TolerantFileEnumerator.InternalDirectoryPrefix"/>
        /// so the dataset loader never picks up staged leftovers as content.
        /// </summary>
        public const string TrashDirectoryName = ".bdtm-trash";

        public static bool DeleteImageWithTags(string imageFile, string tagFile, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(imageFile))
            {
                error = "Image path is empty.";
                return false;
            }
            string directory = Path.GetDirectoryName(imageFile);
            if (string.IsNullOrEmpty(directory))
                directory = ".";
            string trashDirectory = Path.Combine(directory, TrashDirectoryName, Guid.NewGuid().ToString("N"));
            string stagedImage = null;
            try
            {
                if (File.Exists(imageFile))
                {
                    Directory.CreateDirectory(trashDirectory);
                    stagedImage = Path.Combine(trashDirectory, Path.GetFileName(imageFile));
                    File.Move(imageFile, stagedImage);
                }
                if (!string.IsNullOrWhiteSpace(tagFile) && File.Exists(tagFile))
                {
                    Directory.CreateDirectory(trashDirectory);
                    File.Move(tagFile, Path.Combine(trashDirectory, Path.GetFileName(tagFile)));
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (stagedImage != null && File.Exists(stagedImage) && !File.Exists(imageFile))
                {
                    try
                    {
                        File.Move(stagedImage, imageFile);
                    }
                    catch (Exception restoreEx)
                    {
                        // The image stays recoverable inside the trash folder.
                        error += $" (restore also failed: {restoreEx.Message}; the image is in '{trashDirectory}')";
                        return false;
                    }
                }
                PurgeTrash(trashDirectory);
                return false;
            }
            // Both files are logically deleted; purging is best effort. A
            // leftover (e.g. read-only file) stays inside ".bdtm-trash", which
            // the loader skips, and remains manually recoverable.
            PurgeTrash(trashDirectory);
            return true;
        }

        private static void PurgeTrash(string trashDirectory)
        {
            try
            {
                if (Directory.Exists(trashDirectory))
                    Directory.Delete(trashDirectory, true);
                string parent = Path.GetDirectoryName(trashDirectory);
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    Directory.Delete(parent);
            }
            catch
            {
                // Leftover trash is invisible to the dataset loader.
            }
        }
    }
}
