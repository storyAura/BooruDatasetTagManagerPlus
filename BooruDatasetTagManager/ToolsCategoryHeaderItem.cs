using System;
using System.Drawing;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Non-clickable Tools-menu section title. A <see cref="ToolStripLabel"/>
    /// (not a disabled <see cref="ToolStripMenuItem"/>): a disabled menu item
    /// as the first dropdown row can swallow the click meant for the command
    /// under it, which made "Replace transparent background" look dead.
    /// Pale accent wash matches tag-category row tints.
    /// </summary>
    internal sealed class ToolsCategoryHeaderItem : ToolStripLabel
    {
        internal static readonly Color ProcessingAccent = Color.FromArgb(70, 130, 180);
        internal static readonly Color TaggingAccent = Color.FromArgb(196, 64, 64);
        internal static readonly Color PreprocessAccent = Color.FromArgb(46, 160, 67);

        private readonly Color accent;

        public ToolsCategoryHeaderItem(string name, Color accent)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            this.accent = accent;
            Enabled = false;
            DisplayStyle = ToolStripItemDisplayStyle.Text;
            Overflow = ToolStripItemOverflow.Never;
            Padding = new Padding(8, 2, 8, 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = new Rectangle(Point.Empty, Size);
            Color back = ResolveColor(BackColor, SystemColors.Menu);
            Color text = ResolveColor(ForeColor, SystemColors.MenuText);
            Color fill = TagSemanticClassifier.ApplyTint(accent, back);

            using (SolidBrush fillBrush = new SolidBrush(fill))
                e.Graphics.FillRectangle(fillBrush, bounds);

            using (Pen hairline = new Pen(Blend(text, back, 0.16f)))
                e.Graphics.DrawLine(hairline, 0, bounds.Height - 1, bounds.Width, bounds.Height - 1);

            Rectangle textBounds = new Rectangle(
                Padding.Left,
                0,
                Math.Max(0, bounds.Width - Padding.Horizontal),
                bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text ?? string.Empty,
                Font,
                textBounds,
                text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static Color ResolveColor(Color color, Color fallback)
        {
            return color.A == 0 ? fallback : color;
        }

        private static Color Blend(Color over, Color under, float amount)
        {
            float rest = 1f - amount;
            return Color.FromArgb(
                (int)(over.R * amount + under.R * rest),
                (int)(over.G * amount + under.G * rest),
                (int)(over.B * amount + under.B * rest));
        }
    }
}
