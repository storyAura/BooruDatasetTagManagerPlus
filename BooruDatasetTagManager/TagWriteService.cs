using System;
using System.Collections.Generic;
using System.Linq;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    public static class TagWriteService
    {
        public static void ApplyTags(DataItem item, IReadOnlyList<AutoTagProviderItem> tags, TaggerSettings settings)
        {
            if (item == null || tags == null)
                return;

            IEnumerable<AutoTagProviderItem> ordered = tags;
            if (settings.SortMode == AutoTaggerSort.Confidence)
                ordered = OrderByConfidenceDescending(tags);
            else if (settings.SortMode == AutoTaggerSort.Alphabetical)
                ordered = tags.OrderBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase);

            if (settings.CharacterFirst)
                ordered = PlaceCharactersFirst(ordered);

            List<string> tagNames = TagPostProcessor.Process(ordered.Select(tag => tag.Tag), settings);
            ApplyTagNames(item, tagNames, settings);
        }

        /// <summary>
        /// WD14/PixAI/CL label files are alphabetical; callers use this so
        /// SortMode.None keeps confidence order (the wd14-tagger default)
        /// instead of CSV A–Z.
        /// </summary>
        public static List<AutoTagProviderItem> OrderByConfidenceDescending(IEnumerable<AutoTagProviderItem> tags)
        {
            if (tags == null)
                return new List<AutoTagProviderItem>();
            return tags
                .OrderByDescending(tag => tag.Confidence)
                .ThenBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<AutoTagProviderItem> PlaceCharactersFirst(IEnumerable<AutoTagProviderItem> tags)
        {
            var characters = new List<AutoTagProviderItem>();
            var rest = new List<AutoTagProviderItem>();
            if (tags != null)
            {
                foreach (AutoTagProviderItem tag in tags)
                {
                    if (tag != null && tag.IsCharacter)
                        characters.Add(tag);
                    else if (tag != null)
                        rest.Add(tag);
                }
            }

            characters.AddRange(rest);
            return characters;
        }

        public static void ApplyTagNames(DataItem item, IReadOnlyList<string> tagNames, TaggerSettings settings)
        {
            if (item == null || tagNames == null || tagNames.Count == 0)
                return;

            if (settings.SetMode == NetworkResultSetMode.AllWithReplacement)
            {
                item.Tags.Clear();
                item.Tags.AddRange(tagNames, true);
            }
            else if (settings.SetMode == NetworkResultSetMode.OnlyNewWithAddition)
            {
                foreach (string tag in tagNames)
                    item.Tags.AddTag(tag, true, DatasetManager.AddingType.Down, 0);
            }
            else if (settings.SetMode == NetworkResultSetMode.SkipExistTagList && item.Tags.Count == 0)
            {
                item.Tags.AddRange(tagNames, true);
            }
        }
    }

    public static class TaggerCategoryFilter
    {
        public const int General = 0;
        public const int Copyright = 3;
        public const int Character = 4;
        public const int Style = 5;
        public const int Meta = 7;
        public const int Rating = 9;

        public static bool TryAccept(
            string name,
            int category,
            float confidence,
            TaggerProFilter filter,
            out AutoTagProviderItem item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(name) || filter == null)
                return false;

            bool professional = filter.ProfessionalMode;
            double threshold;
            bool isCharacter = category == Character;
            switch (category)
            {
                case General:
                    threshold = filter.GeneralThreshold;
                    break;
                case Character:
                    threshold = filter.CharacterThreshold;
                    break;
                case Copyright:
                    if (!professional)
                        return false;
                    threshold = filter.CopyrightThreshold;
                    break;
                case Style:
                    if (!professional || !filter.IncludeStyle)
                        return false;
                    threshold = filter.StyleThreshold;
                    break;
                case Meta:
                    if (!professional || !filter.IncludeMeta)
                        return false;
                    threshold = filter.MetaThreshold;
                    break;
                case Rating:
                    if (!professional)
                        return false;
                    threshold = filter.RatingThreshold;
                    name = PrefixRating(name);
                    break;
                default:
                    return false;
            }

            if (confidence < threshold)
                return false;

            item = new AutoTagProviderItem
            {
                Tag = name,
                Confidence = confidence,
                IsCharacter = isCharacter
            };
            return true;
        }

        public static string PrefixRating(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;
            if (name.StartsWith("rating:", StringComparison.OrdinalIgnoreCase))
                return name;
            return "rating:" + name;
        }

        public static int MapClCategory(string category)
        {
            if (string.Equals(category, "General", StringComparison.OrdinalIgnoreCase))
                return General;
            if (string.Equals(category, "Character", StringComparison.OrdinalIgnoreCase))
                return Character;
            if (string.Equals(category, "Copyright", StringComparison.OrdinalIgnoreCase))
                return Copyright;
            if (string.Equals(category, "Style", StringComparison.OrdinalIgnoreCase))
                return Style;
            if (string.Equals(category, "Meta", StringComparison.OrdinalIgnoreCase))
                return Meta;
            if (string.Equals(category, "Rating", StringComparison.OrdinalIgnoreCase))
                return Rating;
            return -1;
        }
    }

    public sealed class TaggerProFilter
    {
        public bool ProfessionalMode { get; set; }
        public double GeneralThreshold { get; set; }
        public double CharacterThreshold { get; set; }
        public double CopyrightThreshold { get; set; } = 0.24;
        public double StyleThreshold { get; set; } = 0.15;
        public double MetaThreshold { get; set; } = 0.17;
        public double RatingThreshold { get; set; } = 0.41;
        public bool IncludeStyle { get; set; }
        public bool IncludeMeta { get; set; } = true;
        public bool CharacterFirst { get; set; }

        public static TaggerProFilter Basic(double generalThreshold, double characterThreshold)
        {
            return new TaggerProFilter
            {
                ProfessionalMode = false,
                GeneralThreshold = generalThreshold,
                CharacterThreshold = characterThreshold
            };
        }

        public static TaggerProFilter FromSettings(AppSettings settings, OnnxTaggerModelEntry entry, double generalThreshold, double characterThreshold)
        {
            TaggerProFilter filter = Basic(generalThreshold, characterThreshold);
            if (settings == null || entry == null || !settings.OnnxTaggerProfessionalMode)
                return filter;

            TaggerProProfile profile = TaggerProProfile.For(entry);
            OnnxTaggerProOptions options = settings.GetProOptions(entry.Id, profile);
            filter.ProfessionalMode = true;
            filter.CopyrightThreshold = options.CopyrightThreshold;
            filter.StyleThreshold = options.StyleThreshold;
            filter.MetaThreshold = options.MetaThreshold;
            filter.RatingThreshold = options.RatingThreshold;
            filter.IncludeStyle = profile.IncludeStyle && options.IncludeStyle;
            filter.IncludeMeta = profile.IncludeMeta && options.IncludeMeta;
            filter.CharacterFirst = profile.CharacterFirst && options.CharacterFirst;
            return filter;
        }
    }

    public sealed class OnnxTaggerProOptions
    {
        public double CopyrightThreshold { get; set; } = 0.24;
        public double StyleThreshold { get; set; } = 0.15;
        public double MetaThreshold { get; set; } = 0.17;
        public double RatingThreshold { get; set; } = 0.41;
        public bool IncludeStyle { get; set; }
        public bool IncludeMeta { get; set; } = true;
        public bool CharacterFirst { get; set; } = true;
    }

    public sealed class TaggerProProfile
    {
        public bool Copyright { get; init; }
        public bool Style { get; init; }
        public bool Meta { get; init; }
        public bool Rating { get; init; }
        public bool IncludeStyle { get; init; }
        public bool IncludeMeta { get; init; }
        public bool CharacterFirst { get; init; }
        public double DefaultCopyright { get; init; } = 0.24;
        public double DefaultStyle { get; init; } = 0.15;
        public double DefaultMeta { get; init; } = 0.17;
        public double DefaultRating { get; init; } = 0.41;
        public bool DefaultIncludeStyle { get; init; }
        public bool DefaultIncludeMeta { get; init; } = true;
        public bool DefaultCharacterFirst { get; init; } = true;

        public bool HasExtraRows => Copyright || Style || Meta || Rating || IncludeStyle || IncludeMeta || CharacterFirst;

        public OnnxTaggerProOptions CreateDefaults()
        {
            return new OnnxTaggerProOptions
            {
                CopyrightThreshold = DefaultCopyright,
                StyleThreshold = DefaultStyle,
                MetaThreshold = DefaultMeta,
                RatingThreshold = DefaultRating,
                IncludeStyle = DefaultIncludeStyle,
                IncludeMeta = DefaultIncludeMeta,
                CharacterFirst = DefaultCharacterFirst
            };
        }

        public static TaggerProProfile For(OnnxTaggerModelEntry entry)
        {
            if (entry == null)
                return Empty;

            if (entry.Kind == OnnxTaggerModelKind.PixAi
                && string.Equals(entry.Id, PixAiOnnxTaggerService.V10Id, StringComparison.OrdinalIgnoreCase))
            {
                return PixAiV1;
            }

            if (entry.Kind == OnnxTaggerModelKind.PixAi)
                return CharacterOnly;

            if (entry.Kind == OnnxTaggerModelKind.Wd14)
            {
                return new TaggerProProfile
                {
                    Rating = true,
                    CharacterFirst = true,
                    IncludeMeta = false,
                    DefaultIncludeMeta = false,
                    DefaultRating = entry.DefaultThreshold
                };
            }

            if (entry.Kind == OnnxTaggerModelKind.ClTagger)
            {
                double threshold = entry.DefaultThreshold;
                return new TaggerProProfile
                {
                    Copyright = true,
                    Meta = true,
                    Rating = true,
                    IncludeMeta = true,
                    CharacterFirst = true,
                    DefaultCopyright = threshold,
                    DefaultMeta = threshold,
                    DefaultRating = threshold,
                    DefaultIncludeMeta = true
                };
            }

            return Empty;
        }

        public static TaggerProProfile Empty { get; } = new TaggerProProfile
        {
            DefaultIncludeMeta = false,
            DefaultCharacterFirst = false
        };

        public static TaggerProProfile CharacterOnly { get; } = new TaggerProProfile
        {
            CharacterFirst = true,
            IncludeMeta = false,
            DefaultIncludeMeta = false
        };

        public static TaggerProProfile PixAiV1 { get; } = new TaggerProProfile
        {
            Copyright = true,
            Style = true,
            Meta = true,
            Rating = true,
            IncludeStyle = true,
            IncludeMeta = true,
            CharacterFirst = true,
            DefaultCopyright = 0.24,
            DefaultStyle = 0.15,
            DefaultMeta = 0.17,
            DefaultRating = 0.41,
            DefaultIncludeStyle = false,
            DefaultIncludeMeta = true,
            DefaultCharacterFirst = true
        };
    }
}
