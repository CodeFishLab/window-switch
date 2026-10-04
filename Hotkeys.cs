using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace WindowSwitch {
    public class GlobalShortcut {
        public bool Enabled { get; set; }
        public uint Modifiers { get; set; }
        public uint VirtualKey { get; set; }
        public uint Sides { get; set; }
        public GlobalShortcut() { Enabled = true; Modifiers = 3; VirtualKey = 77; }
        public string Display { get { return KeyChord.Display(Modifiers, VirtualKey, Sides); } }
    }
    public sealed class HotkeyManager : IDisposable {
        public const int MinimizeId = 20, OpenAppId = 21, MaxShortcuts = 5;
        // Hotkey id for the n-th shortcut of the app at index.
        static int Id(int index, int shortcut) { return 100 + index * 10 + shortcut; }
        readonly IntPtr handle;
        readonly Dictionary<int, AppRule> registered = new Dictionary<int, AppRule>();
        readonly Dictionary<AppRule, string> states = new Dictionary<AppRule, string>();
        public string MinimizeState { get; private set; }
        public string OpenAppState { get; private set; }
        public int AppCount { get { return registered.Count; } }
        readonly SidedHotkeys sided;
        public bool Navigating { set { sided.Navigating = value; } }
        public void ArmUndo(IntPtr hwnd, int ms) { sided.ArmUndo(hwnd, ms); }
        public HotkeyManager(IntPtr hwnd) { handle = hwnd; sided = new SidedHotkeys(hwnd); }
        public AppRule Rule(int id) { AppRule rule; return registered.TryGetValue(id, out rule) ? rule : null; }
        public string State(AppRule rule) { string value; return states.TryGetValue(rule, out value) ? value : "未注册"; }
        public void Clear() { foreach (int id in registered.Keys) Native.UnregisterHotKey(handle, id); registered.Clear(); Native.UnregisterHotKey(handle, MinimizeId); Native.UnregisterHotKey(handle, OpenAppId); sided.Set(new SidedHotkeys.Binding[0]); sided.NumberHoldKey = 0; }
        public void Apply(Settings settings, bool paused) {
            Clear(); states.Clear(); var hooked = new List<SidedHotkeys.Binding>();
            for (int i = 0; i < settings.Applications.Count; i++) {
                var rule = settings.Applications[i]; if (paused || !rule.Enabled) { states[rule] = paused ? "已暂停" : "已停用"; continue; }
                string failure = null; int n = 0;
                foreach (string shortcut in rule.AllShortcuts().Take(MaxShortcuts)) {
                    KeyChord key = KeyChord.Parse(shortcut); int id = Id(i, n++);
                    // Left/right-specific chords are matched by the keyboard hook; they cannot be taken by other apps.
                    if (key.Sided) { if (sided.Installed) { hooked.Add(new SidedHotkeys.Binding { Id = id, Chord = key }); registered[id] = rule; } else if (failure == null) failure = "无法监听左右键"; continue; }
                    if (Native.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.Key)) registered[id] = rule;
                    else if (failure == null) failure = "被占用（" + Marshal.GetLastWin32Error() + "）";
                }
                states[rule] = failure ?? "已生效";
            }
            var hint = settings.Hint; sided.NumberHoldKey = !paused && hint != null && hint.Enabled && hint.NumberKeys ? hint.Key : 0;
            MinimizeState = RegisterGlobal(settings.Minimize, MinimizeId, hooked, paused);
            OpenAppState = RegisterGlobal(settings.OpenApp, OpenAppId, hooked, paused);
            sided.Set(hooked);
        }
        // Window Switch's own shortcuts (最小化当前窗口, 打开窗口快捷键).
        string RegisterGlobal(GlobalShortcut shortcut, int id, List<SidedHotkeys.Binding> hooked, bool paused) {
            if (paused) return "已暂停";
            if (shortcut == null || !shortcut.Enabled) return "已停用";
            if (shortcut.Sides != 0) { hooked.Add(new SidedHotkeys.Binding { Id = id, Chord = new KeyChord { Modifiers = shortcut.Modifiers, Key = shortcut.VirtualKey, Sides = shortcut.Sides } }); return sided.Installed ? "已生效" : "无法监听左右键"; }
            return Native.RegisterHotKey(handle, id, shortcut.Modifiers | 0x4000, shortcut.VirtualKey) ? "已生效" : "被占用";
        }
        public bool Retry(Settings settings, bool paused) {
            if (paused) return false; bool changed = false;
            for (int i = 0; i < settings.Applications.Count; i++) {
                var rule = settings.Applications[i]; if (!rule.Enabled) continue;
                bool complete = true, gained = false; int n = 0;
                foreach (string shortcut in rule.AllShortcuts().Take(MaxShortcuts)) {
                    int id = Id(i, n++); if (registered.ContainsKey(id)) continue;
                    KeyChord key = KeyChord.Parse(shortcut);
                    if (!key.Sided && Native.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.Key)) { registered[id] = rule; gained = true; } else complete = false;
                }
                if (gained) { changed = true; if (complete) states[rule] = "已生效"; }
            }
            if (settings.Minimize.Enabled && MinimizeState != "已生效" && settings.Minimize.Sides == 0 && Native.RegisterHotKey(handle, MinimizeId, settings.Minimize.Modifiers | 0x4000, settings.Minimize.VirtualKey)) { MinimizeState = "已生效"; changed = true; }
            var open = settings.OpenApp;
            if (open != null && open.Enabled && OpenAppState != "已生效" && open.Sides == 0 && Native.RegisterHotKey(handle, OpenAppId, open.Modifiers | 0x4000, open.VirtualKey)) { OpenAppState = "已生效"; changed = true; }
            return changed;
        }
        // Called while editing has suspended this tool's bindings. A failed proposal leaves the saved binding intact.
        public string Probe(KeyChord chord) {
            if (chord.Sided) return sided.Installed ? null : "无法监听左右键，请改用不区分左右的组合。";
            if (!Native.RegisterHotKey(handle, 9999, chord.Modifiers | 0x4000, chord.Key)) return "该快捷键无法注册，可能已被其他程序占用，请换一个组合。";
            Native.UnregisterHotKey(handle, 9999); return null;
        }
        public static void Validate(Settings settings) {
            MainForm.Validate(settings.Applications);
            if (settings.Minimize == null) settings.Minimize = new GlobalShortcut();
            if (settings.OpenApp == null) settings.OpenApp = new GlobalShortcut { Enabled = false };
            if (!Enum.IsDefined(typeof(ThemeMode), settings.Theme)) throw new ArgumentException("主题设置无效。");
            string minimizeId = CheckGlobal(settings, settings.Minimize, "最小化当前窗口"), openId = CheckGlobal(settings, settings.OpenApp, "打开窗口快捷键");
            if (minimizeId != null && minimizeId == openId) throw new ArgumentException("“打开窗口快捷键”和“最小化当前窗口”不能使用同一个快捷键。");
        }
        // Validates one of Window Switch's own shortcuts; returns its identity, or null when disabled.
        static string CheckGlobal(Settings settings, GlobalShortcut shortcut, string what) {
            if (!shortcut.Enabled) return null;
            if (shortcut.Modifiers > 15 || shortcut.VirtualKey > 254) throw new ArgumentException(what + "的快捷键配置无效。");
            KeyChord.Parse(shortcut.Display);
            string id = new KeyChord { Modifiers = shortcut.Modifiers, Key = shortcut.VirtualKey, Sides = shortcut.Sides }.Id;
            var conflict = settings.Applications.FirstOrDefault(a => a.Enabled && a.AllShortcuts().Any(key => KeyChord.Parse(key).Id == id));
            if (conflict != null) throw new ArgumentException("此快捷键已用于 " + conflict.Name + "，与" + what + "冲突。");
            return id;
        }
        public void Dispose() { Clear(); sided.Dispose(); }
    }
}
