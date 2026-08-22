using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace BooruDatasetTagManager
{
    public enum BatchCropAspectKind
    {
        Free,
        Original,
        Custom,
        Preset
    }

    public readonly struct BatchCropAspect
    {
        public BatchCropAspectKind Kind { get; }
        public int Numerator { get; }
        public int Denominator { get; }

        public bool IsFree => Kind == BatchCropAspectKind.Free || Denominator <= 0 || Numerator <= 0;

        public double WidthOverHeight => IsFree ? 0 : (double)Numerator / Denominator;

        private BatchCropAspect(BatchCropAspectKind kind, int numerator, int denominator)
        {
            Kind = kind;
            Numerator = numerator;
            Denominator = denominator;
        }

        public static BatchCropAspect Free { get; } = new BatchCropAspect(BatchCropAspectKind.Free, 0, 0);

        public static BatchCropAspect Original(int width, int height)
        {
            return new BatchCropAspect(BatchCropAspectKind.Original, Math.Max(1, width), Math.Max(1, height));
        }

        public static BatchCropAspect Custom(int width, int height)
        {
            return new BatchCropAspect(BatchCropAspectKind.Custom, Math.Max(1, width), Math.Max(1, height));
        }

        public static BatchCropAspect Preset(int width, int height)
        {
            return new BatchCropAspect(BatchCropAspectKind.Preset, Math.Max(1, width), Math.Max(1, height));
        }
    }

    public enum BatchCropHandle
    {
        None,
        Move,
        N,
        S,
        E,
        W,
        NE,
        NW,
        SE,
        SW
    }

    /// <summary>
    /// Pixel-space crop geometry for the batch-crop window. Kept UI-free so
    /// the form can stay unlinked from the test project.
    /// </summary>
    public static class BatchCropMath
    {
        public const int MinSize = 8;

        public static readonly (int Width, int Height)[] Presets =
        {
            (1, 1), (1, 2), (2, 1), (2, 3), (3, 2), (3, 4), (4, 3), (16, 9), (9, 16)
        };

        public static Rectangle FromDrag(Point start, Point end, Size image, BatchCropAspect aspect)
        {
            if (image.Width <= 0 || image.Height <= 0)
                return Rectangle.Empty;

            start = ClampPoint(start, image);
            int dx = end.X - start.X;
            int dy = end.Y - start.Y;
            int width = Math.Abs(dx);
            int height = Math.Abs(dy);
            if (width < 1 && height < 1)
                return Rectangle.Empty;

            if (!aspect.IsFree)
            {
                double ratio = aspect.WidthOverHeight;
                if (height < 1 || (width >= 1 && (double)width / Math.Max(height, 1) >= ratio))
                {
                    width = Math.Max(width, MinSize);
                    height = Math.Max(MinSize, (int)Math.Round(width / ratio));
                }
                else
                {
                    height = Math.Max(height, MinSize);
                    width = Math.Max(MinSize, (int)Math.Round(height * ratio));
                }
            }
            else
            {
                width = Math.Max(1, width);
                height = Math.Max(1, height);
            }

            bool right = dx >= 0;
            bool down = dy >= 0;
            int maxWidth = right ? image.Width - start.X : start.X;
            int maxHeight = down ? image.Height - start.Y : start.Y;
            if (width > maxWidth || height > maxHeight)
                ConstrainToAvailable(ref width, ref height, maxWidth, maxHeight, aspect);

            int x = right ? start.X : start.X - width;
            int y = down ? start.Y : start.Y - height;
            return Place(new Rectangle(x, y, width, height), image);
        }

        public static Rectangle ApplyAspect(Rectangle current, Size image, BatchCropAspect aspect)
        {
            current = Place(current.IsEmpty ? new Rectangle(0, 0, image.Width, image.Height) : current, image);
            if (aspect.IsFree)
                return current;

            double ratio = aspect.WidthOverHeight;
            int width = Math.Max(MinSize, current.Width);
            int height = Math.Max(MinSize, (int)Math.Round(width / ratio));
            ConstrainToAvailable(ref width, ref height, image.Width, image.Height, aspect);

            int centerX = current.X + current.Width / 2;
            int centerY = current.Y + current.Height / 2;
            return Place(new Rectangle(centerX - width / 2, centerY - height / 2, width, height), image);
        }

        public static Rectangle Move(Rectangle rect, int deltaX, int deltaY, Size image)
        {
            if (rect.Width > image.Width || rect.Height > image.Height)
                rect = Place(rect, image);
            int x = Clamp(rect.X + deltaX, 0, Math.Max(0, image.Width - rect.Width));
            int y = Clamp(rect.Y + deltaY, 0, Math.Max(0, image.Height - rect.Height));
            return new Rectangle(x, y, rect.Width, rect.Height);
        }

        public static Rectangle Place(Rectangle rect, Size image)
        {
            if (image.Width <= 0 || image.Height <= 0)
                return Rectangle.Empty;
            int width = Math.Min(Math.Max(0, rect.Width), image.Width);
            int height = Math.Min(Math.Max(0, rect.Height), image.Height);
            int x = Clamp(rect.X, 0, image.Width - width);
            int y = Clamp(rect.Y, 0, image.Height - height);
            return new Rectangle(x, y, width, height);
        }

        public static Rectangle SetSize(Rectangle rect, int width, int height, Size image, BatchCropAspect aspect)
        {
            width = Math.Max(MinSize, width);
            height = Math.Max(MinSize, height);
            if (!aspect.IsFree)
            {
                double ratio = aspect.WidthOverHeight;
                height = Math.Max(MinSize, (int)Math.Round(width / ratio));
            }
            ConstrainToAvailable(ref width, ref height, image.Width, image.Height, aspect);
            return Place(new Rectangle(rect.X, rect.Y, width, height), image);
        }

        public static Rectangle SetPosition(Rectangle rect, int x, int y, Size image)
        {
            return Place(new Rectangle(x, y, rect.Width, rect.Height), image);
        }

        public static Rectangle Resize(Rectangle rect, BatchCropHandle handle, Point imagePoint, Size image, BatchCropAspect aspect)
        {
            Point start;
            Point end = imagePoint;
            switch (handle)
            {
                case BatchCropHandle.SE:
                    start = new Point(rect.Left, rect.Top);
                    break;
                case BatchCropHandle.SW:
                    start = new Point(rect.Right, rect.Top);
                    break;
                case BatchCropHandle.NE:
                    start = new Point(rect.Left, rect.Bottom);
                    break;
                case BatchCropHandle.NW:
                    start = new Point(rect.Right, rect.Bottom);
                    break;
                case BatchCropHandle.E:
                    start = new Point(rect.Left, rect.Top);
                    end = new Point(imagePoint.X, rect.Bottom);
                    break;
                case BatchCropHandle.W:
                    start = new Point(rect.Right, rect.Top);
                    end = new Point(imagePoint.X, rect.Bottom);
                    break;
                case BatchCropHandle.S:
                    start = new Point(rect.Left, rect.Top);
                    end = new Point(rect.Right, imagePoint.Y);
                    break;
                case BatchCropHandle.N:
                    start = new Point(rect.Left, rect.Bottom);
                    end = new Point(rect.Right, imagePoint.Y);
                    break;
                default:
                    return rect;
            }
            Rectangle next = FromDrag(start, end, image, aspect);
            return next.Width < MinSize || next.Height < MinSize ? rect : next;
        }

        public static IReadOnlyList<string> FilterSameSize(
            IEnumerable<(string Path, Size? Size)> items,
            Size reference)
        {
            var kept = new List<string>();
            if (items == null || reference.Width <= 0 || reference.Height <= 0)
                return kept;
            foreach ((string path, Size? size) in items)
            {
                if (string.IsNullOrEmpty(path) || VideoProcessingService.IsVideoFile(path))
                    continue;
                if (size is Size actual && actual == reference)
                    kept.Add(path);
            }
            return kept;
        }

        private static void ConstrainToAvailable(
            ref int width,
            ref int height,
            int maxWidth,
            int maxHeight,
            BatchCropAspect aspect)
        {
            maxWidth = Math.Max(0, maxWidth);
            maxHeight = Math.Max(0, maxHeight);
            if (aspect.IsFree)
            {
                width = Math.Min(width, maxWidth);
                height = Math.Min(height, maxHeight);
                return;
            }

            double ratio = aspect.WidthOverHeight;
            int fitWidth = Math.Min(width, maxWidth);
            int fitHeight = Math.Min(height, maxHeight);
            if (fitWidth <= 0 || fitHeight <= 0)
            {
                width = 0;
                height = 0;
                return;
            }

            if (fitWidth / ratio > fitHeight)
            {
                height = Math.Max(1, fitHeight);
                width = Math.Max(1, (int)Math.Round(height * ratio));
                if (width > maxWidth)
                {
                    width = Math.Max(1, maxWidth);
                    height = Math.Max(1, (int)Math.Round(width / ratio));
                }
            }
            else
            {
                width = Math.Max(1, fitWidth);
                height = Math.Max(1, (int)Math.Round(width / ratio));
                if (height > maxHeight)
                {
                    height = Math.Max(1, maxHeight);
                    width = Math.Max(1, (int)Math.Round(height * ratio));
                }
            }
        }

        private static Point ClampPoint(Point point, Size image)
        {
            return new Point(
                Clamp(point.X, 0, Math.Max(0, image.Width)),
                Clamp(point.Y, 0, Math.Max(0, image.Height)));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min)
                return min;
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
