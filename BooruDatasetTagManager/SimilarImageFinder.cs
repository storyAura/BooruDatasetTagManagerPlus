using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// czkawka-style similar-image detection: a 64-bit difference hash (dHash)
    /// per image, compared by Hamming distance and greedily clustered. The
    /// hash survives resizing, re-encoding and uniform brightness shifts, so
    /// near-duplicate dataset images land in the same group.
    /// </summary>
    public static class SimilarImageFinder
    {
        /// <summary>
        /// Difference hash: downscale to 9x8 grayscale and emit one bit per
        /// horizontal neighbor pair (left brighter than right). Works from any
        /// already-decoded image, including the dataset's small thumbnails.
        /// </summary>
        public static ulong ComputeDHash(Image image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            using var small = new Bitmap(9, 8);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Flatten alpha onto white: otherwise transparent PNGs hash
                // their alpha holes as black and all match each other.
                g.Clear(Color.White);
                g.DrawImage(image, new Rectangle(0, 0, 9, 8));
            }

            ulong hash = 0UL;
            int bit = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (Luma(small.GetPixel(x, y)) > Luma(small.GetPixel(x + 1, y)))
                        hash |= 1UL << bit;
                    bit++;
                }
            }
            return hash;
        }

        public static int HammingDistance(ulong a, ulong b)
        {
            return BitOperations.PopCount(a ^ b);
        }

        /// <summary>
        /// Greedy star clustering (czkawka's approach): each not-yet-grouped
        /// item collects every later ungrouped item within
        /// <paramref name="maxDistance"/>. Items with no partner are omitted,
        /// so every returned group has at least two members.
        /// </summary>
        // ponytail: O(n²) pairwise compare — ~25M popcounts for a 5k dataset,
        // well under a second; switch to a BK-tree only if huge datasets hurt.
        public static List<List<T>> GroupBySimilarity<T>(
            IReadOnlyList<T> items, IReadOnlyList<ulong> hashes, int maxDistance)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (hashes == null)
                throw new ArgumentNullException(nameof(hashes));
            if (items.Count != hashes.Count)
                throw new ArgumentException("items and hashes must have the same length.");
            if (maxDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(maxDistance));

            var groups = new List<List<T>>();
            bool[] used = new bool[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (used[i])
                    continue;
                List<T> group = null;
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (used[j] || HammingDistance(hashes[i], hashes[j]) > maxDistance)
                        continue;
                    group ??= new List<T> { items[i] };
                    group.Add(items[j]);
                    used[j] = true;
                }
                if (group != null)
                {
                    used[i] = true;
                    groups.Add(group);
                }
            }
            return groups;
        }

        private static double Luma(Color c)
        {
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        }
    }
}
