using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace WindowSwitch {
    // Use the Shell's HWND/ID identity for compatibility with the taskbar's
    // application visibility settings, and check actual Shell registration.
    public sealed class TrayIcon : IDisposable {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct IconData {
            public uint cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)] struct IconIdentifier { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Shell_NotifyIcon(uint action, ref IconData data);
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
        const uint Callback = 0x8120;
        readonly Host host;
        readonly Timer recovery = new Timer { Interval = 3000 };
        readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Icon icon; string text = ""; bool visible, disposed, version4;
        public readonly Guid Identity;
        public bool Registered { get; private set; }
        public int LastError { get; private set; }
        public event MouseEventHandler MouseClick;
        public ContextMenuStrip ContextMenuStrip { get; set; }
        public string BalloonTipTitle { get; set; }
        public string BalloonTipText { get; set; }
        public Icon Icon { get { return icon; } set { icon = value; if (visible) EnsureRegistered(); } }
        public string Text { get { return text; } set { text = value ?? ""; if (visible) EnsureRegistered(); } }
        public bool Visible { get { return visible; } set { if (disposed || value == visible) return; visible = value; if (value) EnsureRegistered(); else Remove(); } }
        public IntPtr Handle { get { return host.Handle; } }
        public string LocationState { get { Rectangle rect; int hr = GetRectangle(out rect); return hr == 0 ? "Visible" : hr == 1 ? "Overflow" : "Missing"; } }
        public static Guid ForConfiguration(string path) {
            using (var hash = SHA256.Create()) { byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes("WindowSwitch.Tray\0" + Application.ExecutablePath.ToLowerInvariant() + "\0" + Path.GetFullPath(path).ToLowerInvariant())); byte[] id = new byte[16]; Array.Copy(bytes, id, id.Length); return new Guid(id); }
        }
        public TrayIcon(Guid identity) { Identity = identity; host = new Host(this); recovery.Tick += delegate { if (visible) { Rectangle rect; if (!Registered || GetRectangle(out rect) < 0) EnsureRegistered(); } }; recovery.Start(); }
        IconData Data() { return new IconData { cbSize = (uint)Marshal.SizeOf(typeof(IconData)), hWnd = host.Handle, uID = 1, uFlags = 1 | 2 | 4 | 0x80, uCallbackMessage = Callback, hIcon = icon == null ? IntPtr.Zero : icon.Handle, szTip = text.Length > 127 ? text.Substring(0, 127) : text, szInfo = "", szInfoTitle = "" }; }
        public int GetRectangle(out Rectangle rectangle) {
            var id = new IconIdentifier { cbSize = (uint)Marshal.SizeOf(typeof(IconIdentifier)), hWnd = host.Handle, uID = 1 }; Rect result;
            int hr = Shell_NotifyIconGetRect(ref id, out result); rectangle = new Rectangle(result.left, result.top, Math.Max(0, result.right - result.left), Math.Max(0, result.bottom - result.top)); return hr;
        }
        public void EnsureRegistered() {
            if (disposed || !visible || icon == null) return;
            // GetRect can still return the previous process's cached location.
            // A new host must ADD first; if either operation fails, try the other.
            var data = Data(); uint action = Registered ? 1u : 0u;
            bool success = Shell_NotifyIcon(action, ref data);
            if (!success) success = Shell_NotifyIcon(action == 0 ? 1u : 0u, ref data);
            LastError = success ? 0 : Marshal.GetLastWin32Error(); Registered = success;
            if (success) { data.uVersion = 4; version4 = Shell_NotifyIcon(4, ref data); }
        }
        void Remove() { var data = Data(); Shell_NotifyIcon(2, ref data); Registered = version4 = false; }
        public void ReturnFocus() { if (disposed || !Registered) return; var data = Data(); Shell_NotifyIcon(3, ref data); }
        public void ShowBalloonTip(int milliseconds) { if (!visible) return; EnsureRegistered(); if (!Registered) return; var data = Data(); data.uFlags = 0x10; data.szInfo = (BalloonTipText ?? ""); if (data.szInfo.Length > 255) data.szInfo = data.szInfo.Substring(0, 255); data.szInfoTitle = BalloonTipTitle ?? ""; if (data.szInfoTitle.Length > 63) data.szInfoTitle = data.szInfoTitle.Substring(0, 63); data.uVersion = (uint)Math.Max(1000, milliseconds); data.dwInfoFlags = 1; Shell_NotifyIcon(1, ref data); }
        void Dispatch(ref Message message) {
            if ((uint)message.Msg == taskbarCreated) { Registered = false; version4 = false; if (visible) EnsureRegistered(); }
            if ((uint)message.Msg != Callback || disposed) return;
            int action = version4 ? (int)(message.LParam.ToInt64() & 0xFFFF) : message.LParam.ToInt32();
            if (action == (version4 ? 0x400 : 0x202) || (version4 && action == 0x401)) {
                if (MouseClick != null) MouseClick(this, new MouseEventArgs(MouseButtons.Left, 1, Cursor.Position.X, Cursor.Position.Y, 0));
            } else if (action == (version4 ? 0x7B : 0x205)) {
                if (ContextMenuStrip != null) { Native.SetForegroundWindow(host.Handle); ContextMenuStrip.Show(Cursor.Position); Native.PostMessage(host.Handle, 0, IntPtr.Zero, IntPtr.Zero); }
            }
        }
        public void Dispose() { if (disposed) return; recovery.Stop(); recovery.Dispose(); visible = false; Remove(); disposed = true; host.Dispose(); if (ContextMenuStrip != null) ContextMenuStrip.Dispose(); }
        sealed class Host : NativeWindow, IDisposable {
            readonly TrayIcon owner;
            public Host(TrayIcon owner) { this.owner = owner; CreateHandle(new CreateParams { Caption = "WindowSwitch.Tray." + owner.Identity.ToString("N"), ExStyle = 0x80 }); }
            protected override void WndProc(ref Message msg) { owner.Dispatch(ref msg); base.WndProc(ref msg); }
            public void Dispose() { DestroyHandle(); }
        }
    }
}
