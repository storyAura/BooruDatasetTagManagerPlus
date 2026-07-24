namespace BooruDatasetTagManager.AiApi
{
    /// <summary>
    /// Provider-neutral auto-tagging result item (tag text + confidence).
    /// Formerly nested in the legacy AiApiServer client, which has been
    /// removed; every tagging provider still exchanges results as this type.
    /// </summary>
    public class AutoTagItem
    {
        public string Tag { get; set; }
        public float Confidence { get; set; }

        public AutoTagItem() { }

        public AutoTagItem(string tag, float confidence)
        {
            Tag = tag;
            Confidence = confidence;
        }
    }
}
