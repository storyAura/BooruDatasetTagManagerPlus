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
                    List<string> tagSnapshot = item.Tags.TextTags.ToList();
                    item.Tags.ClearWithoutTagNotifications();

                    foreach (string tag in tagSnapshot)
                        AllTags.RemoveTag(tag);

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
                lst = DataSet.Select(a => a.Value);
            return lst;
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
            foreach (var item in DataSet)
            {
                if (onlyEmpty)
                {
                    if (item.Value.Tags.Count == 0)
                    {
                        item.Value.Tags.AddRange(tags, true);
                    }
                }
                else
                {
                    item.Value.Tags.Clear();
                    item.Value.Tags.AddRange(tags, true);
                }
            }
        }

        public void SetTagListToAll(EditableTagList tagList, bool onlyEmpty)
        {
            foreach (var item in DataSet)
            {
                if (onlyEmpty)
                {
                    if (item.Value.Tags.Count == 0)
                    {
                        item.Value.Tags = (EditableTagList)tagList.Clone();
                    }
                }
                else
                {
                    item.Value.Tags = (EditableTagList)tagList.Clone();
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
                        items = DataSet.Values.Where(a => lastTagsFilter.All(t => a.Tags.Contains(t))).ToList();
                        break;
                    case FilterType.Or:
                        // If the logical operation is OR, filter the data items by requiring at least one tag to be present.
                        items = DataSet.Values.Where(a => lastTagsFilter.Any(t => a.Tags.Contains(t))).ToList();
                        break;
                    case FilterType.Not:
                        // If the logical operation is NOT, filter the data items by requiring none of the tags to be present.
                        items = DataSet.Values.Where(a => lastTagsFilter.All(t => !a.Tags.Contains(t))).ToList();
                        break;
                    case FilterType.Xor:
                        // If the logical operation is XOR, filter the data items by requiring exactly one tag to be present.
                        items = DataSet.Values.Where(a => lastTagsFilter.Count(t => a.Tags.Contains(t)) == 1).ToList();
                        break;
                    default:
                        throw new ArgumentException($"Invalid filter type: {andOp}");
                }
                // Store the last logical operation used for filtering, moved here so it is only updated if we actually perform the operation
                lastAndOperation = andOp;
            }
            // If there are no tags to filter by, return all data items.
            else
                items = DataSet.Values.ToList();

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
            CharacterTagFileTransaction.RecoverIncompleteAsync(folder).GetAwaiter().GetResult();
            List<string> allowedExt = new List<string>();
            allowedExt.AddRange(Extensions.ImageExtensions);
            allowedExt.AddRange(Extensions.VideoExtensions);
            string[] imgs = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
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
                var dt = new DataItem();
                dt.Tags.TagsListChanged += Tags_TagsListChanged;
                dt.LoadData(x, loadPreviewImages ? imgSize : 0, readMetadata);
                DataSet.TryAdd(dt.ImageFilePath, dt);
                // Atomic increment instead of serializing the whole parallel body
                // behind a SemaphoreSlim (which defeated the parallelism).
                int current = Interlocked.Increment(ref progress);
                // Throttle: one event per image was a cross-thread UI storm.
                if (current % 32 == 0 || current == imgs.Length)
                    LoadingProgressChanged?.Invoke(current, imgs.Length);
            });
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
            //EditableTagList eTagList = (EditableTagList)sender;
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
