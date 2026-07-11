using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public enum TranslationService
    {
        GoogleTranslate,
        ChineseTranslate,
        MyMemoryTranslate,
        GoogleJsonTranslate
    }

    public enum AutocompleteMode
    {
        Disable,
        StartWith,
        StartWithAndContains,
        StartWithIncludeTranslations,
        StartWithAndContainsIncludeTranslations
    }

    public enum AutocompleteSort
    {
        Alphabetical,
        ByCount
    }

    public enum AutoTaggerSort
    {
        None,
        Confidence,
        Alphabetical
    }


    public enum FilterType
    {
        And,
        Or,
        Not,
        Xor
    }

    public enum ImagePreviewType
    {
        PreviewInMainWindow,
        SeparateWindow
    }

    public enum HuggingFaceDownloadSource
    {
        HuggingFace,
        HfMirror
    }

    public enum NetworkUnionMode
    {
        Addition,
        Intersection,
        Subtraction
    }

    public enum NetworkResultSetMode
    {
        AllWithReplacement,
        OnlyNewWithAddition,
        SkipExistTagList
    }

    // Output mode of the unified LLM tagging window (Form_LlmTagger).
    public enum LlmTaggerMode
    {
        Tags,             // image -> booru tags, written back into the dataset
        NaturalLanguage   // image (+ existing tags) -> natural-language caption (former TAG2NL)
    }

    // Where NaturalLanguage-mode captions are written.
    public enum LlmCaptionOutputTarget
    {
        SeparateFolder,   // non-destructive: <folder>_captioned (original tags + caption)
        InPlace           // replace the image's own .txt content with the caption
    }

    // Content of a NaturalLanguage-mode result (independent of the output target).
    public enum LlmCaptionFormat
    {
        TagsAndNaturalLanguage,   // original tags + newline + caption (the original TAG2NL)
        NaturalLanguageOnly       // just the natural-language caption
    }

    public enum TagFilteringMode
    {
        None,
        Equal,
        Containing,
        NotEqual,
        NotContaining,
        Regex
    }

    public enum DataSourceType
    {
        None,
        Single,
        Multi
    }
}
