using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace WindowSwitch {
    public class RoundButton : Button {
        public bool Primary;
        // Danger: destructive action (red text); Armed: waiting for the confirming second click.
        public bool Danger, Armed;
        bool hover, pressed;
        public RoundButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent == null ? p.AppBackground : Parent.BackColor);
            Color bg = Primary ? p.PrimaryButtonBackground : p.SecondaryButtonBackground, fg = Primary ? p.PrimaryButtonForeground : p.PrimaryText;
            if (Danger) fg = p.DangerForeground;
            if (!Enabled) { bg = p.SecondaryButtonBackground; fg = p.DisabledText; } else if (Armed) bg = p.DangerBackground; else if (pressed || hover) bg = Primary ? p.PrimaryButtonHover : pressed ? p.Pressed : p.Hover;
            int radius = Dpi.Px(this, Theme.SmallRadius); Theme.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), bg, radius);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) Dpi.FocusRing(e.Graphics, this, radius);
        }
    }
    public class Card : Panel {
        public Card() { DoubleBuffered = true; BackColor = Theme.Current.Surface; }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? Theme.Current.AppBackground : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Theme.Current.Surface, (int)(Theme.Radius * DeviceDpi / 96f)); }
    }
    public class Toggle : CheckBox {
        public Toggle() { AutoSize = false; Size = new Size(42, 26); Text = ""; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; var p = Theme.Current; int pad = Math.Max(2, Height / 10); int h = Height - pad * 2;
            Theme.Fill(e.Graphics, new Rectangle(0, pad, Width - 1, h), Checked ? p.ToggleOn : p.ToggleOff, h / 2);
            using (var brush = new SolidBrush(Checked ? p.ToggleKnobOn : p.ToggleKnob)) e.Graphics.FillEllipse(brush, Checked ? Width - h + pad : pad, pad * 2, h - pad * 2, h - pad * 2);
            if (Focused && ShowFocusCues) Dpi.FocusRing(e.Graphics, this, h / 2);
        }
    }
    public sealed class ShortcutRecorder : Control {
        public event EventHandler Recorded;
        public event EventHandler RecordingStarted, RecordingEnded, Cancelled;
        string before; bool cue; int beforeKey;
        // Single-key mode records one key (e.g. Right Ctrl) instead of a modifier chord.
        public bool SingleKey; public int KeyCode;
        bool highlight;
        // Drawn like the focused field while its row is being dragged.
        public bool Highlight { get { return highlight; } set { if (highlight == value) return; highlight = value; Invalidate(); } }
        public ShortcutRecorder() { Cursor = Cursors.Hand; Font = new Font("Consolas", 10.5f); AccessibleName = "快捷键录制"; Size = new Size(180, 34); AutoSize = true; TabStop = true; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        public override Size GetPreferredSize(Size proposedSize) { return new Size(Dpi.Px(this, 180), Math.Max(Dpi.Px(this, Dpi.FieldHeight), Font.Height + Dpi.Px(this, 10))); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        protected override void OnEnter(EventArgs e) { before = Text; beforeKey = KeyCode; cue = true; if (RecordingStarted != null) RecordingStarted(this, e); Invalidate(); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { cue = false; if (RecordingEnded != null) RecordingEnded(this, e); Invalidate(); base.OnLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; e.Graphics.Clear(Parent.BackColor);
            bool strong = Focused || highlight;
            Dpi.Field(e.Graphics, this, strong ? p.Surface : p.ShortcutBadgeBackground, strong ? p.Focus : p.Border, strong);
            // The cue uses the UI font; key names use the monospace font.
            bool hint = cue || string.IsNullOrEmpty(Text);
            if (hint) TextRenderer.DrawText(e.Graphics, cue ? "请按下快捷键…" : "点击设置快捷键", Parent.Font, ClientRectangle, p.SecondaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            else KeyText.Draw(e.Graphics, ClientRectangle, KeyChord.Pretty(Text), Font, p.ShortcutBadgeForeground, p.MutedText, Dpi.Scale(this));
        }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) != Keys.Tab; }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Tab) { base.OnKeyDown(e); return; } e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { Text = before; KeyCode = beforeKey; if (Cancelled != null) Cancelled(this, EventArgs.Empty); Parent.SelectNextControl(this, true, true, true, true); return; }
            if (SingleKey) {
                int code = (int)e.KeyCode;
                if (e.KeyCode == Keys.ControlKey) code = (Native.GetAsyncKeyState(0xA3) & 0x8000) != 0 ? 0xA3 : 0xA2;
                else if (e.KeyCode == Keys.Menu) code = (Native.GetAsyncKeyState(0xA5) & 0x8000) != 0 ? 0xA5 : 0xA4;
                else if (e.KeyCode == Keys.ShiftKey) code = (Native.GetAsyncKeyState(0xA1) & 0x8000) != 0 ? 0xA1 : 0xA0;
                KeyCode = code; cue = false; Text = Ui.KeyName(code); if (Recorded != null) Recorded(this, EventArgs.Empty); return;
            }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
            bool win = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (e.Modifiers == Keys.None && !win) return;
            cue = false; Text = (win ? "Win+" : "") + KeyChord.Format(e.KeyData, KeyChord.PressedSides()); if (Recorded != null) Recorded(this, EventArgs.Empty);
        }
    }
    // Shortcut text drawn key by key: key names in full colour, the "+" between them dimmed and tight.
    // Shortcut text drawn key by key. The key itself (Alt, Shift, F7) is bold in full colour; a side prefix
    // ("Left ", "Right ") is regular weight in a softer colour; the "+" between keys is the dimmest and tight.
    public static class KeyText {
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        static int Measure(Graphics g, string text, Font font) { return text.Length == 0 ? 0 : TextRenderer.MeasureText(g, text, font, Size.Empty, Flags).Width; }
        static void Split(string part, out string prefix, out string key) {
            prefix = ""; key = part;
            foreach (string side in new[] { "Left ", "Right " }) if (part.StartsWith(side, StringComparison.Ordinal) && part.Length > side.Length) { prefix = side; key = part.Substring(side.Length); break; }
        }
        static Color Mix(Color a, Color b) { return Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2); }
        public static int Width(Graphics g, string pretty, Font font, float scale) {
            using (var bold = new Font(font, FontStyle.Bold)) return Layout(g, pretty, font, bold, scale, Rectangle.Empty, Color.Empty, Color.Empty, false);
        }
        public static void Draw(Graphics g, Rectangle bounds, string pretty, Font font, Color color, Color dim, float scale) {
            using (var bold = new Font(font, FontStyle.Bold)) Layout(g, pretty, font, bold, scale, bounds, color, dim, true);
        }
        // Measures (draw = false) or draws the whole shortcut, centred in bounds; returns its width.
        static int Layout(Graphics g, string pretty, Font font, Font bold, float scale, Rectangle bounds, Color color, Color dim, bool draw) {
            var parts = (pretty ?? "").Split('+'); int gap = (int)Math.Round(3 * scale), plus = Measure(g, "+", font);
            int total = 0;
            if (draw) total = Layout(g, pretty, font, bold, scale, bounds, color, dim, false);
            int x = draw ? bounds.X + Math.Max(0, (bounds.Width - total) / 2) : 0, start = x;
            Color soft = draw ? Mix(color, dim) : Color.Empty;
            for (int i = 0; i < parts.Length; i++) {
                string prefix, key; Split(parts[i], out prefix, out key);
                int pw = Measure(g, prefix, font), kw = Measure(g, key, bold);
                if (draw && pw > 0) TextRenderer.DrawText(g, prefix, font, new Rectangle(x, bounds.Y, pw + 2, bounds.Height), soft, Flags | TextFormatFlags.VerticalCenter);
                x += pw;
                if (draw && kw > 0) TextRenderer.DrawText(g, key, bold, new Rectangle(x, bounds.Y, kw + 2, bounds.Height), color, Flags | TextFormatFlags.VerticalCenter);
                x += kw;
                if (i < parts.Length - 1) { x += gap; if (draw) TextRenderer.DrawText(g, "+", font, new Rectangle(x, bounds.Y, plus + 2, bounds.Height), dim, Flags | TextFormatFlags.VerticalCenter); x += plus + gap; }
            }
            return x - start;
        }
    }
    public static class RowPainter {
        public static string ShortState(string value) { return value == "已生效" ? value : value == "已暂停" ? value : value == "未启用" ? "未启用" : "有冲突"; }
        public static void Draw(Graphics g, Rectangle bounds, HintRow row, Font font, float scale, bool overlay) {
            var p = Theme.Current; g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = (int)(16 * scale), iconSize = (int)(32 * scale), gap = (int)(12 * scale);
            var iconRect = new Rectangle(bounds.X + pad, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
            Image image = row.Rule == null ? null : AppIcons.Get(row.Rule, iconSize);
            if (image != null) Dpi.DrawIcon(g, image, iconRect); else {
                Theme.Fill(g, iconRect, p.SecondaryButtonBackground, (int)(8 * scale));
                TextRenderer.DrawText(g, string.IsNullOrEmpty(row.Name) ? "W" : row.Name.Substring(0, 1).ToUpperInvariant(), font, iconRect, p.SecondaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            string state = ShortState(row.State); bool success = state == "已生效";
            using (var small = new Font(font.FontFamily, font.Size * .88f)) using (var mono = new Font("Consolas", font.Size * (overlay ? 1.06f : .96f))) {
                int statusW = TextRenderer.MeasureText(g, state, small).Width + (int)(26 * scale);
                int pillH = Math.Min(bounds.Height - (int)(14 * scale), font.Height + (int)((overlay ? 11 : 8) * scale));
                var status = new Rectangle(bounds.Right - pad - statusW, bounds.Y + (bounds.Height - pillH) / 2, statusW, pillH);
                // One badge per shortcut; when they do not fit next to the name, the rest collapse into "+n".
                var keys = (row.Shortcuts ?? new List<string> { row.Shortcut ?? "" }).Take(overlay ? 1 : int.MaxValue).Select(k => KeyChord.Pretty(k)).ToList();
                // The name keeps its real width (up to 140 px logical), so short names leave room for more badges.
                int nameNeed = Math.Min(TextRenderer.MeasureText(g, row.Name ?? "", font).Width, (int)(140 * scale));
                int badgeGap = (int)(6 * scale), room = Math.Max(0, status.Left - gap - (iconRect.Right + gap + nameNeed + gap));
                Func<string, int> widthOf = text => KeyText.Width(g, text, mono, scale) + (int)((overlay ? 22 : 18) * scale);
                int shown = keys.Count, total = 0;
                while (shown > 0) {
                    total = keys.Take(shown).Sum(k => widthOf(k) + badgeGap) - badgeGap + (shown < keys.Count ? badgeGap + widthOf("+" + (keys.Count - shown)) : 0);
                    if (total <= room || shown == 1) break; shown--;
                }
                var badges = keys.Take(shown).ToList(); if (shown < keys.Count) badges.Add("+" + (keys.Count - shown));
                int x = status.Left - gap - total;
                var key = new Rectangle(x, status.Y, Math.Max(0, total), pillH);
                Color statusBg = success ? p.SuccessBackground : state == "有冲突" ? p.WarningBackground : p.SecondaryButtonBackground;
                Color statusFg = success ? p.SuccessForeground : state == "有冲突" ? p.WarningForeground : p.SecondaryText;
                Theme.Fill(g, status, statusBg, pillH / 2); int dot = (int)(5 * scale);
                using (var b = new SolidBrush(success ? p.StatusDot : statusFg)) g.FillEllipse(b, status.Left + (int)(9 * scale), status.Top + (pillH - dot) / 2, dot, dot);
                TextRenderer.DrawText(g, state, small, new Rectangle(status.X + (int)(15 * scale), status.Y, status.Width - (int)(17 * scale), status.Height), statusFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                foreach (string badge in badges) {
                    var rect = new Rectangle(x, status.Y, widthOf(badge), pillH); x = rect.Right + badgeGap;
                    Theme.Fill(g, rect, p.ShortcutBadgeBackground, (int)(Theme.SmallRadius * scale));
                    KeyText.Draw(g, rect, badge, mono, overlay ? p.PrimaryText : p.ShortcutBadgeForeground, p.MutedText, scale);
                }
                var nameRect = new Rectangle(iconRect.Right + gap, bounds.Y, Math.Max(1, key.Left - iconRect.Right - gap * 2), bounds.Height);
                using (var medium = new Font(font.FontFamily, font.Size, FontStyle.Regular)) TextRenderer.DrawText(g, row.Name ?? "", medium, nameRect, p.PrimaryText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
    public class AppList : ScrollableControl {
        public List<HintRow> Rows = new List<HintRow>();
        public bool Overlay;
        int selected = -1, hovered = -1;
        int offset, dragY, dragOffset; bool dragging;
        // Press on a row, then either release (a click: select it) or move past a small threshold (lift the row and
        // reorder; the rows swap live under the pointer, Reordered fires on release).
        int pressIndex = -1, pressY, reorderFrom, reorderAt = -1; bool pressed, reordering;
        public event EventHandler SelectionChanged;
        public event Action<int, int> Reordered;
        public int SelectedIndex { get { return selected; } set { selected = value; Invalidate(); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); } }
        public int RowHeight { get { return (int)((Overlay ? 56 : 58) * DeviceDpi / 96f); } }
        public bool Scrollable { get { return Rows.Count * RowHeight > ClientSize.Height; } }
        int MaximumOffset { get { return Math.Max(0, Rows.Count * RowHeight - ClientSize.Height); } }
        public AppList() { DoubleBuffered = true; AutoScroll = false; Dock = DockStyle.Fill; TabStop = true; BackColor = Theme.Current.Surface; AppIcons.Changed += IconsChanged; }
        void IconsChanged(object sender, EventArgs e) { if (IsHandleCreated && !IsDisposed) try { BeginInvoke(new Action(Invalidate)); } catch (InvalidOperationException) { } }
        public void SetRows(List<HintRow> rows) { Rows = rows; selected = Math.Min(selected, rows.Count - 1); offset = Math.Min(offset, MaximumOffset); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); offset = Math.Min(offset, MaximumOffset); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Overlay ? Theme.Current.OverlayBackground : Theme.Current.Surface); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var p = Theme.Current; float scale = DeviceDpi / 96f; int width = ClientSize.Width - (Scrollable ? (int)(10 * scale) : 0);
            if (Rows.Count == 0 && !Overlay) TextRenderer.DrawText(e.Graphics, "还没有应用", Font, ClientRectangle, p.SecondaryText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            for (int i = 0; i < Rows.Count; i++) {
                var rect = new Rectangle(0, -offset + i * RowHeight, width, RowHeight); if (!rect.IntersectsWith(e.ClipRectangle)) continue;
                bool lifted = reordering && i == reorderAt; var plate = Rectangle.Inflate(rect, -(int)(4 * scale), -(int)(3 * scale));
                if (!Overlay && (i == selected || i == hovered || lifted)) Theme.Fill(e.Graphics, plate, lifted || i == selected ? p.Selection : p.Hover, (int)(Theme.SmallRadius * scale));
                if (lifted) using (var pen = new Pen(p.Focus)) using (var path = Theme.Round(new RectangleF(plate.X + .5f, plate.Y + .5f, plate.Width - 1, plate.Height - 1), (int)(Theme.SmallRadius * scale))) e.Graphics.DrawPath(pen, path);
                RowPainter.Draw(e.Graphics, rect, Rows[i], Font, scale, Overlay);
                // A faint grip on the hovered (or lifted) row hints that rows can be dragged.
                if (!Overlay && Rows.Count > 1 && (lifted || (!reordering && i == hovered))) {
                    float step = 4.5f * scale, r = 1.3f * scale, cx = 8 * scale, cy = rect.Y + rect.Height / 2f;
                    using (var brush = new SolidBrush(lifted ? p.SecondaryText : p.MutedText))
                        for (int row = -1; row <= 1; row++) for (int col = 0; col < 2; col++) e.Graphics.FillEllipse(brush, cx + (col - .5f) * step - r, cy + row * step - r, r * 2, r * 2);
                }
                if (i < Rows.Count - 1) using (var pen = new Pen(Overlay ? Theme.Current.OverlayBorder : p.Divider)) e.Graphics.DrawLine(pen, (int)(16 * scale), rect.Bottom - 1, width - (int)(16 * scale), rect.Bottom - 1);
            }
            if (Scrollable) { int thumbH = Math.Max((int)(24 * scale), ClientSize.Height * ClientSize.Height / (Rows.Count * RowHeight)); int y = offset * Math.Max(1, ClientSize.Height - thumbH) / Math.Max(1, MaximumOffset); Theme.Fill(e.Graphics, new Rectangle(ClientSize.Width - (int)(6 * scale), y, Math.Max(3, (int)(3 * scale)), thumbH), p.Border, (int)(3 * scale)); }
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (Scrollable && e.X >= Width - 12 * DeviceDpi / 96f) { dragging = true; dragY = e.Y; dragOffset = offset; Capture = true; }
            else if (!Overlay && e.Button == MouseButtons.Left) { Focus(); int index = (e.Y + offset) / RowHeight; if (index < Rows.Count) { pressed = true; pressIndex = index; pressY = e.Y; Capture = true; } }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            if (reordering) FinishReorder();
            else if (pressed) { pressed = false; int index = (e.Y + offset) / RowHeight; if (e.Y >= 0 && index == pressIndex && index < Rows.Count) SelectedIndex = index; }
            dragging = false; Capture = false; base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (reordering) FinishReorder(); pressed = false; dragging = false; base.OnMouseCaptureChanged(e); }
        void FinishReorder() { reordering = false; pressed = false; int from = reorderFrom, to = reorderAt; reorderAt = -1; Invalidate(); if (from != to && Reordered != null) Reordered(from, to); }
        protected override void OnMouseMove(MouseEventArgs e) {
            if (dragging) { offset = Math.Max(0, Math.Min(MaximumOffset, dragOffset + (e.Y - dragY) * Rows.Count * RowHeight / Math.Max(1, Height))); Invalidate(); return; }
            if (pressed && !reordering && Rows.Count > 1 && Math.Abs(e.Y - pressY) > 5 * DeviceDpi / 96f) { reordering = true; reorderFrom = reorderAt = pressIndex; }
            if (reordering) {
                // Scroll when held near an edge, then move the lifted row to the row under the pointer.
                if (e.Y < RowHeight / 3) offset = Math.Max(0, offset - RowHeight / 4); else if (e.Y > Height - RowHeight / 3) offset = Math.Min(MaximumOffset, offset + RowHeight / 4);
                int target = Math.Max(0, Math.Min(Rows.Count - 1, (Math.Max(0, e.Y) + offset) / RowHeight));
                while (reorderAt != target) {
                    int next = reorderAt + Math.Sign(target - reorderAt); var row = Rows[reorderAt]; Rows[reorderAt] = Rows[next]; Rows[next] = row;
                    if (selected == reorderAt) selected = next; else if (selected == next) selected = reorderAt;
                    reorderAt = next;
                }
                Invalidate(); return;
            }
            int index = (e.Y + offset) / RowHeight; if (index != hovered) { hovered = index < Rows.Count ? index : -1; Invalidate(); }
            if (!Overlay) Cursor = hovered >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e) { offset = Math.Max(0, Math.Min(MaximumOffset, offset - e.Delta * RowHeight * Math.Max(1, SystemInformation.MouseWheelScrollLines) / 120)); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override bool IsInputKey(Keys data) { return data == Keys.Up || data == Keys.Down || base.IsInputKey(data); }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (!Overlay && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) { SelectedIndex = Math.Max(0, Math.Min(Rows.Count - 1, selected + (e.KeyCode == Keys.Down ? 1 : -1))); int y = selected * RowHeight; if (y < offset || y + RowHeight > offset + Height) offset = Math.Max(0, Math.Min(MaximumOffset, y - Height / 2)); Invalidate(); e.Handled = true; }
            if (!Overlay && e.KeyCode == Keys.Enter) OnDoubleClick(EventArgs.Empty); base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { AppIcons.Changed -= IconsChanged; } base.Dispose(disposing); }
    }
}
