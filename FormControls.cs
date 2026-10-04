using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowSwitch {
    // Shared Scheme A form controls. Everything is laid out in device pixels derived from DeviceDpi,
    // never scaled after rendering, and text is drawn with GDI (TextRenderer) at full opacity.
    public static class Dpi {
        public const int FieldHeight = 36, FieldRadius = 10;
        public static float Scale(Control c) { return c.DeviceDpi / 96f; }
        public static int Px(Control c, float logical) { return (int)Math.Round(logical * c.DeviceDpi / 96f); }
        // Fill and stroke share one half-pixel-aligned path so the 1 px border lands on whole device pixels.
        public static void Field(Graphics g, Control c, Color fill, Color border, bool strong) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = strong ? Math.Max(1f, (float)Math.Round(1.2f * Scale(c))) : 1f, inset = w / 2f;
            using (var path = Theme.Round(new RectangleF(inset, inset, c.Width - w, c.Height - w), Px(c, FieldRadius)))
            using (var brush = new SolidBrush(fill)) using (var pen = new Pen(border, w)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
        }
        public static void FocusRing(Graphics g, Control c, int radius) {
            g.SmoothingMode = SmoothingMode.AntiAlias; float w = Math.Max(1f, (float)Math.Round(1.2f * Scale(c)));
            using (var pen = new Pen(Theme.Current.Focus, w)) using (var path = Theme.Round(new RectangleF(w / 2, w / 2, c.Width - w - 1, c.Height - w - 1), radius)) g.DrawPath(pen, path);
        }
        public static void Chevron(Graphics g, Control c, Point center, bool down, Color color, bool left = false) {
            g.SmoothingMode = SmoothingMode.AntiAlias; float s = Scale(c), a = (left ? 5f : 3.5f) * s, b = (left ? 3f : 2f) * s;
            if (left) { b = -b; }
            using (var pen = new Pen(color, Math.Max(1.2f, 1.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
                if (down) g.DrawLines(pen, new[] { new PointF(center.X - a, center.Y - b), new PointF(center.X, center.Y + b), new PointF(center.X + a, center.Y - b) });
                else g.DrawLines(pen, new[] { new PointF(center.X - b, center.Y - a), new PointF(center.X + b, center.Y), new PointF(center.X - b, center.Y + a) });
            }
        }
        // App icons are untouched: no tint, opacity or shadow. They arrive at the target size; resampling only
        // happens while a better-sized copy is still loading.
        public static void DrawIcon(Graphics g, Image image, Rectangle rect) {
            var mode = g.InterpolationMode; var offset = g.PixelOffsetMode;
            g.InterpolationMode = image.Width == rect.Width ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, rect); g.InterpolationMode = mode; g.PixelOffsetMode = offset;
        }
    }

    // A borderless TextBox inside a painted Scheme A field: warm surface, hairline border, graphite focus.
    // A read-only value shown in a field-shaped box (same fill and border as the shortcut fields), centred; clickable.
    // Set Key (a pretty shortcut such as "Left Alt") to draw it like the shortcut fields (monospace, soft side, bold key)
    // between Text and Suffix; without a key, Text alone is shown.
    public sealed class ValueField : Control {
        string key = "", suffix = "";
        // Created on first paint, not as a field initializer: this control is built while the main window is being
        // constructed, and a font created that early (before Windows Forms enables per-monitor DPI) makes every
        // native text box in the app use 100%-scale fonts.
        Font mono;
        Font Mono { get { return mono ?? (mono = new Font("Consolas", 10.5f)); } }
        public ValueField() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; TabStop = false; }
        public string Key { get { return key; } set { key = value ?? ""; Invalidate(); } }
        public string Suffix { get { return suffix; } set { suffix = value ?? ""; Invalidate(); } }
        public override Size GetPreferredSize(Size proposed) { return new Size(Dpi.Px(this, 180), Dpi.Px(this, Dpi.FieldHeight)); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor);
            Dpi.Field(g, this, p.ShortcutBadgeBackground, p.Border, false);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            if (key.Length == 0) { TextRenderer.DrawText(g, Text, Font, ClientRectangle, p.ShortcutBadgeForeground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix); return; }
            float scale = Dpi.Scale(this); int space = TextRenderer.MeasureText(g, " ", Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int before = Text.Length == 0 ? 0 : TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags).Width + space;
            int keyW = KeyText.Width(g, key, Mono, scale), after = suffix.Length == 0 ? 0 : space + TextRenderer.MeasureText(g, suffix, Font, Size.Empty, flags).Width;
            int x = Math.Max(0, (Width - before - keyW - after) / 2);
            if (before > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, before, Height), p.ShortcutBadgeForeground, flags);
            x += before; KeyText.Draw(g, new Rectangle(x, 0, keyW, Height), key, Mono, p.ShortcutBadgeForeground, p.MutedText, scale); x += keyW;
            if (after > 0) TextRenderer.DrawText(g, suffix, Font, new Rectangle(x + space, 0, after, Height), p.ShortcutBadgeForeground, flags);
        }
        protected override void Dispose(bool disposing) { if (disposing && mono != null) mono.Dispose(); base.Dispose(disposing); }
    }
    public sealed class TextField : Control {
        readonly TextBox box = new TextBox { BorderStyle = BorderStyle.None };
        public TextField() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false); AutoSize = true; Cursor = Cursors.IBeam; TabStop = false;
            box.GotFocus += delegate { Invalidate(); }; box.LostFocus += delegate { Invalidate(); }; box.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
            Controls.Add(box);
        }
        public TextBox Box { get { return box; } }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, string lparam);
        string placeholder;
        // Native cue banner (EM_SETCUEBANNER), shown while the box is empty, even when focused.
        public string Placeholder { get { return placeholder; } set { placeholder = value; if (box.IsHandleCreated) SendMessage(box.Handle, 0x1501, new IntPtr(1), value ?? ""); else box.HandleCreated += delegate { SendMessage(box.Handle, 0x1501, new IntPtr(1), placeholder ?? ""); }; } }
        public override string Text { get { return box.Text; } set { box.Text = value; } }
        public override Size GetPreferredSize(Size proposed) { return new Size(Math.Max(Dpi.Px(this, 80), proposed.Width), Math.Max(Dpi.Px(this, Dpi.FieldHeight), box.PreferredHeight + Dpi.Px(this, 14))); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); int pad = Dpi.Px(this, 12), h = box.PreferredHeight; box.SetBounds(pad, (Height - h) / 2, Math.Max(1, Width - pad * 2), h); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; e.Graphics.Clear(Parent == null ? p.AppBackground : Parent.BackColor);
            Dpi.Field(e.Graphics, this, p.Surface, box.Focused ? p.Focus : p.Border, box.Focused);
        }
        protected override void OnMouseDown(MouseEventArgs e) { box.Focus(); box.SelectionStart = box.TextLength; base.OnMouseDown(e); }
    }

    public sealed class Option {
        public string Text, Detail; public object Value;
        public Option(string text, string detail = null, object value = null) { Text = text; Detail = detail; Value = value; }
    }

    // The list inside a popover. Height comes from the item count; it scrolls only past MaxRows.
    sealed class OptionList : Control {
        public const int MaxRows = 6;
        readonly IList<Option> items; readonly bool check; int hot, offset; readonly int selected;
        public event Action<int> Picked;
        public OptionList(IList<Option> items, int selected, bool check) {
            this.items = items; this.selected = selected; this.check = check; hot = selected;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        }
        public int RowHeight { get { return Math.Max(Dpi.Px(this, 34), Font.Height + Dpi.Px(this, 14)); } }
        public int Inset { get { return Dpi.Px(this, 4); } }
        public int PreferredHeight { get { return Math.Min(items.Count, MaxRows) * RowHeight + Inset * 2; } }
        int MaxOffset { get { return Math.Max(0, items.Count * RowHeight + Inset * 2 - Height); } }
        Rectangle Row(int i) { return new Rectangle(Inset, Inset + i * RowHeight - offset, Width - Inset * 2, RowHeight); }
        int HitTest(Point pt) { for (int i = 0; i < items.Count; i++) if (Row(i).Contains(pt)) return i; return -1; }
        void Reveal(int i) { var r = Row(i); if (r.Top < Inset) offset += r.Top - Inset; else if (r.Bottom > Height - Inset) offset += r.Bottom - Height + Inset; offset = Math.Max(0, Math.Min(MaxOffset, offset)); }
        protected override void OnCreateControl() { base.OnCreateControl(); if (hot >= 0 && hot < items.Count) Reveal(hot); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(p.PopoverBackground); g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Dpi.Px(this, 10), mark = check ? Dpi.Px(this, 22) : 0;
            for (int i = 0; i < items.Count; i++) {
                var r = Row(i); if (!r.IntersectsWith(e.ClipRectangle)) continue;
                if (i == hot) Theme.Fill(g, r, p.Hover, Dpi.Px(this, 6));
                if (check && i == selected) {
                    float s = Dpi.Scale(this); int x = r.X + pad, y = r.Y + r.Height / 2;
                    using (var pen = new Pen(p.PrimaryText, Math.Max(1.2f, 1.5f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(x + 1 * s, y), new PointF(x + 4.5f * s, y + 3.5f * s), new PointF(x + 11 * s, y - 4 * s) });
                }
                var textRect = new Rectangle(r.X + pad + mark, r.Y, r.Width - pad * 2 - mark, r.Height);
                int detailWidth = 0; var item = items[i];
                if (!string.IsNullOrEmpty(item.Detail)) {
                    detailWidth = Math.Min(textRect.Width / 2, TextRenderer.MeasureText(g, item.Detail, Font).Width);
                    TextRenderer.DrawText(g, item.Detail, Font, new Rectangle(textRect.Right - detailWidth, r.Y, detailWidth, r.Height), p.SecondaryText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                TextRenderer.DrawText(g, item.Text, Font, new Rectangle(textRect.X, r.Y, textRect.Width - detailWidth - pad, r.Height), p.PrimaryText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (MaxOffset > 0) { int track = Height - Inset * 2, thumb = Math.Max(Dpi.Px(this, 20), track * Height / (items.Count * RowHeight + Inset * 2)); int y = Inset + (track - thumb) * offset / MaxOffset; Theme.Fill(g, new Rectangle(Width - Dpi.Px(this, 5), y, Dpi.Px(this, 3), thumb), p.Border, Dpi.Px(this, 2)); }
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = HitTest(e.Location); if (i >= 0 && i != hot) { hot = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { int i = HitTest(e.Location); if (e.Button == MouseButtons.Left && i >= 0 && Picked != null) Picked(i); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { offset = Math.Max(0, Math.Min(MaxOffset, offset - Math.Sign(e.Delta) * RowHeight)); Invalidate(); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.Enter || key == Keys.Space || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) {
            int next = hot;
            if (e.KeyCode == Keys.Down) next = Math.Min(items.Count - 1, hot + 1); else if (e.KeyCode == Keys.Up) next = Math.Max(0, hot - 1);
            else if (e.KeyCode == Keys.Home) next = 0; else if (e.KeyCode == Keys.End) next = items.Count - 1;
            else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && hot >= 0 && Picked != null) { Picked(hot); e.Handled = true; return; }
            if (next != hot) { hot = next; Reveal(hot); Invalidate(); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    // Content-sized popup (Escape and outside clicks close it). Windows 11 rounds it and draws a themed hairline.
    public static class Popover {
        sealed class BareRenderer : ToolStripRenderer { }
        static bool Later(Control owner, Action action) {
            if (owner.IsDisposed || !owner.IsHandleCreated) return false;
            try { owner.BeginInvoke(action); return true; } catch (InvalidOperationException) { return false; }
        }
        public static ToolStripDropDown Show(Control owner, IList<Option> items, int selected, bool check, int width, bool alignRight, Action<int> picked, Action closed) {
            var list = new OptionList(items, selected, check) { Font = owner.FindForm() == null ? owner.Font : owner.FindForm().Font };
            var host = new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
            var drop = new ToolStripDropDown { Padding = Padding.Empty, AutoSize = false, DropShadowEnabled = true, Renderer = new BareRenderer(), BackColor = Theme.Current.PopoverBackground };
            drop.Items.Add(host); host.Font = list.Font;
            var size = new Size(width, 0); list.Size = size; size.Height = list.PreferredHeight; list.Size = size; host.Size = size; drop.Size = size;
            list.Picked += delegate(int index) { drop.Close(ToolStripDropDownCloseReason.ItemClicked); Later(owner, delegate { picked(index); }); };
            // The owner may already be gone when its window closes underneath an open popover.
            drop.Closed += delegate {
                if (closed != null) closed();
                EventHandler idle = null; idle = delegate { Application.Idle -= idle; drop.Dispose(); }; Application.Idle += idle;
            };
            NativeStyle.Popup(drop.Handle);
            drop.Show(owner, new Point(alignRight ? owner.Width - width : 0, owner.Height + Dpi.Px(owner, 4)));
            list.Focus();
            return drop;
        }
        // A small number field (digits, '.', '-') under owner (right-aligned): Enter hands the text to commit, which returns null to
        // close or a message to show in place of the hint; Esc or clicking elsewhere closes without a change.
        public static ToolStripDropDown ShowInput(Control owner, string text, string unit, string hint, int width, Func<string, string> commit, Action closed) {
            var font = owner.FindForm() == null ? owner.Font : owner.FindForm().Font; var p = Theme.Current;
            int pad = Dpi.Px(owner, 12), gap = Dpi.Px(owner, 8), field = Dpi.Px(owner, Dpi.FieldHeight);
            var panel = new Panel { BackColor = p.PopoverBackground, Font = font };
            var input = new TextField { Text = text, Font = font, AutoSize = false }; input.Box.MaxLength = 6;
            input.Box.KeyPress += delegate(object sender, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != '-') e.Handled = true; };
            var unitLabel = new Label { Text = unit, AutoSize = true, Font = font, ForeColor = p.PrimaryText, BackColor = p.PopoverBackground };
            var note = new Label { Text = hint, AutoSize = false, Font = font, ForeColor = p.SecondaryText, BackColor = p.PopoverBackground, TextAlign = ContentAlignment.MiddleLeft };
            panel.Controls.Add(input); panel.Controls.Add(unitLabel); panel.Controls.Add(note);
            // Theme the text box like the window's own fields, then restore the popover's own surface colours.
            Theme.Apply(panel); panel.BackColor = unitLabel.BackColor = note.BackColor = p.PopoverBackground; unitLabel.ForeColor = p.PrimaryText; note.ForeColor = p.SecondaryText;
            int unitW = TextRenderer.MeasureText(unit, font).Width, noteH = font.Height + Dpi.Px(owner, 6);
            input.SetBounds(pad, pad, width - pad * 2 - unitW - gap, field);
            unitLabel.Location = new Point(input.Right + gap, pad + (field - unitLabel.PreferredHeight) / 2);
            note.SetBounds(pad, pad + field + gap / 2, width - pad * 2, noteH);
            var size = new Size(width, note.Bottom + pad); panel.Size = size;
            var host = new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = size };
            var drop = new ToolStripDropDown { Padding = Padding.Empty, AutoSize = false, DropShadowEnabled = true, Renderer = new BareRenderer(), BackColor = p.PopoverBackground, Size = size, Font = font };
            // The host would otherwise hand its own default font to the panel (a small text box).
            drop.Items.Add(host); host.Font = font; panel.Font = font; input.Font = font; input.Box.Font = font; input.PerformLayout();
            input.Box.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; drop.Close(); }
                else if (e.KeyCode == Keys.Enter) {
                    e.SuppressKeyPress = true; string problem = commit(input.Text);
                    if (problem == null) drop.Close(); else { note.Text = problem; note.ForeColor = Theme.Current.WarningForeground; input.Box.SelectAll(); }
                }
            };
            drop.Closed += delegate {
                if (closed != null) closed();
                EventHandler idle = null; idle = delegate { Application.Idle -= idle; drop.Dispose(); }; Application.Idle += idle;
            };
            NativeStyle.Popup(drop.Handle);
            drop.Show(owner, new Point(owner.Width - width, owner.Height + Dpi.Px(owner, 4)));
            input.Box.Focus(); input.Box.SelectAll();
            return drop;
        }
    }

    // Replaces the native ComboBox: a field-shaped button that opens a content-sized Popover.
    public sealed class SelectField : Control {
        public readonly List<Option> Items = new List<Option>();
        int selected = -1, closedAt; bool open, hover;
        public event EventHandler SelectedIndexChanged;
        public SelectField() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            AutoSize = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
        }
        public int SelectedIndex {
            get { return selected; }
            set { if (value == selected) return; selected = value; AccessibleName = selected >= 0 ? Items[selected].Text : ""; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        public object SelectedValue { get { return selected >= 0 && selected < Items.Count ? Items[selected].Value : null; } }
        public void Clear() { Items.Clear(); selected = -1; Invalidate(); }
        public override Size GetPreferredSize(Size proposed) { return new Size(Math.Max(Dpi.Px(this, 120), proposed.Width), Math.Max(Dpi.Px(this, Dpi.FieldHeight), Font.Height + Dpi.Px(this, 14))); }
        public void Open() {
            // A click on the field that dismissed the popup must not immediately reopen it.
            if (open || Items.Count == 0 || unchecked(Environment.TickCount - closedAt) < 250) return;
            open = true; Invalidate();
            Popover.Show(this, Items, selected, true, Width, false, delegate(int i) { SelectedIndex = i; Focus(); }, delegate { open = false; closedAt = Environment.TickCount; Invalidate(); });
        }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor);
            Dpi.Field(g, this, hover && !open ? p.ElevatedSurface : p.Surface, Focused || open ? p.Focus : p.Border, Focused || open);
            int pad = Dpi.Px(this, 12), chevron = Dpi.Px(this, 16);
            string text = selected >= 0 ? Items[selected].Text : "";
            TextRenderer.DrawText(g, text, Font, new Rectangle(pad, 0, Width - pad * 2 - chevron, Height), p.PrimaryText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Dpi.Chevron(g, this, new Point(Width - pad - chevron / 2, Height / 2), true, p.SecondaryText);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); Open(); } base.OnMouseDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.Enter || key == Keys.Space || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) { Open(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && selected < Items.Count - 1) { SelectedIndex = selected + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up && selected > 0) { SelectedIndex = selected - 1; e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    // The current window target: real app icon, name, how it is matched, and one action button.
    public sealed class TargetCard : Card {
        public readonly RoundButton ActionButton = new RoundButton { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(56, 32), Padding = new Padding(10, 4, 10, 4), Cursor = Cursors.Hand };
        string title = "", subtitle = ""; bool warning; AppRule rule;
        public TargetCard() {
            SetStyle(ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; Controls.Add(ActionButton); AutoSize = true;
            AppIcons.Changed += IconsChanged;
        }
        void IconsChanged(object sender, EventArgs e) { if (IsHandleCreated && !IsDisposed) try { BeginInvoke(new Action(Invalidate)); } catch (InvalidOperationException) { } }
        public void SetTarget(AppRule target, string name, string detail, bool isWarning, string action, bool primary) {
            rule = target; title = name ?? ""; subtitle = detail ?? ""; warning = isWarning; ActionButton.Text = action; ActionButton.Primary = primary;
            AccessibleName = title + "，" + subtitle; PerformLayout(); Invalidate(); ActionButton.Invalidate();
        }
        int IconSize { get { return Dpi.Px(this, 36); } }
        public override Size GetPreferredSize(Size proposed) { return new Size(proposed.Width, Math.Max(Dpi.Px(this, 68), Font.Height * 2 + Dpi.Px(this, 28))); }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e); var s = ActionButton.GetPreferredSize(Size.Empty); ActionButton.SetBounds(Width - Dpi.Px(this, 16) - s.Width, (Height - s.Height) / 2, s.Width, s.Height);
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left) ActionButton.PerformClick(); base.OnMouseClick(e); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var p = Theme.Current; var g = e.Graphics; int pad = Dpi.Px(this, 16), size = IconSize, gap = Dpi.Px(this, 12);
            var icon = new Rectangle(pad, (Height - size) / 2, size, size);
            Image image = rule == null ? null : AppIcons.Get(rule, size);
            if (image != null) Dpi.DrawIcon(g, image, icon);
            else {
                Theme.Fill(g, icon, p.SecondaryButtonBackground, Dpi.Px(this, 9));
                if (rule == null) using (var pen = new Pen(p.SecondaryText, Math.Max(1.2f, 1.5f * Dpi.Scale(this)))) { int c = size / 2, a = size / 5; g.DrawLine(pen, icon.X + c - a, icon.Y + c, icon.X + c + a, icon.Y + c); g.DrawLine(pen, icon.X + c, icon.Y + c - a, icon.X + c, icon.Y + c + a); }
                else TextRenderer.DrawText(g, title.Length == 0 ? "?" : title.Substring(0, 1).ToUpperInvariant(), Font, icon, p.SecondaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            int x = icon.Right + gap, width = Math.Max(1, ActionButton.Left - gap - x), line = Font.Height, top = subtitle.Length == 0 ? (Height - line) / 2 : (Height - line * 2 - Dpi.Px(this, 2)) / 2;
            TextRenderer.DrawText(g, title, Font, new Rectangle(x, top, width, line), p.PrimaryText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, subtitle, Font, new Rectangle(x, top + line + Dpi.Px(this, 2), width, line), warning ? p.WarningForeground : p.SecondaryText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
        protected override void Dispose(bool disposing) { if (disposing) AppIcons.Changed -= IconsChanged; base.Dispose(disposing); }
    }

    // "▸ 高级设置" / "▾ 高级设置": a quiet text disclosure, not a button.
    public sealed class Disclosure : Control {
        bool expanded, hover;
        public event EventHandler ExpandedChanged;
        public Disclosure() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            AutoSize = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.OutlineButton;
        }
        public bool Expanded { get { return expanded; } set { if (expanded == value) return; expanded = value; Invalidate(); if (ExpandedChanged != null) ExpandedChanged(this, EventArgs.Empty); } }
        public override Size GetPreferredSize(Size proposed) { return new Size(Dpi.Px(this, 30) + TextRenderer.MeasureText(Text, Font).Width, Font.Height + Dpi.Px(this, 12)); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (Parent != null) Parent.PerformLayout(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor);
            Color color = hover || Focused ? p.PrimaryText : p.SecondaryText; int arrow = Dpi.Px(this, 18);
            Dpi.Chevron(g, this, new Point(Dpi.Px(this, 4) + arrow / 2 - Dpi.Px(this, 2), Height / 2), expanded, color);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Dpi.Px(this, 4) + arrow, 0, Width - arrow, Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) Dpi.FocusRing(g, this, Dpi.Px(this, 6));
        }
        protected override void OnClick(EventArgs e) { Focus(); Expanded = !Expanded; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Enter || key == Keys.Space || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Expanded = !Expanded; e.Handled = true; } base.OnKeyDown(e); }
    }

    // Segmented choice (跟随系统 | 亮色 | 暗色): the selected segment is a raised pill inside a recessed track.
    public sealed class SegmentedControl : Control {
        public readonly List<string> Items = new List<string>();
        int selected = -1, hover = -1;
        public event EventHandler SelectedIndexChanged;
        public SegmentedControl() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            AutoSize = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PageTabList;
        }
        public int SelectedIndex {
            get { return selected; }
            set { if (value == selected) return; selected = value; AccessibleName = selected >= 0 && selected < Items.Count ? Items[selected] : ""; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        int Inset { get { return Dpi.Px(this, 3); } }
        int SegmentWidth(int i) { return Math.Max(Dpi.Px(this, 56), TextRenderer.MeasureText(Items[i], Font).Width + Dpi.Px(this, 28)); }
        Rectangle Segment(int i) { int x = Inset; for (int j = 0; j < i; j++) x += SegmentWidth(j); return new Rectangle(x, Inset, SegmentWidth(i), Height - Inset * 2); }
        int HitTest(Point pt) { for (int i = 0; i < Items.Count; i++) if (Segment(i).Contains(pt)) return i; return -1; }
        public override Size GetPreferredSize(Size proposed) { int w = Inset * 2; for (int i = 0; i < Items.Count; i++) w += SegmentWidth(i); return new Size(w, Math.Max(Dpi.Px(this, Dpi.FieldHeight), Font.Height + Dpi.Px(this, 14))); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            // Dark: a track recessed into a raised card, but one step lighter when it sits on the window background
            // itself (the slide-in sheets), where a background-coloured track would vanish.
            bool onBackground = Parent != null && Parent.BackColor.ToArgb() == p.AppBackground.ToArgb();
            Theme.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), p.Dark && !onBackground ? p.AppBackground : p.SecondaryButtonBackground, Dpi.Px(this, 10));
            for (int i = 0; i < Items.Count; i++) {
                var r = Segment(i);
                if (i == selected) {
                    using (var path = Theme.Round(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), Dpi.Px(this, 8)))
                    using (var brush = new SolidBrush(p.Dark ? p.Pressed : p.ElevatedSurface)) using (var pen = new Pen(p.Dark ? p.Pressed : p.Border)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
                }
                TextRenderer.DrawText(g, Items[i], Font, r, i == selected || i == hover ? p.PrimaryText : p.SecondaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues) Dpi.FocusRing(g, this, Dpi.Px(this, 10));
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = HitTest(e.Location); if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int i = HitTest(e.Location); if (e.Button == MouseButtons.Left && i >= 0) { Focus(); SelectedIndex = i; } base.OnMouseDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Left && selected > 0) { SelectedIndex = selected - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right && selected < Items.Count - 1) { SelectedIndex = selected + 1; e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    // Renderer for the tray / context menus, matching the popover: warm surface, rounded hover, graphite checks.
    public sealed class MenuRenderer : ToolStripRenderer {
        static float Scale(ToolStrip strip) { return strip == null ? 1f : strip.DeviceDpi / 96f; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Current.PopoverBackground); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { if (!NativeStyle.SystemCorners) using (var pen = new Pen(Theme.Current.Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if (!e.Item.Selected || !e.Item.Enabled) return; float s = Scale(e.ToolStrip); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(e.Graphics, new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1), Theme.Current.Hover, (int)(6 * s));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Current.PrimaryText : Theme.Current.DisabledText; base.OnRenderItemText(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            float s = Scale(e.ToolStrip); var r = e.ImageRectangle; float x = r.X + r.Width / 2f - 5.5f * s, y = r.Y + r.Height / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Current.PrimaryText, Math.Max(1.2f, 1.5f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                e.Graphics.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + 3.5f * s, y + 3.5f * s), new PointF(x + 10 * s, y - 4 * s) });
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { int inset = (int)(10 * Scale(e.ToolStrip)), y = e.Item.Height / 2; using (var pen = new Pen(Theme.Current.Divider)) e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            float s = Scale(e.Item == null ? null : e.Item.Owner); var r = e.ArrowRectangle; float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, a = 3.5f * s, b = 2f * s;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Current.SecondaryText, Math.Max(1.2f, 1.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                e.Graphics.DrawLines(pen, new[] { new PointF(cx - b, cy - a), new PointF(cx + b, cy), new PointF(cx - b, cy + a) });
        }
    }

    // Vertical scroller for sheet content. Computes the content height itself (the native AutoScroll range came out
    // short, hiding the last field behind the footer), scrolls smoothly, and draws a thin themed scrollbar in the
    // right padding instead of the wide, light-only native one. Focused controls are scrolled into view.
    public sealed class ScrollPanel : Panel {
        Control content; int offset, target, contentHeight, dragY, dragOffset; bool layingOut, dragging, barHover, relayoutQueued;
        readonly Timer timer = new Timer { Interval = 10 };
        public ScrollPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); timer.Tick += delegate { Step(); }; }
        public void SetContent(Control control) {
            content = control; control.Dock = DockStyle.None; control.AutoSize = false; Controls.Add(control);
            // Content changing size (sections shown/hidden, labels re-wrapping) re-measures on the next message.
            control.Layout += delegate { if (!layingOut) QueueRelayout(); };
            Track(control);
        }
        void QueueRelayout() {
            if (relayoutQueued) return;
            if (!IsHandleCreated) { PerformLayout(); return; }
            relayoutQueued = true; BeginInvoke(new Action(delegate { relayoutQueued = false; if (!IsDisposed) PerformLayout(); }));
        }
        void Track(Control control) {
            control.GotFocus += delegate { Reveal(control); };
            control.ControlAdded += delegate(object sender, ControlEventArgs e) { Track(e.Control); };
            foreach (Control child in control.Controls) Track(child);
        }
        int MaxOffset { get { return Math.Max(0, contentHeight + Padding.Vertical - ClientSize.Height); } }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e); if (content == null) return;
            layingOut = true;
            try {
                int width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                contentHeight = content.GetPreferredSize(new Size(width, 0)).Height;
                offset = Math.Min(offset, MaxOffset); target = Math.Min(target, MaxOffset);
                content.SetBounds(Padding.Left, Padding.Top - offset, width, contentHeight);
            } finally { layingOut = false; }
            Invalidate();
        }
        public void ScrollTo(int value) { target = Math.Max(0, Math.Min(MaxOffset, value)); if (target != offset) timer.Start(); }
        void Step() {
            int diff = target - offset;
            if (Math.Abs(diff) <= 1) { offset = target; timer.Stop(); } else offset += (int)Math.Round(diff * 0.35);
            if (content != null) content.Top = Padding.Top - offset; Invalidate();
        }
        // Scroll just enough to show a control (e.g. one reached with Tab, or a section that was just expanded).
        public void Reveal(Control control) {
            if (content == null || IsDisposed) return;
            int y = 0; for (var c = control; c != null && c != content; c = c.Parent) y += c.Top;
            int margin = Dpi.Px(this, 12), view = ClientSize.Height - Padding.Vertical, top = y - margin, bottom = y + control.Height + margin;
            if (top < target) ScrollTo(top); else if (bottom > target + view) ScrollTo(Math.Min(top, bottom - view));
        }
        public void RevealLater(Control control) { if (IsHandleCreated) BeginInvoke(new Action(delegate { Reveal(control); })); else Reveal(control); }
        protected override void OnMouseWheel(MouseEventArgs e) {
            ScrollTo(target - e.Delta * Dpi.Px(this, 72) / 120);
            var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
        }
        Rectangle Thumb() {
            int inset = Dpi.Px(this, 8), track = Math.Max(1, ClientSize.Height - inset * 2), total = Math.Max(1, contentHeight + Padding.Vertical);
            int height = Math.Max(Dpi.Px(this, 32), track * ClientSize.Height / total), width = Dpi.Px(this, barHover || dragging ? 6 : 4);
            int y = inset + (track - height) * offset / Math.Max(1, MaxOffset);
            return new Rectangle(ClientSize.Width - Dpi.Px(this, 6) - width, y, width, height);
        }
        bool OnBar(Point pt) { return MaxOffset > 0 && pt.X >= ClientSize.Width - Dpi.Px(this, 16); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); if (MaxOffset <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; var thumb = Thumb();
            Theme.Fill(e.Graphics, thumb, barHover || dragging ? Theme.Current.MutedText : Theme.Current.Border, thumb.Width / 2);
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            if (dragging) {
                var thumb = Thumb(); int track = ClientSize.Height - Dpi.Px(this, 16) - thumb.Height;
                offset = target = Math.Max(0, Math.Min(MaxOffset, dragOffset + (e.Y - dragY) * MaxOffset / Math.Max(1, track)));
                if (content != null) content.Top = Padding.Top - offset; Invalidate(); return;
            }
            bool over = OnBar(e.Location); if (over != barHover) { barHover = over; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left && OnBar(e.Location)) {
                var thumb = Thumb();
                if (e.Y < thumb.Top) ScrollTo(target - (ClientSize.Height - Padding.Vertical));
                else if (e.Y > thumb.Bottom) ScrollTo(target + (ClientSize.Height - Padding.Vertical));
                else { timer.Stop(); target = offset; dragging = true; dragY = e.Y; dragOffset = offset; Capture = true; }
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; Capture = false; Invalidate(); } base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { if (barHover && !dragging) { barHover = false; Invalidate(); } base.OnMouseLeave(e); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // Quiet "+ text" action, e.g. 添加快捷键.
    public sealed class TextButton : Control {
        bool hover;
        public TextButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            AutoSize = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PushButton;
        }
        public override Size GetPreferredSize(Size proposed) { return new Size(Dpi.Px(this, 22) + TextRenderer.MeasureText(Text, Font).Width + Dpi.Px(this, 4), Font.Height + Dpi.Px(this, 12)); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (Parent != null) Parent.PerformLayout(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = hover || Focused ? p.PrimaryText : p.SecondaryText; float s = Dpi.Scale(this), a = 4.5f * s, cx = Dpi.Px(this, 9), cy = Height / 2f;
            using (var pen = new Pen(color, Math.Max(1.2f, 1.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(pen, cx - a, cy, cx + a, cy); g.DrawLine(pen, cx, cy - a, cx, cy + a); }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Dpi.Px(this, 22), 0, Width - Dpi.Px(this, 22), Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) Dpi.FocusRing(g, this, Dpi.Px(this, 6));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Enter || key == Keys.Space || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
    }

    // Six-dot grip. Dragging it reports pointer positions in screen coordinates; the owner decides what moves.
    public sealed class DragHandle : Control {
        bool hover, dragging, active;
        // Shown dark on the row that currently holds the dragged item (which may not be the grip being held).
        public bool Active { get { return active; } set { if (active == value) return; active = value; Invalidate(); } }
        public event Action<Point> DragStarted, DragMoved;
        public event EventHandler DragEnded;
        public DragHandle() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(20, 32); Cursor = Cursors.Hand; TabStop = false; AccessibleName = "拖动排序";
        }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Dpi.Scale(this), r = 1.6f * s, step = 5f * s, x0 = Width / 2f - step / 2, y0 = Height / 2f - step;
            using (var brush = new SolidBrush(active || (hover && !dragging) ? p.PrimaryText : p.MutedText))
                for (int row = 0; row < 3; row++) for (int col = 0; col < 2; col++) g.FillEllipse(brush, x0 + col * step - r, y0 + row * step - r, r * 2, r * 2);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { dragging = true; Capture = true; Invalidate(); if (DragStarted != null) DragStarted(PointToScreen(e.Location)); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging && DragMoved != null) DragMoved(PointToScreen(e.Location)); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Finish(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { Finish(); base.OnMouseCaptureChanged(e); }
        void Finish() { if (!dragging) return; dragging = false; Capture = false; Invalidate(); if (DragEnded != null) DragEnded(this, EventArgs.Empty); }
    }

    // Small icon button for sheets: "×" by default, "‹" with Back.
    public sealed class CloseButton : Control {
        bool hover;
        public bool Back;
        public CloseButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            Size = new Size(32, 32); TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PushButton;
        }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover) Theme.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), p.Hover, Dpi.Px(this, 8));
            float s = Dpi.Scale(this), a = 5f * s, cx = Width / 2f, cy = Height / 2f;
            if (Back) Dpi.Chevron(g, this, new Point((int)(cx + 1.5f * s), (int)cy), false, hover ? p.PrimaryText : p.SecondaryText, true);
            else using (var pen = new Pen(hover ? p.PrimaryText : p.SecondaryText, Math.Max(1.2f, 1.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(pen, cx - a, cy - a, cx + a, cy + a); g.DrawLine(pen, cx + a, cy - a, cx - a, cy + a); }
            if (Focused && ShowFocusCues) Dpi.FocusRing(g, this, Dpi.Px(this, 8));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Enter || key == Keys.Space || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
    }

    // Main content plus a sheet that slides in from the right. The main content gives up the sheet's width at once,
    // then the sheet slides over the freed space (ease-out), with a soft shadow on its leading edge.
    public sealed class SheetHost : Panel {
        public Control Main;
        Control sheet; int sheetWidth, started, duration; float progress, from, to; Action done; bool leaving;
        readonly Timer timer = new Timer { Interval = 10 };
        const int OpenDuration = 180, CloseDuration = 150;
        public SheetHost() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); timer.Tick += delegate { Step(); }; }
        public Control Sheet { get { return sheet; } }
        int Shadow { get { return Dpi.Px(this, 14); } }
        // Replacing an open sheet swaps the content in place; only the first open slides.
        public void Present(Control content, int width, bool animate) {
            bool swap = sheet != null;
            if (swap) { var old = sheet; Controls.Remove(old); old.Dispose(); }
            timer.Stop(); done = null; leaving = false; sheet = content; sheetWidth = width; Controls.Add(content); content.BringToFront();
            progress = swap || !animate ? 1 : 0; PerformLayout(); Invalidate();
            if (!swap && animate) Animate(1, null);
        }
        public void Dismiss(bool animate, Action after) {
            if (sheet == null) { if (after != null) after(); return; }
            Action finish = delegate { var old = sheet; sheet = null; leaving = false; Controls.Remove(old); old.Dispose(); PerformLayout(); Invalidate(); if (after != null) after(); };
            // The main content takes its full width back immediately; the sheet slides out on top of it.
            leaving = true; PerformLayout();
            if (animate) Animate(0, finish); else { timer.Stop(); done = null; finish(); }
        }
        void Animate(float target, Action after) { from = progress; to = target; duration = target > progress ? OpenDuration : CloseDuration; started = Environment.TickCount; done = after; timer.Start(); }
        void Step() {
            float t = Math.Min(1f, unchecked(Environment.TickCount - started) / (float)duration), eased = 1 - (float)Math.Pow(1 - t, 3);
            progress = from + (to - from) * eased; PlaceSheet(); Invalidate(); Update();
            if (t >= 1) { timer.Stop(); var finish = done; done = null; if (finish != null) finish(); }
        }
        void PlaceSheet() { if (sheet != null) sheet.SetBounds(ClientSize.Width - (int)Math.Round(sheetWidth * progress), 0, sheetWidth, ClientSize.Height); }
        protected override void OnLayout(LayoutEventArgs e) {
            if (Main != null) Main.SetBounds(0, 0, Math.Max(0, ClientSize.Width - (sheet == null || leaving ? 0 : sheetWidth + Shadow)), ClientSize.Height);
            PlaceSheet();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); if (sheet == null) return;
            var rect = new Rectangle(sheet.Left - Shadow, 0, Shadow, Height); if (rect.Width <= 0) return;
            using (var brush = new LinearGradientBrush(new Rectangle(rect.X - 1, 0, rect.Width + 2, Math.Max(1, Height)), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(Theme.Current.Dark ? 90 : 26, 0, 0, 0), LinearGradientMode.Horizontal)) e.Graphics.FillRectangle(brush, rect);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // Rows of candidate apps/windows: real icon, name, and window title or source. Click or Enter picks one.
    public sealed class ChoiceList : Control {
        List<AppChoice> items = new List<AppChoice>();
        readonly Dictionary<AppChoice, AppRule> rules = new Dictionary<AppChoice, AppRule>();
        int hot = -1, offset;
        public event Action<AppChoice> Picked;
        public ChoiceList() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.List; AppIcons.Changed += IconsChanged;
        }
        void IconsChanged(object sender, EventArgs e) { if (IsHandleCreated && !IsDisposed) try { BeginInvoke(new Action(Invalidate)); } catch (InvalidOperationException) { } }
        public void SetItems(List<AppChoice> list) { items = list; hot = list.Count > 0 ? 0 : -1; offset = 0; Invalidate(); }
        int RowHeight { get { return Math.Max(Dpi.Px(this, 54), Font.Height * 2 + Dpi.Px(this, 16)); } }
        int Inset { get { return Dpi.Px(this, 4); } }
        int MaxOffset { get { return Math.Max(0, items.Count * RowHeight + Inset * 2 - Height); } }
        Rectangle Row(int i) { return new Rectangle(Inset, Inset + i * RowHeight - offset, Width - Inset * 2 - (MaxOffset > 0 ? Dpi.Px(this, 6) : 0), RowHeight); }
        int HitTest(Point pt) { for (int i = 0; i < items.Count; i++) if (Row(i).Contains(pt)) return i; return -1; }
        void Reveal(int i) { var r = Row(i); if (r.Top < Inset) offset += r.Top - Inset; else if (r.Bottom > Height - Inset) offset += r.Bottom - Height + Inset; offset = Math.Max(0, Math.Min(MaxOffset, offset)); }
        public void MoveHot(int delta) { if (items.Count == 0) return; hot = Math.Max(0, Math.Min(items.Count - 1, hot + delta)); Reveal(hot); Invalidate(); }
        public void PickHot() { if (hot >= 0 && hot < items.Count && Picked != null) Picked(items[hot]); }
        AppRule RuleFor(AppChoice choice) { AppRule rule; if (!rules.TryGetValue(choice, out rule)) { rule = choice.ToRule(); rules[choice] = rule; } return rule; }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(Parent == null ? p.AppBackground : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), p.Surface, Dpi.Px(this, Theme.Radius));
            int pad = Dpi.Px(this, 12), icon = Dpi.Px(this, 32), gap = Dpi.Px(this, 12), line = Font.Height;
            var clip = g.Clip; g.SetClip(new Rectangle(0, Inset, Width, Height - Inset * 2));
            for (int i = 0; i < items.Count; i++) {
                var r = Row(i); if (!r.IntersectsWith(e.ClipRectangle)) continue;
                if (i == hot) Theme.Fill(g, r, p.Hover, Dpi.Px(this, 8));
                var iconRect = new Rectangle(r.X + pad, r.Y + (r.Height - icon) / 2, icon, icon);
                Image image = AppIcons.Get(RuleFor(items[i]), icon);
                if (image != null) Dpi.DrawIcon(g, image, iconRect); else Theme.Fill(g, iconRect, p.SecondaryButtonBackground, Dpi.Px(this, 8));
                int x = iconRect.Right + gap, w = Math.Max(1, r.Right - pad - x), top = r.Y + (r.Height - line * 2 - Dpi.Px(this, 2)) / 2;
                TextRenderer.DrawText(g, items[i].Name ?? "", Font, new Rectangle(x, top, w, line), p.PrimaryText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                string detail = string.IsNullOrEmpty(items[i].WindowTitle) || items[i].WindowTitle == items[i].Name ? items[i].ProcessName + ".exe" : items[i].WindowTitle;
                TextRenderer.DrawText(g, detail, Font, new Rectangle(x, top + line + Dpi.Px(this, 2), w, line), p.SecondaryText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            g.Clip = clip;
            if (MaxOffset > 0) { int track = Height - Inset * 4, thumb = Math.Max(Dpi.Px(this, 24), track * Height / (items.Count * RowHeight + Inset * 2)); int y = Inset * 2 + (track - thumb) * offset / MaxOffset; Theme.Fill(g, new Rectangle(Width - Dpi.Px(this, 7), y, Dpi.Px(this, 3), thumb), p.Border, Dpi.Px(this, 2)); }
            if (Focused && ShowFocusCues) Dpi.FocusRing(g, this, Dpi.Px(this, Theme.Radius));
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = HitTest(e.Location); if (i >= 0 && i != hot) { hot = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { int i = HitTest(e.Location); if (e.Button == MouseButtons.Left && i >= 0) { hot = i; PickHot(); } base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { offset = Math.Max(0, Math.Min(MaxOffset, offset - e.Delta * RowHeight / 120 * Math.Max(1, SystemInformation.MouseWheelScrollLines) / 2)); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); offset = Math.Min(offset, MaxOffset); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.Enter || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { MoveHot(e.KeyCode == Keys.Down ? 1 : -1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { PickHot(); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void Dispose(bool disposing) { if (disposing) AppIcons.Changed -= IconsChanged; base.Dispose(disposing); }
    }

    // Dialog footer: hairline divider, an optional status message on the left, actions on the right (secondary, then primary).
    public sealed class DialogFooter : TableLayoutPanel {
        // Columns: [optional leading action, e.g. 移除] [message] [actions…]
        public DialogFooter(Control message, params Button[] actions) {
            Dock = DockStyle.Bottom; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(24, 14, 24, 16); Margin = Padding.Empty;
            ColumnCount = actions.Length + 2; RowCount = 1; RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foreach (var a in actions) ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            message.Dock = DockStyle.Fill; message.Margin = new Padding(0, 0, 12, 0); var label = message as Label; if (label != null) label.TextAlign = ContentAlignment.MiddleLeft; Controls.Add(message, 1, 0);
            for (int i = 0; i < actions.Length; i++) { actions[i].Margin = new Padding(i == 0 ? 0 : 8, 0, 0, 0); actions[i].MinimumSize = new Size(84, 34); Controls.Add(actions[i], i + 2, 0); }
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        public void SetLeading(Control leading) { leading.Margin = new Padding(0, 0, 8, 0); leading.Anchor = AnchorStyles.Left; Controls.Add(leading, 0, 0); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Theme.Current.Divider)) e.Graphics.DrawLine(pen, 0, 0, Width, 0); }
    }
}
