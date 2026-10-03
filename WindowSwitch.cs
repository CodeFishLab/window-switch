using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.Versioning;
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace WindowSwitch {
    public class AppRule {
        public string Name { get; set; }
        public string Shortcut { get; set; }
        public string ProcessName { get; set; }
        public string Executable { get; set; }
        public string Arguments { get; set; }
        public string TitleContains { get; set; }
        public bool Enabled { get; set; }
        public string TargetId { get; set; }
        public TargetType TargetType { get; set; }
        public string ProfileDirectory { get; set; }
        public string UserDataDirectory { get; set; }
        public string WindowAppId { get; set; }
        public long WindowHandle { get; set; }
        public int WindowProcessId { get; set; }
        public string WindowProcessStart { get; set; }
        public string IconPath { get; set; }
        // Extra shortcuts for the same app. Shortcut stays the first one, so older configs and tools keep working.
        public List<string> MoreShortcuts { get; set; }
        // Left out of the hold-to-preview overlay (the shortcut keeps working and stays in the main list).
        public bool HideInPreview { get; set; }
        public AppRule() { Enabled = true; Arguments = ""; TitleContains = ""; TargetId = Guid.NewGuid().ToString("N"); MoreShortcuts = new List<string>(); }
        public IEnumerable<string> AllShortcuts() {
            if (!string.IsNullOrWhiteSpace(Shortcut)) yield return Shortcut;
            if (MoreShortcuts != null) foreach (string shortcut in MoreShortcuts) if (!string.IsNullOrWhiteSpace(shortcut)) yield return shortcut;
        }
    }
    public class Settings {
        public int Version { get; set; }
        public List<AppRule> Applications { get; set; }
        public HintOptions Hint { get; set; }
        public ThemeMode Theme { get; set; }
        public GlobalShortcut Minimize { get; set; }
        // Shows (or, when already in front, hides) the Window Switch window. Off until the user sets it.
        public GlobalShortcut OpenApp { get; set; }
        // Without a tray icon the window is reopened with the open-window shortcut, the preview's gear, or by running the exe again.
        public bool ShowTrayIcon { get; set; }
        public Settings() { Version = 2; Applications = new List<AppRule>(); Hint = new HintOptions(); Minimize = new GlobalShortcut(); OpenApp = new GlobalShortcut { Enabled = false }; ShowTrayIcon = true; }
    }
    public class KeyChord {
        public uint Modifiers, Key;
        // Specific modifier keys ("LAlt", "RCtrl", ...). Zero means either side, which RegisterHotKey can handle;
        // side-specific chords are matched by SidedHotkeys instead.
        public uint Sides;
        public const uint LeftCtrl = 1, RightCtrl = 2, LeftAlt = 4, RightAlt = 8, LeftShift = 16, RightShift = 32;
        public bool Sided { get { return Sides != 0; } }
        public string Id { get { return Modifiers + ":" + Key + ":" + Sides; } }
        public static KeyChord Parse(string value) {
            uint mods = 0, sides = 0; Keys key = Keys.None;
            foreach (string raw in (value ?? "").Split('+')) {
                string part = raw.Trim();
                if (part.Equals("LCtrl", StringComparison.OrdinalIgnoreCase)) { mods |= 2; sides |= LeftCtrl; }
                else if (part.Equals("RCtrl", StringComparison.OrdinalIgnoreCase)) { mods |= 2; sides |= RightCtrl; }
                else if (part.Equals("LAlt", StringComparison.OrdinalIgnoreCase)) { mods |= 1; sides |= LeftAlt; }
                else if (part.Equals("RAlt", StringComparison.OrdinalIgnoreCase)) { mods |= 1; sides |= RightAlt; }
                else if (part.Equals("LShift", StringComparison.OrdinalIgnoreCase)) { mods |= 4; sides |= LeftShift; }
                else if (part.Equals("RShift", StringComparison.OrdinalIgnoreCase)) { mods |= 4; sides |= RightShift; }
                else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= 2;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 1;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 4;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= 8;
                else {
                    if (key != Keys.None) throw new ArgumentException("快捷键只能包含一个普通按键。");
                    if (part.Length == 1 && char.IsDigit(part[0])) part = "D" + part;
                    if (part.Equals("Space", StringComparison.OrdinalIgnoreCase)) part = "Space";
                    Keys parsed;
                    if (!Enum.TryParse<Keys>(part, true, out parsed)) throw new ArgumentException("无法识别快捷键：" + value);
                    key = parsed & Keys.KeyCode;
                }
            }
            if (mods == 0 || key == Keys.None || key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.LWin || key == Keys.RWin || key == Keys.F12)
                throw new ArgumentException("请使用 Ctrl、Alt、Shift 或 Win 搭配普通按键；F12 为系统保留键。");
            return new KeyChord { Modifiers = mods, Key = (uint)key, Sides = sides };
        }
        // Which side of each modifier is held right now (used while recording a shortcut).
        public static uint PressedSides() {
            Func<int, bool> down = vk => (Native.GetAsyncKeyState(vk) & 0x8000) != 0; uint sides = 0;
            if (down(0xA2)) sides |= LeftCtrl; if (down(0xA3)) sides |= RightCtrl; if (down(0xA4)) sides |= LeftAlt;
            if (down(0xA5)) sides |= RightAlt; if (down(0xA0)) sides |= LeftShift; if (down(0xA1)) sides |= RightShift;
            return sides;
        }
        static string Side(uint sides, uint left, uint right, string name) { bool l = (sides & left) != 0, r = (sides & right) != 0; return l == r ? name : (l ? "L" : "R") + name; }
        public static string Format(Keys keys, uint sides = 0) {
            List<string> parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add(Side(sides, LeftCtrl, RightCtrl, "Ctrl"));
            if ((keys & Keys.Alt) != 0) parts.Add(Side(sides, LeftAlt, RightAlt, "Alt"));
            if ((keys & Keys.Shift) != 0) parts.Add(Side(sides, LeftShift, RightShift, "Shift"));
            Keys code = keys & Keys.KeyCode;
            string name = code.ToString();
            if (code >= Keys.D0 && code <= Keys.D9) name = ((int)code - (int)Keys.D0).ToString();
            parts.Add(name); return string.Join("+", parts.ToArray());
        }
        // Display-only names for keys whose enum names are cryptic ("Oemtilde" -> "`"). Stored strings are unchanged.
        static readonly Dictionary<string, string> symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Oemtilde", "`" }, { "Oem3", "`" }, { "OemMinus", "-" }, { "Oemplus", "=" }, { "OemOpenBrackets", "[" }, { "Oem4", "[" }, { "OemCloseBrackets", "]" }, { "Oem6", "]" },
            { "OemPipe", "\\" }, { "Oem5", "\\" }, { "OemSemicolon", ";" }, { "Oem1", ";" }, { "OemQuotes", "'" }, { "Oem7", "'" }, { "Oemcomma", "," }, { "OemPeriod", "." }, { "OemQuestion", "/" }, { "Oem2", "/" },
            { "Next", "PageDown" }, { "Prior", "PageUp" }, { "Back", "Backspace" }, { "Return", "Enter" }, { "Capital", "Caps Lock" },
            { "LCtrl", "Left Ctrl" }, { "RCtrl", "Right Ctrl" }, { "LAlt", "Left Alt" }, { "RAlt", "Right Alt" }, { "LShift", "Left Shift" }, { "RShift", "Right Shift" }
        };
        public static string Pretty(string value) {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return string.Join("+", value.Split('+').Select(part => { string symbol; return symbols.TryGetValue(part.Trim(), out symbol) ? symbol : part; }).ToArray());
        }
        public static string Display(uint modifiers, uint key, uint sides = 0) {
            var parts = new List<string>(); if ((modifiers & 2) != 0) parts.Add(Side(sides, LeftCtrl, RightCtrl, "Ctrl")); if ((modifiers & 1) != 0) parts.Add(Side(sides, LeftAlt, RightAlt, "Alt")); if ((modifiers & 4) != 0) parts.Add(Side(sides, LeftShift, RightShift, "Shift")); if ((modifiers & 8) != 0) parts.Add("Win");
            parts.Add(key >= 48 && key <= 57 ? ((char)key).ToString() : ((Keys)key).ToString()); return string.Join("+", parts.ToArray());
        }
    }
    public static class Native {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] public static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint to, bool on);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string subApp, string idList);
        // Batch several layout changes into one repaint (WM_SETREDRAW off, then a single full redraw).
        public static void Batch(Control control, Action change) {
            SendMessage(control.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); control.SuspendLayout();
            try { change(); }
            finally { control.ResumeLayout(true); SendMessage(control.Handle, 0x000B, new IntPtr(1), IntPtr.Zero); RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400); }
        }
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder path);
        public static uint ProcessId(IntPtr hwnd) { uint id; GetWindowThreadProcessId(hwnd, out id); return id; }
        public static string Title(IntPtr hwnd) { var value = new StringBuilder(512); GetWindowText(hwnd, value, value.Capacity); return value.ToString(); }
    }
    public sealed class SwitchEngine : IDisposable {
        readonly Dictionary<string, Timer> pending = new Dictionary<string, Timer>();
        readonly Dictionary<string, DateTime> lastPress = new Dictionary<string, DateTime>();
        public Action<string, string> Report;
        public readonly WindowTargetResolver Resolver = new WindowTargetResolver();
        public IntPtr Find(AppRule rule) {
            return Resolver.Find(rule);
        }
        void Log(AppRule rule, string action) { if (Report != null) Report(rule.Name, action); }
        void BringForward(AppRule rule, IntPtr hwnd) {
            if (Native.IsIconic(hwnd)) Native.ShowWindowAsync(hwnd, 9);
            else Native.ShowWindowAsync(hwnd, 5);
            IntPtr target = hwnd;
            if (!Native.IsWindowEnabled(hwnd)) {
                IntPtr popup = Native.GetLastActivePopup(hwnd);
                if (popup != IntPtr.Zero && Native.IsWindowVisible(popup)) target = popup;
            }
            bool activated = Native.SetForegroundWindow(target) && Native.GetForegroundWindow() == target;
            if (!activated) activated = ForceForeground(target);
            if (activated) { recentId = rule.TargetId ?? rule.Name; recentWindow = hwnd; recentAt = Environment.TickCount; }
            Log(rule, activated ? "已调至前台" : "已恢复窗口；Windows 暂未允许前置，请再按一次快捷键");
        }
        // When Windows refuses a plain SetForegroundWindow (foreground lock), sharing the current foreground thread's
        // input state for a moment lets the request through.
        internal static bool ForceForeground(IntPtr hwnd) {
            uint pid, self = Native.GetCurrentThreadId(), owner = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            bool attached = owner != 0 && owner != self && Native.AttachThreadInput(self, owner, true);
            try { Native.BringWindowToTop(hwnd); Native.SetForegroundWindow(hwnd); } finally { if (attached) Native.AttachThreadInput(self, owner, false); }
            return Native.GetForegroundWindow() == hwnd;
        }
        // The window this engine activated last. Right after activation the system may not report it as foreground
        // yet; a quick second press must still minimize it rather than "activate" it again.
        string recentId; IntPtr recentWindow; int recentAt;
        bool RecentlyActivated(string id, IntPtr hwnd) { return recentId == id && recentWindow == hwnd && unchecked(Environment.TickCount - recentAt) < 500; }
        public void Toggle(AppRule rule) {
            DateTime previous;
            string id = rule.TargetId ?? rule.Name;
            // Only guards against one press being delivered twice; quick deliberate presses must all count.
            if (lastPress.TryGetValue(id, out previous) && (DateTime.UtcNow - previous).TotalMilliseconds < 40) return;
            lastPress[id] = DateTime.UtcNow;
            try {
                IntPtr hwnd = Find(rule);
                if (hwnd != IntPtr.Zero) {
                    IntPtr foreground = Native.GetForegroundWindow();
                    if (!Native.IsIconic(hwnd) && (WindowIdentity.SameForeground(hwnd, foreground) || RecentlyActivated(id, hwnd))) {
                        Native.ShowWindowAsync(hwnd, 6); recentId = null; Log(rule, "已最小化");
                    } else BringForward(rule, hwnd);
                    return;
                }
                if (rule.TargetType == TargetType.SpecificWindow) { Log(rule, "关联窗口已关闭，请编辑此应用重新选择窗口"); return; }
                if (rule.TargetType == TargetType.Profile && string.IsNullOrWhiteSpace(rule.WindowAppId)) { Log(rule, "尚未可靠关联浏览器配置文件，请编辑此应用选择配置文件或窗口"); return; }
                if (pending.ContainsKey(id)) { Log(rule, "正在启动，请稍候"); return; }
                string executable = Environment.ExpandEnvironmentVariables(rule.Executable);
                Process started = Process.Start(new ProcessStartInfo(executable, Resolver.LaunchArguments(rule)) {
                    UseShellExecute = true,
                    WorkingDirectory = Path.IsPathRooted(executable) && File.Exists(executable) ? Path.GetDirectoryName(executable) : Environment.CurrentDirectory
                });
                if (started != null) { Native.AllowSetForegroundWindow((uint)started.Id); started.Dispose(); }
                Log(rule, "正在启动");
                Timer timer = new Timer { Interval = 250 }; DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                pending.Add(id, timer);
                timer.Tick += delegate {
                    IntPtr found = Find(rule);
                    if (found != IntPtr.Zero || DateTime.UtcNow > deadline) {
                        timer.Stop(); pending.Remove(id); timer.Dispose();
                        if (found != IntPtr.Zero) BringForward(rule, found);
                        else Log(rule, "已发送启动请求，但未找到窗口；请检查程序和进程名称");
                    }
                };
                timer.Start();
            } catch (Exception ex) { Log(rule, "失败：" + ex.Message); }
        }
        public void Dispose() { foreach (Timer timer in pending.Values) timer.Dispose(); pending.Clear(); }
        public void MinimizeForeground(IntPtr excluded) {
            IntPtr target = Native.GetForegroundWindow();
            if (target == IntPtr.Zero || target == excluded || !WindowIdentity.Eligible(target)) return;
            Native.ShowWindowAsync(target, 6); if (Report != null) Report("当前窗口", "已最小化");
        }
    }
    public sealed class Endpoint : NativeWindow, IDisposable {
        public Action<int> Hotkey, Toggle;
        public Action Open, Quit, Preview, Diagnose, SystemTheme;
        public Endpoint(string title) { CreateHandle(new CreateParams { Caption = title }); }
        protected override void WndProc(ref Message msg) {
            if ((msg.Msg == 0x312 || msg.Msg == SidedHotkeys.Message) && Hotkey != null) Hotkey(msg.WParam.ToInt32());
            else if (msg.Msg == 0x8001 && Toggle != null) Toggle(msg.WParam.ToInt32());
            else if (msg.Msg == 0x8002 && Open != null) Open();
            else if (msg.Msg == 0x8003 && Quit != null) Quit();
            else if (msg.Msg == 0x8004 && Preview != null) Preview();
            else if (msg.Msg == 0x8005 && Diagnose != null) Diagnose();
            else if (msg.Msg == 0x8006 && SystemTheme != null) SystemTheme();
            base.WndProc(ref msg);
        }
        public void Dispose() { DestroyHandle(); }
    }
    public sealed partial class MainForm : UiForm {
        readonly AppList list = new AppList();
        readonly Label note = Ui.Label("", true), summary = Ui.Label("", true);
        readonly Toggle startup = new Toggle();
        readonly TrayIcon tray;
        readonly Endpoint endpoint;
        readonly SwitchEngine engine = new SwitchEngine();
        readonly HintMonitor hints;
        readonly HotkeyManager hotkeys;
        readonly string configPath, statusPath;
        Settings settings;
        readonly Dictionary<string, string> lastActions = new Dictionary<string, string>();
        readonly Timer retry = new Timer { Interval = 3000 };
        bool exiting, refreshing, paused;
        const string StartupName = "WindowSwitch";
        public MainForm(string path, Settings initial, string endpointTitle) {
            configPath = path; statusPath = Path.Combine(Path.GetDirectoryName(path), "status.json"); settings = initial;
            tray = new TrayIcon(TrayIcon.ForConfiguration(configPath));
            if (settings.Hint == null) settings.Hint = new HintOptions();
            if (settings.Minimize == null) settings.Minimize = new GlobalShortcut();
            if (settings.OpenApp == null) settings.OpenApp = new GlobalShortcut { Enabled = false };
            Theme.Set(settings.Theme); BuildUi();
            hints = new HintMonitor(() => settings.Hint, delegate { return HintRows().Where(r => !r.Rule.HideInPreview).ToList(); });
            hints.SettingsRequested += delegate { ShowSettings(); };
            endpoint = new Endpoint(endpointTitle); hotkeys = new HotkeyManager(endpoint.Handle);
            endpoint.Hotkey = delegate(int id) {
                hints.Interrupt();
                if (id == HotkeyManager.OpenAppId) { if (Visible && WindowState != FormWindowState.Minimized && WindowIdentity.SameForeground(Handle, Native.GetForegroundWindow())) HideToTray(); else ShowSettings(); return; }
                if (id == HotkeyManager.MinimizeId) { if (Visible && WindowIdentity.SameForeground(Handle, Native.GetForegroundWindow())) HideToTray(); else engine.MinimizeForeground(hints.Overlay == null ? IntPtr.Zero : hints.Overlay.Handle); return; }
                AppRule rule = hotkeys.Rule(id); if (rule != null) engine.Toggle(rule);
            };
            endpoint.Toggle = delegate(int index) { if (index >= 0 && index < settings.Applications.Count) { hints.Interrupt(); engine.Toggle(settings.Applications[index]); } };
            endpoint.Open = ShowSettings; endpoint.Quit = Quit; endpoint.Preview = delegate { hints.ShowPreview(); }; endpoint.Diagnose = WriteStatus;
            endpoint.SystemTheme = Theme.RefreshSystem;
            engine.Report = delegate(string name, string action) {
                // Successful switches are not narrated; only problems reach the status line.
                bool problem = action.StartsWith("失败") || action.Contains("未找到") || action.Contains("暂未允许") || action.Contains("已关闭") || action.Contains("尚未");
                lastActions[name] = action; note.Text = problem ? name + " · " + action : ""; WriteStatus();
                if (action.StartsWith("失败") || action.Contains("未找到窗口")) { tray.BalloonTipTitle = "窗口切换"; tray.BalloonTipText = name + "：" + action; tray.ShowBalloonTip(4000); }
            };
            BuildTray();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); } };
            // With no tray icon, minimizing stays an ordinary taskbar minimize so the window cannot get lost.
            Resize += delegate { if (WindowState == FormWindowState.Minimized && !exiting && settings.ShowTrayIcon) HideToTray(); };
            RegisterRules();
            retry.Tick += delegate { if (!paused && !editing && hotkeys.Retry(settings, paused)) { RefreshRows(); WriteStatus(); note.Text = ""; } }; retry.Start();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        }
        void SystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) { if (!IsDisposed && endpoint != null) Native.PostMessage(endpoint.Handle, 0x8006, IntPtr.Zero, IntPtr.Zero); }
        void UpdateRules(AppRule rule, int index) { var copy = new List<AppRule>(settings.Applications); if (index < 0) copy.Add(rule); else copy[index] = rule; SaveRules(copy); }
        Settings CopySettings() { return new Settings { Applications = settings.Applications, Hint = settings.Hint, Theme = settings.Theme, Minimize = settings.Minimize, OpenApp = settings.OpenApp, ShowTrayIcon = settings.ShowTrayIcon }; }
        void SaveRules(List<AppRule> rules) { var proposed = CopySettings(); proposed.Applications = rules; SaveSettings(proposed); }
        bool SaveSettings(Settings proposed) {
            try {
                HotkeyManager.Validate(proposed); proposed.Version = 2;
                string temp = configPath + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(proposed), new UTF8Encoding(false));
                if (File.Exists(configPath)) File.Replace(temp, configPath, configPath + ".bak"); else File.Move(temp, configPath);
                settings = proposed; note.Text = ""; if (!editing) RegisterRules(); return true;
            } catch (Exception ex) { note.Text = "保存失败：" + ex.Message; return false; }
        }
        public static void Validate(List<AppRule> rules) {
            if (rules == null || rules.Count > 100) throw new ArgumentException("配置中的应用列表无效，最多支持 100 个应用。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var keys = new Dictionary<string, string>();
            foreach (AppRule rule in rules) {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.ProcessName) || string.IsNullOrWhiteSpace(rule.Executable)) throw new ArgumentException("应用名称、进程名称和启动程序不能为空。");
                if (!names.Add(rule.Name)) throw new ArgumentException("显示名称重复：" + rule.Name);
                if (!Enum.IsDefined(typeof(TargetType), rule.TargetType)) throw new ArgumentException("窗口目标类型无效。");
                if (rule.TargetType == TargetType.Profile && (string.IsNullOrWhiteSpace(rule.ProfileDirectory) || string.IsNullOrWhiteSpace(rule.WindowAppId))) throw new ArgumentException("请选择明确的浏览器配置文件或关联窗口。");
                if (!rule.AllShortcuts().Any()) throw new ArgumentException("请为 " + rule.Name + " 设置至少一个快捷键。");
                if (rule.AllShortcuts().Count() > HotkeyManager.MaxShortcuts) throw new ArgumentException("每个应用最多 " + HotkeyManager.MaxShortcuts + " 个快捷键。");
                foreach (string shortcut in rule.AllShortcuts()) {
                    string id = KeyChord.Parse(shortcut).Id;
                    if (rule.Enabled && keys.ContainsKey(id)) throw new ArgumentException(keys[id] == rule.Name ? "同一个快捷键填写了两次：" + KeyChord.Pretty(shortcut) : "此快捷键已用于 " + keys[id]);
                    if (rule.Enabled) keys[id] = rule.Name;
                }
            }
        }
        void UnregisterRules() { hotkeys.Clear(); }
        void RegisterRules() {
            if (editing) return; // An open editor or recorder re-registers when it closes.
            hotkeys.Apply(settings, paused); RefreshRows(); RefreshGlobalShortcuts(); WriteStatus();
            if (paused) note.Text = "快捷键已暂停";
            else if (hotkeys.AppCount < settings.Applications.Count(a => a.Enabled) || (settings.Minimize.Enabled && hotkeys.MinimizeState != "已生效") || (settings.OpenApp.Enabled && hotkeys.OpenAppState != "已生效")) note.Text = "部分快捷键被其他程序占用";
        }
        void RefreshRows() { list.SetRows(HintRows()); summary.Text = hotkeys.AppCount + " 个快捷键已生效"; }
        // Diagnostics only. Resolving each app's window walks every top-level window (hundreds of ms), so the UI
        // thread just takes a snapshot; a background worker resolves windows and writes the newest snapshot.
        readonly object statusGate = new object(); StatusSnapshot pendingStatus; bool statusWorker;
        sealed class StatusSnapshot { public List<AppRule> Rules; public List<Dictionary<string, object>> Apps; public object Root; }
        void WriteStatus() {
            try {
                var rules = settings.Applications.Select(a => new Dictionary<string, object> { { "name", a.Name }, { "shortcut", string.Join(" / ", a.AllShortcuts()) }, { "state", hotkeys.State(a) }, { "targetType", a.TargetType.ToString() }, { "profile", a.ProfileDirectory }, { "appId", a.WindowAppId }, { "window", 0L }, { "lastAction", lastActions.ContainsKey(a.Name) ? lastActions[a.Name] : "" } }).ToList();
                var snapshot = new StatusSnapshot { Rules = settings.Applications.ToList(), Apps = rules };
                snapshot.Root = new { pid = Process.GetCurrentProcess().Id, updatedUtc = DateTime.UtcNow.ToString("o"), applications = rules, theme = settings.Theme.ToString(), tray = new { registered = tray.Registered, location = tray.LocationState, identity = tray.Identity.ToString(), error = tray.LastError }, minimize = new { enabled = settings.Minimize.Enabled, shortcut = settings.Minimize.Display, state = hotkeys.MinimizeState }, openApp = new { enabled = settings.OpenApp.Enabled, shortcut = settings.OpenApp.Display, state = hotkeys.OpenAppState }, hint = new { enabled = settings.Hint.Enabled, key = settings.Hint.Key, delayMs = settings.Hint.DelayMs, visible = hints.Visible, reason = hints.Reason.ToString(), held = hints.LastHeld, otherKeyHeld = hints.LastOther, suspended = hints.Suspended, tickAgeMs = unchecked(Environment.TickCount - hints.LastTick) } };
                lock (statusGate) { pendingStatus = snapshot; if (statusWorker) return; statusWorker = true; }
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { StatusLoop(); });
            } catch { /* Diagnostics cannot interrupt shortcut handling. */ }
        }
        void StatusLoop() {
            while (true) {
                StatusSnapshot next;
                lock (statusGate) { next = pendingStatus; pendingStatus = null; if (next == null) { statusWorker = false; return; } }
                try {
                    // A private resolver: the engine's own resolver remembers matches and belongs to the UI thread.
                    var resolver = new WindowTargetResolver();
                    for (int i = 0; i < next.Rules.Count; i++) next.Apps[i]["window"] = resolver.Find(next.Rules[i]).ToInt64();
                    File.WriteAllText(statusPath, new JavaScriptSerializer().Serialize(next.Root), new UTF8Encoding(false));
                } catch { /* Diagnostics cannot interrupt shortcut handling. */ }
            }
        }
        public void ShowSettings() { refreshing = true; startup.Checked = IsStartupEnabled(); refreshing = false; Show(); WindowState = FormWindowState.Normal; FitToScreen(); Activate(); if (Native.GetForegroundWindow() != Handle) SwitchEngine.ForceForeground(Handle); }
        // Hidden windows are not on the taskbar anyway; toggling ShowInTaskbar would recreate the window (and flash).
        void HideToTray() { CloseEditor(false); Hide(); WindowState = FormWindowState.Normal; }
        public void Quit() { exiting = true; Close(); Application.ExitThread(); }
        protected override void Dispose(bool disposing) {
            if (disposing) { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemThemeChanged; retry.Dispose(); hints.Dispose(); hotkeys.Dispose(); engine.Dispose(); tray.Visible = false; tray.Dispose(); endpoint.Dispose(); }
            base.Dispose(disposing);
        }
        static string StartupPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupName + ".lnk"); } }
        static bool IsStartupEnabled() { return File.Exists(StartupPath); }
        public static void SetStartup(bool enable) {
            if (!enable) { if (File.Exists(StartupPath)) File.Delete(StartupPath); return; }
            object shell = null, shortcut = null;
            try {
                Type type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { StartupPath });
                Type st = shortcut.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
                st.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "--background" });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "窗口切换快捷键" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            } finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        internal static Icon MakeIcon() {
            // Use the bundled icon directly, independent of Explorer's association/icon cache.
            try { using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("WindowSwitch.AppIcon")) {
                if (stream != null) using (var embedded = new Icon(stream, 32, 32)) return (Icon)embedded.Clone();
            } } catch { }
            using (var image = new Bitmap(32, 32)) {
                using (Graphics g = Graphics.FromImage(image)) { g.Clear(Color.FromArgb(30, 41, 59)); using (var pen = new Pen(Color.White, 2)) { g.DrawRectangle(pen, 6, 7, 16, 12); g.DrawRectangle(pen, 11, 13, 15, 12); } }
                IntPtr handle = image.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
            }
        }
    }
    static class Program {
        static string Arg(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        [STAThread] static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string config = Path.GetFullPath(Arg(args, "--config") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"));
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(config.ToLowerInvariant()))).Replace("-", "").Substring(0, 20);
            string title = "WindowSwitch.Endpoint." + hash;
            try {
                // First run (or a fresh copy): start from an empty configuration instead of failing.
                if (!File.Exists(config)) File.WriteAllText(config, new JavaScriptSerializer().Serialize(new Settings { Minimize = new GlobalShortcut { Enabled = false } }), new UTF8Encoding(false));
                Settings settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(config, Encoding.UTF8));
                if (settings == null || settings.Version < 1 || settings.Version > 2) throw new ArgumentException("配置版本不受支持。");
                if (settings.Minimize == null) settings.Minimize = new GlobalShortcut();
                if (settings.OpenApp == null) settings.OpenApp = new GlobalShortcut { Enabled = false };
                // Upgrade legacy configurations without changing existing app bindings.
                if (settings.Version == 1 && settings.Applications.Any(a => a.Enabled && KeyChord.Parse(a.Shortcut).Modifiers == 3 && KeyChord.Parse(a.Shortcut).Key == 77)) settings.Minimize.Enabled = false;
                HotkeyManager.Validate(settings);
                if (settings.Hint == null) settings.Hint = new HintOptions();
                if (settings.Hint.Key < 8 || settings.Hint.Key > 254 || settings.Hint.DelayMs < 250 || settings.Hint.DelayMs > 3000) throw new ArgumentException("查看按键或长按时间的配置无效。");
                string toggle = Arg(args, "--toggle"); bool stop = args.Contains("--stop");
                IntPtr existing = Native.FindWindow(null, title);
                if (existing != IntPtr.Zero) {
                    Native.AllowSetForegroundWindow(Native.ProcessId(existing));
                    if (stop) Native.PostMessage(existing, 0x8003, IntPtr.Zero, IntPtr.Zero);
                    else if (args.Contains("--preview")) Native.PostMessage(existing, 0x8004, IntPtr.Zero, IntPtr.Zero);
                    else if (args.Contains("--diagnostics")) Native.PostMessage(existing, 0x8005, IntPtr.Zero, IntPtr.Zero);
                    else if (toggle != null) {
                        int index = settings.Applications.FindIndex(a => a.Name.Equals(toggle, StringComparison.OrdinalIgnoreCase));
                        if (index < 0) throw new ArgumentException("未找到应用：" + toggle);
                        Native.PostMessage(existing, 0x8001, new IntPtr(index), IntPtr.Zero);
                    } else if (!args.Contains("--background")) Native.PostMessage(existing, 0x8002, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                if (stop) return 0;
                bool owner;
                using (var mutex = new System.Threading.Mutex(true, "Local\\WindowSwitch." + hash, out owner)) {
                    if (!owner) return 0;
                    using (var form = new MainForm(config, settings, title)) {
                        if (args.Contains("--enable-startup")) MainForm.SetStartup(true);
                        if (!args.Contains("--background")) form.Show();
                        if (args.Contains("--preview")) Native.PostMessage(Native.FindWindow(null, title), 0x8004, IntPtr.Zero, IntPtr.Zero);
                        if (toggle != null) { int index = settings.Applications.FindIndex(a => a.Name.Equals(toggle, StringComparison.OrdinalIgnoreCase)); if (index < 0) throw new ArgumentException("未找到应用：" + toggle); Native.PostMessage(Native.FindWindow(null, title), 0x8001, new IntPtr(index), IntPtr.Zero); }
                        Application.Run();
                    }
                    mutex.ReleaseMutex();
                }
                return 0;
            } catch (Exception ex) {
                try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(config), "error.log"), DateTime.UtcNow.ToString("o") + " " + ex.ToString()); } catch { }
                MessageBox.Show("窗口切换启动失败：\n" + ex.Message + "\n\n原配置已保留。", "窗口切换", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
    }
}
