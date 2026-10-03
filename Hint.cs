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
        public HintOptions() { Enabled = true; Key = 0xA3; DelayMs = 650; }
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
    public class HintRow { public string Name, Shortcut, State; public AppRule Rule; public List<string> Shortcuts; }
    public enum OverlayDisplayReason { None, Hold, ManualPreview, TrayPreview }
    public static class OverlayLayout {
        // Screen rectangles are physical pixels. The card dimensions below are 96-DPI logical pixels.
        public static Rectangle Calculate(Rectangle work, int dpi, int count, int logicalWidth = 500) {
            float scale = dpi / 96f; int edge = (int)(16 * scale), width = Math.Min((int)(logicalWidth * scale), Math.Max(1, work.Width - edge * 2));
            int height = Math.Min((int)(Math.Max(1, count) * 56 * scale + 24 * scale), Math.Max(1, Math.Min((int)(work.Height * .6), work.Height - edge * 2)));
            int gap = (int)(56 * scale), x = work.Left + (work.Width - width) / 2, y = work.Bottom - gap - height;
            y = Math.Max(work.Top + edge, Math.Min(y, work.Bottom - height - edge));
            return new Rectangle(x, y, width, height);
        }
    }
    // Small settings gear in the preview's top-right corner; it opens the Window Switch window.
    sealed class OverlayGear : Control {
        bool hover; static string iconFont;
        public OverlayGear() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; TabStop = false; AccessibleName = "设置"; AccessibleRole = AccessibleRole.PushButton;
        }
        // The system icon font draws a crisp gear at any DPI (Windows 11: Segoe Fluent Icons, Windows 10: Segoe MDL2 Assets).
        static string IconFont {
            get {
                if (iconFont == null) using (var fonts = new System.Drawing.Text.InstalledFontCollection()) iconFont = fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                return iconFont;
            }
        }
        protected override void OnPaint(PaintEventArgs e) {
            var p = Theme.Current; var g = e.Graphics; g.Clear(p.OverlayBackground); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (hover) Theme.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), p.Hover, Height / 2);
            using (var font = new Font(IconFont, 9.5f))
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
        public HintOverlay() {
            Text = "WindowSwitch.HintOverlay"; FormBorderStyle = FormBorderStyle.None; ControlBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Padding = new Padding(4, 12, 4, 12); Controls.Add(list); Controls.Add(gear); gear.BringToFront(); ClientSize = new Size(460, 136);
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
        public void Present(List<HintRow> rows, HintOptions options) {
            if (rows.Count == 0) { Hide(); return; }
            IntPtr foreground = Native.GetForegroundWindow(); Screen screen = foreground != IntPtr.Zero ? Screen.FromHandle(foreground) : Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea; int dpi = foreground == IntPtr.Zero ? DeviceDpi : (int)NativeStyle.Dpi(foreground);
            Rectangle rect = OverlayLayout.Calculate(area, dpi, rows.Count);
            Native.SetWindowPos(Handle, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, 0x10);
            UpdateSurface(); list.SetRows(rows); Show();
            rect = OverlayLayout.Calculate(area, DeviceDpi, rows.Count);
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
        Point shownAt;
        void EnsureOverlay() {
            if (overlay != null) return;
            overlay = new HintOverlay();
            overlay.SettingsRequested += delegate {
                Reason = OverlayDisplayReason.None; overlay.Hide(); state.Block();
                if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty);
            };
        }
        void Present(OverlayDisplayReason reason, HintOptions option) { EnsureOverlay(); overlay.Present(rows(), option); Reason = reason; shownAt = Cursor.Position; }
        // Once the pointer has moved onto the visible preview (e.g. towards the gear), it stays until the pointer leaves,
        // even after the hold key is released. A pointer that merely happened to be there when it appeared does not count.
        bool PointerResting() { if (!Visible) return false; var at = Cursor.Position; return at != shownAt && overlay.Bounds.Contains(at); }
        public void Suspend(bool value) { suspended = value; state.Reset(); if (value) { Reason = OverlayDisplayReason.None; if (overlay != null) overlay.Hide(); } }
        public void Interrupt() { state.Block(); if (Reason == OverlayDisplayReason.Hold) { Reason = OverlayDisplayReason.None; if (overlay != null) overlay.Hide(); } }
        static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }
        void Tick() {
            LastTick = Environment.TickCount;
            HintOptions option = options();
            if (suspended) { state.Reset(); if (overlay != null) overlay.Hide(); return; }
            // Manual previews have a single renewable deadline and do not depend on the hold key.
            if (Reason == OverlayDisplayReason.ManualPreview || Reason == OverlayDisplayReason.TrayPreview) {
                if (DateTime.UtcNow < manualUntil || PointerResting()) return;
                Reason = OverlayDisplayReason.None; if (overlay != null) overlay.Hide(); state.Reset();
            }
            if (!option.Enabled) { state.Reset(); if (overlay != null) overlay.Hide(); Reason = OverlayDisplayReason.None; return; }
            if (previousKey != option.Key) { state.Reset(); previousKey = option.Key; }
            bool held = Down(option.Key), other = false;
            if (held) {
                for (int key = 8; key < 255; key++) {
                    if (key == option.Key || (key == 0x11 && (option.Key == 0xA2 || option.Key == 0xA3)) || (key == 0x10 && (option.Key == 0xA0 || option.Key == 0xA1)) || (key == 0x12 && (option.Key == 0xA4 || option.Key == 0xA5))) continue;
                    if (Down(key)) { other = true; break; }
                }
            }
            bool show = state.Observe(held, other, Environment.TickCount, option.DelayMs);
            LastHeld = held; LastOther = other;
            if (show && !Visible) Present(OverlayDisplayReason.Hold, option);
            else if (!show && Visible && !PointerResting()) { overlay.Hide(); Reason = OverlayDisplayReason.None; }
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
            var custom = Ui.Table(3); custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); custom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); custom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            custom.RowStyles.Add(new RowStyle(SizeType.AutoSize)); custom.RowCount = 1; custom.Margin = new Padding(0, 10, 0, 0);
            customDelay.Text = current.DelayMs.ToString(); customDelay.Box.MaxLength = 4; customDelay.Box.AccessibleName = "自定义长按时间（毫秒）";
            customDelay.Box.KeyPress += delegate(object sender, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            customDelay.Dock = DockStyle.Fill; customDelay.Margin = Padding.Empty; custom.Controls.Add(customDelay, 0, 0);
            var unit = Ui.Label("毫秒"); unit.Dock = DockStyle.None; unit.Anchor = AnchorStyles.Left; unit.Margin = new Padding(10, 0, 0, 0); custom.Controls.Add(unit, 1, 0);
            custom.Visible = delay.SelectedIndex == presets.Length; Ui.Row(Content, custom);
            var spacer = new Panel { Height = 14, Margin = Padding.Empty, Dock = DockStyle.Top }; Ui.Row(Content, spacer);
            delay.SelectedIndexChanged += delegate {
                custom.Visible = delay.SelectedIndex == presets.Length; Error.Text = "";
                if (custom.Visible) { customDelay.Box.Focus(); customDelay.Box.SelectAll(); }
            };
            Ui.Row(Content, Ui.ToggleRow("启用长按查看", enabled));
        }
        // Opening on the recorder would start recording at once and hide the current key.
        public override void FocusFirst() { delay.Focus(); }
        protected override void Save() {
            int ms;
            if (delay.SelectedIndex < presets.Length) ms = presets[Math.Max(0, delay.SelectedIndex)];
            else if (!int.TryParse(customDelay.Text.Trim(), out ms) || ms < MinDelay || ms > MaxDelay) { Error.Text = "长按时间需在 " + MinDelay + "–" + MaxDelay + " 毫秒之间。"; customDelay.Box.Focus(); customDelay.Box.SelectAll(); return; }
            Options = new HintOptions { Enabled = enabled.Checked, Key = keyBox.KeyCode, DelayMs = ms }; RaiseSaved();
        }
    }
}
