using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowSwitch {
    public static class AppIcons {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();
        public static event EventHandler Changed;
        // Icons are requested at the exact device-pixel size they are drawn at, so the shell picks the
        // best frame (e.g. the 64 px frame at 175%) instead of WinForms stretching a 32 px bitmap.
        public static Image Get(AppRule rule, int pixels) {
            pixels = Math.Max(16, Math.Min(256, pixels)); bool dark = Theme.Current.Dark;
            // Only packaged apps ship theme-specific logos, so only their cache entries depend on the theme.
            bool themed = (rule.Arguments ?? "").StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase);
            string baseKey = (rule.IconPath ?? rule.Executable ?? "") + "|" + rule.Arguments + "|" + rule.ProcessName + "|" + pixels;
            string key = baseKey + (themed ? (dark ? "|dark" : "|light") : ""), otherKey = baseKey + (dark ? "|light" : "|dark");
            Image stale = null;
            lock (gate) {
                Image known; if (cache.TryGetValue(key, out known)) return known ?? (themed && cache.TryGetValue(otherKey, out stale) ? stale : null);
                cache[key] = null; if (themed) cache.TryGetValue(otherKey, out stale);
            }
            string iconPath = rule.IconPath, executable = rule.Executable, arguments = rule.Arguments, processName = rule.ProcessName;
            // Shell image handlers expect an STA thread.
            var worker = new Thread(delegate() {
                Image found = null;
                try { found = Load(iconPath, executable, arguments, processName, pixels, dark); } catch { }
                lock (gate) cache[key] = found;
                if (Changed != null) Changed(null, EventArgs.Empty);
            });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
            return stale; // the other theme's logo until this one arrives, rather than a blank placeholder
        }
        static Image Load(string iconPath, string executable, string arguments, string processName, int pixels, bool dark) {
            string path = Environment.ExpandEnvironmentVariables(iconPath ?? executable ?? "");
            bool viaExplorer = Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
            const string apps = "shell:AppsFolder\\";
            // Packaged apps (launched via shell:AppsFolder) use their theme-aware package logo, whatever the icon path says.
            if ((arguments ?? "").StartsWith(apps, StringComparison.OrdinalIgnoreCase)) {
                Image logo = PackageLogo(arguments.Trim().Substring(apps.Length), pixels, dark); if (logo != null) return logo;
            }
            if ((!File.Exists(path) || viaExplorer) && !string.IsNullOrWhiteSpace(processName)) {
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName))) using (process) { try { path = process.MainModule.FileName; viaExplorer = false; break; } catch { } }
            }
            // A packaged app that is not running: use its Start menu logo (it may carry the tile plate).
            if (viaExplorer && (arguments ?? "").StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)) return ShellImage(arguments.Trim(), pixels);
            if (!File.Exists(path)) return null;
            Image image = ShellImage(path, pixels);
            if (image != null) return image;
            using (Icon icon = Icon.ExtractAssociatedIcon(path)) return icon == null ? null : icon.ToBitmap();
        }

        // Packaged apps ship theme-specific taskbar logos: "altform-unplated" is drawn for dark surfaces and
        // "altform-lightunplated" for light ones. Use the variant Windows itself shows on a taskbar of that theme.
        static Image PackageLogo(string aumid, int pixels, bool dark) {
            int bang = aumid.IndexOf('!'); if (bang <= 0) return null;
            string root; var app = Catalog.PackageApplication(aumid.Substring(0, bang), aumid.Substring(bang + 1), out root);
            var visual = app == null ? null : app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            string logo = visual == null ? null : (string)visual.Attribute("Square44x44Logo"); if (string.IsNullOrEmpty(logo)) return null;
            logo = logo.Replace('/', '\\');
            string folder = Path.Combine(root, Path.GetDirectoryName(logo)), stem = Path.GetFileNameWithoutExtension(logo), extension = Path.GetExtension(logo);
            if (!Directory.Exists(folder)) return null;
            foreach (string variant in dark ? new[] { "_altform-unplated", "_altform-lightunplated" } : new[] { "_altform-lightunplated", "_altform-unplated" }) {
                // Smallest size at least as large as needed, else the largest available.
                string best = null; int bestSize = 0;
                foreach (string file in Directory.GetFiles(folder, stem + ".targetsize-*" + variant + extension)) {
                    string name = Path.GetFileNameWithoutExtension(file); int start = name.IndexOf(".targetsize-", StringComparison.OrdinalIgnoreCase) + 12, end = name.IndexOf('_', start), size;
                    if (end <= start || !int.TryParse(name.Substring(start, end - start), out size)) continue;
                    if (best == null || (bestSize < pixels ? size > bestSize : size >= pixels && size < bestSize)) { best = file; bestSize = size; }
                }
                if (best != null) return Resample(best, pixels);
            }
            return null;
        }
        static Image Resample(string file, int pixels) {
            using (var source = new Bitmap(new MemoryStream(File.ReadAllBytes(file)))) {
                var result = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.SmoothingMode = SmoothingMode.HighQuality; g.DrawImage(source, new Rectangle(0, 0, pixels, pixels)); }
                return result;
            }
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap); }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint bf0, bf1, bf2; public IntPtr dshSection; public uint dsOffset; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr handle, int size, out DIBSECTION section);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);

        static Image ShellImage(string parsingName, int pixels) {
            IShellItemImageFactory factory = null; IntPtr hbitmap = IntPtr.Zero;
            try {
                SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
                const int ResizeToFit = 0x0, IconOnly = 0x4;
                if (factory.GetImage(new SIZE { cx = pixels, cy = pixels }, ResizeToFit | IconOnly, out hbitmap) != 0 || hbitmap == IntPtr.Zero) return null;
                DIBSECTION section; if (GetObject(hbitmap, Marshal.SizeOf(typeof(DIBSECTION)), out section) == 0) return null;
                BITMAP info = section.dsBm; if (info.bmBits == IntPtr.Zero || info.bmBitsPixel != 32) return null;
                // Copy the 32-bit DIB by hand so the alpha channel survives (Image.FromHbitmap drops it).
                // The pixels are straight (not premultiplied) ARGB, and a positive biHeight means bottom-up rows.
                bool bottomUp = section.dsBmih.biHeight > 0;
                var result = new Bitmap(info.bmWidth, info.bmHeight, PixelFormat.Format32bppArgb);
                var data = result.LockBits(new Rectangle(0, 0, info.bmWidth, info.bmHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try {
                    var row = new byte[info.bmWidthBytes];
                    for (int y = 0; y < info.bmHeight; y++) {
                        Marshal.Copy(IntPtr.Add(info.bmBits, (bottomUp ? info.bmHeight - 1 - y : y) * info.bmWidthBytes), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), Math.Min(row.Length, data.Stride));
                    }
                } finally { result.UnlockBits(data); }
                return result;
            } catch { return null; }
            finally { if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap); if (factory != null) Marshal.ReleaseComObject(factory); }
        }
    }
}
