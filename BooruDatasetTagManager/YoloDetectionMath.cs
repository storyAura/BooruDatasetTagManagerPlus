using System;
using System.Collections.Generic;
using System.Drawing;

namespace BooruDatasetTagManager
{
    public readonly struct YoloLetterbox
    {
        public int InputSize { get; init; }
        public float Scale { get; init; }
        public int PadX { get; init; }
        public int PadY { get; init; }
        public int NewWidth { get; init; }
        public int NewHeight { get; init; }
    }

    /// <summary>
    /// Letterbox / YOLOv8 decode / NMS / aspect expansion. UI-free so the
    /// detector form can stay unlinked from the test project.
    /// </summary>
    public static class YoloDetectionMath
    {
        public const int DefaultInputSize = 640;
        public const float DefaultIou = 0.45f;
        public const byte LetterboxFill = 114;

        public static YoloLetterbox ComputeLetterbox(int sourceWidth, int sourceHeight, int inputSize = DefaultInputSize)
        {
            if (sourceWidth < 1 || sourceHeight < 1 || inputSize < 1)
            {
                return new YoloLetterbox
                {
                    InputSize = Math.Max(1, inputSize),
                    Scale = 1f
                };
            }

            float scale = Math.Min((float)inputSize / sourceWidth, (float)inputSize / sourceHeight);
            int newWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int newHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            if (newWidth > inputSize)
                newWidth = inputSize;
            if (newHeight > inputSize)
                newHeight = inputSize;
            return new YoloLetterbox
            {
                InputSize = inputSize,
                Scale = scale,
                PadX = (inputSize - newWidth) / 2,
                PadY = (inputSize - newHeight) / 2,
                NewWidth = newWidth,
                NewHeight = newHeight
            };
        }

        public static Rectangle MapLetterboxBoxToImage(
            float x1,
            float y1,
            float x2,
            float y2,
            YoloLetterbox map,
            Size image)
        {
            if (map.Scale <= 0f || image.Width < 1 || image.Height < 1)
                return Rectangle.Empty;

            float left = (x1 - map.PadX) / map.Scale;
            float top = (y1 - map.PadY) / map.Scale;
            float right = (x2 - map.PadX) / map.Scale;
            float bottom = (y2 - map.PadY) / map.Scale;
            int ix = (int)Math.Floor(Math.Min(left, right));
            int iy = (int)Math.Floor(Math.Min(top, bottom));
            int ix2 = (int)Math.Ceiling(Math.Max(left, right));
            int iy2 = (int)Math.Ceiling(Math.Max(top, bottom));
            var rect = new Rectangle(ix, iy, ix2 - ix, iy2 - iy);
            return BatchCropMath.Place(rect, image);
        }

        public static float IoU(Rectangle a, Rectangle b)
        {
            int x1 = Math.Max(a.Left, b.Left);
            int y1 = Math.Max(a.Top, b.Top);
            int x2 = Math.Min(a.Right, b.Right);
            int y2 = Math.Min(a.Bottom, b.Bottom);
            int width = Math.Max(0, x2 - x1);
            int height = Math.Max(0, y2 - y1);
            int inter = width * height;
            int union = a.Width * a.Height + b.Width * b.Height - inter;
            return union <= 0 ? 0f : (float)inter / union;
        }

        public static List<(Rectangle Box, float Score)> NonMaxSuppression(
            IReadOnlyList<(Rectangle Box, float Score)> boxes,
            float iouThreshold = DefaultIou)
        {
            var keep = new List<(Rectangle Box, float Score)>();
            if (boxes == null || boxes.Count == 0)
                return keep;

            var pending = new List<(Rectangle Box, float Score)>(boxes);
            pending.Sort((a, b) => b.Score.CompareTo(a.Score));
            bool[] dropped = new bool[pending.Count];
            float iou = iouThreshold <= 0f ? DefaultIou : iouThreshold;

            for (int i = 0; i < pending.Count; i++)
            {
                if (dropped[i])
                    continue;
                keep.Add(pending[i]);
                for (int j = i + 1; j < pending.Count; j++)
                {
                    if (dropped[j])
                        continue;
                    if (IoU(pending[i].Box, pending[j].Box) > iou)
                        dropped[j] = true;
                }
            }

            return keep;
        }

        /// <summary>
        /// Grow <paramref name="bbox"/> around its center to <paramref name="aspectWidth"/>:<paramref name="aspectHeight"/>.
        /// If that rectangle does not fit, shift then shrink so the person stays inside the frame.
        /// </summary>
        public static Rectangle ExpandToAspect(Rectangle bbox, Size image, int aspectWidth, int aspectHeight)
        {
            bbox = BatchCropMath.Place(bbox, image);
            if (bbox.Width < 1 || bbox.Height < 1 || aspectWidth < 1 || aspectHeight < 1)
                return Rectangle.Empty;

            int centerX = bbox.X + bbox.Width / 2;
            int centerY = bbox.Y + bbox.Height / 2;
            double ratio = (double)aspectWidth / aspectHeight;
            int width;
            int height;
            if ((double)bbox.Width / Math.Max(1, bbox.Height) < ratio)
            {
                height = bbox.Height;
                width = Math.Max(1, (int)Math.Round(height * ratio));
            }
            else
            {
                width = bbox.Width;
                height = Math.Max(1, (int)Math.Round(width / ratio));
            }

            var expanded = new Rectangle(centerX - width / 2, centerY - height / 2, width, height);
            return BatchCropMath.ApplyAspect(expanded, image, BatchCropAspect.Preset(aspectWidth, aspectHeight));
        }

        public static List<(Rectangle Box, float Score)> ParseYoloOutput(
            float[] data,
            int[] dimensions,
            float confThreshold,
            YoloLetterbox map,
            Size image)
        {
            var detections = new List<(Rectangle Box, float Score)>();
            if (data == null || data.Length == 0 || image.Width < 1 || image.Height < 1)
                return detections;

            if (!TryDescribeLayout(dimensions, data.Length, out int channels, out int count, out bool channelsFirst))
                return detections;

            float minScore = confThreshold < 0f ? 0f : confThreshold;
            for (int n = 0; n < count; n++)
            {
                float v0 = Read(data, channelsFirst, channels, count, n, 0);
                float v1 = Read(data, channelsFirst, channels, count, n, 1);
                float v2 = Read(data, channelsFirst, channels, count, n, 2);
                float v3 = Read(data, channelsFirst, channels, count, n, 3);
                float score;
                float x1;
                float y1;
                float x2;
                float y2;
                if (channels == 6)
                {
                    score = Read(data, channelsFirst, channels, count, n, 4);
                    x1 = v0;
                    y1 = v1;
                    x2 = v2;
                    y2 = v3;
                }
                else
                {
                    score = channels == 5
                        ? Read(data, channelsFirst, channels, count, n, 4)
                        : MaxClassScore(data, channelsFirst, channels, count, n);
                    x1 = v0 - v2 * 0.5f;
                    y1 = v1 - v3 * 0.5f;
                    x2 = v0 + v2 * 0.5f;
                    y2 = v1 + v3 * 0.5f;
                }

                if (score < minScore)
                    continue;

                Rectangle box = MapLetterboxBoxToImage(x1, y1, x2, y2, map, image);
                if (box.Width < 1 || box.Height < 1)
                    continue;
                detections.Add((box, score));
            }

            return detections;
        }

        private static float MaxClassScore(float[] data, bool channelsFirst, int channels, int count, int n)
        {
            float best = 0f;
            for (int c = 4; c < channels; c++)
            {
                float value = Read(data, channelsFirst, channels, count, n, c);
                if (value > best)
                    best = value;
            }
            return best;
        }

        private static float Read(float[] data, bool channelsFirst, int channels, int count, int n, int c)
        {
            int index = channelsFirst ? c * count + n : n * channels + c;
            return index >= 0 && index < data.Length ? data[index] : 0f;
        }

        private static bool TryDescribeLayout(
            int[] dimensions,
            int length,
            out int channels,
            out int count,
            out bool channelsFirst)
        {
            channels = 0;
            count = 0;
            channelsFirst = true;
            int[] dims = SqueezeLeadingOnes(dimensions, length);
            if (dims.Length == 1)
            {
                if (dims[0] >= 5 && dims[0] <= 90 && length >= dims[0] && length % dims[0] == 0)
                {
                    channels = dims[0];
                    count = length / dims[0];
                    return true;
                }
                return false;
            }

            if (dims.Length == 2)
            {
                int a = dims[0];
                int b = dims[1];
                if (a >= 5 && a <= 90 && a * b <= length)
                {
                    channels = a;
                    count = b;
                    channelsFirst = true;
                    return true;
                }
                if (b >= 5 && b <= 90 && a * b <= length)
                {
                    count = a;
                    channels = b;
                    channelsFirst = false;
                    return true;
                }
            }

            return false;
        }

        private static int[] SqueezeLeadingOnes(int[] dimensions, int length)
        {
            if (dimensions == null || dimensions.Length == 0)
            {
                if (length % 5 == 0 && length / 5 >= 1)
                    return new[] { 5, length / 5 };
                return Array.Empty<int>();
            }

            var squeezed = new List<int>();
            foreach (int dim in dimensions)
            {
                if (dim <= 1 && squeezed.Count == 0)
                    continue;
                squeezed.Add(Math.Max(1, dim));
            }

            if (squeezed.Count == 0)
                squeezed.Add(length);
            return squeezed.ToArray();
        }
    }
}
