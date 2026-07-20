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

    // How the image editor stores an edited image (Form_ImageEditor).
    public enum ImageEditorSaveMode
    {
        Ask,        // prompt "overwrite or new file" on every save
        Overwrite,  // always overwrite the original image file
        NewFile     // always save as a new "<name>_edit" file next to the original
    }

    // Double-click action on the All Tags grid (single click selects). Member
    // names double as i18n keys via Extensions.GetFriendlyEnumValues, and each
    // maps onto one of the All Tags toolbar buttons.
    public enum AllTagsQuickAction
    {
        QuickActionReplaceTag,          // open "Replace all" with the tag as source
        QuickActionAddTagToAll,
        QuickActionDeleteTagFromAll,
        QuickActionAddTagToSelected,
        QuickActionDeleteTagFromSelected,
        QuickActionAddTagToFiltered,
        QuickActionDeleteTagFromFiltered,
        QuickActionFilterByTag
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
