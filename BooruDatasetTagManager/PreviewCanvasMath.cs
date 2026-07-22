using System;
using System.Drawing;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Pure zoom/pan math for the floating preview window, kept out of the
    /// Form (repo rule: canvas math lives in static helpers that are linked
    /// into the test project). Zoom is image-pixels → screen-pixels scale;
    /// offset is the screen position of the image's top-left corner.
    /// </summary>
    public static class PreviewCanvasMath
    {
        public const float MinZoom = 0.05f;
        public const float MaxZoom = 32f;

        /// <summary>Scale that makes the whole image fit the viewport.</summary>
        public static float FitZoom(Size viewport, Size image)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0 || image.Width <= 0 || image.Height <= 0)
                return 1f;
            return Math.Min((float)viewport.Width / image.Width, (float)viewport.Height / image.Height);
        }

        /// <summary>Offset that centers the scaled image in the viewport.</summary>
        public static PointF CenterOffset(Size viewport, Size image, float zoom)
        {
            return new PointF(
                (viewport.Width - image.Width * zoom) / 2f,
                (viewport.Height - image.Height * zoom) / 2f);
        }

        /// <summary>
        /// New offset that keeps the image point under <paramref name="cursor"/>
        /// stationary while the zoom changes.
        /// </summary>
        public static PointF ZoomAroundPoint(PointF cursor, PointF offset, float oldZoom, float newZoom)
        {
            if (oldZoom <= 0f)
                return offset;
            float ratio = newZoom / oldZoom;
            return new PointF(
                cursor.X - (cursor.X - offset.X) * ratio,
                cursor.Y - (cursor.Y - offset.Y) * ratio);
        }

        /// <summary>
        /// Keeps the image usable after pan/zoom: an axis smaller than the
        /// viewport is centered, a larger one may not leave a gap at either
        /// edge.
        /// </summary>
        public static PointF ClampOffset(Size viewport, Size image, float zoom, PointF offset)
        {
            return new PointF(
                ClampAxis(offset.X, viewport.Width, image.Width * zoom),
                ClampAxis(offset.Y, viewport.Height, image.Height * zoom));
        }

        public static float ClampZoom(float zoom)
        {
            return Math.Clamp(zoom, MinZoom, MaxZoom);
        }

        private static float ClampAxis(float offset, float viewport, float scaledImage)
        {
            if (scaledImage <= viewport)
                return (viewport - scaledImage) / 2f;
            return Math.Clamp(offset, viewport - scaledImage, 0f);
        }
    }
}
