using System.Threading;

namespace Diffusion.IO
{
    public static class Metadata
    {
        public static MetadataResult ReadFromFile(string path)
        {
            return new MetadataResult();
        }
    }

    public sealed class MetadataResult
    {
        public string Prompt { get; set; } = string.Empty;
    }
}

namespace Manina.Windows.Forms
{
}

namespace BooruDatasetTagManager
{
    public static class Program
    {
        public static AppSettingsStub Settings { get; } = new AppSettingsStub();
        public static SemaphoreSlim EditableTagListLocker { get; } = new SemaphoreSlim(1, 1);
        public static SemaphoreSlim TranslationLocker { get; } = new SemaphoreSlim(1, 1);
        public static SemaphoreSlim LoadingLocker { get; } = new SemaphoreSlim(1, 1);
        public static object ListChangeLocker { get; } = new object();
        public static string AppPath { get; set; } = Path.GetTempPath();
        public static TranslationManager TransManager { get; set; }
        public static ChineseTagLookupService ChineseTagLookup { get; set; } = ChineseTagLookupService.Empty;
        public static CharacterTagCatalog CharacterTagLookup { get; set; }
    }

    public sealed class AppSettingsStub
    {
        public bool FixTagsOnSaveLoad { get; set; }
        public string SeparatorOnSave { get; set; } = ", ";
        public string SeparatorOnLoad { get; set; } = ",";
        public string DefaultTagsFileExtension { get; set; } = "txt";
        public int PreviewSize { get; set; }
        public bool CacheOpenImages { get; set; }
        public bool UseDanbooruZhCsvBeforeTranslation { get; set; }
        public string FfmpegPath { get; set; } = string.Empty;
        public HotkeyDataStub Hotkeys { get; } = new HotkeyDataStub();

        public string[] GetTagFilesExtensions()
        {
            return new[] { DefaultTagsFileExtension };
        }
    }

    public sealed class HotkeyDataStub
    {
        public HotkeyItemStub this[string key] => null;
    }

    public sealed class HotkeyItemStub
    {
        public string GetHotkeyString() => string.Empty;
    }

    public static class Extensions
    {
        public delegate void ProgressHandler(int current, int max);

        public static string[] ImageExtensions = { ".jpg", ".png", ".bmp", ".jpeg", ".webp" };
        public static string[] VideoExtensions = { ".mp4", ".webm", ".mov" };

        public static long GetHash(this string text)
        {
            return Translator.Crypto.Adler32.GenerateHash(text);
        }

        public static int CalcBracketsCount(float weight, bool positive)
        {
            return 0;
        }

        public static string GetBetween(this string source, string start, string end)
        {
            int startIndex = source.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;
            startIndex += start.Length;
            int endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
            return endIndex < 0 ? string.Empty : source.Substring(startIndex, endIndex - startIndex);
        }

        public static System.Drawing.Image GetImageFromFile(string imagePath)
        {
            return new System.Drawing.Bitmap(1, 1);
        }

        public static System.Drawing.Image MakeThumb(string imagePath, int imgSize)
        {
            return new System.Drawing.Bitmap(1, 1);
        }

        public static T Pop<T>(this List<T> list)
        {
            // Mirror the real Extensions.Pop: empty list returns default instead
            // of throwing, so PromptParser behaves the same under test.
            if (list == null || list.Count == 0)
                return default;
            int lastIndex = list.Count - 1;
            T item = list[lastIndex];
            list.RemoveAt(lastIndex);
            return item;
        }
    }

    public sealed class FileNamesComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public static int StrCmpLogicalW(string x, string y)
        {
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class TagsDB
    {
        public sealed class TagItem
        {
            public string Tag { get; private set; }
            public long TagHash { get; private set; }
            public int Count;
            public bool IsAlias;
            public string Parent;
            public string AutocompleteDisplayText;
            public string Translation;

            public void SetTag(string tag)
            {
                Tag = tag.Trim().ToLower();
                TagHash = Tag.GetHash();
            }

            public string GetTag()
            {
                return IsAlias ? Parent : Tag;
            }

            public override string ToString()
            {
                return string.IsNullOrEmpty(AutocompleteDisplayText)
                    ? Tag
                    : AutocompleteDisplayText;
            }
        }
    }
}
