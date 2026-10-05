using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WindowSwitch {
    public class HintOptions {
        public bool Enabled { get; set; }
        public int Key { get; set; }
        public int DelayMs { get; set; }
        // Where the preview sits: an OverlayLayout.Positions index (紧贴 / 适中 / 较高 / 居中).
        public int Position { get; set; }
        // Which screen: the one with the active window (default) or the one with the pointer.
        public bool FollowMouse { get; set; }
        // Hold key + numpad 1–9 opens the matching app of the preview (numbered when on). Off unless switched on.
        public bool NumberKeys { get; set; }
        public HintOptions() { Enabled = true; Key = 0xA3; DelayMs = 650; Position = OverlayLayout.DefaultPosition; }
    }
    // Pure state machine: holding a key after a chord never opens the overlay.
    public class HoldState {
        bool down, blocked; int began;
        public bool Observe(bool held, bool other, int now, int delay) {
            if (!held) { Reset(); return false; }
            if (!down) { down = true; began = now; }
            if (other) blocked = true;
            return !blocked && unchecked(now - began) >= delay;
        }
        public void Block() { down = true; blocked = true; }
        public void Reset() { down = blocked = false; }
    }
    // Number: the row's numpad digit in the preview (1–9), 0 when numbers are off or past 9.
    public class HintRow { public string Name, Shortcut, State; public AppRule Rule; public List<string> Shortcuts; public int Number; }
    public enum OverlayDisplayReason { None, Hold, ManualPreview, TrayPreview }
    public static class OverlayLayout {
        // Screen rectangles are physical pixels. The card dimensions below are 96-DPI logical pixels.
        // Distance above the taskbar (work-area bottom) in logical pixels; the last position centres the preview.
        public static readonly string[] Positions = { "紧贴", "适中", "较高", "居中" };
        static readonly int[] gaps = { 12, 28, 56 };
        public const int DefaultPosition = 1;
        public static Rectangle Calculate(Rectangle work, int dpi, int count, int logicalWidth = 500, int position = DefaultPosition) {
            float scale = dpi / 96f; int edge = (int)(12 * scale), width = Math.Min((int)(logicalWidth * scale), Math.Max(1, work.Width - edge * 2));
            int height = Math.Min((int)(Math.Max(1, count) * 56 * scale + 16 * scale), Math.Max(1, Math.Min((int)(work.Height * .6), work.Height - edge * 2)));
            if (position < 0 || position >= Positions.Length) position = DefaultPosition;
            int x = work.Left + (work.Width - width) / 2;
            int y = position < gaps.Length ? work.Bottom - (int)(gaps[position] * scale) - height : work.Top + (work.Height - height) / 2;
            y = Math.Max(work.Top + edge, Math.Min(y, work.Bottom - height - edge));
            return new Rectangle(x, y, width, height);
        }
    }
    // Small settings gear in the preview's top-right corner; it opens the Window Switch window.
    sealed class OverlayGear : Control {
        bool hover;
        public OverlayGear() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; TabStop = false; AccessibleName = "设置"; AccessibleRole = AccessibleRole.PushButton;
        }
        // The list underneath (e.g. the first row's highlight) shows through: only the glyph and its hover disc are the gear's own.
        public AppList Behind;
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(p.OverlayBackground);
            if (Behind != null && Parent != null) {
                var at = new Point(Behind.Left - Left, Behind.Top - Top); var state = g.Save();
                g.TranslateTransform(at.X, at.Y); Behind.RenderBehind(g, new Rectangle(-at.X, -at.Y, Width, Height)); g.Restore(state);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (hover) Theme.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), p.Hover, Height / 2);
            using (var font = new Font(Dpi.IconFont, 9.5f))
                TextRenderer.DrawText(g, "", font, ClientRectangle, hover ? p.PrimaryText : p.MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }
    public sealed class HintOverlay : UiForm {
        readonly AppList list = new AppList { Overlay = true, TabStop = false };
        readonly OverlayGear gear = new OverlayGear();
        public bool NativeBackdrop { get; private set; }
        public AppList RowList { get { return list; } }
        public event EventHandler SettingsRequested;
        public event Action<HintRow> AppClicked, SwitchClicked;
        public HintOverlay() {
            list.TargetClicked += row => { if (AppClicked != null) AppClicked(row); };
            list.StatusClicked += row => { if (SwitchClicked != null) SwitchClicked(row); };
            Text = "WindowSwitch.HintOverlay"; FormBorderStyle = FormBorderStyle.None; ControlBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            // The top edge leaves room for the gear; the bottom only needs a sliver (rows carry their own spacing).
            Padding = new Padding(4, 12, 4, 4); Controls.Add(list); Controls.Add(gear); gear.BringToFront(); ClientSize = new Size(460, 136);
            gear.Behind = list; list.Invalidated += delegate { gear.Invalidate(); };
            gear.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            ResumeLayout(true); UpdateSurface();
        }
        // The gear sits in the top-right corner, above the first row's status pill (no extra height).
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e); int size = Dpi.Px(this, 20);
            gear.SetBounds(ClientSize.Width - size - Dpi.Px(this, 6), Dpi.Px(this, 3), size, size); gear.BringToFront();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        // Form.TopMost's managed setter uses SetWindowPos without NOACTIVATE on .NET Framework.
        // Use the native topmost style/placement instead, including during initial handle creation.
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80 | 0x8; value.ClassStyle |= 0x20000; return value; } }
        protected override void WndProc(ref Message msg) { if (msg.Msg == 0x21) { msg.Result = new IntPtr(3); return; } base.WndProc(ref msg); }
        // No system backdrop: extending the glass frame over the client area makes GDI-drawn text and fills
        // partly transparent (washed-out content, a bright rim). The card is opaque with system-rounded corners.
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); NativeBackdrop = false; if (NativeStyle.SystemCorners) NativeStyle.Rounded(Handle, Theme.Current.OverlayBorder); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RoundRegion(); }
        // A clipping region cannot be antialiased (jagged corners), so it is only the Windows 10 fallback.
        void RoundRegion() { if (NativeStyle.SystemCorners) { if (IsHandleCreated) NativeStyle.Rounded(Handle, Theme.Current.OverlayBorder); return; } if (Width <= 0 || Height <= 0) return; using (var path = Theme.Round(new Rectangle(0, 0, Width, Height), (int)(Theme.LargeRadius * DeviceDpi / 96f))) { var old = Region; Region = new Region(path); if (old != null) old.Dispose(); } }
        void UpdateSurface() { BackColor = Theme.Current.OverlayBackground; list.BackColor = BackColor; RoundRegion(); Invalidate(true); }
        protected override void ApplyTheme() { UpdateSurface(); NativeStyle.TitleBar(Handle, Theme.Current.Dark); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); if (NativeStyle.SystemCorners) return; e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using (var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)(Theme.LargeRadius * DeviceDpi / 96f))) using (var pen = new Pen(Theme.Current.OverlayBorder)) e.Graphics.DrawPath(pen, path); }
        // Logical width: 460 (a calm, steady card), growing only when names and keys need it (several shortcuts per
        // app can be shown); past the maximum, extra shortcuts collapse into "+n".
        public const int MinWidth = 460, MaxWidth = 640;
        int FitWidth(List<HintRow> rows) {
            float scale = DeviceDpi / 96f;
            using (var g = list.CreateGraphics()) {
                int px = RowPainter.OverlayRowWidth(g, rows, list.Font, scale) + Padding.Horizontal + (int)(2 * scale);
                return Math.Max(MinWidth, Math.Min(MaxWidth, (int)Math.Ceiling(px / scale)));
            }
        }
        public void Present(List<HintRow> rows, HintOptions options) {
            if (rows.Count == 0) { Hide(); return; }
            // The desktop itself spans every screen, so with nothing but the desktop active the pointer decides too.
            IntPtr foreground = Native.GetForegroundWindow(); string cls = foreground == IntPtr.Zero ? "" : WindowIdentity.ClassName(foreground);
            bool byWindow = !options.FollowMouse && foreground != IntPtr.Zero && cls != "Progman" && cls != "WorkerW";
            Screen screen = byWindow ? Screen.FromHandle(foreground) : Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea; int dpi = byWindow ? (int)NativeStyle.Dpi(foreground) : DeviceDpi;
            Rectangle rect = OverlayLayout.Calculate(area, dpi, rows.Count, FitWidth(rows), options.Position);
            Native.SetWindowPos(Handle, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, 0x10);
            UpdateSurface(); list.SetRows(rows); Show();
            rect = OverlayLayout.Calculate(area, DeviceDpi, rows.Count, FitWidth(rows), options.Position);
            Native.SetWindowPos(Handle, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, 0x10 | 0x40); PerformLayout(); list.SetRows(rows); RoundRegion();
        }
    }
    public sealed class HintMonitor : IDisposable {
        readonly Timer timer = new Timer { Interval = 40 };
        readonly HoldState state = new HoldState();
        readonly Func<HintOptions> options;
        readonly Func<List<HintRow>> rows;
        HintOverlay overlay;
        bool suspended;
        DateTime manualUntil;
        public OverlayDisplayReason Reason { get; private set; }
        public HintOverlay Overlay { get { return overlay; } }
        int previousKey;
        public bool LastHeld { get; private set; }
        public bool LastOther { get; private set; }
        public int LastTick { get; private set; }
        public bool Suspended { get { return suspended; } }
        public bool Visible { get { return overlay != null && overlay.Visible; } }
        public HintMonitor(Func<HintOptions> setting, Func<List<HintRow>> content) {
            options = setting; rows = content; timer.Tick += delegate { Tick(); }; timer.Start();
        }
        public event EventHandler SettingsRequested;
        // Clicking an app in the preview opens it (the preview steps aside); its status pill switches it on or off
        // while the preview stays up to show the new state.
        public event Action<AppRule> AppRequested, EnabledToggleRequested;
        // A numpad digit acts like the app's own shortcut: brings it up, or minimizes it when it is already in front.
        public event Action<AppRule> AppToggleRequested;
        // True while a hold preview is on screen: ↑ ↓ Enter Esc then steer it (the keyboard hook captures them).
        public event Action<bool> NavigableChanged;
        bool navigable;
        void UpdateNavigable() { bool now = Visible && Reason == OverlayDisplayReason.Hold; if (now == navigable) return; navigable = now; if (NavigableChanged != null) NavigableChanged(now); }
        // The preview's rows, numbered 1–9 when numpad opening is on.
        List<HintRow> Rows(HintOptions option) {
            var list = rows(); if (option != null && option.NumberKeys) for (int i = 0; i < list.Count && i < 9; i++) list[i].Number = i + 1;
            return list;
        }
        // Numpad 1–9 (sent as VK_NUMPAD1–9 by the hook) opens that row's app, with or without the preview on screen;
        // ↑/↓ move a highlight (wrapping), releasing the hold key or Enter opens it, Esc closes the preview.
        public void Navigate(int key) {
            if (key >= 0x61 && key <= 0x69) {
                var option = options(); if (suspended || !option.Enabled || !option.NumberKeys) return;
                var numbered = Rows(option); int index = key - 0x61; if (index >= numbered.Count) return;
                if (overlay != null) Dismiss(); else state.Block();
                if (AppToggleRequested != null) AppToggleRequested(numbered[index].Rule); return;
            }
            if (!Visible || Reason != OverlayDisplayReason.Hold) return;
            var list = overlay.RowList; int n = list.Rows.Count; if (n == 0) return;
            if (key == 0x28) list.Highlight = list.Highlight < 0 ? 0 : (list.Highlight + 1) % n;
            else if (key == 0x26) list.Highlight = list.Highlight < 0 ? n - 1 : (list.Highlight - 1 + n) % n;
            else if (key == 0x0D) OpenHighlighted();
            else if (key == 0x1B) Dismiss();
        }
        bool OpenHighlighted() {
            var list = overlay.RowList; int i = list.Highlight; if (i < 0 || i >= list.Rows.Count) return false;
            var rule = list.Rows[i].Rule; Dismiss(); if (AppRequested != null) AppRequested(rule); return true;
        }
        Point shownAt;
        void EnsureOverlay() {
            if (overlay != null) return;
            overlay = new HintOverlay();
            overlay.VisibleChanged += delegate { UpdateNavigable(); };
            overlay.SettingsRequested += delegate { Dismiss(); if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            overlay.AppClicked += row => { Dismiss(); if (AppRequested != null) AppRequested(row.Rule); };
            overlay.SwitchClicked += row => { if (EnabledToggleRequested != null) EnabledToggleRequested(row.Rule); };
        }
        // The hold key may still be down: blocking the hold state keeps the preview from popping straight back up.
        void Dismiss() { Reason = OverlayDisplayReason.None; overlay.Hide(); state.Block(); }
        public void Refresh() { if (Visible) overlay.RowList.SetRows(Rows(options())); }
        void Present(OverlayDisplayReason reason, HintOptions option) { EnsureOverlay(); Reason = reason; overlay.RowList.Highlight = -1; overlay.Present(Rows(option), option); shownAt = Cursor.Position; UpdateNavigable(); }
        // Once the pointer has moved onto the visible preview (e.g. towards the gear), it stays until the pointer leaves,
        // even after the hold key is released. A pointer that merely happened to be there when it appeared does not count.
        bool PointerResting() { if (!Visible) return false; var at = Cursor.Position; return at != shownAt && overlay.Bounds.Contains(at); }
        // A short look at the preview while its settings are being edited (hotkeys are suspended meanwhile).
        DateTime sampleUntil;
        public void Sample(HintOptions option, int ms) { sampleUntil = manualUntil = DateTime.UtcNow.AddMilliseconds(ms); Present(OverlayDisplayReason.ManualPreview, option); }
        public void Suspend(bool value) { suspended = value; state.Reset(); if (value) { Reason = OverlayDisplayReason.None; if (overlay != null) overlay.Hide(); } }
        public void Interrupt() { state.Block(); if (Reason == OverlayDisplayReason.Hold) { Reason = OverlayDisplayReason.None; if (overlay != null) overlay.Hide(); } }
        static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }
        // Reads whether a key is down; replaceable so tests can hold keys without touching the real keyboard.
        public Func<int, bool> KeyState = Down;
        void Tick() {
            LastTick = Environment.TickCount;
            HintOptions option = options();
            if (suspended) { state.Reset(); if (DateTime.UtcNow < sampleUntil || PointerResting()) return; if (overlay != null) overlay.Hide(); return; }
            // Manual previews have a single renewable deadline and do not depend on the hold key.
            if (Reason == OverlayDisplayReason.ManualPreview || Reason == OverlayDisplayReason.TrayPreview) {
                if (DateTime.UtcNow < manualUntil || PointerResting()) return;
                Reason = OverlayDisplayReason.None; if (overlay != null) overlay.Hide(); state.Reset();
            }
            if (!option.Enabled) { state.Reset(); if (overlay != null) overlay.Hide(); Reason = OverlayDisplayReason.None; return; }
            if (previousKey != option.Key) { state.Reset(); previousKey = option.Key; }
            bool held = KeyState(option.Key), other = false;
            if (held) {
                for (int key = 8; key < 255; key++) {
                    // The hook's mask keystroke and, while the preview is up, the keys that steer it are not "other" keys.
                    if (key == SidedHotkeys.MaskVirtualKey || (Visible && SidedHotkeys.IsNavigation((uint)key))) continue;
                    if (key == option.Key || (key == 0x11 && (option.Key == 0xA2 || option.Key == 0xA3)) || (key == 0x10 && (option.Key == 0xA0 || option.Key == 0xA1)) || (key == 0x12 && (option.Key == 0xA4 || option.Key == 0xA5))) continue;
                    if (KeyState(key)) { other = true; break; }
                }
            }
            bool show = state.Observe(held, other, Environment.TickCount, option.DelayMs);
            LastHeld = held; LastOther = other;
            if (show && !Visible) Present(OverlayDisplayReason.Hold, option);
            else if (!show && Visible && Reason == OverlayDisplayReason.Hold && !held && OpenHighlighted()) { }
            else if (!show && Visible && !PointerResting()) { overlay.Hide(); Reason = OverlayDisplayReason.None; }
            UpdateNavigable();
        }
        public void ShowPreview() { ShowPreview(OverlayDisplayReason.ManualPreview); }
        public void ShowPreview(OverlayDisplayReason reason) { manualUntil = DateTime.UtcNow.AddSeconds(5); Present(reason, options()); }
        public void Dispose() { timer.Dispose(); if (overlay != null) overlay.Dispose(); }
    }
    public sealed class HintEditor : SheetPanel {
        readonly ShortcutRecorder keyBox = new ShortcutRecorder { SingleKey = true, AccessibleName = "查看按键" };
        readonly SegmentedControl delay = new SegmentedControl { AccessibleName = "长按时间" };
        readonly TextField customDelay = new TextField();
        readonly Toggle enabled = new Toggle { AccessibleName = "启用长按查看" };
        readonly Toggle numbers = new Toggle { AccessibleName = "数字键盘快速打开" };
        readonly SegmentedControl position = new SegmentedControl { AccessibleName = "预览位置" };
        readonly SegmentedControl screenChoice = new SegmentedControl { AccessibleName = "显示在哪个屏幕" };
        // Shows the preview with the sheet's current (unsaved) choices for the given milliseconds.
        public event Action<HintOptions, int> PreviewRequested;
        HintOptions Draft() { return new HintOptions { Enabled = true, Key = keyBox.KeyCode, DelayMs = MinDelay, Position = position.SelectedIndex, FollowMouse = screenChoice.SelectedIndex == 1, NumberKeys = numbers.Checked }; }
        void RequestPreview(int ms) { if (PreviewRequested != null) PreviewRequested(Draft(), ms); }
        static readonly int[] presets = { 250, 350, 500, 800 };
        public const int MinDelay = 250, MaxDelay = 3000;
        public HintOptions Options;
        public HintEditor(HintOptions current) : base("长按查看快捷键", null) {
            keyBox.KeyCode = current.Key; keyBox.Text = Ui.KeyName(current.Key); enabled.Checked = current.Enabled;
            foreach (int ms in presets) delay.Items.Add((ms / 1000.0).ToString("0.##"));
            delay.Items.Add("自定义");
            int preset = Array.IndexOf(presets, current.DelayMs); delay.SelectedIndex = preset >= 0 ? preset : presets.Length;
            Ui.Row(Content, Ui.Stack("查看按键", keyBox, null));

            var caption = Ui.Label("长按时间（秒）"); caption.Margin = new Padding(0, 0, 0, 6); Ui.Row(Content, caption);
            delay.Dock = DockStyle.None; delay.Anchor = AnchorStyles.Left; delay.Margin = Padding.Empty; Ui.Row(Content, delay);
            // "自定义" reveals a millisecond field; digits only, checked on save.
            var custom = Ui.Table(3); custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); // 250–3000 ms: four digits custom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); custom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            custom.RowStyles.Add(new RowStyle(SizeType.AutoSize)); custom.RowCount = 1; custom.Margin = new Padding(0, 10, 0, 0);
            customDelay.Text = current.DelayMs.ToString(); customDelay.Box.MaxLength = 4; customDelay.Box.AccessibleName = "自定义长按时间（毫秒）";
            customDelay.Box.KeyPress += delegate(object sender, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            customDelay.Dock = DockStyle.Fill; customDelay.Margin = Padding.Empty; custom.Controls.Add(customDelay, 0, 0);
            var unit = Ui.Label("毫秒"); unit.Dock = DockStyle.None; unit.Anchor = AnchorStyles.Left; unit.Margin = new Padding(10, 0, 0, 0); custom.Controls.Add(unit, 1, 0);
            custom.Visible = delay.SelectedIndex == presets.Length; Ui.Row(Content, custom);
            // Picking a position shows the preview there at once.
            foreach (string name in OverlayLayout.Positions) position.Items.Add(name);
            position.SelectedIndex = current.Position >= 0 && current.Position < OverlayLayout.Positions.Length ? current.Position : OverlayLayout.DefaultPosition;
            var where = Ui.Label("预览位置"); where.Margin = new Padding(0, 16, 0, 6); Ui.Row(Content, where);
            position.Dock = DockStyle.None; position.Anchor = AnchorStyles.Left; position.Margin = Padding.Empty; Ui.Row(Content, position);
            screenChoice.Items.Add("跟随当前窗口"); screenChoice.Items.Add("跟随鼠标"); screenChoice.SelectedIndex = current.FollowMouse ? 1 : 0;
            screenChoice.Dock = DockStyle.None; screenChoice.Anchor = AnchorStyles.Left; screenChoice.Margin = new Padding(0, 10, 0, 0); Ui.Row(Content, screenChoice);
            position.SelectedIndexChanged += delegate { RequestPreview(1600); }; screenChoice.SelectedIndexChanged += delegate { RequestPreview(1600); };
            var preview = Ui.Button("预览"); preview.MinimumSize = new Size(64, 34); preview.Click += delegate { RequestPreview(5000); }; Footer.SetLeading(preview);
            var spacer = new Panel { Height = 14, Margin = Padding.Empty, Dock = DockStyle.Top }; Ui.Row(Content, spacer);
            delay.SelectedIndexChanged += delegate {
                custom.Visible = delay.SelectedIndex == presets.Length; Error.Text = "";
                if (custom.Visible) { customDelay.Box.Focus(); customDelay.Box.SelectAll(); }
            };
            numbers.Checked = current.NumberKeys; numbers.CheckedChanged += delegate { RequestPreview(1600); };
            Ui.Row(Content, Ui.ToggleRow("数字键盘快速打开", numbers));
            Ui.Row(Content, Ui.ToggleRow("启用长按查看", enabled));
        }
        // Opening on the recorder would start recording at once and hide the current key.
        public override void FocusFirst() { delay.Focus(); initial = Snapshot(); }
        string initial;
        string Snapshot() { return string.Join("|", keyBox.KeyCode, delay.SelectedIndex, customDelay.Text.Trim(), position.SelectedIndex, screenChoice.SelectedIndex, enabled.Checked, numbers.Checked); }
        public override bool HasChanges { get { return initial != null && Snapshot() != initial; } }
        protected override void Save() {
            int ms;
            if (delay.SelectedIndex < presets.Length) ms = presets[Math.Max(0, delay.SelectedIndex)];
            else if (!int.TryParse(customDelay.Text.Trim(), out ms) || ms < MinDelay || ms > MaxDelay) { Error.Text = "长按时间需在 " + MinDelay + "–" + MaxDelay + " 毫秒之间。"; customDelay.Box.Focus(); customDelay.Box.SelectAll(); return; }
            Options = new HintOptions { Enabled = enabled.Checked, Key = keyBox.KeyCode, DelayMs = ms, Position = position.SelectedIndex, FollowMouse = screenChoice.SelectedIndex == 1, NumberKeys = numbers.Checked }; RaiseSaved();
        }
    }
}
