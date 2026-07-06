using System.Threading;

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
        public static string AppPath { get; set; } = Path.GetTempPath();
        public static TranslationManager TransManager { get; set; }
        public static ChineseTagLookupService ChineseTagLookup { get; set; } = ChineseTagLookupService.Empty;
    }

    public sealed class AppSettingsStub
    {
        public bool FixTagsOnSaveLoad { get; set; }
        public string SeparatorOnSave { get; set; } = ", ";
        public bool UseDanbooruZhCsvBeforeTranslation { get; set; }
        public string FfmpegPath { get; set; } = string.Empty;
        public HotkeyDataStub Hotkeys { get; } = new HotkeyDataStub();
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
    }

    public static class PromptParser
    {
        public static float round_bracket_multiplier = 1.1f;
        public static float square_bracket_multiplier = 1f / 1.1f;

        public sealed class PromptItem
        {
            public string Text { get; set; }
            public float Weight { get; set; }
        }
    }

    public sealed class DatasetManager
    {
        public enum AddingType
        {
            Top,
            Center,
            Down,
            Custom
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
