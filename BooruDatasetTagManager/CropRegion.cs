using System.Drawing;

namespace BooruDatasetTagManager
{
    public sealed class CropRegion
    {
        public Rectangle Bounds { get; set; }
        public int Index { get; set; }
        public Color DisplayColor { get; set; }
    }
}
