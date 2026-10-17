using System.Drawing;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Point = System.Drawing.Point;

namespace IslandDesk.Themes;

internal sealed class TrayThemeRenderer : Forms.ToolStripProfessionalRenderer
{
    private readonly MenuColors _colors;
    private DrawingColor _text;
    private DrawingColor _disabled;
    internal TrayThemeRenderer() : this(new MenuColors()) { }
    private TrayThemeRenderer(MenuColors colors) : base(colors) { _colors = colors; RoundedEdges = false; }
    internal void Refresh(Forms.ContextMenuStrip menu)
    {
        static DrawingColor Token(string key)
        {
            var color = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            return DrawingColor.FromArgb(color.R, color.G, color.B);
        }
        _text = Token("Primary"); _disabled = Token("Muted"); _colors.Surface = Token("PopupSurface");
        _colors.Hover = Token("PopupHover"); _colors.Border = Token("PopupBorder");
        _colors.Pressed = Token("ControlPressed");
        void RefreshItems(Forms.ToolStrip strip)
        {
            if (!ReferenceEquals(strip.Renderer, this)) strip.Renderer = this;
            strip.BackColor = _colors.Surface; strip.ForeColor = _text;
            if (strip is Forms.ToolStripDropDownMenu dropDown) { dropDown.ShowImageMargin = false; dropDown.ShowCheckMargin = true; }
            foreach (Forms.ToolStripItem item in strip.Items)
            {
                item.ForeColor = _text; item.BackColor = _colors.Surface;
                if (item is Forms.ToolStripMenuItem { HasDropDownItems: true } submenu) RefreshItems(submenu.DropDown);
            }
            strip.Invalidate();
        }
        RefreshItems(menu);
    }
    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e) { e.ArrowColor = e.Item?.Enabled != false ? _text : _disabled; base.OnRenderArrow(e); }
    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    { using var brush = new SolidBrush(_colors.Surface); e.Graphics.FillRectangle(brush, e.AffectedBounds); }
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle; var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(e.Item.Enabled ? _text : _disabled, 1.7f * scale)
            { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            e.Graphics.DrawLines(pen, new[] { new PointF(center.X - 4 * scale, center.Y),
                new PointF(center.X - scale, center.Y + 3 * scale), new PointF(center.X + 5 * scale, center.Y - 4 * scale) });
        }
        finally { e.Graphics.Restore(state); }
    }
    // A WPF host may not enable WinForms visual styles. The base professional
    // renderer then delegates selection painting to its system-color fallback.
    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    { using var brush = new SolidBrush(_colors.Surface); e.Graphics.FillRectangle(brush, e.AffectedBounds); }
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        using (var surface = new SolidBrush(_colors.Surface)) e.Graphics.FillRectangle(surface, new Rectangle(Point.Empty, e.Item.Size));
        if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed)) return;
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var bounds = new RectangleF(2 * scale, scale, e.Item.Width - 4 * scale, e.Item.Height - 2 * scale);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var diameter = Math.Min(8 * scale, Math.Min(bounds.Width, bounds.Height));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(e.Item.Pressed ? _colors.Pressed : _colors.Hover);
            e.Graphics.FillPath(brush, path);
        }
        finally { e.Graphics.Restore(state); }
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) =>
        Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.Item.Enabled ? _text : _disabled, e.TextFormat);
    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    { using var pen = new Pen(_colors.Border); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    { using var pen = new Pen(_colors.Border); e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2); }
    private sealed class MenuColors : Forms.ProfessionalColorTable
    {
        internal DrawingColor Surface, Hover, Border, Pressed;
        public MenuColors() => UseSystemColors = false;
        public override DrawingColor ToolStripDropDownBackground => Surface;
        public override DrawingColor ImageMarginGradientBegin => Surface;
        public override DrawingColor ImageMarginGradientMiddle => Surface;
        public override DrawingColor ImageMarginGradientEnd => Surface;
        public override DrawingColor MenuItemSelected => Hover;
        public override DrawingColor MenuItemBorder => Border;
        public override DrawingColor MenuBorder => Border;
        public override DrawingColor MenuItemSelectedGradientBegin => Hover;
        public override DrawingColor MenuItemSelectedGradientEnd => Hover;
        public override DrawingColor MenuItemPressedGradientBegin => Hover;
        public override DrawingColor MenuItemPressedGradientEnd => Hover;
        public override DrawingColor SeparatorDark => Border;
        public override DrawingColor SeparatorLight => Surface;
    }
}
