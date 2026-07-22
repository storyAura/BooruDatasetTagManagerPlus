using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Danbooru character-tag catalog loaded from
    /// Data/danbooru_character_tags.csv (columns: character_tag,
    /// other_names, copyright; RFC-4180 quoting; other_names is a
    /// comma-separated alternative-name list whose first entry is the
    /// primary translated name). Keys are normalized — lowercase, '_'→' ',
    /// dataset-style escaped parens unescaped — so raw danbooru tags and
    /// FixTags-style tags both match. Backs the Character bucket of the tag
    /// classifier and the "译名 (作品)" translation hit; a settings toggle
    /// (<c>MatchCharacterTags</c>) controls whether it is loaded at all.
    /// Pure data class: linked into the test project.
    /// </summary>
    public sealed class CharacterTagCatalog
    {
        private readonly Dictionary<string, (string PrimaryName, string Copyright)> entries =
            new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        public int Count => entries.Count;

        public bool Contains(string tag)
        {
            return entries.ContainsKey(Normalize(tag));
        }

        /// <summary>
        /// "主译名 (作品)" for the tag, or null when the catalog has no name
        /// for it (classification can still hit through <see cref="Contains"/>).
        /// </summary>
        public string GetDisplayTranslation(string tag)
        {
            if (!entries.TryGetValue(Normalize(tag), out (string PrimaryName, string Copyright) entry)
                || string.IsNullOrEmpty(entry.PrimaryName))
            {
                return null;
            }
            return string.IsNullOrEmpty(entry.Copyright)
                ? entry.PrimaryName
                : entry.PrimaryName + " (" + entry.Copyright + ")";
        }

        /// <summary>
        /// Loads the catalog; a missing or unreadable file yields an empty
        /// catalog (character matching then simply never hits).
        /// </summary>
        public static CharacterTagCatalog LoadFromFile(string path)
        {
            var catalog = new CharacterTagCatalog();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return catalog;
            try
            {
                // Copyright strings repeat massively (one per franchise):
                // intern them so 330k rows stay tens of MB smaller.
                var copyrightPool = new Dictionary<string, string>(StringComparer.Ordinal);
                using var reader = new StreamReader(path);
                string line = reader.ReadLine(); // header
                while ((line = reader.ReadLine()) != null)
                    catalog.AddLine(line, copyrightPool);
            }
            catch (Exception)
            {
                // Keep whatever parsed; a broken catalog must not break startup.
            }
            return catalog;
        }

        private void AddLine(string line, Dictionary<string, string> copyrightPool)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            List<string> fields = ParseCsvLine(line);
            if (fields.Count == 0)
                return;
            string key = Normalize(fields[0]);
            if (key.Length == 0)
                return;
            string primaryName = FirstName(fields.Count > 1 ? fields[1] : null);
            string copyright = fields.Count > 2 ? fields[2].Trim() : string.Empty;
            string pooledCopyright = null;
            if (copyright.Length > 0)
            {
                if (!copyrightPool.TryGetValue(copyright, out pooledCopyright))
                {
                    pooledCopyright = copyright.Replace('_', ' ');
                    copyrightPool[copyright] = pooledCopyright;
                }
            }
            entries[key] = (primaryName, pooledCopyright);
        }

        /// <summary>First entry of the comma-separated alternative-name list.</summary>
        private static string FirstName(string otherNames)
        {
            if (string.IsNullOrWhiteSpace(otherNames))
                return null;
            int comma = otherNames.IndexOf(',');
            string first = comma > 0 ? otherNames.Substring(0, comma) : otherNames;
            first = first.Trim().Replace('_', ' ');
            return first.Length == 0 ? null : first;
        }

        /// <summary>Minimal single-line RFC-4180 field splitter ("" unescaping).</summary>
        public static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            if (line == null)
                return fields;
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static string Normalize(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return string.Empty;
            return tag.Trim()
                .ToLowerInvariant()
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace('_', ' ');
        }
    }
}
