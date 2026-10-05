using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WindowSwitch {
    public static class Ui {
        public static Color Canvas { get { return Theme.Current.AppBackground; } } public static Color Ink { get { return Theme.Current.PrimaryText; } } public static Color Muted { get { return Theme.Current.SecondaryText; } } public static Color Line { get { return Theme.Current.Border; } } public static Color Green { get { return Theme.Current.SuccessForeground; } }
        public static Label Label(string text, bool muted = false) { return new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 8), ForeColor = muted ? Muted : Ink, Tag = muted ? "muted" : "text", UseMnemonic = false }; }
        public static Label Heading(string text, float size = 21) { var label = Label(text); label.Font = new Font("Microsoft YaHei UI", size, FontStyle.Bold); label.Margin = new Padding(0, 0, 0, 8); return label; }
        public static Button Button(string text, bool primary = false) {
            return new RoundButton { Primary = primary, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(72, 34), Padding = new Padding(12, 5, 12, 5), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
        }
        public static TableLayoutPanel Table(int columns = 1) { var table = new TableLayoutPanel { ColumnCount = columns, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty }; if (columns == 1) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table; }
        public static FlowLayoutPanel Flow() { return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, WrapContents = true }; }
        public static void Row(TableLayoutPanel table, Control control, bool stretch = false) {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(stretch ? SizeType.Percent : SizeType.AutoSize, stretch ? 100 : 0)); table.Controls.Add(control, 0, row); if (table.ColumnCount > 1) table.SetColumnSpan(control, table.ColumnCount);
        }
        public static void Field(TableLayoutPanel table, string caption, Control control) {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = Label(caption); label.Dock = DockStyle.None; label.Anchor = AnchorStyles.Left; label.Margin = new Padding(0, 6, 18, 12);
            table.Controls.Add(label, 0, row); control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 4, 0, 12); table.Controls.Add(control, 1, row);
        }
        public static TableLayoutPanel Fields() { var table = Table(2); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table; }
        public static string KeyName(int key) {
            switch (key) { case 0xA2: return "Left Ctrl"; case 0xA3: return "Right Ctrl"; case 0xA4: return "Left Alt"; case 0xA5: return "Right Alt"; case 0xA0: return "Left Shift"; case 0xA1: return "Right Shift"; case 0x5B: return "Left Win"; case 0x5C: return "Right Win"; case 0x20: return "Space"; case 0x14: return "Caps Lock"; }
            if (key >= 0x30 && key <= 0x39) return ((char)key).ToString(); return KeyChord.Pretty(((Keys)key).ToString());
        }
        static Icon appIcon;
        public static Icon AppIcon { get { return appIcon ?? (appIcon = MainForm.MakeIcon()); } }
        // Label above a field, optional muted help below. Top-docked so a taller neighbour never stretches it.
        public static Control Stack(string caption, Control field, string help) {
            var stack = Table(); var label = Label(caption); label.Margin = new Padding(0, 0, 0, 6); Row(stack, label);
            field.Dock = DockStyle.Fill; field.Margin = Padding.Empty; Row(stack, field);
            if (!string.IsNullOrEmpty(help)) { var note = Label(help, true); note.Margin = new Padding(0, 6, 0, 0); Row(stack, note); }
            stack.Dock = DockStyle.Top; stack.Margin = new Padding(0, 0, 0, 14); return stack;
        }
        // "Caption ............ [toggle]"; clicking the caption flips the toggle too.
        public static Control ToggleRow(string caption, Toggle toggle) {
            var row = Table(2); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.RowStyles.Add(new RowStyle(SizeType.AutoSize)); row.RowCount = 1;
            var label = Label(caption); label.Dock = DockStyle.None; label.Anchor = AnchorStyles.Left; label.Margin = Padding.Empty; label.Cursor = Cursors.Hand;
            label.Click += delegate { toggle.Checked = !toggle.Checked; };
            toggle.Anchor = AnchorStyles.Right; toggle.Margin = Padding.Empty;
            row.Controls.Add(label, 0, 0); row.Controls.Add(toggle, 1, 0); row.Margin = new Padding(0, 2, 0, 10); return row;
        }
        // Tray and context menus: warm popover surface, rounded hover, graphite check marks.
        public static void StyleMenu(ToolStripDropDownMenu menu) {
            menu.Renderer = new MenuRenderer(); menu.ShowImageMargin = false; menu.ShowCheckMargin = true; menu.DropShadowEnabled = true;
            menu.Font = new Font("Microsoft YaHei UI", 9f); menu.Padding = new Padding(Dpi.Px(menu, 3), Dpi.Px(menu, 4), Dpi.Px(menu, 3), Dpi.Px(menu, 4));
            menu.Opening += delegate { NativeStyle.Popup(menu.Handle); };
            foreach (ToolStripItem item in menu.Items) {
                if (!(item is ToolStripSeparator)) item.Padding = new Padding(Dpi.Px(menu, 2), Dpi.Px(menu, 3), Dpi.Px(menu, 8), Dpi.Px(menu, 3));
                var sub = item as ToolStripMenuItem; if (sub != null && sub.HasDropDownItems) StyleMenu((ToolStripDropDownMenu)sub.DropDown);
            }
        }
    }
    public class UiForm : Form {
        public UiForm() {
            SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Font = new Font("Microsoft YaHei UI", 9.5f); BackColor = Ui.Canvas; ForeColor = Ui.Ink;
            StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
            if (!(this is HintOverlay)) Icon = Ui.AppIcon;
            Theme.Changing += ThemeChanging; Theme.Changed += ThemeChanged;
        }
        void ThemeChanged(object sender, EventArgs e) { if (IsDisposed) return; if (InvokeRequired && IsHandleCreated) BeginInvoke(new Action(ApplyThemeBatched)); else ApplyThemeBatched(); }
        // Repainting every control takes longer than a screen refresh, so a plain repaint can show a half-old,
        // half-new frame. Instead, a snapshot of the old look covers the window while it repaints underneath,
        // then fades out: the theme cross-fades and no in-between state is ever visible.
        // The snapshot must be taken before the palette switches: controls read the current palette whenever they
        // repaint, so any paint after the switch would already show a mix.
        ThemeCurtain curtain;
        bool CanCover { get { return IsHandleCreated && Visible && WindowState != FormWindowState.Minimized && !(this is HintOverlay) && ClientSize.Width > 0 && ClientSize.Height > 0; } }
        void ThemeChanging(object sender, EventArgs e) { if (IsDisposed || InvokeRequired || !CanCover || curtain != null) return; try { curtain = ThemeCurtain.Cover(this); } catch { curtain = null; } }
        void ApplyThemeBatched() {
            var cover = curtain; curtain = null;
            if (!CanCover) { ApplyTheme(); if (cover != null) cover.Close(); return; }
            Native.Batch(this, ApplyTheme);
            if (cover != null) cover.FadeOut();
        }
        protected virtual void ApplyTheme() { Theme.Apply(this); }
        protected override void OnLoad(EventArgs e) { ApplyTheme(); base.OnLoad(e); }
        // Windows appear only once fully painted: they are cloaked (laid out and painted, but kept off screen by DWM)
        // while becoming visible, so controls never pop in one by one.
        bool uncloakOnShown, shownOnce;
        protected override void OnShown(EventArgs e) {
            if (!(this is HintOverlay)) FitToScreen();
            shownOnce = true;
            if (uncloakOnShown) { uncloakOnShown = false; Reveal(); }
            base.OnShown(e);
        }
        // Paints the window and every child control right now (Refresh() only paints the form itself; children would
        // paint later, after the window is already on screen), then takes the window out of its cloak.
        void Reveal() {
            if (!IsHandleCreated) return;
            Native.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
            NativeStyle.Cloak(Handle, false);
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e); NativeStyle.TitleBar(Handle, Theme.Current.Dark);
            // Only before the first show: Shown (which uncloaks) never fires again for a recreated handle.
            if (!(this is HintOverlay) && !shownOnce) { NativeStyle.Cloak(Handle, true); uncloakOnShown = true; }
        }
        protected override void SetVisibleCore(bool value) {
            if (!value || this is HintOverlay) { base.SetVisibleCore(value); return; }
            // First show: the handle is created cloaked (here, or earlier when the app started in the background), and
            // Load and layout run inside base.SetVisibleCore. Reveal right away instead of waiting for the deferred
            // Shown event — while that is pending the window counts as open but is invisible.
            if (!IsHandleCreated || uncloakOnShown) { base.SetVisibleCore(true); if (uncloakOnShown) { uncloakOnShown = false; Reveal(); } return; }
            NativeStyle.Cloak(Handle, true);
            try { base.SetVisibleCore(true); } finally { Reveal(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { Theme.Changing -= ThemeChanging; Theme.Changed -= ThemeChanged; } base.Dispose(disposing); }
        public void FitToScreen() {
            Rectangle area = Screen.FromControl(this).WorkingArea; int inset = 16;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - inset * 2), Math.Min(MinimumSize.Height, area.Height - inset * 2));
            Size = new Size(Math.Min(Width, area.Width - inset * 2), Math.Min(Height, area.Height - inset * 2));
            Left = Math.Max(area.Left + inset, Math.Min(Left, area.Right - Width - inset)); Top = Math.Max(area.Top + inset, Math.Min(Top, area.Bottom - Height - inset));
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            WrapLabels(this);
        }
        internal static void WrapLabels(Control parent) {
            foreach (Control child in parent.Controls) {
                var label = child as Label;
                if (label != null && label.AutoSize && label.Dock == DockStyle.Fill && label.Parent.ClientSize.Width > 0) {
                    int width = Math.Max(60, label.Parent.ClientSize.Width - label.Parent.Padding.Horizontal - label.Margin.Horizontal);
                    if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
                }
                if (child.HasChildren) WrapLabels(child);
            }
        }
    }
    // Borderless, non-activating snapshot of a window's client area, faded out after the window has repainted.
    sealed class ThemeCurtain : Form {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmFlush();
        const double Visible99 = 0.99;
        readonly Bitmap snapshot; int started;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        ThemeCurtain(Bitmap image, Rectangle screenBounds) {
            snapshot = image; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Bounds = screenBounds;
            // Created fully transparent: a new window's first composed frame can be blank (black) before it paints.
            Opacity = 0;
            timer.Tick += delegate { double t = Math.Min(1, unchecked(Environment.TickCount - started) / 160.0); Opacity = Visible99 * (1 - t * t * (3 - 2 * t)); if (t >= 1) { timer.Stop(); Close(); } };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80; return value; } } // no-activate, tool window
        // Only the snapshot is ever drawn (no background fill first, which would flash).
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(snapshot, 0, 0); }
        public static ThemeCurtain Cover(Form owner) {
            var size = owner.ClientSize; var image = new Bitmap(size.Width, size.Height); var origin = owner.PointToScreen(Point.Empty);
            using (var g = Graphics.FromImage(image)) {
                // In front, the screen already shows exactly the client area (fast). Otherwise render it, since other
                // windows may overlap it (PrintWindow with full DWM content, client only).
                if (Native.GetForegroundWindow() == owner.Handle) g.CopyFromScreen(origin, Point.Empty, size);
                else { IntPtr dc = g.GetHdc(); try { PrintWindow(owner.Handle, dc, 0x1 | 0x2); } finally { g.ReleaseHdc(dc); } }
            }
            var curtain = new ThemeCurtain(image, new Rectangle(origin, size)) { Owner = owner, TopMost = owner.TopMost };
            // Paint while invisible, then reveal. 0.99 rather than 1 keeps it a layered window (no style change, no flash).
            curtain.Show(); curtain.Update(); curtain.Opacity = Visible99;
            // DWM does not commit two windows' updates in the same frame: wait until the curtain is on screen before
            // the owner starts repainting underneath it.
            try { DwmFlush(); DwmFlush(); } catch (DllNotFoundException) { }
            return curtain;
        }
        public void FadeOut() { started = Environment.TickCount; timer.Start(); }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); snapshot.Dispose(); } base.Dispose(disposing); }
    }
    // Shared shell for the main window's slide-in sheets: title + close, scrolling body, footer with 取消/保存.
    public abstract class SheetPanel : Panel {
        protected readonly TableLayoutPanel Content = Ui.Table();
        protected readonly ScrollPanel Body = new ScrollPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        protected readonly Label Error = Ui.Label("", true);
        protected readonly DialogFooter Footer;
        public readonly Button SaveButton = Ui.Button("保存", true);
        public event EventHandler Saved, Cancelled;
        protected SheetPanel(string title, string lead) {
            Padding = new Padding(1, 0, 0, 0); SetStyle(ControlStyles.ResizeRedraw, true); // its left divider spans the full height
            // Wrap widths follow the sheet's own width (e.g. when its scrollbar appears), not just the window's.
            Body.Layout += delegate { UiForm.WrapLabels(Body); };
            Body.Padding = new Padding(20, 4, 20, 12);
            var header = Ui.Table(2); header.Dock = DockStyle.Top; header.Padding = new Padding(20, 18, 14, 14);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowCount = 1;
            var titles = Ui.Table();
            var heading = Ui.Label(title); heading.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold); heading.Margin = new Padding(0, 0, 0, 2); Ui.Row(titles, heading);
            if (!string.IsNullOrEmpty(lead)) { var note = Ui.Label(lead, true); note.Margin = Padding.Empty; Ui.Row(titles, note); }
            var close = new CloseButton { AccessibleName = "关闭" + title, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = Padding.Empty };
            close.Click += delegate { RaiseCancelled(); };
            header.Controls.Add(titles, 0, 0); header.Controls.Add(close, 1, 0);
            Button cancel = Ui.Button("取消");
            Error.Tag = "error"; Error.ForeColor = Theme.Current.WarningForeground;
            Footer = new DialogFooter(Error, cancel, SaveButton); Footer.Padding = new Padding(20, 14, 20, 16);
            // Dock order: header and footer claim their edges first, the scrolling body takes the rest.
            Body.SetContent(Content); Controls.Add(Body); Controls.Add(Footer); Controls.Add(header);
            SaveButton.Click += delegate { Save(); }; cancel.Click += delegate { RaiseCancelled(); };
        }
        protected abstract void Save();
        public abstract void FocusFirst();
        // Writes the sheet's values (set by the owner); false when they could not be stored.
        public Func<bool> Committing;
        bool committed;
        // Saves as if 保存 were clicked; false when validation or storing failed (the sheet then shows why).
        public bool TrySave() { committed = false; Save(); return committed; }
        // Whether the form differs from what it was opened with.
        public virtual bool HasChanges { get { return false; } }
        // Brief confirmation on the button after the sheet was saved and reopened.
        public void FlashSaved() {
            SaveButton.Text = "已保存"; var timer = new System.Windows.Forms.Timer { Interval = 1400 };
            timer.Tick += delegate { timer.Stop(); timer.Dispose(); if (!SaveButton.IsDisposed) SaveButton.Text = "保存"; }; timer.Start();
        }
        // Esc first closes inner views (e.g. the app picker); returns false when the sheet itself should close.
        public virtual bool HandleEscape() { return false; }
        protected void RaiseSaved() { committed = Committing == null || Committing(); if (committed && Saved != null) Saved(this, EventArgs.Empty); }
        void RaiseCancelled() { if (Cancelled != null) Cancelled(this, EventArgs.Empty); }
        // Composited painting keeps the sheet from flickering while it slides.
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x02000000; return value; } }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Theme.Current.Divider)) e.Graphics.DrawLine(pen, 0, 0, 0, Height); }
    }
    // Target picker shown inside the editor sheet: 正在运行/已安装, search, and a list with real icons.
    // One click (or Enter) chooses; the back arrow or Esc returns to the form.
    public sealed class AppPickerView : Panel {
        readonly bool windowsOnly;
        readonly SegmentedControl source = new SegmentedControl { AccessibleName = "应用来源" };
        readonly TextField search = new TextField { Placeholder = "搜索名称或窗口标题" };
        readonly ChoiceList list = new ChoiceList();
        readonly Label message = Ui.Label("正在识别应用…", true);
        List<AppChoice> running = new List<AppChoice>(), installed = new List<AppChoice>();
        public event Action<AppChoice> Picked;
        public event EventHandler Back;
        public AppPickerView(bool selectWindow) {
            windowsOnly = selectWindow;
            var root = Ui.Table(); root.Padding = new Padding(12, 14, 20, 16);
            var header = Ui.Table(2); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowCount = 1; header.Margin = new Padding(0, 0, 0, 14);
            var back = new CloseButton { Back = true, AccessibleName = "返回", Margin = new Padding(0, 2, 6, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            back.Click += delegate { if (Back != null) Back(this, EventArgs.Empty); };
            var titles = Ui.Table();
            var heading = Ui.Label(selectWindow ? "选择打开的窗口" : "选择应用"); heading.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold); heading.Margin = new Padding(0, 0, 0, 2); Ui.Row(titles, heading);
            header.Controls.Add(back, 0, 0); header.Controls.Add(titles, 1, 0); Ui.Row(root, header);
            var tools = Ui.Table(2); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tools.RowStyles.Add(new RowStyle(SizeType.AutoSize)); tools.RowCount = 1; tools.Margin = new Padding(8, 0, 0, 10);
            source.Items.AddRange(new[] { "正在运行", "已安装" }); source.SelectedIndex = 0; source.Dock = DockStyle.None; source.Anchor = AnchorStyles.Left; source.Margin = Padding.Empty; source.Visible = !selectWindow;
            var refresh = Ui.Button("刷新"); refresh.Anchor = AnchorStyles.Right; refresh.Margin = Padding.Empty; refresh.MinimumSize = new Size(64, Dpi.FieldHeight);
            tools.Controls.Add(source, 0, 0); tools.Controls.Add(refresh, 1, 0); Ui.Row(root, tools);
            search.Dock = DockStyle.Fill; search.Margin = new Padding(8, 0, 0, 10); search.Box.AccessibleName = "搜索应用"; Ui.Row(root, search);
            list.Dock = DockStyle.Fill; list.Margin = new Padding(8, 0, 0, 8); list.AccessibleName = "应用列表"; Ui.Row(root, list, true);
            message.Margin = new Padding(8, 0, 0, 0); Ui.Row(root, message);
            Controls.Add(root);
            source.SelectedIndexChanged += delegate { Filter(); }; search.TextChanged += delegate { Filter(); }; refresh.Click += delegate { LoadApps(); };
            list.Picked += Choose;
            // The search box drives the list: arrows move, Enter chooses (the form's default button is lifted meanwhile).
            search.Box.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { list.MoveHot(e.KeyCode == Keys.Down ? 1 : -1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { list.PickHot(); e.Handled = e.SuppressKeyPress = true; }
            };
        }
        public void Start() { search.Box.Focus(); LoadApps(); }
        void Choose(AppChoice choice) {
            if (windowsOnly && choice.Profile == null && choice.Window != null) choice.TargetType = TargetType.SpecificWindow;
            if (Picked != null) Picked(choice);
        }
        void LoadApps() {
            message.Text = "正在识别应用…";
            var worker = new Thread(delegate() {
                List<AppChoice> windows;
                try { windows = Catalog.Running(); } catch { windows = new List<AppChoice>(); }
                List<AppChoice> apps;
                try { apps = windowsOnly ? new List<AppChoice>() : Catalog.Installed(); } catch { apps = new List<AppChoice>(); }
                foreach (AppChoice window in windows) {
                    var match = apps.FirstOrDefault(a => (!string.IsNullOrWhiteSpace(window.ApplicationId) && a.ApplicationId == window.ApplicationId) || (a.ProcessName.Equals(window.ProcessName, StringComparison.OrdinalIgnoreCase) && !a.Executable.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)));
                    if (match != null && window.Profile == null) { window.Name = match.Name; window.Executable = match.Executable; window.Arguments = match.Arguments; }
                }
                if (!IsDisposed && IsHandleCreated) try { BeginInvoke(new Action(delegate { if (IsDisposed) return; running = windows; installed = apps; Filter(); })); } catch (InvalidOperationException) { }
            }); worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
        }
        void Filter() {
            string query = search.Text.Trim(); var choices = source.SelectedIndex == 0 ? running : installed;
            if (!windowsOnly && source.SelectedIndex == 0) choices = choices.GroupBy(a => a.Name + "|" + a.ProcessName).Select(g => g.First()).ToList();
            var shown = choices.Where(a => a.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || (a.WindowTitle ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            list.SetItems(shown);
            message.Text = shown.Count == 0 ? (query.Length > 0 ? "没有匹配的应用" : "没有找到应用") : shown.Count + " 个" + (windowsOnly ? "窗口" : "应用");
        }
    }
    // The add/edit form, shown in the main window's slide-in sheet. Add: the empty target card leads with "选择".
    // Edit: the current target is a card with "更换"; the ways to pick a target live in a popover.
    public sealed class RuleEditor : SheetPanel {
        readonly bool adding;
        readonly TextField name = new TextField(), process = new TextField(), executable = new TextField(), arguments = new TextField(), title = new TextField(), userData = new TextField();
        readonly TableLayoutPanel shortcutList = Ui.Table();
        readonly List<ShortcutRecorder> recorders = new List<ShortcutRecorder>();
        readonly List<Control> shortcutRows = new List<Control>();
        readonly List<Control[]> rowButtons = new List<Control[]>(); // per row: grip, remove
        readonly List<EyeToggle> eyes = new List<EyeToggle>();          // per row: shown in the hold preview
        int dragIndex = -1;
        readonly TextButton addShortcut = new TextButton { Text = "添加快捷键", AccessibleName = "添加快捷键" };
        SizeF scaledBy = new SizeF(1, 1);
        readonly SelectField profileChoice = new SelectField();
        readonly TargetCard card = new TargetCard();
        readonly Toggle enabled = new Toggle { Checked = true, AccessibleName = "启用这个快捷键" };
        readonly Toggle inPreview = new Toggle { Checked = true, AccessibleName = "显示预览" };
        readonly Toggle previewSides = new Toggle { Checked = true, AccessibleName = "预览显示 Left / Right" };
        // 随鼠标换屏 for this app: follow the main switch, or always / never.
        readonly SegmentedControl screenMode = new SegmentedControl { AccessibleName = "随鼠标换屏" };
        // What pressing the shortcut does while the app is in front: minimize it, or close the window.
        readonly SegmentedControl pressAgain = new SegmentedControl { AccessibleName = "再次按下时" };
        readonly Disclosure disclosure = new Disclosure { Text = "高级设置" };
        readonly TableLayoutPanel advanced = Ui.Table(2);
        Label error { get { return Error; } }
        readonly Control profileBlock, userDataBlock;
        AppRule target = new AppRule(), baseTarget;
        AppPickerView picker; IButtonControl liftedAccept;
        string targetTitle = ""; bool hasTarget, browser, missingProfile, menuOpen, loadingProfiles; int menuClosedAt, profileRequest;
        readonly SynchronizationContext ui;
        public AppRule Rule;
        public Func<AppRule, string> ValidateRule;
        // What the form showed when it opened; browser-profile details that load later are left out on purpose.
        string initial;
        string Snapshot() {
            var keys = string.Join("|", recorders.Select((r, i) => r.Text.Trim() + (eyes.Count > i && eyes[i].Checked ? "*" : "")).Where(t => t.Trim('*').Length > 0));
            return string.Join("", new[] { name.Text.Trim(), process.Text.Trim(), executable.Text.Trim(), arguments.Text, title.Text.Trim(), userData.Text.Trim(), keys,
                enabled.Checked.ToString(), inPreview.Checked.ToString(), previewSides.Checked.ToString(), screenMode.SelectedIndex.ToString(), pressAgain.SelectedIndex.ToString(), target.TargetType.ToString(), target.ProfileDirectory ?? "", target.WindowAppId ?? "" });
        }
        public override bool HasChanges { get { return initial != null && Snapshot() != initial; } }
        // 移除 asks for a second click before it fires.
        public event EventHandler RemoveRequested;
        public RuleEditor(AppRule existing) : base(existing == null ? "添加应用" : "编辑应用", null) {
            adding = existing == null; ui = SynchronizationContext.Current;

            card.Margin = new Padding(0, 0, 0, 16); card.ActionButton.Click += delegate { ChooseTarget(); }; Ui.Row(Content, card);
            if (!adding) {
                var remove = (RoundButton)Ui.Button("移除"); remove.Danger = true; remove.MinimumSize = new Size(64, 34); Footer.SetLeading(remove);
                var disarm = new System.Windows.Forms.Timer { Interval = 3000 };
                disarm.Tick += delegate { disarm.Stop(); remove.Armed = false; remove.Text = "移除"; };
                remove.Click += delegate {
                    if (!remove.Armed) { remove.Armed = true; remove.Text = "确认移除"; disarm.Start(); return; }
                    disarm.Stop(); if (RemoveRequested != null) RemoveRequested(this, EventArgs.Empty);
                };
                Disposed += delegate { disarm.Dispose(); };
            }

            profileBlock = Ui.Stack("浏览器用户", profileChoice, null); profileBlock.Visible = false; Ui.Row(Content, profileBlock);
            profileChoice.AccessibleName = "浏览器用户";
            profileChoice.SelectedIndexChanged += delegate {
                if (loadingProfiles) return;
                var p = profileChoice.SelectedValue as BrowserProfile;
                if (p != null) { string id = target.TargetId; target = p.ToRule(); target.TargetId = id; if (name.Text == "Google Chrome" || name.Text == "Microsoft Edge" || name.Text == "Chrome" || name.Text == "Edge") name.Text = target.Name; arguments.Text = target.Arguments; userData.Text = p.UserData; }
                else if (profileChoice.SelectedIndex == 0 && baseTarget != null) { target = Clone(baseTarget); if (target.TargetType == TargetType.Profile) { target.TargetType = TargetType.Application; target.ProfileDirectory = null; target.WindowAppId = null; } arguments.Text = target.Arguments; }
                missingProfile = false; UpdateCard();
            };

            name.Box.AccessibleName = "显示名称"; Ui.Row(Content, Ui.Stack("显示名称", name, null));
            // Several shortcuts per app: one recorder per row, × removes a row, "+ 添加快捷键" adds one.
            var keyCaption = Ui.Label("快捷键"); keyCaption.Margin = new Padding(0, 0, 0, 6); Ui.Row(Content, keyCaption);
            shortcutList.Margin = Padding.Empty; Ui.Row(Content, shortcutList);
            var keyTools = Ui.Table(2); keyTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); keyTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keyTools.RowStyles.Add(new RowStyle(SizeType.AutoSize)); keyTools.RowCount = 1; keyTools.Margin = new Padding(0, 0, 0, 12);
            addShortcut.Anchor = AnchorStyles.Left; addShortcut.Margin = new Padding(0, 0, 12, 0); keyTools.Controls.Add(addShortcut, 0, 0);
            Ui.Row(Content, keyTools);
            // A new extra shortcut starts hidden from the preview (open its eye to show it there too).
            addShortcut.Click += delegate { var added = AddShortcutRow("", recorders.Count == 0); added.Focus(); };

            Ui.Row(Content, Ui.ToggleRow("启用这个快捷键", enabled));
            Ui.Row(Content, Ui.ToggleRow("显示预览", inPreview));
            var sidesRow = Ui.ToggleRow("预览显示 Left / Right", previewSides); Ui.Row(Content, sidesRow);
            screenMode.Items.AddRange(new[] { "跟随总开关", "总是", "从不" }); screenMode.SelectedIndex = 0;
            var screenRow = Ui.Table(2); screenRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); screenRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            screenRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); screenRow.RowCount = 1; screenRow.Margin = new Padding(0, 0, 0, 12);
            var screenLabel = Ui.Label("随鼠标换屏"); screenLabel.Dock = DockStyle.None; screenLabel.Anchor = AnchorStyles.Left; screenLabel.Margin = Padding.Empty;
            screenMode.Dock = DockStyle.None; screenMode.Anchor = AnchorStyles.Right; screenMode.Margin = Padding.Empty;
            screenRow.Controls.Add(screenLabel, 0, 0); screenRow.Controls.Add(screenMode, 1, 0); Ui.Row(Content, screenRow);
            pressAgain.Items.AddRange(new[] { "最小化", "关闭窗口" }); pressAgain.SelectedIndex = 0;
            var againRow = Ui.Table(2); againRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); againRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            againRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); againRow.RowCount = 1; againRow.Margin = new Padding(0, 0, 0, 12);
            var againLabel = Ui.Label("再次按下时"); againLabel.Dock = DockStyle.None; againLabel.Anchor = AnchorStyles.Left; againLabel.Margin = Padding.Empty;
            pressAgain.Dock = DockStyle.None; pressAgain.Anchor = AnchorStyles.Right; pressAgain.Margin = Padding.Empty;
            againRow.Controls.Add(againLabel, 0, 0); againRow.Controls.Add(pressAgain, 1, 0); Ui.Row(Content, againRow);
            inPreview.CheckedChanged += delegate { sidesRow.Visible = inPreview.Checked; foreach (var eye in eyes) eye.Enabled = inPreview.Checked; };

            disclosure.Dock = DockStyle.None; disclosure.Anchor = AnchorStyles.Left; disclosure.Margin = Padding.Empty; Ui.Row(Content, disclosure);
            advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44)); advanced.Visible = false; advanced.Margin = new Padding(0, 10, 0, 0);
            AddAdvanced(Ui.Stack("进程名称", process, null), 0, false); AddAdvanced(Ui.Stack("窗口标题包含", title, null), 1, false);
            AddAdvanced(Ui.Stack("启动程序", executable, null), 0, true); AddAdvanced(Ui.Stack("启动参数", arguments, null), 0, true);
            var dataRow = Ui.Table(2); dataRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); dataRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); dataRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); dataRow.RowCount = 1;
            var reloadProfiles = Ui.Button("重新识别用户"); reloadProfiles.MinimumSize = new Size(72, Dpi.FieldHeight); reloadProfiles.Margin = new Padding(8, 0, 0, 0); reloadProfiles.Click += delegate { LoadProfiles(target.ProfileDirectory); };
            userData.Dock = DockStyle.Fill; userData.Margin = Padding.Empty; dataRow.Controls.Add(userData, 0, 0); dataRow.Controls.Add(reloadProfiles, 1, 0);
            userDataBlock = Ui.Stack("浏览器数据目录", dataRow, null); userDataBlock.Visible = false; AddAdvanced(userDataBlock, 0, true);
            Ui.Row(Content, advanced);
            disclosure.ExpandedChanged += delegate { advanced.Visible = disclosure.Expanded; if (disclosure.Expanded) Body.RevealLater(advanced); };

            if (existing != null) {
                target = Clone(existing); name.Text = existing.Name; int n = 0; foreach (string key in existing.AllShortcuts()) AddShortcutRow(key, existing.PreviewShortcuts == null ? n++ == 0 : existing.PreviewShortcuts.Contains(key)); process.Text = existing.ProcessName; executable.Text = existing.Executable; arguments.Text = existing.Arguments; title.Text = existing.TitleContains; userData.Text = existing.UserDataDirectory; enabled.Checked = existing.Enabled; inPreview.Checked = !existing.HideInPreview; previewSides.Checked = !existing.HideSidesInPreview; screenMode.SelectedIndex = existing.ScreenMode >= 0 && existing.ScreenMode <= 2 ? existing.ScreenMode : 0; pressAgain.SelectedIndex = existing.PressAgain == 1 ? 1 : 0;
                hasTarget = true; targetTitle = TargetTitle(existing); LoadProfiles(existing.ProfileDirectory);
            }
            if (recorders.Count == 0) AddShortcutRow("", true);
            initial = Snapshot();
            UpdateCard();
        }
        // Rows added after the sheet was DPI-scaled must be scaled the same way.
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { base.ScaleControl(factor, specified); scaledBy = new SizeF(scaledBy.Width * factor.Width, scaledBy.Height * factor.Height); }
        ShortcutRecorder AddShortcutRow(string value, bool shown) {
            var recorder = new ShortcutRecorder { Text = value ?? "", AccessibleName = "快捷键" };
            var grip = new DragHandle { Margin = new Padding(0, 0, 4, 0), Anchor = AnchorStyles.Left };
            var eye = new EyeToggle { Checked = shown, Enabled = inPreview.Checked, Size = new Size(30, 32), Margin = new Padding(6, 0, 0, 0), Anchor = AnchorStyles.Right };
            var remove = new CloseButton { AccessibleName = "删除这个快捷键", Size = new Size(28, 32), Margin = new Padding(2, 0, 0, 0), Anchor = AnchorStyles.Right };
            var row = Ui.Table(4); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize)); row.RowCount = 1; row.Margin = new Padding(0, 0, 0, 8);
            recorder.Dock = DockStyle.Fill; recorder.Margin = Padding.Empty;
            row.Controls.Add(grip, 0, 0); row.Controls.Add(recorder, 1, 0); row.Controls.Add(eye, 2, 0); row.Controls.Add(remove, 3, 0);
            if (scaledBy.Width != 1) row.Scale(scaledBy);
            // Rows stay in place; dragging moves the value through them, so the dragged shortcut follows the pointer.
            grip.DragStarted += delegate { dragIndex = shortcutRows.IndexOf(row); if (dragIndex >= 0) MarkDragged(dragIndex, true); };
            grip.DragMoved += delegate(Point screen) { if (dragIndex >= 0) DragTo(RowAt(shortcutList.PointToClient(screen).Y)); };
            grip.DragEnded += delegate { if (dragIndex >= 0 && dragIndex < recorders.Count) MarkDragged(dragIndex, false); dragIndex = -1; };
            remove.Click += delegate {
                int at = shortcutRows.IndexOf(row); recorders.RemoveAt(at); shortcutRows.RemoveAt(at); rowButtons.RemoveAt(at); eyes.RemoveAt(at);
                shortcutList.Controls.Remove(row); row.Dispose(); UpdateShortcutRows();
                if (recorders.Count > 0) addShortcut.Focus();
            };
            recorders.Add(recorder); shortcutRows.Add(row); rowButtons.Add(new Control[] { grip, remove }); eyes.Add(eye);
            Ui.Row(shortcutList, row); Theme.Apply(row); UpdateShortcutRows();
            return recorder;
        }
        // Index of the row whose centre is nearest to y (shortcut list coordinates).
        int RowAt(int y) {
            int best = 0, distance = int.MaxValue;
            for (int i = 0; i < shortcutRows.Count; i++) { int d = Math.Abs(shortcutRows[i].Top + shortcutRows[i].Height / 2 - y); if (d < distance) { distance = d; best = i; } }
            return best;
        }
        void DragTo(int target) {
            while (dragIndex != target) {
                int next = dragIndex + Math.Sign(target - dragIndex);
                string text = recorders[dragIndex].Text; recorders[dragIndex].Text = recorders[next].Text; recorders[next].Text = text;
                bool shown = eyes[dragIndex].Checked; eyes[dragIndex].Checked = eyes[next].Checked; eyes[next].Checked = shown;
                MarkDragged(dragIndex, false); MarkDragged(next, true); dragIndex = next;
            }
        }
        void MarkDragged(int index, bool on) { recorders[index].Highlight = on; ((DragHandle)rowButtons[index][0]).Active = on; }
        // The grip and × only appear when there is more than one shortcut.
        void UpdateShortcutRows() {
            foreach (var buttons in rowButtons) foreach (var button in buttons) button.Visible = recorders.Count > 1;
            addShortcut.Visible = recorders.Count < HotkeyManager.MaxShortcuts;
        }
        // Add mode starts on the target picker; edit mode on the name (focusing the recorder would start recording).
        public override void FocusFirst() { if (adding) card.ActionButton.Focus(); else { name.Box.Focus(); name.Box.SelectionStart = name.Box.TextLength; } }
        void AddAdvanced(Control block, int column, bool span) {
            if (column == 0) { advanced.RowCount++; advanced.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            block.Margin = new Padding(column == 1 ? 8 : 0, 0, column == 0 && !span ? 8 : 0, 12);
            advanced.Controls.Add(block, column, advanced.RowCount - 1); if (span) advanced.SetColumnSpan(block, 2);
        }
        static string TargetTitle(AppRule rule) {
            string proc = rule.ProcessName ?? "";
            if (proc.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
            if (proc.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            try {
                string exe = Environment.ExpandEnvironmentVariables(rule.Executable ?? "");
                if (File.Exists(exe) && !Path.GetFileName(exe).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) {
                    string product = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductName;
                    if (!string.IsNullOrWhiteSpace(product) && !product.Equals("Electron", StringComparison.OrdinalIgnoreCase) && product.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) < 0) return product.Trim();
                }
            } catch { }
            return rule.Name;
        }
        void UpdateCard() {
            if (!hasTarget) { card.SetTarget(null, "选择要切换的应用", "", false, "选择", true); return; }
            string how; bool warn = false; var profile = profileChoice.SelectedValue as BrowserProfile;
            if (target.TargetType == TargetType.SpecificWindow) how = "仅所选窗口";
            else if (profile != null) how = "浏览器用户 " + profile.Name;
            else if (loadingProfiles && target.TargetType == TargetType.Profile) how = "浏览器用户 · 读取中…";
            else if (missingProfile) { how = "未找到保存的浏览器用户，请重新选择"; warn = true; }
            else how = "整个应用";
            string exe = Path.GetFileNameWithoutExtension(process.Text.Trim());
            card.SetTarget(target, targetTitle, warn || exe.Length == 0 ? how : how + " · " + exe + ".exe", warn, "更换", false);
        }
        void ChooseTarget() {
            if (menuOpen || unchecked(Environment.TickCount - menuClosedAt) < 250) return;
            var options = new List<Option> { new Option("从应用中选择"), new Option("选择一个打开的窗口"), new Option("程序文件…") };
            menuOpen = true;
            Popover.Show(card.ActionButton, options, -1, false, Dpi.Px(this, 300), true, delegate(int i) {
                if (i == 0 || i == 1) ShowPicker(i == 1);
                else using (var d = new OpenFileDialog { Filter = "应用或快捷方式|*.exe;*.lnk|所有文件|*.*" }) { if (d.ShowDialog(FindForm()) == DialogResult.OK) try { ApplyChoice(Catalog.FromFile(d.FileName)); } catch (Exception ex) { error.Text = ex.Message; } }
            }, delegate { menuOpen = false; menuClosedAt = Environment.TickCount; });
        }
        void ShowPicker(bool windows) {
            ClosePicker();
            var view = new AppPickerView(windows);
            view.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f)); Theme.Apply(view);
            view.Picked += delegate(AppChoice choice) { ClosePicker(); ApplyChoice(choice); };
            view.Back += delegate { ClosePicker(); card.ActionButton.Focus(); };
            picker = view; Controls.Add(view); view.BringToFront(); PerformLayout();
            var form = FindForm(); if (form != null) { liftedAccept = form.AcceptButton; form.AcceptButton = null; }
            view.Start();
        }
        void ClosePicker() {
            if (picker == null) return;
            var view = picker; picker = null; Controls.Remove(view); view.Dispose();
            var form = FindForm(); if (form != null && form.AcceptButton == null) form.AcceptButton = liftedAccept;
        }
        // The picker covers the whole sheet (header and footer included), keeping the 1 px divider on the left.
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (picker != null) picker.Bounds = DisplayRectangle; }
        public override bool HandleEscape() { if (picker == null) return false; ClosePicker(); card.ActionButton.Focus(); return true; }
        public void ApplyChoice(AppChoice choice) {
            string id = target.TargetId; target = choice.ToRule(); target.TargetId = id;
            name.Text = choice.Name; process.Text = target.ProcessName; executable.Text = target.Executable; arguments.Text = target.Arguments ?? ""; title.Text = ""; userData.Text = target.UserDataDirectory;
            hasTarget = true; targetTitle = TargetTitle(target); error.Text = "";
            LoadProfiles(target.ProfileDirectory); UpdateCard();
            var empty = recorders.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Text)); (empty ?? recorders[0]).Focus();
        }
        internal static AppRule Clone(AppRule rule) { var json = new System.Web.Script.Serialization.JavaScriptSerializer(); return json.Deserialize<AppRule>(json.Serialize(rule)); }
        // Reading Chrome/Edge "Local State" and enumerating browser windows takes a few hundred milliseconds,
        // so it runs off the UI thread and the dialog opens at once. The newest request wins.
        void LoadProfiles(string selected) {
            baseTarget = Clone(target); profileChoice.Clear(); missingProfile = false; loadingProfiles = false;
            browser = BrowserProfiles.IsBrowser(process.Text); profileBlock.Visible = browser; userDataBlock.Visible = browser;
            int request = ++profileRequest;
            if (browser) {
                bool window = target.TargetType == TargetType.SpecificWindow;
                var first = new Option(window ? "仅关联所选窗口" : "整个应用");
                loadingProfiles = true; profileChoice.Items.Add(new Option("正在读取浏览器用户…")); profileChoice.SelectedIndex = 0; profileChoice.Enabled = false;
                string exe = executable.Text, proc = process.Text, data = userData.Text;
                ThreadPool.QueueUserWorkItem(delegate {
                    List<BrowserProfile> found; try { found = BrowserProfiles.Read(exe, proc, data); } catch { found = new List<BrowserProfile>(); }
                    if (ui != null) ui.Post(delegate { if (!IsDisposed && request == profileRequest) ShowProfiles(first, found, selected); }, null);
                });
            }
            UpdateCard();
        }
        void ShowProfiles(Option first, List<BrowserProfile> found, string selected) {
            profileChoice.Clear(); profileChoice.Enabled = true; loadingProfiles = false;
            profileChoice.Items.Add(first); foreach (var p in found) profileChoice.Items.Add(new Option(p.Name, p.Directory, p));
            profileChoice.SelectedIndex = 0;
            for (int i = 1; i < profileChoice.Items.Count; i++) if (((BrowserProfile)profileChoice.Items[i].Value).Directory == selected) { profileChoice.SelectedIndex = i; break; }
            missingProfile = baseTarget.TargetType == TargetType.Profile && profileChoice.SelectedIndex == 0;
            UpdateCard();
        }
        // The rule as currently shown in the form (target, name, advanced fields), without validation.
        AppRule Draft() {
            if (string.IsNullOrWhiteSpace(executable.Text) || string.IsNullOrWhiteSpace(process.Text)) return null;
            var draft = Clone(target); draft.Name = name.Text.Trim(); draft.ProcessName = Path.GetFileNameWithoutExtension(process.Text.Trim()); draft.Executable = executable.Text.Trim().Trim('"'); draft.Arguments = arguments.Text; draft.TitleContains = title.Text.Trim(); draft.UserDataDirectory = userData.Text.Trim();
            if (browser && !loadingProfiles && profileChoice.SelectedIndex == 0 && target.TargetType != TargetType.SpecificWindow) { draft.TargetType = TargetType.Application; draft.ProfileDirectory = null; draft.WindowAppId = null; }
            return draft;
        }
        protected override void Save() {
            try {
                if (string.IsNullOrWhiteSpace(executable.Text) || string.IsNullOrWhiteSpace(process.Text)) throw new ArgumentException("请先选择要切换的应用。");
                if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("请填写显示名称。");
                var keys = recorders.Select(r => r.Text.Trim()).Where(t => t.Length > 0).ToList();
                if (keys.Count == 0) throw new ArgumentException("请至少设置一个快捷键。");
                var seen = new HashSet<string>(); foreach (string key in keys) if (!seen.Add(KeyChord.Parse(key).Id)) throw new ArgumentException("同一个快捷键填写了两次：" + KeyChord.Pretty(key));
                var selectedProfile = profileChoice.SelectedValue as BrowserProfile;
                if (target.TargetType == TargetType.Profile && selectedProfile != null && !selectedProfile.UserData.Equals(userData.Text.Trim(), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("数据目录已改变，请点击高级设置里的“重新识别用户”。");
                var proposed = Draft(); proposed.Shortcut = keys[0]; proposed.MoreShortcuts = keys.Skip(1).ToList(); proposed.Enabled = enabled.Checked; proposed.HideInPreview = !inPreview.Checked; proposed.HideSidesInPreview = !previewSides.Checked; proposed.ScreenMode = screenMode.SelectedIndex; proposed.PressAgain = pressAgain.SelectedIndex;
                proposed.PreviewShortcuts = recorders.Select((r, i) => new { Key = r.Text.Trim(), Shown = eyes[i].Checked }).Where(x => x.Key.Length > 0 && x.Shown).Select(x => x.Key).ToList();
                if (ValidateRule != null) { string message = ValidateRule(proposed); if (message != null) throw new ArgumentException(message); }
                Rule = proposed; RaiseSaved();
            } catch (Exception ex) { error.Text = ex.Message; }
        }
    }
}
