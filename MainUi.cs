using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowSwitch {
    public sealed partial class MainForm {
        readonly Label hintCaption = Ui.Label("", true);
        GlobalShortcutSetting openApp, minimizeSetting;
        readonly SegmentedControl themeChoice = new SegmentedControl();
        readonly SheetHost sheetHost = new SheetHost { Dock = DockStyle.Fill };
        Card settingsCard;
        SheetPanel sheet; int editorIndex = -1; AppRule editorRule; bool switching, widened; int widenedWidth, restoreWidth, restoreLeft;
        bool editing;
        void BuildUi() {
            Text = "窗口切换 · Window Switch"; ClientSize = new Size(960, 660); MinimumSize = new Size(760, 620); Icon = MakeIcon();
            var root = Ui.Table(); root.Padding = new Padding(24);
            var header = Ui.Table(2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Ui.Heading("窗口切换", 16), 0, 0); summary.TextAlign = ContentAlignment.MiddleRight; summary.Margin = new Padding(12, 0, 0, 6); header.Controls.Add(summary, 1, 0); Ui.Row(root, header);
            var tools = Ui.Flow(); tools.WrapContents = false; tools.Margin = new Padding(0, 12, 0, 16);
            AddButton(tools, "＋ 添加应用", true, delegate { OpenRuleDialog(-1); });
            // Rows open in the sheet with a single click; 测试 and 移除 live in that sheet, 重试 in the tray menu.
            Ui.Row(root, tools);
            var listSurface = new Card { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = Padding.Empty };
            // Dragging a row reorders the apps (hotkeys, preview and list follow the new order).
            list.Reordered += delegate(int from, int to) {
                var copy = new List<AppRule>(settings.Applications); var moved = copy[from]; copy.RemoveAt(from); copy.Insert(to, moved);
                SaveRules(copy);
                if (sheet != null && editorRule != null) editorIndex = settings.Applications.IndexOf(editorRule);
            };
            list.SelectionChanged += delegate { if (!switching && list.SelectedIndex >= 0 && (sheet == null || list.SelectedIndex != editorIndex)) OpenRuleDialog(list.SelectedIndex); }; listSurface.Controls.Add(list); Ui.Row(root, listSurface, true);
            settingsCard = new Card { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16), Margin = new Padding(0, 16, 0, 0) };
            var grid = Ui.Table(2); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowCount = 2;
            // Row 1: Window Switch's own shortcuts side by side. Row 2: hold-to-preview | theme and startup.
            openApp = new GlobalShortcutSetting(this, "打开窗口快捷键", x => x.OpenApp, (x, value) => x.OpenApp = value, () => hotkeys.OpenAppState, null);
            minimizeSetting = new GlobalShortcutSetting(this, "最小化当前窗口", x => x.Minimize, (x, value) => x.Minimize = value, () => hotkeys.MinimizeState, null);
            openApp.View.Margin = new Padding(0, 0, 16, 16); grid.Controls.Add(openApp.View, 0, 0);
            minimizeSetting.View.Margin = new Padding(12, 0, 0, 16); grid.Controls.Add(minimizeSetting.View, 1, 0);
            var hint = Ui.Table(); hint.Margin = new Padding(0, 0, 16, 0); Ui.Row(hint, Ui.Label("快捷键预览")); Ui.Row(hint, hintCaption);
            var buttons = Ui.Flow(); buttons.WrapContents = false; Button customize = Ui.Button("设置查看键"), preview = Ui.Button("预览"); buttons.Controls.Add(customize); buttons.Controls.Add(preview); Ui.Row(hint, buttons);
            customize.Click += delegate { SetHintOptions(); }; preview.Click += delegate { hints.ShowPreview(); note.Text = settings.Applications.Count == 0 ? "请先添加一个应用" : ""; }; grid.Controls.Add(hint, 0, 1);
            var prefs = Ui.Table(); prefs.Margin = new Padding(12, 0, 0, 0);
            themeChoice.Items.AddRange(new[] { "跟随系统", "亮色", "暗色" }); themeChoice.SelectedIndex = (int)settings.Theme; themeChoice.AccessibleName = "主题";
            themeChoice.SelectedIndexChanged += delegate { if (!refreshing) ChangeTheme((ThemeMode)themeChoice.SelectedIndex); };
            var appearance = SettingsPair("主题", themeChoice); appearance.Margin = new Padding(0, 4, 0, 12); Ui.Row(prefs, appearance);
            var launch = SettingsPair("随 Windows 登录启动", startup); launch.Margin = Padding.Empty; Ui.Row(prefs, launch);
            startup.Checked = IsStartupEnabled(); startup.CheckedChanged += delegate { if (!refreshing) ChangeStartup(startup.Checked); };
            grid.Controls.Add(prefs, 1, 1); settingsCard.Controls.Add(grid); Ui.Row(root, settingsCard);
            // The status line only appears when there is something to report (a problem, mostly).
            note.Margin = new Padding(0, 12, 0, 0); note.Text = ""; note.Visible = false; note.TextChanged += delegate { note.Visible = note.Text.Length > 0; }; Ui.Row(root, note);
            root.Dock = DockStyle.None; sheetHost.Main = root; sheetHost.Controls.Add(root); Controls.Add(sheetHost);
            UpdateHintCaption(); ResumeLayout(true); Theme.Apply(this); Application.Idle += WarmUpEditor;
        }
        static Control SettingsPair(string caption, Control control) {
            var table = Ui.Table(2); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var label = Ui.Label(caption); label.TextAlign = ContentAlignment.MiddleLeft; label.Margin = Padding.Empty; table.Controls.Add(label, 0, 0);
            control.Anchor = AnchorStyles.Right; control.Margin = Padding.Empty; table.Controls.Add(control, 1, 0); return table;
        }
        static Button AddButton(FlowLayoutPanel panel, string text, bool primary, EventHandler action) { var button = Ui.Button(text, primary); button.Click += action; panel.Controls.Add(button); return button; }
        void ChangeStartup(bool enable) { try { SetStartup(enable); } catch (Exception ex) { note.Text = "启动设置失败：" + ex.Message; } refreshing = true; startup.Checked = IsStartupEnabled(); refreshing = false; }
        void ChangeTheme(ThemeMode mode) { var proposed = CopySettings(); proposed.Theme = mode; if (SaveSettings(proposed)) Theme.Set(mode); refreshing = true; themeChoice.SelectedIndex = (int)settings.Theme; refreshing = false; }
        void BuildTray() {
            tray.Icon = Icon; tray.Text = "窗口切换 · Window Switch"; tray.Visible = true;
            var menu = new ContextMenuStrip(); menu.Items.Add("打开 Window Switch", null, delegate { ShowSettings(); }); menu.Items.Add("快捷键预览", null, delegate { hints.ShowPreview(OverlayDisplayReason.TrayPreview); });
            menu.Items.Add(new ToolStripSeparator()); var launch = new ToolStripMenuItem("随 Windows 登录启动") { CheckOnClick = false }; launch.Click += delegate { ChangeStartup(!IsStartupEnabled()); }; menu.Items.Add(launch);
            var themes = new ToolStripMenuItem("主题"); string[] names = { "跟随系统", "亮色", "暗色" }; for (int i = 0; i < names.Length; i++) { int mode = i; var item = new ToolStripMenuItem(names[i]); item.Click += delegate { ChangeTheme((ThemeMode)mode); }; themes.DropDownItems.Add(item); } menu.Items.Add(themes);
            var pause = new ToolStripMenuItem("暂停快捷键") { CheckOnClick = true }; pause.CheckedChanged += delegate { paused = pause.Checked; RegisterRules(); }; menu.Items.Add(pause);
            menu.Items.Add("重试快捷键", null, delegate { RegisterRules(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, delegate { Quit(); });
            menu.Opening += delegate { launch.Checked = IsStartupEnabled(); for (int i = 0; i < 3; i++) ((ToolStripMenuItem)themes.DropDownItems[i]).Checked = i == (int)settings.Theme; };
            Ui.StyleMenu(menu);
            menu.Closed += delegate { tray.ReturnFocus(); };
            tray.ContextMenuStrip = menu; tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
        }
        // Add/edit happens in a sheet that slides in from the right; the list stays visible and clicking a row
        // opens (or switches the sheet to) it. Hotkeys stay suspended while a sheet is open (recording must not trigger them).
        void OpenRuleDialog(int index) {
            // The sheet stays tied to its app, wherever that app moves in the list while the sheet is open.
            var original = index < 0 ? null : settings.Applications[index];
            Func<int> at = () => original == null ? -1 : settings.Applications.IndexOf(original);
            var next = new RuleEditor(original);
            next.ValidateRule = delegate(AppRule rule) {
                int i = at(); var copy = new List<AppRule>(settings.Applications); if (i < 0) copy.Add(rule); else copy[i] = rule; var proposed = CopySettings(); proposed.Applications = copy;
                try { HotkeyManager.Validate(proposed); return rule.Enabled ? rule.AllShortcuts().Select(key => hotkeys.Probe(KeyChord.Parse(key))).FirstOrDefault(message => message != null) : null; } catch (Exception ex) { return ex.Message; }
            };
            next.RemoveRequested += delegate {
                int i = at(); CloseEditor(true); if (i < 0) return; var copy = new List<AppRule>(settings.Applications); copy.RemoveAt(i); SaveRules(copy);
                switching = true; list.SelectedIndex = -1; switching = false;
            };
            OpenSheet(next, index, delegate { UpdateRules(next.Rule, at()); });
        }
        void SetHintOptions() {
            var next = new HintEditor(settings.Hint);
            OpenSheet(next, -1, delegate { var proposed = CopySettings(); proposed.Hint = next.Options; if (SaveSettings(proposed)) UpdateHintCaption(); });
        }
        void OpenSheet(SheetPanel next, int index, Action saved) {
            bool replacing = sheet != null;
            if (!replacing) { editing = true; UnregisterRules(); hints.Suspend(true); }
            // Controls created after the window was DPI-scaled are scaled explicitly (margins, padding, fixed sizes).
            next.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
            next.Saved += delegate { saved(); CloseEditor(true); };
            next.Cancelled += delegate { CloseEditor(true); };
            sheet = next; editorIndex = index; editorRule = index >= 0 ? settings.Applications[index] : null; Theme.Apply(next);
            // Widening the window, hiding the settings card and adding the sheet would each relayout and repaint
            // the whole window; do them as one batch so the slide starts right away.
            Native.Batch(this, delegate {
                if (!replacing) { MakeRoomForEditor(); settingsCard.Visible = false; }
                sheetHost.Present(next, Dpi.Px(this, 400), !replacing);
                // A sheet that is not about a row (add, hold-key settings) clears the selection; picking a row switches to it.
                switching = true; if (index >= 0 && list.SelectedIndex != index) list.SelectedIndex = index; else if (index < 0) list.SelectedIndex = -1; switching = false;
            });
            AcceptButton = next.SaveButton; next.FocusFirst();
        }
        void CloseEditor(bool animate) {
            if (sheet == null) return;
            sheet = null; editorIndex = -1; editorRule = null; AcceptButton = null; editing = false; hints.Suspend(false); RegisterRules();
            Action restore = delegate { settingsCard.Visible = true; RestoreWidth(); sheetHost.Dismiss(animate, null); };
            if (IsHandleCreated) Native.Batch(this, restore); else restore();
        }
        // Compile the editor's code paths once while idle, so the first open is as quick as later ones.
        void WarmUpEditor(object sender, EventArgs e) {
            Application.Idle -= WarmUpEditor;
            try { using (var warm = new RuleEditor(null)) { warm.SetBounds(0, 0, 400, 600); warm.CreateControl(); } } catch { }
        }
        // The list keeps at least ~560 logical px next to the sheet; widen the window if needed (within the screen).
        void MakeRoomForEditor() {
            int need = Dpi.Px(this, 960); widened = false;
            if (WindowState != FormWindowState.Normal || ClientSize.Width >= need) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            restoreWidth = Width; restoreLeft = Left; Width = Math.Min(area.Width, Width + need - ClientSize.Width);
            if (Right > area.Right) Left = Math.Max(area.Left, area.Right - Width);
            widened = true; widenedWidth = Width;
        }
        void RestoreWidth() { if (widened && Width == widenedWidth && WindowState == FormWindowState.Normal) { Width = restoreWidth; Left = restoreLeft; } widened = false; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // Esc closes the sheet, except while a shortcut field is recording (there it cancels the recording).
            if (keyData == Keys.Escape && sheet != null && !(ActiveControl is ShortcutRecorder)) { if (!sheet.HandleEscape()) CloseEditor(true); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        // Reports every mouse press inside this app (client or caption area) with the window it landed on.
        sealed class ClickWatcher : IMessageFilter {
            readonly Action<IntPtr> pressed; bool active;
            public ClickWatcher(Action<IntPtr> onPress) { pressed = onPress; }
            public void Start() { if (!active) { active = true; Application.AddMessageFilter(this); } }
            public void Stop() { if (active) { active = false; Application.RemoveMessageFilter(this); } }
            public bool PreFilterMessage(ref Message m) {
                if (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x207 || m.Msg == 0xA1 || m.Msg == 0xA4) pressed(m.HWnd);
                return false;
            }
        }
        // One of Window Switch's own shortcuts, edited as a draft: while the field is active, 停用 becomes 保存 and
        // nothing changes until 保存 is clicked. Clicking anywhere else, Esc, or leaving the window restores it.
        sealed class GlobalShortcutSetting {
            readonly MainForm form; readonly Func<Settings, GlobalShortcut> read; readonly Action<Settings, GlobalShortcut> write; readonly Func<string> state; readonly string idleHint;
            public readonly ShortcutRecorder Recorder = new ShortcutRecorder();
            public readonly Label Caption = Ui.Label("", true);
            public readonly Control View;
            readonly RoundButton action = (RoundButton)Ui.Button("停用");
            readonly ClickWatcher clicks; bool draft;
            public GlobalShortcutSetting(MainForm owner, string title, Func<Settings, GlobalShortcut> read, Action<Settings, GlobalShortcut> write, Func<string> state, string idleHint) {
                form = owner; this.read = read; this.write = write; this.state = state; this.idleHint = idleHint; Recorder.AccessibleName = title;
                var view = Ui.Table(); Ui.Row(view, Ui.Label(title)); Ui.Row(view, Caption);
                Caption.Visible = false; Caption.TextChanged += delegate { Caption.Visible = Caption.Text.Length > 0; };
                var field = Ui.Table(2); field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                Recorder.Dock = DockStyle.Fill; Recorder.Margin = new Padding(0, 4, 8, 0); action.Margin = Padding.Empty;
                field.Controls.Add(Recorder, 0, 0); field.Controls.Add(action, 1, 0); Ui.Row(view, field); View = view;
                Recorder.RecordingStarted += delegate { Begin(); };
                Recorder.Recorded += delegate { if (draft) Caption.Text = ""; };
                Recorder.Cancelled += delegate { End(); };
                // Focus moves recorder -> 保存 when the button is pressed, so only revert once neither has focus.
                EventHandler leave = delegate { if (form.IsHandleCreated) form.BeginInvoke(new Action(delegate { if (draft && !Recorder.Focused && !action.Focused) End(); })); };
                Recorder.Leave += leave; action.Leave += leave;
                // Clicks on labels, cards or empty space do not move keyboard focus, so Leave alone never fires for them.
                clicks = new ClickWatcher(delegate(IntPtr target) { if (draft && target != Recorder.Handle && target != action.Handle) form.BeginInvoke(new Action(End)); });
                form.Deactivate += delegate { if (draft) End(); };
                action.Click += delegate { if (draft) Save(); else Disable(); };
            }
            GlobalShortcut Saved { get { return read(form.settings); } }
            void Begin() {
                if (draft || form.editing) return;
                draft = true; form.editing = true; form.UnregisterRules(); form.hints.Suspend(true);
                action.Enabled = true; action.Text = "保存"; action.Primary = true; action.Invalidate(); clicks.Start();
                Caption.Text = "";
            }
            void End() {
                if (!draft) return;
                draft = false; action.Text = "停用"; action.Primary = false; action.Invalidate(); clicks.Stop();
                form.editing = false; form.hints.Suspend(false); form.RegisterRules();
                if (Recorder.Focused) form.ActiveControl = null;
                Refresh(true);
            }
            void Save() {
                string text = Recorder.Text.Trim(); var saved = Saved;
                if (text.Length == 0 || text == "未设置" || (saved.Enabled && text == saved.Display)) { End(); return; }
                try {
                    var key = KeyChord.Parse(text); var proposed = form.CopySettings();
                    write(proposed, new GlobalShortcut { Enabled = true, Modifiers = key.Modifiers, VirtualKey = key.Key, Sides = key.Sides });
                    HotkeyManager.Validate(proposed); string error = form.hotkeys.Probe(key); if (error != null) throw new ArgumentException(error);
                    if (!form.SaveSettings(proposed)) throw new ArgumentException(form.note.Text);
                    End();
                } catch (Exception ex) { Caption.Text = ex.Message; Recorder.Focus(); }
            }
            void Disable() {
                var saved = Saved; if (!saved.Enabled) return;
                var proposed = form.CopySettings(); write(proposed, new GlobalShortcut { Enabled = false, Modifiers = saved.Modifiers, VirtualKey = saved.VirtualKey, Sides = saved.Sides });
                if (form.SaveSettings(proposed)) Refresh(true);
            }
            // Shows the saved shortcut and its state; left alone while it is being edited.
            public void Refresh(bool force = false) {
                if (draft || (Recorder.Focused && !force)) return;
                var saved = Saved; Recorder.Text = saved.Enabled ? saved.Display : "未设置"; action.Enabled = saved.Enabled;
                string current = state();
                Caption.Text = !saved.Enabled || current == "已生效" ? (idleHint ?? "") : current;
            }
        }
        void RefreshGlobalShortcuts() { if (openApp != null) openApp.Refresh(); if (minimizeSetting != null) minimizeSetting.Refresh(); }
        void UpdateHintCaption() { hintCaption.Text = settings.Hint.Enabled ? "长按 " + Ui.KeyName(settings.Hint.Key) + " · " + (settings.Hint.DelayMs / 1000.0).ToString("0.##") + " 秒" : "长按查看已关闭"; }
        List<HintRow> HintRows() { return settings.Applications.Select(a => new HintRow { Name = a.Name, Shortcut = a.Shortcut, Shortcuts = a.AllShortcuts().ToList(), State = hotkeys.State(a), Rule = a }).ToList(); }
    }
}
