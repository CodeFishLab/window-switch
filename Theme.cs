using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowSwitch {
    public enum ThemeMode { System, Light, Dark }
    public sealed class ThemePalette {
        public Color AppBackground, Surface, ElevatedSurface, PrimaryText, SecondaryText, DisabledText, Border, Divider;
        public Color PrimaryButtonBackground, PrimaryButtonForeground, SecondaryButtonBackground, Hover, Pressed;
        public Color SuccessBackground, SuccessForeground, ShortcutBadgeBackground, ShortcutBadgeForeground, OverlayBackground, OverlayBorder;
        public Color WarningBackground, WarningForeground, Selection;
        // Scheme A additions: muted text is a real color (never opacity), focus is graphite (never Windows blue),
        // and green is reserved for the dark-mode status dot.
        public Color MutedText, Focus, PrimaryButtonHover, ToggleOn, ToggleOff, ToggleKnob, ToggleKnobOn, PopoverBackground, StatusDot, ConflictDot, DangerForeground, DangerBackground;
        public bool Dark;
    }
    public static class Theme {
        public const int Space4 = 4, Space8 = 8, Space12 = 12, Space16 = 16, Space20 = 20, Space24 = 24, Space32 = 32;
        public const int SmallRadius = 8, Radius = 12, LargeRadius = 16, Shadow = 8;
        public static ThemePalette Current { get; private set; }
        public static ThemeMode Mode { get; private set; }
        // Changing fires while the old palette is still current (windows snapshot their old look); Changed after.
        public static event EventHandler Changing, Changed;
        static Theme() { Set(ThemeMode.System); }
        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        static bool SystemDark() {
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0; } catch { return false; }
        }
        public static void RefreshSystem() { if (Mode == ThemeMode.System) Set(Mode); }
        public static void Set(ThemeMode mode) {
            if (Changing != null) Changing(null, EventArgs.Empty);
            Mode = mode; bool dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemDark());
            // Scheme A · Warm Ivory / Warm Charcoal + Graphite.
            // Every neutral keeps R >= G >= B: a green channel above red reads as an olive cast on dark surfaces.
            Current = dark ? new ThemePalette {
                // Near-black, like Claude's dark surfaces; cards and controls step up in small neutral increments.
                Dark = true, AppBackground = C(0x141413), Surface = C(0x1D1D1C), ElevatedSurface = C(0x252524), PrimaryText = C(0xF2F0EC), SecondaryText = C(0xA8A6A1), MutedText = C(0x7C7A76), DisabledText = C(0x5E5D5A), Border = C(0x30302E), Divider = C(0x282827),
                PrimaryButtonBackground = C(0xF2F0EC), PrimaryButtonForeground = C(0x141413), PrimaryButtonHover = C(0xDCDAD5), SecondaryButtonBackground = C(0x252524), Hover = C(0x2C2C2A), Pressed = C(0x343432), Selection = C(0x2E2E2C),
                SuccessBackground = C(0x252524), SuccessForeground = C(0xD6D4CF), StatusDot = C(0x6DBE7B), ConflictDot = C(0xE5867A), ShortcutBadgeBackground = C(0x252524), ShortcutBadgeForeground = C(0xD6D4CF), OverlayBackground = C(0x1D1D1C), OverlayBorder = C(0x2A2A28), WarningBackground = C(0x3A3229), WarningForeground = C(0xE2C595),
                Focus = C(0x9C9A95), ToggleOn = C(0xEAE8E3), ToggleOff = C(0x3A3A38), ToggleKnob = C(0xF2F0EC), ToggleKnobOn = C(0x141413), PopoverBackground = C(0x232322), DangerForeground = C(0xEC8E80), DangerBackground = C(0x3B2421)
            } : new ThemePalette {
                AppBackground = C(0xF6F3ED), Surface = C(0xFCFBF8), ElevatedSurface = C(0xFFFFFF), PrimaryText = C(0x252422), SecondaryText = C(0x78766F), MutedText = C(0x9C9A95), DisabledText = C(0xB6B4AF), Border = C(0xE4E1DA), Divider = C(0xE9E6E0),
                PrimaryButtonBackground = C(0x272624), PrimaryButtonForeground = C(0xFAF9F6), PrimaryButtonHover = C(0x3D3B38), SecondaryButtonBackground = C(0xEFEEE9), Hover = C(0xE9E7E1), Pressed = C(0xE1DFD8), Selection = C(0xE6E4DE),
                SuccessBackground = C(0xEFEEE9), SuccessForeground = C(0x78766F), StatusDot = C(0x46A660), ConflictDot = C(0xDE7B6E), ShortcutBadgeBackground = C(0xEFEEE9), ShortcutBadgeForeground = C(0x4C4A46), OverlayBackground = C(0xFBFAF7), OverlayBorder = C(0xE7E4DE), WarningBackground = C(0xF4EADA), WarningForeground = C(0x95703C),
                Focus = C(0x6F6D69), ToggleOn = C(0x272624), ToggleOff = C(0xDCD9D2), ToggleKnob = C(0xFFFFFF), ToggleKnobOn = C(0xFFFFFF), PopoverBackground = C(0xFFFFFF), DangerForeground = C(0xB4473A), DangerBackground = C(0xF6E4E0)
            };
            if (Changed != null) Changed(null, EventArgs.Empty);
        }
        public static GraphicsPath Round(RectangleF rect, float radius) {
            var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0) { path.AddRectangle(rect); return path; }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static void Fill(Graphics g, Rectangle bounds, Color color, int radius) { using (var path = Round(bounds, radius)) using (var brush = new SolidBrush(color)) g.FillPath(brush, path); }
        public static void Apply(Control root) {
            var p = Current; root.ForeColor = p.PrimaryText;
            if (root is Form) { root.BackColor = p.AppBackground; if (root.IsHandleCreated) NativeStyle.TitleBar(root.Handle, p.Dark); }
            else if (root is Label) { root.ForeColor = root.Tag as string == "muted" ? p.SecondaryText : root.Tag as string == "faint" ? p.MutedText : root.Tag as string == "success" ? p.SuccessForeground : root.Tag as string == "error" ? p.WarningForeground : p.PrimaryText; }
            else if (root is TextBox || root is ComboBox || root is NumericUpDown || root is ListView) { root.BackColor = p.Surface; root.ForeColor = p.PrimaryText; }
            else if (root is Card) root.BackColor = p.Surface;
            else if (!(root is RoundButton) && !(root is AppList) && !(root is Toggle)) root.BackColor = root.Parent == null ? p.AppBackground : root.Parent.BackColor;
            foreach (Control child in root.Controls) Apply(child); root.Invalidate();
        }
    }
    public static class NativeStyle {
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int left, right, top, bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }
        public static void TitleBar(IntPtr hwnd, bool dark) {
            try {
                int value = dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref value, 4);
                // Windows 11: tint the caption with the window background so the frame reads as one surface.
                // Older builds ignore these attributes and keep the standard caption.
                int caption = ColorRef(Theme.Current.AppBackground), text = ColorRef(Theme.Current.PrimaryText);
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4); DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            } catch (DllNotFoundException) { }
        }
        // Windows 11 draws antialiased corners, border and shadow itself; Win10 needs a clipping region instead.
        public static bool SystemCorners { get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000; } }
        // Rounded Windows 11 corners and a themed hairline for popups; no-op on older systems.
        public static void Popup(IntPtr hwnd) { Rounded(hwnd, Theme.Current.Border); }
        public static void Rounded(IntPtr hwnd, Color borderColor) {
            try { int corner = 2; DwmSetWindowAttribute(hwnd, 33, ref corner, 4); int border = ColorRef(borderColor); DwmSetWindowAttribute(hwnd, 34, ref border, 4); } catch (DllNotFoundException) { }
        }
        public static bool Backdrop(IntPtr hwnd) {
            try { int corner = 2; DwmSetWindowAttribute(hwnd, 33, ref corner, 4); int type = 3; int result = DwmSetWindowAttribute(hwnd, 38, ref type, 4); if (result == 0) { var margins = new Margins { left = -1 }; DwmExtendFrameIntoClientArea(hwnd, ref margins); return true; } } catch (DllNotFoundException) { }
            return false;
        }
        // Native scrollbars (e.g. an AutoScroll panel) follow the app theme instead of staying light.
        public static void ScrollBars(IntPtr hwnd, bool dark) { try { Native.SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }
        // A cloaked window is laid out and painted normally but kept off screen by DWM.
        public static void Cloak(IntPtr hwnd, bool cloaked) { try { int value = cloaked ? 1 : 0; DwmSetWindowAttribute(hwnd, 13, ref value, 4); } catch (DllNotFoundException) { } }
        public static uint Dpi(IntPtr hwnd) { try { uint value = GetDpiForWindow(hwnd); return value == 0 ? 96 : value; } catch (EntryPointNotFoundException) { return 96; } }
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
