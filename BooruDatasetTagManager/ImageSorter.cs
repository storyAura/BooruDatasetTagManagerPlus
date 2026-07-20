using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public class ImageSorter
    {
        public string RootDir { get; set; }
        public SortItem RootItem { get; set; }

        public Dictionary<string,List<string>> FileQueue { get; set; }
        private Dictionary<string, SortItem> indexedSortItems;

        public long FileIndex { get; set; } = 1;

        public ImageSorter(string rootDir)
        {
            RootDir = rootDir;
            RootItem = new SortItem();
            RootItem.Name = "Root";
            RootItem.Id = "Root";
            RootItem.Level = 0;
            FileQueue = new Dictionary<string, List<string>>();
            indexedSortItems = new Dictionary<string, SortItem>();
        }

        /// <summary>Per-file failures from the most recent copy run.</summary>
        public List<string> LastCopyErrors { get; } = new List<string>();

        /// <summary>
        /// True when <paramref name="name"/> can be used as one directory
        /// segment under the sorter root: no separators, no "..", no rooted
        /// paths, no invalid characters. Category names come straight from a
        /// text box, so anything else could escape the chosen root folder.
        /// </summary>
        public static bool IsValidCategoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            string trimmed = name.Trim();
            return trimmed != "." && trimmed != ".."
                && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public void StartCopy()
        {
            LastCopyErrors.Clear();
            string rootFull = Path.GetFullPath(RootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var item in FileQueue)
            {
                try
                {
                    string dstDir = Path.GetFullPath(Path.Combine(RootDir, indexedSortItems[item.Key].Path));
                    // Defense in depth behind the UI-side name validation: never
                    // create or copy outside the user-selected root folder.
                    if (!string.Equals(dstDir, rootFull, StringComparison.OrdinalIgnoreCase)
                        && !dstDir.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        LastCopyErrors.Add($"{item.Key}: destination '{dstDir}' is outside the root folder");
                        continue;
                    }
                    Directory.CreateDirectory(dstDir);
                    foreach (var file in item.Value)
                    {
                        try
                        {
                            File.Copy(file, GetDstFile(file, dstDir));
                        }
                        catch (Exception ex)
                        {
                            // A locked/missing source must not abort the whole batch.
                            LastCopyErrors.Add($"{file}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastCopyErrors.Add($"{item.Key}: {ex.Message}");
                }
            }
            FileQueue.Clear();
        }

        public async Task StartCopyAsync()
        {
            await Task.Run(() => StartCopy());
        }

        private string GetDstFile(string origPath, string dstDir)
        {
            string dstFile = Path.Combine(dstDir, FileIndex.ToString() + Path.GetExtension(origPath));
            FileIndex++;
            if (File.Exists(dstFile))
            {
                int dIndex = 1;
                do
                {
                    dstFile = Path.Combine(dstDir, FileIndex.ToString() + "_(" + (dIndex++) + ")" + Path.GetExtension(origPath));
                } 
                while (File.Exists(dstFile));
            }
            return dstFile;
        }

        public void CreateFromTreeNode(TreeNode node)
        {
            if (node.Parent == null && node.Name == "Root")
            {
                foreach (TreeNode childNode in node.Nodes)
                {
                    RootItem.AddChild(childNode);
                }
            }
            indexedSortItems.Clear();
            UpdateSortItemIndex(RootItem);
        }

        public void UpdateSortItemIndex(SortItem item)
        {
            indexedSortItems.Add(item.Id, item);
            foreach (var childItem in item.Items)
            {
                UpdateSortItemIndex(childItem);
            }
        }

        public void AddFileQueue(string element, string filePath)
        {
            if (!FileQueue.ContainsKey(element))
                FileQueue.Add(element, new List<string>());
            if (!FileQueue[element].Contains(filePath))
                FileQueue[element].Add(filePath);
        }

        public void AddFileRangeQueue(string element, IEnumerable<string> filesPath)
        {
            if (!FileQueue.ContainsKey(element))
                FileQueue.Add(element, new List<string>());
            foreach (var item in filesPath)
            {
                if (!FileQueue[element].Contains(item))
                    FileQueue[element].Add(item);
            }
        }

        public class SortItem
        {
            public SortItem()
            {
                Items = new List<SortItem>();
            }
            public string Name { get; set; }
            public string Id { get; set; }
            public int Level { get; set; }
            public SortItem Parent { get; set; }
            public List<SortItem> Items { get; }

            public string Path
            {
                get
                {
                    List<string> tempLst = new List<string>();
                    SortItem curItem = this;
                    while (curItem.Parent != null)
                    {
                        tempLst.Add(curItem.Name);
                        curItem = curItem.Parent;
                    }
                    tempLst.Reverse();
                    return string.Join("\\", tempLst);
                }
            }

            public void AddChild(string name)
            {
                SortItem childItem = new SortItem();
                childItem.Parent = this;
                childItem.Name = name;
                childItem.Level = this.Level + 1;
                Items.Add(childItem);
            }

            public void AddChild(TreeNode node)
            {
                SortItem childItem = new SortItem();
                childItem.Parent = this;
                childItem.Name = node.Text;
                childItem.Id = node.Name;
                childItem.Level = this.Level + 1;
                if (node.Nodes != null && node.Nodes.Count > 0)
                {
                    foreach (TreeNode childNode in node.Nodes)
                    {
                        childItem.AddChild(childNode);
                    }
                }
                Items.Add(childItem);
            }
        }
    }
}
