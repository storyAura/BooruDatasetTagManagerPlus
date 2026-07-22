using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Semantic tag buckets for the light color-coding and the category
    /// sort. Declaration order doubles as the sort rank: identity first
    /// (character/copyright), then subject count, appearance, clothing,
    /// action, scene; General and Meta last. General carries no tint.
    /// </summary>
    public enum TagSemanticCategory
    {
        Character = 0,
        Copyright,
        Artist,
        SubjectCount,
        Hair,
        Eyes,
        Body,
        Expression,
        Clothing,
        Accessory,
        Object,
        Animal,
        Food,
        Action,
        Composition,
        Background,
        Style,
        General,
        Meta
    }

    /// <summary>
    /// Rule-based tag classifier. The danbooru tag type (column 2 of the
    /// autocomplete CSVs — 0 general, 1 artist, 3 copyright, 4 character,
    /// 5 meta — retained by TagsDB since v102) decides the identity buckets
    /// exactly; general/unknown tags fall through to token/suffix
    /// heuristics. Pure static, no Program.* references: linked into the
    /// test project.
    /// </summary>
    public static class TagSemanticClassifier
    {
        private static readonly Regex SubjectCountPattern = new Regex(
            @"^\d+\+?\s?(girls?|boys?|others?)$", RegexOptions.Compiled);

        private static readonly HashSet<string> SubjectExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "solo", "multiple girls", "multiple boys", "everyone", "no humans", "solo focus", "group"
        };

        private static readonly HashSet<string> MetaExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "highres", "absurdres", "lowres", "traditional media", "official art", "watermark",
            "signature", "artist name", "dated", "commentary", "english commentary",
            "chinese commentary", "translation request", "translated", "scan", "jpeg artifacts",
            "bad id", "bad pixiv id", "duplicate", "virtual youtuber"
        };

        // Checked before Hair/Clothing so "hair ornament" / "hair bow" land here.
        private static readonly HashSet<string> AccessoryLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "ornament", "hairclip", "hairband", "hairpin", "headband", "headphones", "headdress",
            "headwear", "hat", "cap", "beret", "crown", "tiara", "halo", "glasses", "sunglasses",
            "eyewear", "earring", "earrings", "jewelry", "necklace", "pendant", "bracelet",
            "choker", "bag", "backpack", "handbag", "mask", "ribbon", "bow", "scrunchie",
            "bell", "umbrella", "eyepatch", "hairpods"
        };

        private static readonly HashSet<string> EyesLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "eyes", "eye", "pupils", "sclera", "eyelashes", "eyebrows", "eyeshadow", "eyeliner"
        };

        private static readonly HashSet<string> EyesExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "heterochromia"
        };

        private static readonly HashSet<string> HairLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "hair", "hairstyle", "bangs", "braid", "braids", "ponytail", "twintails", "twintail",
            "drills", "bun", "ahoge", "sidelocks", "intakes", "forelocks", "dreadlocks"
        };

        private static readonly HashSet<string> HairExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "hime cut", "bob cut", "pixie cut", "wolf cut", "jellyfish cut", "buzz cut"
        };

        // Expression is matched before Body so "open mouth" wins over mouth-ish tokens.
        private static readonly HashSet<string> ExpressionExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "open mouth", "closed mouth", "parted lips", "tongue out", "one eye closed",
            "smile", "blush", "frown", "grin", "smirk", "wink", "crying", "tears", "angry",
            "sad", "happy", "embarrassed", "expressionless", "pout", "laughing", "surprised",
            "scared", "annoyed", "light smile", "evil smile"
        };

        private static readonly HashSet<string> BodyAnyToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "breasts", "skin", "tail", "tails", "ears", "horns", "horn", "wings", "wing",
            "fang", "fangs", "teeth", "tongue", "navel", "collarbone", "thighs", "shoulders",
            "freckles", "scar", "tattoo", "cleavage", "abs", "muscles", "mole", "feet",
            "fingernails", "lips"
        };

        private static readonly HashSet<string> BodyExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "barefoot", "tan", "tanlines", "dark skin", "pale skin"
        };

        private static readonly HashSet<string> ClothingLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "shirt", "skirt", "dress", "jacket", "coat", "sweater", "hoodie", "uniform",
            "serafuku", "bikini", "swimsuit", "leotard", "pantyhose", "thighhighs", "kneehighs",
            "socks", "shoes", "boots", "sneakers", "sandals", "heels", "gloves", "sleeves",
            "pants", "shorts", "jeans", "kimono", "yukata", "apron", "vest", "cardigan",
            "blouse", "camisole", "bra", "panties", "underwear", "necktie", "bowtie", "tie",
            "belt", "cape", "cloak", "costume", "outfit", "clothes", "frills", "collar",
            "footwear", "hood", "obi", "hakama", "sash", "top", "legwear", "armband", "bodysuit"
        };

        private static readonly HashSet<string> ClothingExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "sleeveless", "off shoulder", "midriff", "zettai ryouiki"
        };

        private static readonly HashSet<string> ActionFirstToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "holding", "looking", "wearing", "carrying", "riding"
        };

        private static readonly HashSet<string> ActionAnyToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "sitting", "standing", "lying", "walking", "running", "jumping", "hug", "hugging",
            "pointing", "waving", "stretching", "leaning", "kneeling", "squatting", "reading",
            "eating", "drinking", "sleeping", "dancing", "singing", "gesture", "sign",
            "hand", "hands", "arm", "arms", "pose", "posing", "salute", "clenched"
        };

        // Checked before Animal so "stuffed animal" stays a prop.
        private static readonly HashSet<string> ObjectExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "stuffed animal", "stuffed toy", "teddy bear"
        };

        private static readonly HashSet<string> ObjectLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "weapon", "sword", "katana", "gun", "rifle", "pistol", "knife", "dagger", "staff",
            "wand", "shield", "spear", "instrument", "guitar", "piano", "violin",
            "viola", "cello", "flute", "drum", "keyboard", "microphone", "phone", "smartphone",
            "cellphone", "book", "cup", "mug", "bottle", "plate", "fork", "spoon", "chopsticks",
            "pen", "pencil", "paintbrush", "controller", "laptop", "computer", "camera",
            "basket", "box", "doll", "plush", "plushie", "balloon", "flower", "flowers",
            "bouquet", "pillow", "chair", "table", "desk", "letter", "card", "flag", "banner",
            "broom", "clock", "watch", "lantern", "candle", "toy"
        };

        private static readonly HashSet<string> AnimalLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "animal", "cat", "dog", "bird", "rabbit", "bunny", "fox", "wolf", "bear", "horse",
            "fish", "butterfly", "penguin", "hamster", "mouse", "snake", "dragon", "cow",
            "pig", "frog", "chick", "chicken", "duck", "deer", "sheep", "lion", "tiger",
            "panda", "owl", "bee", "spider", "turtle", "whale", "shark", "dolphin", "bat"
        };

        private static readonly HashSet<string> FoodLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "food", "cake", "bread", "fruit", "apple", "strawberry", "banana", "candy",
            "chocolate", "cookie", "tea", "coffee", "juice", "ramen", "sushi", "onigiri",
            "bento", "pizza", "burger", "hamburger", "pocky", "dango", "taiyaki", "drink",
            "beverage", "rice", "soup", "sandwich", "donut", "doughnut", "pudding", "parfait",
            "crepe", "lollipop", "popsicle"
        };

        private static readonly HashSet<string> FoodExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "ice cream", "cotton candy", "shaved ice"
        };

        private static readonly HashSet<string> CompositionExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "upper body", "lower body", "full body", "cowboy shot", "portrait", "close-up",
            "wide shot", "dutch angle", "pov", "straight-on", "depth of field", "blurry",
            "foreshortening", "out of frame", "cropped", "profile", "face", "head tilt"
        };

        private static readonly HashSet<string> StyleExact = new HashSet<string>(StringComparer.Ordinal)
        {
            "monochrome", "greyscale", "grayscale", "sketch", "lineart", "chibi", "comic",
            "4koma", "pixel art", "halftone", "limited palette", "flat color", "no lineart",
            "retro artstyle", "watercolor (medium)", "spot color", "high contrast"
        };

        private static readonly HashSet<string> BackgroundLastToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "background", "scenery"
        };

        private static readonly HashSet<string> BackgroundAnyToken = new HashSet<string>(StringComparer.Ordinal)
        {
            "outdoors", "indoors", "sky", "night", "day", "sunlight", "moon", "beach",
            "snow", "rain", "sunset", "cityscape"
        };

        /// <summary>
        /// danbooru tag types: 0 general, 1 artist, 3 copyright, 4 character,
        /// 5 meta; pass -1 when unknown.
        /// </summary>
        public static TagSemanticCategory Classify(string tag, int danbooruType)
        {
            switch (danbooruType)
            {
                case 1:
                    return TagSemanticCategory.Artist;
                case 3:
                    return TagSemanticCategory.Copyright;
                case 4:
                    return TagSemanticCategory.Character;
                case 5:
                    return TagSemanticCategory.Meta;
            }

            string normalized = Normalize(tag);
            if (normalized.Length == 0)
                return TagSemanticCategory.General;
            if (SubjectCountPattern.IsMatch(normalized) || SubjectExact.Contains(normalized))
                return TagSemanticCategory.SubjectCount;
            if (MetaExact.Contains(normalized))
                return TagSemanticCategory.Meta;

            string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return TagSemanticCategory.General;
            string lastToken = tokens[tokens.Length - 1];

            if (AccessoryLastToken.Contains(lastToken))
                return TagSemanticCategory.Accessory;
            if (EyesLastToken.Contains(lastToken) || EyesExact.Contains(normalized))
                return TagSemanticCategory.Eyes;
            if (HairLastToken.Contains(lastToken) || HairExact.Contains(normalized))
                return TagSemanticCategory.Hair;
            if (ExpressionExact.Contains(normalized))
                return TagSemanticCategory.Expression;
            if (BodyExact.Contains(normalized) || ContainsAny(tokens, BodyAnyToken))
                return TagSemanticCategory.Body;
            if (ClothingLastToken.Contains(lastToken) || ClothingExact.Contains(normalized))
                return TagSemanticCategory.Clothing;
            if (ObjectExact.Contains(normalized) || ObjectLastToken.Contains(lastToken))
                return TagSemanticCategory.Object;
            if (AnimalLastToken.Contains(lastToken))
                return TagSemanticCategory.Animal;
            if (FoodExact.Contains(normalized) || FoodLastToken.Contains(lastToken))
                return TagSemanticCategory.Food;
            if (ActionFirstToken.Contains(tokens[0]) || ContainsAny(tokens, ActionAnyToken))
                return TagSemanticCategory.Action;
            if (CompositionExact.Contains(normalized) || tokens[0] == "from")
                return TagSemanticCategory.Composition;
            if (BackgroundLastToken.Contains(lastToken) || ContainsAny(tokens, BackgroundAnyToken))
                return TagSemanticCategory.Background;
            if (StyleExact.Contains(normalized))
                return TagSemanticCategory.Style;
            return TagSemanticCategory.General;
        }

        /// <summary>Accent hue for a category; null = no tint (General).</summary>
        public static Color? GetAccent(TagSemanticCategory category)
        {
            switch (category)
            {
                case TagSemanticCategory.Character: return Color.FromArgb(46, 160, 67);
                case TagSemanticCategory.Copyright: return Color.FromArgb(163, 79, 163);
                case TagSemanticCategory.Artist: return Color.FromArgb(196, 64, 64);
                case TagSemanticCategory.SubjectCount: return Color.FromArgb(70, 130, 180);
                case TagSemanticCategory.Hair: return Color.FromArgb(218, 165, 32);
                case TagSemanticCategory.Eyes: return Color.FromArgb(0, 153, 153);
                case TagSemanticCategory.Body: return Color.FromArgb(233, 116, 81);
                case TagSemanticCategory.Expression: return Color.FromArgb(214, 191, 0);
                case TagSemanticCategory.Clothing: return Color.FromArgb(219, 112, 147);
                case TagSemanticCategory.Accessory: return Color.FromArgb(138, 103, 220);
                case TagSemanticCategory.Object: return Color.FromArgb(181, 137, 84);
                case TagSemanticCategory.Animal: return Color.FromArgb(64, 178, 128);
                case TagSemanticCategory.Food: return Color.FromArgb(204, 120, 44);
                case TagSemanticCategory.Action: return Color.FromArgb(122, 158, 63);
                case TagSemanticCategory.Composition: return Color.FromArgb(100, 110, 200);
                case TagSemanticCategory.Background: return Color.FromArgb(120, 144, 168);
                case TagSemanticCategory.Style: return Color.FromArgb(150, 120, 170);
                case TagSemanticCategory.Meta: return Color.FromArgb(230, 140, 20);
                default: return null;
            }
        }

        /// <summary>
        /// Light tint: the accent blended over the current cell background,
        /// so light and dark schemes both get a subtle wash.
        /// </summary>
        public static Color ApplyTint(Color accent, Color background)
        {
            const float amount = 0.18f;
            const float rest = 1f - amount;
            return Color.FromArgb(
                (int)(accent.R * amount + background.R * rest),
                (int)(accent.G * amount + background.G * rest),
                (int)(accent.B * amount + background.B * rest));
        }

        private static bool ContainsAny(string[] tokens, HashSet<string> set)
        {
            foreach (string token in tokens)
            {
                if (set.Contains(token))
                    return true;
            }
            return false;
        }

        private static string Normalize(string tag)
        {
            return (tag ?? string.Empty).Trim().ToLowerInvariant().Replace('_', ' ');
        }
    }
}
