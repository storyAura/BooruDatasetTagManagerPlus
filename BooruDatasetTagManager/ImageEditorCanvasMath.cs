using System;
using System.Drawing;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Pure screen↔image coordinate math of the image editor canvas (zoom +
    /// pan transform). Kept UI-free, like <see cref="CropCanvasHelper"/>, so
    /// the mapping rules are unit-testable.
    /// </summary>
    public static class ImageEditorCanvasMath
    {
        /// <summary>Maps a screen point to the image pixel it lies on,
        /// clamped to the image bounds.</summary>
        public static Point ScreenPointToImagePixel(Point screenPoint, float zoom, PointF offset, Size imageSize)
        {
            int x = (int)Math.Floor((screenPoint.X - offset.X) / zoom);
            int y = (int)Math.Floor((screenPoint.Y - offset.Y) / zoom);
            return new Point(
                Math.Clamp(x, 0, imageSize.Width - 1),
                Math.Clamp(y, 0, imageSize.Height - 1));
        }

        /// <summary>
        /// Maps a screen-space selection rectangle to the covered image pixels.
        /// </summary>
        public static Rectangle ScreenRectToImageRect(Rectangle screenRect, float zoom, PointF offset, Size imageSize)
        {
            if (screenRect.Width < 1 || screenRect.Height < 1)
                return Rectangle.Empty;
            Point topLeft = ScreenPointToImagePixel(screenRect.Location, zoom, offset, imageSize);
            // Right/Bottom are exclusive bounds, so the last covered screen
            // pixel is one before them. Mapping the exclusive bound itself
            // through the inclusive floor mapping would inflate the selection
            // by one image pixel at every integral zoom level.
            Point bottomRight = ScreenPointToImagePixel(
                new Point(screenRect.Right - 1, screenRect.Bottom - 1), zoom, offset, imageSize);
            var rect = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X + 1, bottomRight.Y + 1);
            return Rectangle.Intersect(rect, new Rectangle(Point.Empty, imageSize));
        }
    }
}
