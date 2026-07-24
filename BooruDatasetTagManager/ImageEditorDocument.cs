using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Mutable image state behind <see cref="Form_ImageEditor"/>: the current
    /// bitmap plus bounded undo/redo snapshots. UI-free so the crop/rotate/
    /// stroke/undo semantics can be unit tested.
    /// </summary>
    public sealed class ImageEditorDocument : IDisposable
    {
        // Full-bitmap snapshots at ~4 bytes/pixel. Steps alone are no bound —
        // 15 clones of a 24MP image is ~1.4GB — so a byte budget trims the
        // oldest snapshots first, always keeping at least one so undo works.
        private const int MaxUndoSteps = 15;
        private const long MaxUndoBytes = 512L * 1024 * 1024;

        private readonly List<Snapshot> undoStack = new List<Snapshot>();
        private readonly List<Snapshot> redoStack = new List<Snapshot>();
        private int revisionCounter;
        private int currentRevision;

        public ImageEditorDocument(Bitmap initialImage)
        {
            Image = initialImage ?? throw new ArgumentNullException(nameof(initialImage));
        }

        public Bitmap Image { get; private set; }

        public bool CanUndo => undoStack.Count > 0;

        public bool CanRedo => redoStack.Count > 0;

        /// <summary>True when the current pixels differ from the opened file.</summary>
        public bool IsDirty => currentRevision != 0;

        /// <summary>Snapshots the current image once; the following
        /// <see cref="DrawStrokeSegment"/> calls mutate it directly so a whole
        /// stroke undoes as a single step.</summary>
        public void BeginStroke()
        {
            PushUndo();
        }

        public void DrawStrokeSegment(Point from, Point to, Color color, float width, bool erase, bool eraseToTransparent)
        {
            using Graphics graphics = Graphics.FromImage(Image);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color strokeColor = erase
                ? (eraseToTransparent ? Color.Transparent : Color.White)
                : color;
            if (erase && eraseToTransparent)
                graphics.CompositingMode = CompositingMode.SourceCopy;
            if (from == to)
            {
                float radius = width / 2f;
                using var brush = new SolidBrush(strokeColor);
                graphics.FillEllipse(brush, to.X - radius, to.Y - radius, width, width);
                return;
            }
            using var pen = new Pen(strokeColor, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawLine(pen, from, to);
        }

        /// <summary>Crops to <paramref name="imageRect"/> (clamped to the image).
        /// Returns false when the rectangle is empty or covers the whole image.</summary>
        public bool ApplyCrop(Rectangle imageRect)
        {
            Rectangle full = new Rectangle(Point.Empty, Image.Size);
            Rectangle bounds = Rectangle.Intersect(imageRect, full);
            if (bounds.Width < 1 || bounds.Height < 1 || bounds == full)
                return false;
            PushUndo();
            Bitmap cropped = Image.Clone(bounds, Image.PixelFormat);
            Bitmap old = Image;
            Image = cropped;
            old.Dispose();
            return true;
        }

        public void RotateFlip(RotateFlipType type)
        {
            PushUndo();
            Image.RotateFlip(type);
        }

        public bool Undo()
        {
            if (!CanUndo)
                return false;
            Snapshot snapshot = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            redoStack.Add(new Snapshot((Bitmap)Image.Clone(), currentRevision));
            Restore(snapshot);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo)
                return false;
            Snapshot snapshot = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            undoStack.Add(new Snapshot((Bitmap)Image.Clone(), currentRevision));
            Restore(snapshot);
            return true;
        }

        private void Restore(Snapshot snapshot)
        {
            Bitmap old = Image;
            Image = snapshot.Bitmap;
            currentRevision = snapshot.Revision;
            old.Dispose();
        }

        private void PushUndo()
        {
            undoStack.Add(new Snapshot((Bitmap)Image.Clone(), currentRevision));
            // ponytail: budget covers the undo stack only; redo holds at most
            // what Undo moved over and is cleared on the next edit.
            while (undoStack.Count > MaxUndoSteps
                || (undoStack.Count > 1 && TotalUndoBytes() > MaxUndoBytes))
            {
                undoStack[0].Dispose();
                undoStack.RemoveAt(0);
            }
            foreach (Snapshot snapshot in redoStack)
                snapshot.Dispose();
            redoStack.Clear();
            currentRevision = ++revisionCounter;
        }

        private long TotalUndoBytes()
        {
            long total = 0;
            foreach (Snapshot snapshot in undoStack)
                total += snapshot.Bytes;
            return total;
        }

        public void Dispose()
        {
            foreach (Snapshot snapshot in undoStack)
                snapshot.Dispose();
            undoStack.Clear();
            foreach (Snapshot snapshot in redoStack)
                snapshot.Dispose();
            redoStack.Clear();
            Image?.Dispose();
            Image = null;
        }

        private sealed class Snapshot : IDisposable
        {
            public Snapshot(Bitmap bitmap, int revision)
            {
                Bitmap = bitmap;
                Revision = revision;
                // Cached at creation: the budget check must not touch a
                // possibly-disposed bitmap later.
                Bytes = bitmap == null ? 0 : 4L * bitmap.Width * bitmap.Height;
            }

            public Bitmap Bitmap { get; }
            public int Revision { get; }
            public long Bytes { get; }

            public void Dispose() => Bitmap?.Dispose();
        }
    }
}
