using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BooruDatasetTagManager
{
    public enum BatchRenameNumbering
    {
        Numeric,
        Letters,
        None
    }

    /// <summary>
    /// Pure name planning for the folder batch-rename: builds the target base
    /// names (no extension) from prefix + counter + suffix. Numeric counters
    /// are zero-padded, letter counters run a, b, ..., z, aa, ab (Excel
    /// style), None keeps each original base name between prefix and suffix.
    /// Linked into the test project.
    /// </summary>
    public static class BatchRenamePlanner
    {
        public static IReadOnlyList<string> BuildNames(
            int count,
            string prefix,
            string suffix,
            BatchRenameNumbering numbering,
            int startNumber,
            int digits,
            IReadOnlyList<string> originalBaseNames = null)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            prefix = Sanitize(prefix);
            suffix = Sanitize(suffix);
            var names = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string middle;
                switch (numbering)
                {
                    case BatchRenameNumbering.Numeric:
                        middle = (startNumber + i).ToString().PadLeft(Math.Max(1, digits), '0');
                        break;
                    case BatchRenameNumbering.Letters:
                        middle = ToLetters(i);
                        break;
                    default:
                        middle = originalBaseNames != null && i < originalBaseNames.Count
                            ? originalBaseNames[i]
                            : string.Empty;
                        break;
                }
                string name = prefix + middle + suffix;
                if (name.Trim().Length == 0)
                    throw new ArgumentException("Empty file name produced.", nameof(prefix));
                names.Add(name);
            }
            return names;
        }

        /// <summary>0→a, 25→z, 26→aa, 27→ab, ... (Excel column style).</summary>
        public static string ToLetters(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            var builder = new StringBuilder();
            index++;
            while (index > 0)
            {
                index--;
                builder.Insert(0, (char)('a' + index % 26));
                index /= 26;
            }
            return builder.ToString();
        }

        /// <summary>True when the text is safe inside a file name.</summary>
        public static bool IsValidNamePart(string text)
        {
            return string.IsNullOrEmpty(text)
                || text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static string Sanitize(string part)
        {
            part ??= string.Empty;
            if (!IsValidNamePart(part))
                throw new ArgumentException("Invalid characters in name part.", nameof(part));
            return part;
        }
    }
}
