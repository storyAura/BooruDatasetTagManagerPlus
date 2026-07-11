using System;
using System.Collections.Generic;
using System.Drawing;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Thread-safe, capacity-bounded LRU cache for <see cref="Image"/> instances.
    /// Evicted, removed and cleared images are disposed so GDI handles are not
    /// leaked (previous implementation used an unbounded Dictionary that never
    /// released anything).
    /// </summary>
    public sealed class ImageLruCache : IDisposable
    {
        private readonly object sync = new object();
        private readonly int capacity;

        // Maps key -> node in the usage list. The list is ordered most-recently-used
        // at the front, least-recently-used at the back.
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> map;
        private readonly LinkedList<CacheEntry> usage = new LinkedList<CacheEntry>();

        public ImageLruCache(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
            map = new Dictionary<string, LinkedListNode<CacheEntry>>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Returns an independent clone of the cached image for <paramref name="key"/>,
        /// produced while holding the lock so a concurrent eviction/removal can never
        /// dispose the source mid-clone. Never hands out the shared cached instance,
        /// so callers can freely bind the result to a control and dispose it later.
        /// </summary>
        public bool TryGetClone(string key, out Image clone)
        {
            clone = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (sync)
            {
                if (!map.TryGetValue(key, out var node))
                    return false;

                clone = CloneUnderLock(node.Value.Image);
                if (clone == null)
                    return false;

                // Promote to most-recently-used only when the clone succeeded.
                usage.Remove(node);
                usage.AddFirst(node);
                return true;
            }
        }

        private static Image CloneUnderLock(Image source)
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

        /// <summary>
        /// Stores <paramref name="image"/> under <paramref name="key"/>. If the key
        /// already exists with a different image, the old image is disposed. Evicts
        /// (and disposes) the least-recently-used entries beyond capacity.
        /// </summary>
        public void Set(string key, Image image)
        {
            if (string.IsNullOrEmpty(key) || image == null)
                return;

            Image toDispose = null;
            List<Image> evicted = null;
            lock (sync)
            {
                if (map.TryGetValue(key, out var existing))
                {
                    if (!ReferenceEquals(existing.Value.Image, image))
                    {
                        toDispose = existing.Value.Image;
                        existing.Value.Image = image;
                    }
                    usage.Remove(existing);
                    usage.AddFirst(existing);
                }
                else
                {
                    var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, image));
                    usage.AddFirst(node);
                    map[key] = node;

                    while (map.Count > capacity)
                    {
                        var lru = usage.Last;
                        if (lru == null)
                            break;
                        usage.RemoveLast();
                        map.Remove(lru.Value.Key);
                        (evicted ??= new List<Image>()).Add(lru.Value.Image);
                    }
                }
            }

            toDispose?.Dispose();
            if (evicted != null)
            {
                foreach (Image img in evicted)
                    img?.Dispose();
            }
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            Image toDispose = null;
            lock (sync)
            {
                if (map.TryGetValue(key, out var node))
                {
                    usage.Remove(node);
                    map.Remove(key);
                    toDispose = node.Value.Image;
                }
            }

            toDispose?.Dispose();
        }

        public void Clear()
        {
            List<Image> images = new List<Image>();
            lock (sync)
            {
                foreach (var node in usage)
                    images.Add(node.Image);
                usage.Clear();
                map.Clear();
            }

            foreach (Image img in images)
                img?.Dispose();
        }

        public void Dispose()
        {
            Clear();
        }

        private sealed class CacheEntry
        {
            public CacheEntry(string key, Image image)
            {
                Key = key;
                Image = image;
            }

            public string Key { get; }
            public Image Image { get; set; }
        }
    }
}
