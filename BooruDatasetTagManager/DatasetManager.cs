using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Translator.Crypto;
using static BooruDatasetTagManager.Extensions;

namespace BooruDatasetTagManager
{
    public class DatasetManager : IDisposable
    {
        public ConcurrentDictionary<string, DataItem> DataSet;
        public AllTagsList AllTags;
        public BindingSource AllTagsBindingSource;

        public string DatasetRoot { get; private set; } = string.Empty;

        /// <summary>
        /// Active sub-folder scope ('/'-normalized path relative to
        /// <see cref="DatasetRoot"/>). Null or empty means "all folders".
        /// Every filtered enumeration and the AllTags counts honor this scope.
        /// </summary>
        public string ActiveFolder { get; private set; }

        // Order-independent signature of the key set at the last save/accept point.
        private long originalStructureSignature;

        private bool isTranslate = false;
        private int bulkMutationDepth = 0;
        private bool translateAfterBulkMutation = false;

        private FilterType lastAndOperation = FilterType.Or;
        private IEnumerable<string> lastTagsFilter = null;

        // Capacity-bounded, thread-safe cache that disposes evicted images to
        // avoid the unbounded memory / GDI-handle growth of the old Dictionary.
        private const int ImageCacheCapacity = 256;
        private readonly ImageLruCache imagesCache;

        public event ProgressHandler LoadingProgressChanged;

        public DatasetManager()
        {
            imagesCache = new ImageLruCache(ImageCacheCapacity);
            DataSet = new ConcurrentDictionary<string, DataItem>();
            AllTags = new AllTagsList();
            AllTagsBindingSource = new BindingSource();
            AllTagsBindingSource.DataSource = AllTags;
        }

        /// <summary>
        /// Files that failed to write during the most recent <see cref="SaveAll"/>.
        /// Items that failed stay marked as modified.
        /// </summary>
        public List<string> LastSaveErrors { get; } = new List<string>();

        /// <summary>
        /// Per-directory/per-file failures from the most recent
        /// <see cref="LoadFromFolder"/>. Failed items are skipped; the rest of
        /// the dataset still loads.
        /// </summary>
        public List<string> LastLoadErrors { get; } = new List<string>();

        public bool SaveAll()
        {
            bool saved = false;
            LastSaveErrors.Clear();
            foreach (var item in DataSet)
            {
                if (item.Value.IsModified)
                {
                    try
                    {
                        item.Value.DeduplicateTags();
                        string promptText = item.Value.Tags.ToString();
                        // Atomic write: a locked/read-only/full-disk failure must not
                        // truncate the existing tag file or abort the whole batch.
                        SafeFile.WriteAllText(item.Value.TextFilePath, promptText);
                        // Reset the per-item saved snapshot so IsModified/IsDataSetChanged
                        // correctly report "unchanged" after a successful write.
                        item.Value.AcceptCurrentTagsAsSaved();
                        saved = true;
                    }
                    catch (Exception ex)
                    {
                        LastSaveErrors.Add($"{item.Value.TextFilePath}: {ex.Message}");
                        Trace.WriteLine($"DatasetManager.SaveAll: failed to write '{item.Value.TextFilePath}': {ex}");
                    }
                }
            }
            return saved;
        }

        public bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !DataSet.ContainsKey(name))
                return false;

            RemoveMany(new[] { name });
            return true;
        }

        public void RemoveMany(IEnumerable<string> paths)
        {
            if (paths == null)
                return;

            List<string> normalizedPaths = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedPaths.Count == 0)
                return;

            bool removedAny = false;
            ExecuteBulkMutation(() =>
            {
                foreach (string path in normalizedPaths)
                {
                    // Remove from the dataset first: only the thread that wins the
                    // TryRemove race may touch the item's tags and global counts,
                    // otherwise a concurrent removal would double-decrement AllTags.
                    if (!DataSet.TryRemove(path, out DataItem item))
                        continue;

                    item.Tags.TagsListChanged -= Tags_TagsListChanged;
                    bool inScope = IsInActiveScope(item.ImageFilePath);
                    List<string> tagSnapshot = item.Tags.TextTags.ToList();
                    item.Tags.ClearWithoutTagNotifications();

                    // Out-of-scope items were never counted into the scoped
                    // AllTags view, so removing them must not decrement it.
                    if (inScope)
                    {
                        foreach (string tag in tagSnapshot)
                            AllTags.RemoveTag(tag);
                    }

                    RemoveFromCache(path);
                    item.Img?.Dispose();
                    item.Img = null;
                    removedAny = true;
                }
            });

            if (removedAny)
                UpdateDatasetHash();
        }

        /// <summary>
        /// Returns an image for <paramref name="path"/>. The caller owns the
        /// returned instance and is responsible for disposing it. When caching
        /// is enabled the cache keeps its own copy (which it disposes on
        /// eviction/clear); callers receive an independent clone so a cache
        /// eviction can never dispose an image still bound to a UI control.
        /// </summary>
        public Image GetImageFromFileWithCache(string path)
        {
            if (Program.Settings.CacheOpenImages)
            {
                // Clone under the cache lock so a concurrent eviction/removal (e.g.
                // during a multi-delete) can never dispose the source mid-clone and
                // leave us returning a disposed image to a UI control.
                if (imagesCache.TryGetClone(path, out Image cachedClone))
                    return cachedClone;

                Image img = Extensions.GetImageFromFile(path);
                if (img == null)
                    return null;
                // Cache keeps its own copy; hand the freshly loaded original to the
                // caller (which owns and disposes it). If for some reason it cannot
                // be cached, the caller still gets a valid image.
                Image forCache = CloneImage(img);
                if (forCache != null)
                    imagesCache.Set(path, forCache);
                return img;
            }
            else
                return Extensions.GetImageFromFile(path);
        }

        private static Image CloneImage(Image source)
        {
            if (source == null)
                return null;
            try
            {
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        public void ClearCache()
        {
            imagesCache.Clear();
        }

        /// <summary>
        /// Deterministically releases every thumbnail bitmap and the image cache.
        /// Without this, replacing the manager leaves thousands of GDI+ bitmaps
        /// waiting on finalizers and can exhaust the process GDI handle limit.
        /// </summary>
        public void Dispose()
        {
            foreach (var item in DataSet)
            {
                item.Value.Tags.TagsListChanged -= Tags_TagsListChanged;
                item.Value.Img?.Dispose();
                item.Value.Img = null;
            }
            DataSet.Clear();
            imagesCache.Clear();
        }

        public void RemoveFromCache(string path)
        {
            imagesCache.Remove(path);
        }

        private IEnumerable<DataItem> GetEnumerator(bool useFilter)
        {
            IEnumerable<DataItem> lst = null;
            if (useFilter)
            {
                lst = FilterLogic(lastAndOperation, lastTagsFilter);
            }
            else
                lst = ScopedItems();
            return lst;
        }

        /// <summary>
        /// True when the image belongs to the active folder scope (always true
        /// while no folder is selected).
        /// </summary>
        public bool IsInActiveScope(string imagePath)
        {
            return string.IsNullOrEmpty(ActiveFolder)
                || DatasetFolderIndex.IsInFolder(imagePath, DatasetRoot, ActiveFolder);
        }

        private IEnumerable<DataItem> ScopedItems()
        {
            return DataSet.Values.Where(item => IsInActiveScope(item.ImageFilePath));
        }

        /// <summary>
        /// Folder gallery entries for the loaded dataset, grouped by directory
        /// relative to <see cref="DatasetRoot"/>.
        /// </summary>
        public IReadOnlyList<DatasetFolderEntry> GetFolderEntries()
        {
            return DatasetFolderIndex.Create(DataSet.Keys, DatasetRoot);
        }

        /// <summary>Number of images inside the active folder scope.</summary>
        public int GetActiveScopeCount()
        {
            return string.IsNullOrEmpty(ActiveFolder) ? DataSet.Count : ScopedItems().Count();
        }

        /// <summary>Items inside the active folder scope (audit tools use this
        /// so a selected folder bounds their inventory and gallery).</summary>
        public List<DataItem> GetScopedItems()
        {
            return ScopedItems().ToList();
        }

        /// <summary>
        /// Switches the active folder scope (null/empty selects every folder)
        /// and rebuilds the AllTags counts so the tag pane reflects only the
        /// scoped images.
        /// </summary>
        public void SetActiveFolder(string relativeFolder)
        {
            string normalized = DatasetFolderIndex.NormalizeRelative(relativeFolder);
            ActiveFolder = normalized.Length == 0 ? null : normalized;
            RebuildAllTagsForScope();
        }

        /// <summary>
        /// Renames the leaf directory of <paramref name="relativeFolder"/> on
        /// disk (captions move with it) and remaps every affected in-memory
        /// path — dataset keys, item image/caption paths, tag-sync owner
        /// paths, the image cache and the active scope — so unsaved tag edits
        /// survive without a reload. Returns the new relative folder. Throws
        /// ArgumentException for an invalid/conflicting name and IO exceptions
        /// for filesystem failures; nothing is remapped unless the disk move
        /// succeeded.
        /// </summary>
        public string RenameFolder(string relativeFolder, string newLeafName)
        {
            string normalized = DatasetFolderIndex.NormalizeRelative(relativeFolder);
            if (normalized.Length == 0 || string.IsNullOrEmpty(DatasetRoot))
                throw new ArgumentException("No folder selected.", nameof(relativeFolder));
            newLeafName = (newLeafName ?? string.Empty).Trim();
            if (newLeafName.Length == 0
                || newLeafName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || newLeafName == "." || newLeafName == "..")
            {
                throw new ArgumentException("Invalid folder name.", nameof(newLeafName));
            }
            int slash = normalized.LastIndexOf('/');
            string parent = slash < 0 ? string.Empty : normalized.Substring(0, slash);
            string newRelative = parent.Length == 0 ? newLeafName : parent + "/" + newLeafName;
            if (string.Equals(newRelative, normalized, StringComparison.Ordinal))
                return normalized;

            string oldAbsolute = Path.GetFullPath(Path.Combine(
                DatasetRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string newAbsolute = Path.GetFullPath(Path.Combine(
                DatasetRoot, newRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Directory.Exists(oldAbsolute))
                throw new DirectoryNotFoundException(oldAbsolute);
            bool caseOnlyRename = string.Equals(oldAbsolute, newAbsolute, StringComparison.OrdinalIgnoreCase);
            if (!caseOnlyRename && (Directory.Exists(newAbsolute) || File.Exists(newAbsolute)))
                throw new ArgumentException("A folder with this name already exists.", nameof(newLeafName));

            bool structureWasClean = ComputeStructureSignature() == originalStructureSignature;
            Directory.Move(oldAbsolute, newAbsolute);

            string oldPrefix = oldAbsolute + Path.DirectorySeparatorChar;
            string newPrefix = newAbsolute + Path.DirectorySeparatorChar;
            foreach (KeyValuePair<string, DataItem> pair in DataSet.ToList())
            {
                if (!pair.Key.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                DataItem item = pair.Value;
                string newPath = newPrefix + pair.Key.Substring(oldPrefix.Length);
                DataSet.TryRemove(pair.Key, out _);
                imagesCache.Remove(pair.Key);
                item.ImageFilePath = newPath;
                item.ImageFilePathHash = newPath.GetHashCode();
                if (!string.IsNullOrEmpty(item.TextFilePath)
                    && item.TextFilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    item.TextFilePath = newPrefix + item.TextFilePath.Substring(oldPrefix.Length);
                }
                item.Tags.OwnerImagePath = newPath;
                DataSet[newPath] = item;
            }

            if (!string.IsNullOrEmpty(ActiveFolder))
            {
                string active = DatasetFolderIndex.NormalizeRelative(ActiveFolder);
                if (string.Equals(active, normalized, StringComparison.OrdinalIgnoreCase))
                    ActiveFolder = newRelative;
                else if (active.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
                    ActiveFolder = newRelative + active.Substring(normalized.Length);
            }

            // The rename changed every key, but nothing needs saving because
            // captions moved with their images: keep a clean dataset clean.
            if (structureWasClean)
                originalStructureSignature = ComputeStructureSignature();
            return newRelative;
        }

        /// <summary>
        /// Batch-renames dataset images inside their directories (captions
        /// follow) and remaps all in-memory state. Two-phase via temp names,
        /// so swapping name sets (1→2 while 2→1) cannot collide. Every target
        /// is validated before anything moves: invalid names, duplicate
        /// targets, or collisions with files outside the rename set throw
        /// ArgumentException up front. Returns the number of renamed images.
        /// </summary>
        public int RenameImages(IReadOnlyList<KeyValuePair<string, string>> renames)
        {
            if (renames == null)
                throw new ArgumentNullException(nameof(renames));

            var plan = new List<(DataItem Item, string OldImage, string NewImage, string OldText, string NewText)>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> rename in renames)
            {
                if (!DataSet.TryGetValue(rename.Key, out DataItem item))
                    continue;
                string newBase = (rename.Value ?? string.Empty).Trim();
                if (newBase.Length == 0 || !BatchRenamePlanner.IsValidNamePart(newBase))
                    throw new ArgumentException("Invalid file name: " + rename.Value);
                string directory = Path.GetDirectoryName(item.ImageFilePath) ?? string.Empty;
                string newImage = Path.Combine(directory, newBase + Path.GetExtension(item.ImageFilePath));
                string oldText = !string.IsNullOrEmpty(item.TextFilePath) && File.Exists(item.TextFilePath)
                    ? item.TextFilePath
                    : null;
                string newText = oldText == null
                    ? null
                    : Path.Combine(directory, newBase + Path.GetExtension(oldText));
                if (string.Equals(newImage, item.ImageFilePath, StringComparison.Ordinal))
                    continue;
                if (!targets.Add(newImage))
                    throw new ArgumentException("Duplicate target name: " + newBase);
                sources.Add(item.ImageFilePath);
                if (oldText != null)
                    sources.Add(oldText);
                plan.Add((item, item.ImageFilePath, newImage, oldText, newText));
            }
            if (plan.Count == 0)
                return 0;

            foreach ((DataItem _, string oldImage, string newImage, string oldText, string newText) in plan)
            {
                bool caseOnly = string.Equals(oldImage, newImage, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && File.Exists(newImage) && !sources.Contains(newImage))
                    throw new ArgumentException("Target already exists: " + Path.GetFileName(newImage));
                if (newText != null && !string.Equals(oldText, newText, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(newText) && !sources.Contains(newText))
                {
                    throw new ArgumentException("Target already exists: " + Path.GetFileName(newText));
                }
                // File.Exists is false for directories, so a folder named like a
                // target slipped past the checks above and failed mid-move.
                if (Directory.Exists(newImage))
                    throw new ArgumentException("Target already exists: " + Path.GetFileName(newImage));
                if (newText != null && Directory.Exists(newText))
                    throw new ArgumentException("Target already exists: " + Path.GetFileName(newText));
            }

            bool structureWasClean = ComputeStructureSignature() == originalStructureSignature;

            // Phase 1: park every source under a unique temp name (same dir);
            // roll straight back if anything fails here.
            var parked = new List<(string Temp, string Original)>();
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    string tempImage = TempRenamePath(plan[i].OldImage, i);
                    File.Move(plan[i].OldImage, tempImage);
                    parked.Add((tempImage, plan[i].OldImage));
                    if (plan[i].OldText != null)
                    {
                        string tempText = TempRenamePath(plan[i].OldText, i);
                        File.Move(plan[i].OldText, tempText);
                        parked.Add((tempText, plan[i].OldText));
                    }
                }
            }
            catch (Exception)
            {
                for (int i = parked.Count - 1; i >= 0; i--)
                {
                    try { File.Move(parked[i].Temp, parked[i].Original); } catch { }
                }
                throw;
            }

            // Phase 2: temp → final.
            var failures = new List<string>();
            var succeeded = new List<(DataItem Item, string OldImage, string NewImage, string NewText)>();
            for (int i = 0; i < plan.Count; i++)
            {
                (DataItem item, string oldImage, string newImage, string oldText, string newText) = plan[i];
                bool imageMoved = false;
                try
                {
                    File.Move(TempRenamePath(oldImage, i), newImage);
                    imageMoved = true;
                    if (oldText != null)
                        File.Move(TempRenamePath(oldText, i), newText);
                }
                catch (Exception ex)
                {
                    // After a caption failure the image already sits at the final
                    // path, not its temp name — roll it back from where it is.
                    try { File.Move(imageMoved ? newImage : TempRenamePath(oldImage, i), oldImage); } catch { }
                    if (oldText != null)
                    {
                        try { File.Move(TempRenamePath(oldText, i), oldText); } catch { }
                    }
                    failures.Add(Path.GetFileName(oldImage) + ": " + ex.Message);
                    continue;
                }
                succeeded.Add((item, oldImage, newImage, newText));
            }

            // Remap memory in two passes: with swapped name sets one item's
            // old key IS another's new key, so interleaving remove/insert
            // per item would delete freshly inserted entries.
            foreach ((DataItem _, string oldImage, string _, string _) in succeeded)
            {
                DataSet.TryRemove(oldImage, out _);
                imagesCache.Remove(oldImage);
            }
            foreach ((DataItem item, string _, string newImage, string newText) in succeeded)
            {
                item.ImageFilePath = newImage;
                item.ImageFilePathHash = newImage.GetHashCode();
                item.Name = Path.GetFileNameWithoutExtension(newImage);
                if (newText != null)
                {
                    item.TextFilePath = newText;
                }
                else if (!string.IsNullOrEmpty(item.TextFilePath))
                {
                    // No caption on disk yet: retarget the future save path too,
                    // or a later SaveAll resurrects the old base name.
                    item.TextFilePath = Path.Combine(
                        Path.GetDirectoryName(newImage) ?? string.Empty,
                        Path.GetFileNameWithoutExtension(newImage) + Path.GetExtension(item.TextFilePath));
                }
                item.Tags.OwnerImagePath = newImage;
                DataSet[newImage] = item;
            }
            int renamedCount = succeeded.Count;

            if (structureWasClean)
                originalStructureSignature = ComputeStructureSignature();
            if (failures.Count > 0)
                throw new IOException(string.Join("\n", failures));
            return renamedCount;
        }

        /// <summary>
        /// Phase-1 parking name: prefix the FILE NAME and keep the extension.
        /// Appending a suffix after the extension (1.png → 1.png.tmp0) looks
        /// like mass encryption to antivirus behavior monitors — a real AV
        /// hit killed the test host mid-run over exactly that pattern.
        /// </summary>
        private static string TempRenamePath(string path, int index)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            return Path.Combine(directory, "~bdtmren" + index + "_" + Path.GetFileName(path));
        }

        private void RebuildAllTagsForScope()
        {
            ExecuteBulkMutation(() =>
            {
                AllTags.ResetAllTags();
                foreach (DataItem item in ScopedItems())
                {
                    foreach (string tag in item.Tags.TextTags)
                        AllTags.AddTag(tag);
                }
            });
            if (isTranslate)
                _ = AllTags.TranslateAllTags();
        }

        public void AddTagToAll(string tag, bool skipExist, AddingType addType, int pos=-1, bool useFilter = false)
        {
            var items = GetEnumerator(useFilter).ToList();
            ExecuteBulkMutation(() =>
            {
                foreach (var item in items)
                    item.Tags.AddTag(tag, skipExist, addType, pos);
            });
        }

        public void SetTagListToAll(List<string> tags, bool onlyEmpty)
        {
            foreach (var item in ScopedItems())
            {
                if (onlyEmpty)
                {
                    if (item.Tags.Count == 0)
                    {
                        item.Tags.AddRange(tags, true);
                    }
                }
                else
                {
                    item.Tags.Clear();
                    item.Tags.AddRange(tags, true);
                }
            }
        }

        public void SetTagListToAll(EditableTagList tagList, bool onlyEmpty)
        {
            foreach (var item in ScopedItems())
            {
                if (!onlyEmpty || item.Tags.Count == 0)
                {
                    item.Tags = (EditableTagList)tagList.Clone();
                    item.Tags.OwnerImagePath = item.ImageFilePath;
                }
            }
        }

        public List<DataItem> GetDataSourceWithLastFilter(OrderType orderBy = OrderType.Name)
        {
            return GetDataSource(orderBy, lastAndOperation, lastTagsFilter);
        }

        /// <summary>
        /// Retrieves a list of DataItem objects, filtered and ordered based on the given parameters.
        /// </summary>
        /// <param name="orderBy">An optional parameter that specifies how the resulting list should be sorted.</param>
        /// <param name="andOp">An optional parameter that determines the logical operation to be used when filtering by tags.</param>
        /// <param name="filterByTags">An optional parameter that contains a list of tags to filter the data items by.</param>
        /// <returns>A filtered and ordered list of DataItem objects.</returns>
        public List<DataItem> GetDataSource(OrderType orderBy = OrderType.Name, FilterType andOp = FilterType.Or, IEnumerable<string> filterByTags = null)
        {
            // Store the last set of tags used for filtering. FilterLogic will use this value unless passed custom one
            lastTagsFilter = filterByTags;

            // Declare a list to store the filtered and ordered DataItem objects.
            List<DataItem> items = FilterLogic(andOp);

            // Sort the data items based on the orderBy parameter.
            switch (orderBy)
            {
                case OrderType.Name:
                    {
                        // Sort data items by their Name property using a custom string comparison method.
                        items.Sort((a, b) => FileNamesComparer.StrCmpLogicalW(a.Name, b.Name));
                        break;
                    }
                case OrderType.ImageModifyTime:
                    {
                        // Sort data items by their ImageModifyTime property.
                        items.Sort((a, b) => a.ImageModifyTime.CompareTo(b.ImageModifyTime));
                        break;
                    }
                case OrderType.TagsModifyTime:
                    {
                        // Sort data items by their TagsModifyTime property.
                        items.Sort((a, b) => a.TagsModifyTime.CompareTo(b.TagsModifyTime));
                        break;
                    }
            }
            // Return the filtered and sorted list of DataItem objects.
            return items;
        }

        public List<DataItem> FilterLogic(FilterType andOp = FilterType.Or, IEnumerable<string> filterByTags = null)
        {
            List<DataItem> items = null;
            if (filterByTags != null)
                lastTagsFilter = filterByTags;
            // Check if there are tags to filter by.
            if (lastTagsFilter != null)
            {
                switch (andOp)
                {
                    case FilterType.And:
                        // If the logical operation is AND, filter the data items by requiring all tags to be present.
                        items = ScopedItems().Where(a => lastTagsFilter.All(t => a.Tags.Contains(t))).ToList();
                        break;
                    case FilterType.Or:
                        // If the logical operation is OR, filter the data items by requiring at least one tag to be present.
                        items = ScopedItems().Where(a => lastTagsFilter.Any(t => a.Tags.Contains(t))).ToList();
                        break;
                    case FilterType.Not:
                        // If the logical operation is NOT, filter the data items by requiring none of the tags to be present.
                        items = ScopedItems().Where(a => lastTagsFilter.All(t => !a.Tags.Contains(t))).ToList();
                        break;
                    case FilterType.Xor:
                        // If the logical operation is XOR, filter the data items by requiring exactly one tag to be present.
                        items = ScopedItems().Where(a => lastTagsFilter.Count(t => a.Tags.Contains(t)) == 1).ToList();
                        break;
                    default:
                        throw new ArgumentException($"Invalid filter type: {andOp}");
                }
                // Store the last logical operation used for filtering, moved here so it is only updated if we actually perform the operation
                lastAndOperation = andOp;
            }
            // If there are no tags to filter by, return all data items in scope.
            else
                items = ScopedItems().ToList();

            return items;
        }

        public void DeleteTagFromAll(string tag, bool useFilter = false)
        {
            DeleteTagsFromAll(new[] { tag }, useFilter);
        }

        public void DeleteTagsFromAll(IEnumerable<string> tags, bool useFilter = false)
        {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));

            var tagsToDelete = new HashSet<string>(
                tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.ToLowerInvariant().Trim()),
                StringComparer.Ordinal);

            if (tagsToDelete.Count == 0)
                return;

            var items = GetEnumerator(useFilter).ToList();
            ExecuteBulkMutation(() =>
            {
                foreach (var item in items)
                    item.Tags.RemoveTags(tagsToDelete, true);
            });
        }



        public void ReplaceTagInAll(string srcTag, string dstTag, bool useFilter = false)
        {
            srcTag = srcTag.ToLower();
            dstTag = dstTag.ToLower();
            var items = GetEnumerator(useFilter).ToList();
            ExecuteBulkMutation(() =>
            {
                foreach (var item in items)
                    item.Tags.ReplaceTag(srcTag, dstTag);
            });
        }

        public void ReplaceTagsInAll(IEnumerable<string> srcTags, string dstTag, bool useFilter = false)
        {
            if (srcTags == null)
                throw new ArgumentNullException(nameof(srcTags));

            var normalizedSourceTags = srcTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.ToLowerInvariant().Trim())
                .Distinct()
                .ToList();

            if (normalizedSourceTags.Count == 0 || string.IsNullOrWhiteSpace(dstTag))
                return;

            dstTag = dstTag.ToLowerInvariant().Trim();
            var items = GetEnumerator(useFilter).ToList();
            ExecuteBulkMutation(() =>
            {
                foreach (var item in items)
                {
                    foreach (string srcTag in normalizedSourceTags)
                        item.Tags.ReplaceTag(srcTag, dstTag);
                }
            });
        }

        internal void ExecuteBulkMutation(Action mutation)
        {
            bool isOuterMutation = bulkMutationDepth == 0;
            IDisposable allTagsBatch = isOuterMutation ? AllTags.BeginBatchUpdate() : null;
            bulkMutationDepth++;
            try
            {
                mutation();
            }
            finally
            {
                bulkMutationDepth--;
                allTagsBatch?.Dispose();
                if (isOuterMutation && translateAfterBulkMutation)
                {
                    translateAfterBulkMutation = false;
                    _ = AllTags.TranslateAllTags();
                }
            }
        }

        private List<TagValue> GetTagsForDel(List<TagValue> checkedList, List<string> srcList)
        {
            List<TagValue> delList = new List<TagValue>();
            foreach (var item in checkedList)
            {
                if (!srcList.Contains(item.Tag))
                    delList.Add(item);
            }
            return delList;
        }

        public async Task<bool> LoadFromFolderAsync(string folder, bool loadPreviewImages, bool readMetadata)
        {
            return await Task.Run(() => LoadFromFolder(folder, loadPreviewImages, readMetadata));
        }

        public bool LoadFromFolder(string folder, bool loadPreviewImages, bool readMetadata)
        {
            LastLoadErrors.Clear();
            // A fresh load always starts unscoped; the previous dataset's folder
            // selection must not silently filter the new one.
            ActiveFolder = null;
            CharacterTagFileTransaction.RecoverIncompleteAsync(folder).GetAwaiter().GetResult();
            List<string> allowedExt = new List<string>();
            allowedExt.AddRange(Extensions.ImageExtensions);
            allowedExt.AddRange(Extensions.VideoExtensions);
            // Tolerant walk: one protected/vanished subdirectory must not abort
            // the whole load; its failure is collected for the caller instead.
            string[] imgs = TolerantFileEnumerator.GetFiles(folder, LastLoadErrors).ToArray();
            if (imgs.Length == 0)
            {
                return false;
            }
            imgs = imgs.Where(a => allowedExt.Contains(Path.GetExtension(a).ToLower())).OrderBy(a => a, new FileNamesComparer()).ToArray();
            if (imgs.Length == 0)
                return false;
            int imgSize = Program.Settings.PreviewSize;
            int progress = 0;
            imgs.AsParallel()
                // Half the cores: full-width parallel decode of large images
                // spikes memory (each worker holds a full-resolution frame) and
                // starves the UI thread.
                .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount / 2))
                .ForAll(x =>
            {
                try
                {
                    var dt = new DataItem();
                    dt.Tags.TagsListChanged += Tags_TagsListChanged;
                    dt.LoadData(x, loadPreviewImages ? imgSize : 0, readMetadata);
                    DataSet.TryAdd(dt.ImageFilePath, dt);
                }
                catch (Exception ex)
                {
                    // A single locked tag file or unreadable image previously
                    // failed the entire PLINQ query as an AggregateException.
                    lock (LastLoadErrors)
                    {
                        LastLoadErrors.Add($"{x}: {ex.Message}");
                    }
                }
                // Atomic increment instead of serializing the whole parallel body
                // behind a SemaphoreSlim (which defeated the parallelism).
                int current = Interlocked.Increment(ref progress);
                // Throttle: one event per image was a cross-thread UI storm.
                if (current % 32 == 0 || current == imgs.Length)
                    LoadingProgressChanged?.Invoke(current, imgs.Length);
            });
            if (DataSet.IsEmpty)
                return false;
            DatasetRoot = Path.GetFullPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            UpdateDatasetHash();
            AllTagsBindingSource.Sort = "Tag ASC";
            return true;
        }

        public IReadOnlyList<string> AddImages(IEnumerable<string> paths, bool loadPreviewImages, bool readMetadata)
        {
            if (paths == null)
                return Array.Empty<string>();

            int imgSize = loadPreviewImages ? Program.Settings.PreviewSize : 0;
            var added = new List<string>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (!Extensions.ImageExtensions.Contains(extension) && !Extensions.VideoExtensions.Contains(extension))
                    continue;

                if (DataSet.ContainsKey(path))
                    continue;

                var dataItem = new DataItem();
                dataItem.Tags.TagsListChanged += Tags_TagsListChanged;
                dataItem.LoadData(path, imgSize, readMetadata);
                if (DataSet.TryAdd(dataItem.ImageFilePath, dataItem))
                    added.Add(dataItem.ImageFilePath);
            }

            if (added.Count > 0)
                UpdateDatasetHash();

            return added;
        }

        public void SetTranslationMode(bool needTranslate)
        {
            isTranslate = needTranslate;
        }

        private void Tags_TagsListChanged(object sender, string oldTag, string newTag, ListChangedType changedType)
        {
            // While a folder scope is active, tag edits on out-of-scope items
            // (e.g. images added in another folder) must not mutate the scoped
            // AllTags counts; the view is rebuilt on every scope switch.
            if (sender is EditableTagList ownedList
                && !string.IsNullOrEmpty(ownedList.OwnerImagePath)
                && !IsInActiveScope(ownedList.OwnerImagePath))
            {
                return;
            }
            lock (Program.ListChangeLocker)
            {
                if (changedType == ListChangedType.ItemAdded)
                {
                    AllTags.AddTag(newTag);
                    if (isTranslate)
                    {
                        if (bulkMutationDepth > 0)
                            translateAfterBulkMutation = true;
                        else
                            _ = AllTags.TranslateAllTags();
                    }
                }
                else if (changedType == ListChangedType.ItemDeleted)
                {
                    AllTags.RemoveTag(newTag);
                }
                else if (changedType == ListChangedType.ItemChanged)
                {
                    AllTags.ChangeTag(oldTag, newTag);
                    if (isTranslate)
                    {
                        if (bulkMutationDepth > 0)
                            translateAfterBulkMutation = true;
                        else
                            _ = AllTags.TranslateAllTags();
                    }
                }
                else
                    throw new Exception("Unknown list changing operation");
                if (AllTags.IsFilterByCount() && bulkMutationDepth == 0)
                    AllTags.UpdateFilter();
            }
        }

        /// <summary>
        /// Returns true if the dataset has unsaved changes. A change is either:
        /// (a) a structural change since the last <see cref="UpdateDatasetHash"/>
        /// (items added/removed), detected via an order-independent signature so
        /// it is not affected by <see cref="ConcurrentDictionary{TKey,TValue}"/>
        /// enumeration order (fixes the previous order-sensitive false positives);
        /// or (b) any individual item reporting <see cref="DataItem.IsModified"/>,
        /// which now uses exact tag-text comparison instead of a collision-prone
        /// 32-bit hash.
        /// </summary>
        public bool IsDataSetChanged()
        {
            if (ComputeStructureSignature() != originalStructureSignature)
                return true;
            foreach (var item in DataSet)
            {
                if (item.Value.IsModified)
                    return true;
            }
            return false;
        }

        public void UpdateDatasetHash()
        {
            originalStructureSignature = ComputeStructureSignature();
        }

        /// <summary>
        /// Order-independent signature of the current key set (count + XOR of key
        /// hashes). Detects additions/removals without depending on enumeration
        /// order.
        /// </summary>
        private long ComputeStructureSignature()
        {
            long xor = 0;
            int count = 0;
            foreach (var item in DataSet)
            {
                xor ^= item.Key.GetHash();
                count++;
            }
            unchecked
            {
                return xor * 31 + count;
            }
        }

        public enum AddingType
        {
            Top,
            Center,
            Down,
            Custom
        }

        public enum OrderType
        {
            Name,
            ImageModifyTime,
            TagsModifyTime
        }

        public class DataItem : IDisposable
        {
            [JsonIgnore()]
            [DisplayName("Image")]
            public Image Img { get; set; }
            public string Name { get; set; }
            [Browsable(false)]
            public EditableTagList Tags { get; set; }
            [Browsable(false)]
            public string TextFilePath { get; set; }
            //[Browsable(false)]
            public string ImageFilePath { get; set; }
            [Browsable(false)]
            public int ImageFilePathHash { get; set; }

            public DateTime ImageModifyTime { get; set; }
            public DateTime TagsModifyTime { get; set; }
            [Browsable(false)]
            public bool IsModified
            {
                get
                {
                    // Exact text comparison instead of a 32-bit hash: avoids the
                    // (unlikely but real) hash-collision that could silently drop
                    // unsaved edits (#17).
                    return !string.Equals(savedTagsSnapshot, Tags.ToString(), StringComparison.Ordinal);
                }
            }

            // Snapshot of the tag text at the last load/save/accept point.
            private string savedTagsSnapshot = string.Empty;

            public DataItem()
            {
                Tags = new EditableTagList();
            }

            public void LoadData(string imagePath, int imageSize, bool readMetadata)
            {
                ImageFilePath = imagePath;
                Tags.OwnerImagePath = imagePath;
                ImageFilePathHash = ImageFilePath.GetHashCode();
                Name = Path.GetFileNameWithoutExtension(imagePath);
                ImageModifyTime = File.GetLastWriteTime(imagePath);
                foreach (var item in Program.Settings.GetTagFilesExtensions())
                {
                    TextFilePath = Path.Combine(Path.GetDirectoryName(imagePath), Name + "." + item);
                    if (File.Exists(TextFilePath))
                        break;
                    else
                        TextFilePath = string.Empty;
                }
                if (string.IsNullOrEmpty(TextFilePath))
                    TextFilePath = Path.Combine(Path.GetDirectoryName(imagePath), Name + "." + Program.Settings.DefaultTagsFileExtension);
                GetTagsFromFile(readMetadata);
                if (imageSize > 0)
                    Img = Extensions.MakeThumb(imagePath, imageSize);
            }

            public void DeduplicateTags()
            {
                Tags.DeduplicateTags();
            }

            public void AcceptCurrentTagsAsSaved()
            {
                TagsModifyTime = File.Exists(TextFilePath) ? File.GetLastWriteTime(TextFilePath) : DateTime.MinValue;
                savedTagsSnapshot = Tags.ToString();
            }



            public void GetTagsFromFile(bool readMetadata)
            {
                if (File.Exists(TextFilePath))
                {
                    TagsModifyTime = File.GetLastWriteTime(TextFilePath);
                    string text = File.ReadAllText(TextFilePath);

                    var temp_tags = PromptParser.ParsePrompt(text, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                    Tags.LoadFromPromptParserData(temp_tags);
                }
                else
                {
                    if (readMetadata)
                    {
                        var metadata = Diffusion.IO.Metadata.ReadFromFile(ImageFilePath);
                        if (!string.IsNullOrEmpty(metadata.Prompt))
                        {
                            var temp_tags = PromptParser.ParsePrompt(metadata.Prompt, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                            Tags.LoadFromPromptParserData(temp_tags);
                        }
                    }
                    TagsModifyTime = DateTime.MinValue;
                }

                savedTagsSnapshot = Tags.ToString();
            }

            public override string ToString()
            {
                return Tags.ToString();
            }

            public override int GetHashCode()
            {
                return ToString().GetHashCode();
            }

            public bool Equals(DataItem obj)
            {
                return obj.ImageFilePathHash == ImageFilePathHash;
            }

            public void Dispose()
            {
                Img?.Dispose();
                Tags.Clear();
            }
        }
    }


}
