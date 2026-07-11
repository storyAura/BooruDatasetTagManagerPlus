using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Translator.Crypto;

namespace BooruDatasetTagManager
{
    public class TagsDB
    {
        public int Version;
        public bool FixTags;
        public List<TagItem> Tags;
        public Dictionary<string, long> LoadedFiles;

        [JsonIgnore]
        private Dictionary<long, int> hashes;

        // Fast O(1) lookup of tags that are already registered as a parent (canonical)
        // of some alias, replacing the previous O(n) Tags.Exists(...) scan in AddTag.
        [JsonIgnore]
        private HashSet<string> parentTags;

        private const int curVersion = 101;

        public TagsDB()
        {
            Version = curVersion;
            Tags = new List<TagItem>();
            LoadedFiles = new Dictionary<string, long>();
            hashes = new Dictionary<long, int>();
            parentTags = new HashSet<string>();
        }

        private string[] ReadAllLines(byte[] data, Encoding encoding)
        {
            
            List<string> list = new List<string>();
            using (MemoryStream ms = new MemoryStream(data))
            {
                using (StreamReader streamReader = new StreamReader(ms, encoding))
                {
                    string item;
                    while ((item = streamReader.ReadLine()) != null)
                    {
                        list.Add(item);
                    }
                }
            }
            return list.ToArray();
        }

        public void ClearDb()
        {
            Tags.Clear();
            hashes.Clear();
            parentTags.Clear();
        }

        public void SetNeedFixTags(bool fixTags)
        {
            FixTags = fixTags;
        }

        public void ResetVersion()
        {
            Version = curVersion;
        }

        public void ClearLoadedFiles()
        {
            LoadedFiles.Clear();
        }

        public void LoadCSVFromDir(string dir)
        {
            FileInfo[] csvFiles = new DirectoryInfo(dir).GetFiles("*.csv", SearchOption.TopDirectoryOnly);
            foreach (var item in csvFiles)
            {
                try
                {
                    LoadFromCSVFile(item.FullName);
                }
                catch (Exception ex)
                {
                    // One unreadable/broken tag file must not abort startup.
                    Trace.WriteLine($"TagsDB: failed to load '{item.FullName}': {ex}");
                }
            }
        }

        public void LoadTxtFromDir(string dir)
        {
            FileInfo[] txtFiles = new DirectoryInfo(dir).GetFiles("*.txt", SearchOption.TopDirectoryOnly);
            foreach (var item in txtFiles)
            {
                try
                {
                    LoadFromTxtFile(item.FullName);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"TagsDB: failed to load '{item.FullName}': {ex}");
                }
            }
        }

        public void LoadFromTxtFile(string fPath, bool append = true)
        {
            byte[] data = File.ReadAllBytes(fPath);
            long hash = Adler32.GenerateHash(data);
            string fName = Path.GetFileName(fPath);
            if (LoadedFiles.ContainsKey(fName))
            {
                if (LoadedFiles[fName] == hash)
                    return;
                else
                    LoadedFiles[fName] = hash;
            }
            else
            {
                LoadedFiles.Add(fName, hash);
            }


            string[] lines = ReadAllLines(data, Encoding.UTF8);
            if (!append)
                ClearDb();
            foreach (var item in lines)
            {
                AddTag(item, 0);
            }
        }


        public void LoadFromCSVFile(string fPath, bool append = true)
        {
            Regex r = new Regex("(.*?),(\\d+),(\\d+),(.*)");
            char[] splitter = { ',' };
            byte[] data = File.ReadAllBytes(fPath);
            long hash = Adler32.GenerateHash(data);
            string fName = Path.GetFileName(fPath);
            if (LoadedFiles.ContainsKey(fName))
            {
                if (LoadedFiles[fName] == hash)
                    return;
                else
                    LoadedFiles[fName] = hash;
            }
            else
            {
                LoadedFiles.Add(fName, hash);
            }


            string[] lines = ReadAllLines(data, Encoding.UTF8);
            if (!append)
                Tags.Clear();
            foreach (var item in lines)
            {
                Match match = r.Match(item);
                if (match.Success)
                {
                    string tagName = match.Groups[1].Value;
                    string[] aliases = match.Groups[4].Value.Replace("\"", "").Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                    // The regex only guarantees digits, not that they fit an int.
                    int tagCount = int.TryParse(match.Groups[3].Value, out int parsedCount) ? parsedCount : int.MaxValue;
                    AddTag(tagName, tagCount);
                    foreach (var al in aliases)
                    {
                        AddTag(al, tagCount, true, tagName);
                    }
                }
            }
        }

        public void SortTags()
        {
            Tags.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        }

        private void AddTag(string tag, int count, bool isAlias = false, string parent = null)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return;
            tag = PrepareTag(tag);
            // Skip aliases whose text already exists as the canonical parent of another entry.
            // O(1) HashSet lookup instead of the previous O(n) Tags.Exists(...) full scan.
            if (parentTags.Contains(tag))
                return;
            tag = tag.Trim().ToLower();
            long tagHash = tag.GetHash();

            int existTagIndex = -1;
            TagItem tagItem = null;
            if (hashes.TryGetValue(tagHash, out existTagIndex))
            {
                tagItem = Tags[existTagIndex];
                tagItem.Count += count;
            }
            else
            {
                tagItem = new TagItem();
                tagItem.SetTag(tag);
                tagItem.Count = count;
                tagItem.IsAlias = isAlias;
                tagItem.Parent = PrepareTag(parent);
                hashes.Add(tagItem.TagHash, Tags.Count);
                Tags.Add(tagItem);
                if (!string.IsNullOrEmpty(tagItem.Parent))
                    parentTags.Add(tagItem.Parent);
            }
        }

        /// <summary>
        /// Rebuilds the in-memory lookup indexes (hashes / parentTags) after the
        /// Tags list has been populated from disk. Required because these indexes
        /// are not serialized.
        /// </summary>
        private void RebuildIndexes()
        {
            hashes.Clear();
            parentTags.Clear();
            if (Tags == null)
            {
                Tags = new List<TagItem>();
                return;
            }
            for (int i = 0; i < Tags.Count; i++)
            {
                TagItem item = Tags[i];
                if (item == null)
                    continue;
                if (!hashes.ContainsKey(item.TagHash))
                    hashes.Add(item.TagHash, i);
                if (!string.IsNullOrEmpty(item.Parent))
                    parentTags.Add(item.Parent);
            }
        }

        private string PrepareTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return tag;
            if (FixTags)
            {
                tag = tag.Replace('_', ' ');
                tag = tag.Replace("\\(", "(");
                tag = tag.Replace("\\)", ")");
            }
            return tag;
        }

        public bool IsNeedUpdate(string dirToCheck)
        {
            if (Version != curVersion)
                return true;
            if (Program.Settings.FixTagsOnSaveLoad != FixTags)
                return true;
            FileInfo[] tagFiles = new DirectoryInfo(dirToCheck).GetFiles("*.csv", SearchOption.TopDirectoryOnly).
                Concat(new DirectoryInfo(dirToCheck).GetFiles("*.txt", SearchOption.TopDirectoryOnly)).ToArray();
            if (tagFiles.Length == 0)
                return false;
            if (LoadedFiles.Count != tagFiles.Length)
                return true;
            foreach (var item in tagFiles)
            {
                long hash;
                try
                {
                    byte[] data = File.ReadAllBytes(item.FullName);
                    hash = Adler32.GenerateHash(data);
                }
                catch (Exception ex)
                {
                    // Locked/unreadable file: treat as changed and let the reload
                    // path (which tolerates per-file failures) sort it out.
                    Trace.WriteLine($"TagsDB.IsNeedUpdate: failed to read '{item.FullName}': {ex}");
                    return true;
                }
                if (!LoadedFiles.ContainsKey(item.Name))
                    return true;
                if(LoadedFiles[item.Name]!=hash)
                    return true;
            }
            return false;
        }

        public void LoadTranslation(TranslationManager transManager)
        {
            bool onlyManual = Program.Settings.OnlyManualTransInAutocomplete;
            foreach (var tag in Tags)
            {
                tag.Translation = transManager.GetTranslation(tag.TagHash, onlyManual);
            }
        }

        public static TagsDB LoadFromTagFile(string fPath)
        {
            if (!File.Exists(fPath))
                return null;
            try
            {
                string json = File.ReadAllText(fPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return null;
                TagsDB db = JsonConvert.DeserializeObject<TagsDB>(json);
                if (db == null)
                    return null;
                db.Tags ??= new List<TagItem>();
                db.LoadedFiles ??= new Dictionary<string, long>();
                db.RebuildIndexes();
                return db;
            }
            catch (Exception ex)
            {
                // Corrupt/legacy (BinaryFormatter) cache: fall back to a rebuild from CSV/txt.
                Trace.WriteLine($"TagsDB.LoadFromTagFile failed for '{fPath}': {ex}");
                return null;
            }
        }

        public void SaveTags(string fPath)
        {
            try
            {
                string json = JsonConvert.SerializeObject(this);
                File.WriteAllText(fPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Cache write failures are non-fatal; the DB rebuilds next launch.
                Trace.WriteLine($"TagsDB.SaveTags failed for '{fPath}': {ex}");
            }
        }

        public class TagItem
        {
            [JsonProperty]
            public string Tag { get; private set; }
            [JsonProperty]
            public long TagHash { get; private set; }
            public int Count;
            //public List<string> Aliases;
            public bool IsAlias;
            public string Parent;
            public string AutocompleteDisplayText;

            public string Translation;

            public TagItem()
            {
                //Aliases = new List<string>();
            }

            public void SetTag(string tag)
            {
                Tag = tag.Trim().ToLower();
                TagHash = Tag.GetHash();
            }

            public string GetTag()
            {
                if (IsAlias)
                    return Parent;
                else
                    return Tag;
            }

            public override string ToString()
            {
                if (!string.IsNullOrEmpty(AutocompleteDisplayText))
                    return AutocompleteDisplayText;
                if (IsAlias)
                    return $"{Tag} -> {Parent}{$" ({Count})"}{(string.IsNullOrEmpty(Translation) ? "" : $" [{Translation}]")}";
                else
                    return $"{Tag}{$" ({Count})"}{(string.IsNullOrEmpty(Translation) ? "" : $" [{Translation}]")}";
            }
        }

    }
}
