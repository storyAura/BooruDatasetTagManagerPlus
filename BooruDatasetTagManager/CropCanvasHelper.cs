using System;
using System.Collections.Generic;
using System.Drawing;

namespace BooruDatasetTagManager
{
    public static class CropCanvasHelper
    {
        public static readonly Color[] RegionColors =
        {
            Color.FromArgb(220, 231, 76, 60),
            Color.FromArgb(220, 46, 204, 113),
            Color.FromArgb(220, 52, 152, 219),
            Color.FromArgb(220, 241, 196, 15),
            Color.FromArgb(220, 155, 89, 182),
            Color.FromArgb(220, 230, 126, 34),
        };

        public static float CalcZoomMod(Size imageSize, Size viewportSize)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0)
                return 1f;
            float mod = (float)viewportSize.Height / imageSize.Height;
            if ((int)(mod * imageSize.Width) > viewportSize.Width)
                mod = (float)viewportSize.Width / imageSize.Width;
            return mod;
        }

        public static Rectangle CalcImageLocation(Size imageSize, Size viewportSize)
        {
            float mod = CalcZoomMod(imageSize, viewportSize);
            int w = (int)(mod * imageSize.Width);
            int h = (int)(mod * imageSize.Height);
            int x = w == viewportSize.Width ? 0 : (viewportSize.Width - w) / 2;
            int y = h == viewportSize.Height ? 0 : (viewportSize.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        public static Rectangle ScreenRectToImageRect(Rectangle screenRect, Size imageSize, Size viewportSize)
        {
            Rectangle imgLocation = CalcImageLocation(imageSize, viewportSize);
            Rectangle inter = Rectangle.Intersect(imgLocation, screenRect);
            if (inter.IsEmpty)
                return Rectangle.Empty;
            float mod = CalcZoomMod(imageSize, viewportSize);
            return new Rectangle(
                (int)((inter.X - imgLocation.X) / mod),
                (int)((inter.Y - imgLocation.Y) / mod),
                Math.Max(1, (int)(inter.Width / mod)),
                Math.Max(1, (int)(inter.Height / mod)));
        }

        public static Rectangle ImageRectToScreenRect(Rectangle imageRect, Size imageSize, Size viewportSize)
        {
            Rectangle imgLocation = CalcImageLocation(imageSize, viewportSize);
            float mod = CalcZoomMod(imageSize, viewportSize);
            return new Rectangle(
                imgLocation.X + (int)(imageRect.X * mod),
                imgLocation.Y + (int)(imageRect.Y * mod),
                Math.Max(1, (int)(imageRect.Width * mod)),
                Math.Max(1, (int)(imageRect.Height * mod)));
        }

        public static Rectangle NormalizeDragRectangle(Point start, Point end)
        {
            int x = Math.Min(start.X, end.X);
            int y = Math.Min(start.Y, end.Y);
            return new Rectangle(x, y, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        }

        public static Point ScreenPointToImagePoint(Point screenPoint, Size imageSize, Size viewportSize)
        {
            Rectangle imgLocation = CalcImageLocation(imageSize, viewportSize);
            float mod = CalcZoomMod(imageSize, viewportSize);
            return new Point(
                (int)((screenPoint.X - imgLocation.X) / mod),
                (int)((screenPoint.Y - imgLocation.Y) / mod));
        }

        public static CropRegion HitTest(IReadOnlyList<CropRegion> regions, Point screenPoint, Size imageSize, Size viewportSize)
        {
            Point imagePoint = ScreenPointToImagePoint(screenPoint, imageSize, viewportSize);
            for (int i = regions.Count - 1; i >= 0; i--)
            {
                if (regions[i].Bounds.Contains(imagePoint))
                    return regions[i];
            }
            return null;
        }

        public static Rectangle ClampToImage(Rectangle bounds, Size imageSize)
        {
            int x = Math.Max(0, bounds.X);
            int y = Math.Max(0, bounds.Y);
            int right = Math.Min(imageSize.Width, bounds.Right);
            int bottom = Math.Min(imageSize.Height, bounds.Bottom);
            return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }
    }
}
