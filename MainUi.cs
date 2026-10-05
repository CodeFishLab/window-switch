using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowSwitch {
    public sealed partial class MainForm {
        // The hold-preview summary (长按 Left Alt · 0.3 秒), boxed like the shortcut fields; clicking it opens the settings.
        readonly ValueField hintCaption = new ValueField { AccessibleName = "快捷键预览" };
        GlobalShortcutSetting openApp, minimizeSetting, moveScreenSetting;
        readonly SegmentedControl themeChoice = new SegmentedControl();
        readonly Toggle trayToggle = new Toggle { AccessibleName = "显示托盘图标" };
        readonly Toggle screenToggle = new Toggle { AccessibleName = "随鼠标换屏" };
        readonly Toggle doubleTapToggle = new Toggle { AccessibleName = "连续双击放大" };
        // Esc right after a switch puts the window away again; how long that chance lasts (0 = off).
        readonly SegmentedControl undoChoice = new SegmentedControl { AccessibleName = "Esc 撤销切换" };
        static readonly int[] undoPresets = { 0, 500, 1000 };
        // Custom durations in seconds (any positive value); -1 keeps it on without a time limit (until that window is left).
        static string UndoLabel(int ms) { return ms < 0 ? "持续" : (ms / 1000.0).ToString("0.##") + " 秒"; }
        ToolStripDropDown undoPopup;
        // Presets select their segment; any other duration selects the last one, which then shows that value.
        void ShowUndo() {
            int ms = settings.UndoMs, index = Array.IndexOf(undoPresets, ms); bool custom = index < 0;
            undoChoice.Items[undoPresets.Length] = custom ? UndoLabel(ms) : "自定义";
            refreshing = true; undoChoice.SelectedIndex = custom ? undoPresets.Length : index; refreshing = false;
            undoChoice.Invalidate(); if (undoChoice.Parent != null) undoChoice.Parent.PerformLayout();
        }
        void AskUndo() {
            int ms = settings.UndoMs; bool custom = Array.IndexOf(undoPresets, ms) < 0;
            string current = !custom ? "1.5" : ms < 0 ? "-1" : (ms / 1000.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            undoPopup = Popover.ShowInput(undoChoice, current, "秒", "-1 为持续生效", Dpi.Px(this, 140), text => {
                double seconds; text = text.Trim();
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds)) return "请输入秒数或 -1";
                if (seconds == -1) { ChangeUndo(-1); return null; }
                if (seconds <= 0 || seconds * 1000 >= int.MaxValue / 2) return "请输入秒数或 -1";
                ChangeUndo((int)Math.Max(1, Math.Round(seconds * 1000))); return null;
            }, delegate { undoPopup = null; ShowUndo(); });
        }
        readonly SheetHost sheetHost = new SheetHost { Dock = DockStyle.Fill };
        Card settingsCard;
        SheetPanel sheet; int editorIndex = -1; AppRule editorRule; bool switching, widened; int widenedWidth, restoreWidth, restoreLeft;
        bool editing;
        void BuildUi() {
            Text = "窗口切换 · Window Switch"; ClientSize = new Size(960, 660); MinimumSize = new Size(760, 620); Icon = MakeIcon();
            var root = Ui.Table(); root.Padding = new Padding(24);
            var header = Ui.Table(2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Ui.Heading("窗口切换", 16), 0, 0); summary.TextAlign = ContentAlignment.MiddleRight; summary.Margin = new Padding(12, 0, 0, 6); header.Controls.Add(summary, 1, 0); Ui.Row(root, header);
            var toolbar = Ui.Table(2); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); toolbar.Margin = new Padding(0, 12, 0, 16);
            var tools = Ui.Flow(); tools.WrapContents = false;
            AddButton(tools, "＋ 添加应用", true, delegate { if (LeaveSheet()) OpenRuleDialog(-1); });
            // Rows open in the sheet with a single click; 测试 and 移除 live in that sheet, 重试 in the tray menu.
            // The pause switch (a status pill) sits under the summary on the right (the same switch as the tray menu item); not saved.
            pauseSwitch.Anchor = AnchorStyles.Right; pauseSwitch.Margin = Padding.Empty; pauseSwitch.Click += delegate { SetPaused(!paused); };
            toolbar.Controls.Add(tools, 0, 0); toolbar.Controls.Add(pauseSwitch, 1, 0);
            Ui.Row(root, toolbar);
            var listSurface = new Card { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = Padding.Empty };
            // Dragging a row reorders the apps (hotkeys, preview and list follow the new order).
            list.StatusClicked += row => { if (editing || sheet != null) list.SelectedIndex = settings.Applications.IndexOf(row.Rule); else ToggleRuleEnabled(row.Rule); };
            list.Reordered += delegate(int from, int to) {
                var copy = new List<AppRule>(settings.Applications); var moved = copy[from]; copy.RemoveAt(from); copy.Insert(to, moved);
                SaveRules(copy);
                if (sheet != null && editorRule != null) editorIndex = settings.Applications.IndexOf(editorRule);
            };
            list.SelectionChanged += delegate {
                if (switching || list.SelectedIndex < 0 || (sheet != null && list.SelectedIndex == editorIndex)) return;
                int picked = list.SelectedIndex;
                if (!LeaveSheet()) { switching = true; list.SelectedIndex = editorIndex; switching = false; return; }
                OpenRuleDialog(picked);
            }; listSurface.Controls.Add(list); Ui.Row(root, listSurface, true);
            list.Resize += delegate { AlignPauseSwitch(); };
            settingsCard = new Card { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16), Margin = new Padding(0, 16, 0, 0) };
            var grid = Ui.Table(2); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowCount = 1;
            // One setting per row, label left and control right. Left: Window Switch's own keys; right: preferences.
            // Every row is a field's height, so the rows of both columns line up.
            var keys = Ui.Table(3); keys.Margin = new Padding(0, 0, 16, 0);
            keys.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); keys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keys.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            openApp = new GlobalShortcutSetting(this, "打开窗口快捷键", x => x.OpenApp, (x, value) => x.OpenApp = value, () => hotkeys.OpenAppState, null);
            minimizeSetting = new GlobalShortcutSetting(this, "最小化当前窗口", x => x.Minimize, (x, value) => x.Minimize = value, () => hotkeys.MinimizeState, null);
            moveScreenSetting = new GlobalShortcutSetting(this, "快速切屏", x => x.MoveScreen, (x, value) => x.MoveScreen = value, () => hotkeys.MoveScreenState, null);
            InfoTip.Attach(moveScreenSetting.Title, "按下快捷键，将当前窗口移至鼠标所在的显示器。");
            foreach (var setting in new[] { openApp, minimizeSetting, moveScreenSetting }) {
                setting.Recorder.Dock = DockStyle.Fill;
                KeyRow(keys, setting.Title, setting.Recorder, setting.ActionButton);
                // Problems (taken, cannot listen) show under the field; otherwise the row stays compact.
                int at = keys.RowCount++; keys.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                setting.Caption.Margin = new Padding(0, 0, 0, 12); keys.Controls.Add(setting.Caption, 1, at); keys.SetColumnSpan(setting.Caption, 2);
            }
            hintCaption.Dock = DockStyle.Fill; hintCaption.Click += delegate { SetHintOptions(); };
            Button customize = Ui.Button("设置"); customize.Click += delegate { SetHintOptions(); };
            var previewName = Ui.Label("快捷键预览"); KeyRow(keys, previewName, hintCaption, customize);
            InfoTip.Attach(previewName, "长按查看键，显示所有应用及其快捷键。");
            // A quick double press of an app's shortcut maximizes (or restores) its window.
            doubleTapToggle.Checked = settings.DoubleTapMaximize; doubleTapToggle.CheckedChanged += delegate { if (!refreshing) ChangeDoubleTap(doubleTapToggle.Checked); };
            var doubleTapName = Ui.Label("连续双击放大"); KeyRow(keys, doubleTapName, new Panel { BackColor = Color.Transparent }, doubleTapToggle);
            InfoTip.Attach(doubleTapName, "快速按两次应用快捷键，将窗口最大化；再按两次恢复原大小。");
            keys.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); keys.RowCount++; // as below: rows stay at the top
            grid.Controls.Add(keys, 0, 0);
            var prefs = Ui.Table(); prefs.Margin = new Padding(12, 0, 0, 0);
            themeChoice.Items.AddRange(new[] { "跟随系统", "亮色", "暗色" }); themeChoice.SelectedIndex = (int)settings.Theme; themeChoice.AccessibleName = "主题";
            themeChoice.SelectedIndexChanged += delegate { if (!refreshing) ChangeTheme((ThemeMode)themeChoice.SelectedIndex); };
            Ui.Row(prefs, SettingsPair("主题", themeChoice));
            undoChoice.Items.AddRange(new[] { "关", "0.5 秒", "1 秒", "自定义" }); ShowUndo();
            // A preset applies at once; 自定义 (or clicking it again) opens a small field for any duration.
            undoChoice.SelectedIndexChanged += delegate { if (refreshing) return; if (undoChoice.SelectedIndex < undoPresets.Length) ChangeUndo(undoPresets[undoChoice.SelectedIndex]); else AskUndo(); };
            undoChoice.MouseUp += delegate { if (!refreshing && undoChoice.SelectedIndex == undoPresets.Length && undoPopup == null) AskUndo(); };
            Ui.Row(prefs, SettingsPair("Esc 撤销切换", undoChoice, "切换应用后，在设定时间内按 Esc，可将刚打开的窗口最小化。"));
            screenToggle.Checked = settings.FollowPointerScreen; screenToggle.CheckedChanged += delegate { if (!refreshing) ChangeFollowScreen(screenToggle.Checked); };
            Ui.Row(prefs, SettingsPair("随鼠标换屏", screenToggle, "切换应用时，将窗口移至鼠标所在的显示器。\n可在「编辑应用」中单独设置。"));
            Ui.Row(prefs, SettingsPair("随 Windows 登录启动", startup));
            startup.Checked = IsStartupEnabled(); startup.CheckedChanged += delegate { if (!refreshing) ChangeStartup(startup.Checked); };
            var trayPair = SettingsPair("显示托盘图标", trayToggle); trayPair.Margin = Padding.Empty; Ui.Row(prefs, trayPair);
            trayToggle.Checked = settings.ShowTrayIcon; trayToggle.CheckedChanged += delegate { if (!refreshing) ChangeTrayIcon(trayToggle.Checked); };
            // The columns differ in height; an empty stretch row keeps each column's rows together at the top.
            prefs.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); prefs.RowCount++;
            grid.Controls.Add(prefs, 1, 0); settingsCard.Controls.Add(grid); Ui.Row(root, settingsCard);
            // The status line only appears when there is something to report (a problem, mostly).
            note.Margin = new Padding(0, 12, 0, 0); note.Text = ""; note.Visible = false; note.TextChanged += delegate { note.Visible = note.Text.Length > 0; }; Ui.Row(root, note);
            root.Dock = DockStyle.None; sheetHost.Main = root; sheetHost.Controls.Add(root); Controls.Add(sheetHost);
            UpdateHintCaption(); ResumeLayout(true); Theme.Apply(this); Application.Idle += WarmUpEditor;
        }
        // A left-column row: label | stretching content | trailing button, a field's height tall.
        static void KeyRow(TableLayoutPanel table, Label label, Control content, Control trailing) {
            int at = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.FieldHeight + 12));
            label.Anchor = AnchorStyles.Left; label.Margin = new Padding(0, 0, 16, 12); label.TextAlign = ContentAlignment.MiddleLeft;
            content.Margin = new Padding(0, 0, 8, 12); trailing.Margin = new Padding(0, 0, 0, 12); trailing.Anchor = AnchorStyles.Right;
            table.Controls.Add(label, 0, at); table.Controls.Add(content, 1, at); table.Controls.Add(trailing, 2, at);
        }
        // tip: a short explanation shown when the pointer rests on the caption (optional).
        static Control SettingsPair(string caption, Control control, string tip = null) {
            var table = Ui.Table(2); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.FieldHeight)); table.RowCount = 1; table.Margin = new Padding(0, 0, 0, 12);
            var label = Ui.Label(caption); label.TextAlign = ContentAlignment.MiddleLeft; label.Margin = Padding.Empty; table.Controls.Add(label, 0, 0);
            if (tip != null) { label.Anchor = AnchorStyles.Left; InfoTip.Attach(label, tip); }
            control.Anchor = AnchorStyles.Right; control.Margin = Padding.Empty; table.Controls.Add(control, 1, 0); return table;
        }
        static Button AddButton(FlowLayoutPanel panel, string text, bool primary, EventHandler action) { var button = Ui.Button(text, primary); button.Click += action; panel.Controls.Add(button); return button; }
        // All shortcuts off for now (games, recording...): the main window button and the tray menu item stay in step.
        readonly PauseSwitch pauseSwitch = new PauseSwitch(); ToolStripMenuItem pauseItem;
        // The pause pill ends where the list's rows (their highlight plates) end, scrollbar lane included.
        void AlignPauseSwitch() {
            var card = list.Parent; if (card == null) return;
            int inset = card.Padding.Right + (list.Width - list.ClientSize.Width) + list.RowInset;
            if (pauseSwitch.Margin.Right != inset) pauseSwitch.Margin = new Padding(0, 0, inset, 0);
        }
        void SetPaused(bool on) {
            if (paused == on) return;
            paused = on; pauseSwitch.Paused = on; if (pauseItem != null) pauseItem.Checked = on;
            RegisterRules();
        }
        void ChangeStartup(bool enable) { try { SetStartup(enable); } catch (Exception ex) { note.Text = "启动设置失败：" + ex.Message; } refreshing = true; startup.Checked = IsStartupEnabled(); refreshing = false; }
        void ChangeTrayIcon(bool show) {
            var proposed = CopySettings(); proposed.ShowTrayIcon = show;
            if (SaveSettings(proposed)) tray.Visible = show;
            refreshing = true; trayToggle.Checked = settings.ShowTrayIcon; refreshing = false;
        }
        void ChangeDoubleTap(bool on) { var proposed = CopySettings(); proposed.DoubleTapMaximize = on; SaveSettings(proposed); refreshing = true; doubleTapToggle.Checked = settings.DoubleTapMaximize; refreshing = false; }
        void ChangeFollowScreen(bool on) { var proposed = CopySettings(); proposed.FollowPointerScreen = on; SaveSettings(proposed); refreshing = true; screenToggle.Checked = settings.FollowPointerScreen; refreshing = false; }
        void ChangeUndo(int ms) { var proposed = CopySettings(); proposed.UndoMs = ms; SaveSettings(proposed); ShowUndo(); }
        void ChangeTheme(ThemeMode mode) { var proposed = CopySettings(); proposed.Theme = mode; if (SaveSettings(proposed)) Theme.Set(mode); refreshing = true; themeChoice.SelectedIndex = (int)settings.Theme; refreshing = false; }
        void BuildTray() {
            tray.Icon = Icon; tray.Text = "窗口切换 · Window Switch"; tray.Visible = settings.ShowTrayIcon;
            var menu = new ContextMenuStrip(); menu.Items.Add("打开 Window Switch", null, delegate { ShowSettings(); }); menu.Items.Add("快捷键预览", null, delegate { hints.ShowPreview(OverlayDisplayReason.TrayPreview); });
            menu.Items.Add(new ToolStripSeparator()); var launch = new ToolStripMenuItem("随 Windows 登录启动") { CheckOnClick = false }; launch.Click += delegate { ChangeStartup(!IsStartupEnabled()); }; menu.Items.Add(launch);
            var themes = new ToolStripMenuItem("主题"); string[] names = { "跟随系统", "亮色", "暗色" }; for (int i = 0; i < names.Length; i++) { int mode = i; var item = new ToolStripMenuItem(names[i]); item.Click += delegate { ChangeTheme((ThemeMode)mode); }; themes.DropDownItems.Add(item); } menu.Items.Add(themes);
            pauseItem = new ToolStripMenuItem("暂停快捷键") { CheckOnClick = true }; pauseItem.CheckedChanged += delegate { SetPaused(pauseItem.Checked); }; menu.Items.Add(pauseItem);
            menu.Items.Add("重试快捷键", null, delegate { RegisterRules(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, delegate { Quit(); });
            menu.Opening += delegate { launch.Checked = IsStartupEnabled(); for (int i = 0; i < 3; i++) ((ToolStripMenuItem)themes.DropDownItems[i]).Checked = i == (int)settings.Theme; };
            Ui.StyleMenu(menu);
            menu.Closed += delegate { tray.ReturnFocus(); };
            tray.ContextMenuStrip = menu; tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
        }
        // Add/edit happens in a sheet that slides in from the right; the list stays visible and clicking a row
        // opens (or switches the sheet to) it. 保存 keeps the sheet open on the same app, so the next app is one click
        // away; switching away saves pending changes first. Hotkeys pause only while a shortcut field is recording.
        bool reopenAfterSave = true, sheetRecording;
        // Before the sheet is replaced: unsaved changes are saved; false (stay put) when that fails.
        bool LeaveSheet() {
            if (sheet == null || !sheet.HasChanges) return true;
            reopenAfterSave = false;
            try { return sheet.TrySave(); } finally { reopenAfterSave = true; }
        }
        void RecordingChanged(ShortcutRecorder recorder, bool on) { if (sheet != null && sheet.Contains(recorder)) PauseForRecording(on); }
        void PauseForRecording(bool on) {
            if (on == sheetRecording) return; sheetRecording = on;
            if (on) { editing = true; UnregisterRules(); hints.Suspend(true); } else { editing = false; hints.Suspend(false); RegisterRules(); }
        }
        void OpenRuleDialog(int index) {
            // The sheet stays tied to its app, wherever that app moves in the list while the sheet is open.
            var original = index < 0 ? null : settings.Applications[index];
            Func<int> at = () => original == null ? -1 : settings.Applications.IndexOf(original);
            var next = new RuleEditor(original);
            next.ValidateRule = delegate(AppRule rule) {
                int i = at(); var copy = new List<AppRule>(settings.Applications); if (i < 0) copy.Add(rule); else copy[i] = rule; var proposed = CopySettings(); proposed.Applications = copy;
                // Probing needs this tool's own registrations out of the way (otherwise an unchanged key reads as taken).
                bool live = !editing; if (live) UnregisterRules();
                try { HotkeyManager.Validate(proposed); return rule.Enabled ? rule.AllShortcuts().Select(key => hotkeys.Probe(KeyChord.Parse(key))).FirstOrDefault(message => message != null) : null; } catch (Exception ex) { return ex.Message; }
                finally { if (live) RegisterRules(); }
            };
            next.RemoveRequested += delegate {
                int i = at(); CloseEditor(true); if (i < 0) return; var copy = new List<AppRule>(settings.Applications); copy.RemoveAt(i); SaveRules(copy);
                switching = true; list.SelectedIndex = -1; switching = false;
            };
            OpenSheet(next, index, () => UpdateRules(next.Rule, at()), delegate { OpenRuleDialog(settings.Applications.IndexOf(next.Rule)); });
        }
        void SetHintOptions() {
            var next = new HintEditor(settings.Hint); next.PreviewRequested += (option, ms) => { hints.Sample(option, ms); note.Text = settings.Applications.Count == 0 ? "请先添加一个应用" : ""; };
            OpenSheet(next, -1, delegate { var proposed = CopySettings(); proposed.Hint = next.Options; if (!SaveSettings(proposed)) return false; UpdateHintCaption(); return true; }, SetHintOptions);
        }
        void OpenSheet(SheetPanel next, int index, Func<bool> commit, Action reopen) {
            bool replacing = sheet != null;
            if (sheetRecording) PauseForRecording(false);
            // Controls created after the window was DPI-scaled are scaled explicitly (margins, padding, fixed sizes).
            next.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
            next.Committing = commit;
            // Saved: reload the sheet from what was stored (same app, fresh state) and confirm on its button.
            next.Saved += delegate { if (!reopenAfterSave || sheet != next) return; reopen(); if (sheet != null) sheet.FlashSaved(); };
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
            if (sheetRecording) PauseForRecording(false);
            sheet = null; editorIndex = -1; editorRule = null; AcceptButton = null;
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
            // Esc: a recording shortcut field cancels the recording; an open sheet closes; otherwise the window goes away,
            // like pressing 打开窗口快捷键 again.
            if (keyData == Keys.Escape && !(ActiveControl is ShortcutRecorder)) {
                if (sheet != null) { if (!sheet.HandleEscape()) CloseEditor(true); } else Dismiss();
                return true;
            }
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
        // One of Window Switch's own shortcuts, edited as a draft: while the field is active, the button becomes 保存 and
        // nothing changes until 保存 is clicked. Clicking anywhere else, Esc, or leaving the window restores it.
        // Otherwise the button is 停用 / 启用: disabling keeps the key (shown greyed out), so 启用 brings it back.
        sealed class GlobalShortcutSetting {
            readonly MainForm form; readonly Func<Settings, GlobalShortcut> read; readonly Action<Settings, GlobalShortcut> write; readonly Func<string> state; readonly string idleHint;
            public readonly ShortcutRecorder Recorder = new ShortcutRecorder();
            public readonly Label Caption = Ui.Label("", true);
            // Laid out by the window as one settings row: title | shortcut field | 停用/保存, with Caption (problems) below.
            public readonly Label Title;
            readonly RoundButton action = (RoundButton)Ui.Button("停用");
            public Button ActionButton { get { return action; } }
            readonly ClickWatcher clicks; bool draft;
            public GlobalShortcutSetting(MainForm owner, string title, Func<Settings, GlobalShortcut> read, Action<Settings, GlobalShortcut> write, Func<string> state, string idleHint) {
                form = owner; this.read = read; this.write = write; this.state = state; this.idleHint = idleHint; Recorder.AccessibleName = title;
                Title = Ui.Label(title);
                Caption.Visible = false; Caption.TextChanged += delegate { Caption.Visible = Caption.Text.Length > 0; };
                Recorder.RecordingStarted += delegate { Begin(); };
                Recorder.Recorded += delegate { if (draft) Caption.Text = ""; };
                Recorder.Cancelled += delegate { End(); };
                // Focus moves recorder -> 保存 when the button is pressed, so only revert once neither has focus.
                EventHandler leave = delegate { if (form.IsHandleCreated) form.BeginInvoke(new Action(delegate { if (draft && !Recorder.Focused && !action.Focused) End(); })); };
                Recorder.Leave += leave; action.Leave += leave;
                // Clicks on labels, cards or empty space do not move keyboard focus, so Leave alone never fires for them.
                clicks = new ClickWatcher(delegate(IntPtr target) { if (draft && target != Recorder.Handle && target != action.Handle) form.BeginInvoke(new Action(End)); });
                form.Deactivate += delegate { if (draft) End(); };
                action.Click += delegate { if (draft) Save(); else if (Saved.Enabled) Disable(); else Enable(); };
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
                draft = false; action.Primary = false; action.Invalidate(); clicks.Stop();
                if (Recorder.Focused || action.Focused || form.ActiveControl == Recorder || form.ActiveControl == action) ParkFocus();
                form.editing = false; form.hints.Suspend(false); form.RegisterRules();
                Refresh(true);
            }
            // Focus left on the field keeps it recording, and a focused button that gets disabled hands focus to the next
            // control in tab order — the other shortcut field, which would then start recording. The app list is neutral.
            void ParkFocus() { form.ActiveControl = form.list; }
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
            // The kept key goes back on, after the same checks as saving a new one (conflicts, taken by another program).
            void Enable() {
                var saved = Saved; if (saved.Enabled || !saved.HasKey) return;
                try {
                    var proposed = form.CopySettings(); write(proposed, new GlobalShortcut { Enabled = true, Modifiers = saved.Modifiers, VirtualKey = saved.VirtualKey, Sides = saved.Sides });
                    HotkeyManager.Validate(proposed);
                    string error = form.hotkeys.Probe(new KeyChord { Modifiers = saved.Modifiers, Key = saved.VirtualKey, Sides = saved.Sides }); if (error != null) throw new ArgumentException(error);
                    if (!form.SaveSettings(proposed)) throw new ArgumentException(form.note.Text);
                    Refresh(true);
                } catch (Exception ex) { Caption.Text = ex.Message; }
            }
            // Shows the saved shortcut and its state; left alone while it is being edited.
            public void Refresh(bool force = false) {
                if (draft || (Recorder.Focused && !force)) return;
                var saved = Saved; Recorder.Text = saved.HasKey ? saved.Display : "未设置"; Recorder.Dim = !saved.Enabled;
                // 停用 for a working key, 启用 for a kept one; nothing to do for a key that was never set. While all
                // shortcuts are paused the button just says 已暂停 (恢复快捷键 at the top brings them back).
                bool pausedKey = form.paused && saved.HasKey, canAct = (saved.Enabled || saved.HasKey) && !pausedKey;
                if (!canAct && action.Focused) ParkFocus(); // a disabled button would hand focus to the next shortcut field
                action.Enabled = canAct; action.Text = pausedKey ? "已暂停" : saved.Enabled || !saved.HasKey ? "停用" : "启用"; action.Invalidate();
                string current = state();
                Caption.Text = !saved.Enabled || current == "已生效" || current == "已暂停" ? (idleHint ?? "") : current;
            }
        }
        void RefreshGlobalShortcuts() { if (openApp != null) openApp.Refresh(); if (minimizeSetting != null) minimizeSetting.Refresh(); if (moveScreenSetting != null) moveScreenSetting.Refresh(); }
        void UpdateHintCaption() {
            bool on = settings.Hint.Enabled; hintCaption.Text = on ? "长按" : "长按查看已关闭";
            hintCaption.Key = on ? Ui.KeyName(settings.Hint.Key) : ""; hintCaption.Suffix = on ? "· " + (settings.Hint.DelayMs / 1000.0).ToString("0.##") + " 秒" : "";
        }
        List<HintRow> HintRows() { return settings.Applications.Select(a => new HintRow { Name = a.Name, Shortcut = a.Shortcut, Shortcuts = a.AllShortcuts().ToList(), State = hotkeys.State(a), Rule = a }).ToList(); }
    }
}
