using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowSwitch {
    // Shortcuts that name a side (e.g. Right Alt+G). RegisterHotKey cannot tell left from right modifiers, so these
    // are matched by a low-level keyboard hook. The hook runs on its own thread: Windows silently removes a hook whose
    // thread is slow to respond, and the UI thread can be busy. Matches are posted to the endpoint window.
    public sealed class SidedHotkeys : IDisposable {
        public const int Message = 0x8007;
        // While the hold preview is up, ↑ ↓ Enter Esc steer it instead of reaching the active app (posted with the key).
        public const int NavigateMessage = 0x800A;
        public volatile bool Navigating;
        // The hold key (e.g. Left Alt) when numpad opening is on, else 0: while it is down, numpad 1–9 are posted as
        // VK_NUMPAD1–9 (NavigateMessage) and kept from the active app (no Alt-code characters).
        public volatile int NumberHoldKey;
        public const ushort MaskVirtualKey = MaskKey;
        // Esc shortly after a switch (while that window is still in front) is posted as UndoMessage with the window
        // instead of reaching it. ms < 0 means no time limit. Armed by each switch; disarmed when used, when it expires,
        // by a minimize, or as soon as another window comes to the front (leaving and coming back does not re-arm).
        public const int UndoMessage = 0x800B;
        volatile IntPtr undoWindow; volatile int undoAt, undoMs;
        public void ArmUndo(IntPtr hwnd, int ms) { undoAt = Environment.TickCount; undoMs = ms; undoWindow = ms != 0 ? hwnd : IntPtr.Zero; }
        delegate void WinEventProc(IntPtr hook, uint e, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        readonly WinEventProc foregroundProc;
        void ForegroundChanged(IntPtr hook, uint e, IntPtr hwnd, int obj, int child, uint thread, uint time) {
            var armed = undoWindow; if (armed != IntPtr.Zero && hwnd != IntPtr.Zero && !WindowIdentity.SameForeground(armed, hwnd)) undoWindow = IntPtr.Zero;
        }
        public struct Binding { public int Id; public KeyChord Chord; }
        delegate IntPtr HookProc(int code, IntPtr wparam, IntPtr lparam);
        [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public int type; public KEYBDINPUT ki; public long padding; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        // Our own injected "mask" keystrokes carry this marker so the hook ignores them.
        static readonly IntPtr Marker = new IntPtr(0x5753484B);
        const ushort MaskKey = 0xE8; // unassigned virtual key
        readonly IntPtr target;
        readonly HookProc proc;
        // Keys whose key-down we consumed, so their key-up is consumed too (hook thread only).
        readonly HashSet<uint> swallowed = new HashSet<uint>();
        volatile Binding[] bindings = new Binding[0];
        uint threadId; IntPtr hook;
        public bool Installed { get { return hook != IntPtr.Zero; } }

        public SidedHotkeys(IntPtr endpoint) {
            target = endpoint; proc = Callback; foregroundProc = ForegroundChanged;
            var ready = new ManualResetEvent(false);
            var thread = new Thread(delegate() {
                threadId = GetCurrentThreadId();
                hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
                // EVENT_SYSTEM_FOREGROUND, delivered to this thread's message loop (WINEVENT_OUTOFCONTEXT).
                IntPtr foreground = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, foregroundProc, 0, 0, 0);
                ready.Set();
                MSG msg; while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
                if (foreground != IntPtr.Zero) UnhookWinEvent(foreground);
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero;
            });
            thread.IsBackground = true; thread.Name = "WindowSwitch.KeyHook"; thread.Start();
            ready.WaitOne(2000);
        }
        public void Set(IEnumerable<Binding> list) { bindings = list.ToArray(); }

        IntPtr Callback(int code, IntPtr wparam, IntPtr lparam) {
            if (code >= 0) {
                var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lparam, typeof(KBDLLHOOKSTRUCT));
                int message = wparam.ToInt32(); bool down = message == 0x100 || message == 0x104, up = message == 0x101 || message == 0x105;
                if (info.dwExtraInfo != Marker) {
                    if (up && swallowed.Remove(info.vkCode)) return new IntPtr(1);
                    // Inside a low-level hook the async key state is still the state before this event: an
                    // auto-repeat arrives with the key already down, a fresh press with it up. (A consumed key-down
                    // never reaches that state.) This holds even when other keyboard tools re-send or reorder events,
                    // where pairing our key-down with "its" key-up does not.
                    bool repeat = down && Down((int)info.vkCode);
                    int digit = NumberHoldKey != 0 ? NumpadDigit(info.vkCode, info.flags) : 0;
                    if (down && digit > 0 && Down(NumberHoldKey)) {
                        if (!repeat) { Mask(); Native.PostMessage(target, NavigateMessage, new IntPtr(0x60 + digit), IntPtr.Zero); }
                        swallowed.Add(info.vkCode); return new IntPtr(1);
                    }
                    if (down && Navigating && IsNavigation(info.vkCode)) {
                        if (!repeat) Mask(); swallowed.Add(info.vkCode);
                        Native.PostMessage(target, NavigateMessage, new IntPtr((int)info.vkCode), IntPtr.Zero);
                        return new IntPtr(1);
                    }
                    var armed = undoWindow;
                    if (down && !repeat && info.vkCode == 0x1B && armed != IntPtr.Zero) {
                        undoWindow = IntPtr.Zero;
                        if ((undoMs < 0 || unchecked(Environment.TickCount - undoAt) <= undoMs) && WindowIdentity.SameForeground(armed, Native.GetForegroundWindow())) {
                            swallowed.Add(0x1B); Native.PostMessage(target, UndoMessage, armed, IntPtr.Zero); return new IntPtr(1);
                        }
                    }
                    if (repeat && swallowed.Contains(info.vkCode)) return new IntPtr(1);
                    if (down && !repeat) swallowed.Remove(info.vkCode);
                    var current = bindings;
                    if (down && !repeat && current.Length > 0 && !IsModifier(info.vkCode)) {
                        foreach (var binding in current) {
                            if (binding.Chord.Key != info.vkCode || !Matches(binding.Chord)) continue;
                            swallowed.Add(info.vkCode); Mask();
                            Native.PostMessage(target, Message, new IntPtr(binding.Id), IntPtr.Zero);
                            return new IntPtr(1);
                        }
                    }
                }
            }
            return CallNextHookEx(hook, code, wparam, lparam);
        }
        // Numpad 1–9 as a digit: VK_NUMPAD1–9 with NumLock on; with it off they arrive as End/↓/PgDn/←/Clear/→/Home/↑/PgUp
        // without the "extended" flag (the dedicated navigation keys carry it).
        public static int NumpadDigit(uint vk, uint flags) {
            if (vk >= 0x61 && vk <= 0x69) return (int)vk - 0x60;
            if ((flags & 1) != 0) return 0;
            switch (vk) { case 0x23: return 1; case 0x28: return 2; case 0x22: return 3; case 0x25: return 4; case 0x0C: return 5; case 0x27: return 6; case 0x24: return 7; case 0x26: return 8; case 0x21: return 9; }
            return 0;
        }
        public static bool IsNavigation(uint vk) { return vk == 0x26 || vk == 0x28 || vk == 0x0D || vk == 0x1B; }
        static bool IsModifier(uint vk) { return (vk >= 0xA0 && vk <= 0xA5) || vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C; }
        static bool Down(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }
        // Exact match: required modifiers (on the named side, or either side when unnamed) are down; others are up.
        public static bool Matches(KeyChord chord) {
            return Family(chord, 2, KeyChord.LeftCtrl, KeyChord.RightCtrl, 0xA2, 0xA3)
                && Family(chord, 1, KeyChord.LeftAlt, KeyChord.RightAlt, 0xA4, 0xA5)
                && Family(chord, 4, KeyChord.LeftShift, KeyChord.RightShift, 0xA0, 0xA1)
                && ((chord.Modifiers & 8) != 0) == (Down(0x5B) || Down(0x5C));
        }
        static bool Family(KeyChord chord, uint modifier, uint left, uint right, int leftKey, int rightKey) {
            bool l = Down(leftKey), r = Down(rightKey);
            if ((chord.Modifiers & modifier) == 0) return !l && !r;
            if ((chord.Sides & left) != 0) return l && !r;
            if ((chord.Sides & right) != 0) return r && !l;
            return l || r;
        }
        // A harmless keystroke after a consumed chord: releasing Alt/Win alone would otherwise open the active app's
        // menu or the Start menu, and it gives this process the "last input" it needs to bring a window forward.
        static void Mask() {
            var inputs = new[] {
                new INPUT { type = 1, ki = new KEYBDINPUT { wVk = MaskKey, dwExtraInfo = Marker } },
                new INPUT { type = 1, ki = new KEYBDINPUT { wVk = MaskKey, dwFlags = 2, dwExtraInfo = Marker } } };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
        public void Dispose() { bindings = new Binding[0]; if (threadId != 0) PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); }
    }
}
