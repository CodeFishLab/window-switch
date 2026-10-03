using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WindowSwitch {
    public enum TargetType { Application, Profile, SpecificWindow }
    public sealed class WindowTarget {
        public IntPtr Handle;
        public uint ProcessId;
        public string ProcessName, Executable, Title, AppId, ProcessStart;
    }
    public sealed class BrowserProfile {
        public string Name, Directory, UserData, Executable, ProcessName, AppId;
        public override string ToString() { return Name; }
        public AppRule ToRule() {
            return new AppRule { Name = (ProcessName == "msedge" ? "Edge · " : "Chrome · ") + Name, ProcessName = ProcessName, Executable = Executable, IconPath = Executable, TargetType = TargetType.Profile, ProfileDirectory = Directory, UserDataDirectory = UserData, WindowAppId = AppId, Arguments = "--profile-directory=" + Quote(Directory) + " --user-data-dir=" + Quote(UserData) };
        }
        public static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
    }
    public static class WindowIdentity {
        [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public uint pid; public PropertyKey(uint id) { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); pid = id; } }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr value; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        // Per-press lookups must be cheap (a hotkey should feel instant). Process identity (name, executable, start
        // time) is cached per PID and validated by creation time; a window's AppUserModelID is cached per window.
        sealed class ProcessInfo { public string Name, Executable, Start; public long Created; }
        static readonly Dictionary<uint, ProcessInfo> processes = new Dictionary<uint, ProcessInfo>();
        static readonly Dictionary<long, string> appIds = new Dictionary<long, string>();
        static readonly object gate = new object();
        static ProcessInfo Identify(uint pid) {
            IntPtr handle = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle == IntPtr.Zero) return IdentifySlow(pid);
            try {
                long created, exit, kernel, user; if (!GetProcessTimes(handle, out created, out exit, out kernel, out user)) return IdentifySlow(pid);
                lock (gate) { ProcessInfo known; if (processes.TryGetValue(pid, out known) && known.Created == created) return known; }
                var name = new StringBuilder(1024); uint size = (uint)name.Capacity;
                string exe = QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString() : "";
                var info = new ProcessInfo { Executable = exe, Name = Path.GetFileNameWithoutExtension(exe), Start = DateTime.FromFileTimeUtc(created).Ticks.ToString(), Created = created };
                if (info.Name.Length == 0) return IdentifySlow(pid);
                lock (gate) { if (processes.Count > 1024) processes.Clear(); processes[pid] = info; }
                return info;
            } finally { Native.CloseHandle(handle); }
        }
        // Processes we may not query (e.g. elevated): name only, as before.
        static ProcessInfo IdentifySlow(uint pid) {
            try { using (var p = Process.GetProcessById((int)pid)) return new ProcessInfo { Name = p.ProcessName, Executable = "", Start = "" }; } catch { return null; }
        }
        static string CachedAppId(IntPtr hwnd, uint pid) {
            long key = ((long)pid << 32) ^ hwnd.ToInt64(); string id;
            lock (gate) if (appIds.TryGetValue(key, out id)) return id;
            id = AppId(hwnd);
            // An app may set its AppUserModelID shortly after creating the window: only remember found values.
            if (id.Length > 0) lock (gate) { if (appIds.Count > 4096) appIds.Clear(); appIds[key] = id; }
            return id;
        }
        public static string AppId(IntPtr hwnd) {
            IPropertyStore store = null; var value = new PropVariant();
            try { Guid iid = typeof(IPropertyStore).GUID; if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return ""; var key = new PropertyKey(5); if (store.GetValue(ref key, out value) != 0 || value.vt != 31 || value.value == IntPtr.Zero) return ""; return Marshal.PtrToStringUni(value.value) ?? ""; }
            catch (COMException) { return ""; }
            finally { PropVariantClear(ref value); if (store != null) Marshal.ReleaseComObject(store); }
        }
        public static string ClassName(IntPtr hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
        public static bool SameForeground(IntPtr target, IntPtr foreground) { return target != IntPtr.Zero && (target == foreground || GetAncestor(foreground, 3) == target); }
        public static bool Eligible(IntPtr hwnd) {
            if (!Native.IsWindow(hwnd) || (!Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)) || Native.GetWindow(hwnd, 4) != IntPtr.Zero || (Native.GetWindowLong(hwnd, -20) & 0x80) != 0) return false;
            string cls = ClassName(hwnd); return cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd" && cls != "Progman" && cls != "WorkerW" && Native.Title(hwnd).Length > 0;
        }
        public static List<WindowTarget> Windows(string processName = null) {
            var result = new List<WindowTarget>(); var seen = new Dictionary<uint, ProcessInfo>();
            string wanted = processName == null ? null : Path.GetFileNameWithoutExtension(processName);
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr ignored) {
                if (!Eligible(hwnd)) return true; uint pid = Native.ProcessId(hwnd); ProcessInfo info;
                if (!seen.TryGetValue(pid, out info)) { info = Identify(pid); seen[pid] = info; }
                if (info == null || (wanted != null && !info.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))) return true;
                result.Add(new WindowTarget { Handle = hwnd, ProcessId = pid, ProcessName = info.Name, Executable = info.Executable, ProcessStart = info.Start, Title = Native.Title(hwnd), AppId = CachedAppId(hwnd, pid) }); return true;
            }, IntPtr.Zero); return result;
        }
    }
    public static class BrowserProfiles {
        public static bool IsBrowser(string process) { return string.Equals(process, "chrome", StringComparison.OrdinalIgnoreCase) || string.Equals(process, "msedge", StringComparison.OrdinalIgnoreCase); }
        static string DefaultRoot(string process) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), process == "msedge" ? @"Microsoft\Edge\User Data" : @"Google\Chrome\User Data"); }
        public static string Suffix(string userData, string profile, string process) {
            if (Path.GetFullPath(userData).TrimEnd('\\').Equals(DefaultRoot(process), StringComparison.OrdinalIgnoreCase) && profile == "Default") return "";
            string value = Regex.Replace(Path.GetFileName(userData.TrimEnd('\\')) + "." + profile, "[^A-Za-z0-9.]", "");
            // Chromium ShellUtil::BuildAppUserModelId caps a two-component ID at
            // 31 chars/component and shortens in the middle, not at the end.
            return value.Length <= 31 ? value : value.Substring(0, 15) + value.Substring(value.Length - 16);
        }
        public static List<BrowserProfile> Read(string executable, string process, string root = null) {
            var result = new List<BrowserProfile>(); if (!IsBrowser(process)) return result;
            root = string.IsNullOrEmpty(root) ? DefaultRoot(process) : root; string stateFile = Path.Combine(root, "Local State");
            try {
                // Read only profile names/directories from metadata. No browser sessions, cookies, or credentials.
                var json = new JavaScriptSerializer { MaxJsonLength = 16777216 }.DeserializeObject(File.ReadAllText(stateFile)) as Dictionary<string, object>;
                var profile = json["profile"] as Dictionary<string, object>; var info = profile["info_cache"] as Dictionary<string, object>;
                var windows = WindowIdentity.Windows(process); string baseId = process == "msedge" ? "MSEdge" : "Chrome";
                foreach (string directory in info.Keys) {
                    var meta = info[directory] as Dictionary<string, object>; object friendly;
                    string name = meta != null && meta.TryGetValue("name", out friendly) ? Convert.ToString(friendly) : directory == "Default" ? "默认" : "用户 " + directory.Replace("Profile ", "");
                    string suffix = Suffix(root, directory, process); string id = suffix.Length == 0 ? baseId : baseId + "." + suffix;
                    var actual = windows.FirstOrDefault(w => suffix.Length > 0 && w.AppId.StartsWith(baseId + ".", StringComparison.OrdinalIgnoreCase) && w.AppId.EndsWith("." + suffix, StringComparison.Ordinal));
                    if (actual != null) id = actual.AppId;
                    result.Add(new BrowserProfile { Name = name, Directory = directory, UserData = root, Executable = executable, ProcessName = process, AppId = id });
                }
                // A nondefault browser window provides an observed installation prefix, including per-user suffixes.
                foreach (var p in result) {
                    string suffix = Suffix(root, p.Directory, process); var observed = windows.FirstOrDefault(w => suffix.Length > 0 && w.AppId.EndsWith("." + suffix, StringComparison.Ordinal) && w.AppId.StartsWith(baseId + ".", StringComparison.OrdinalIgnoreCase));
                    if (observed != null) { string prefix = observed.AppId.Substring(0, observed.AppId.Length - suffix.Length - 1); foreach (var other in result) { string s = Suffix(root, other.Directory, process); other.AppId = prefix + (s.Length == 0 ? "" : "." + s); } break; }
                }
            } catch (IOException) { } catch (ArgumentException) { } catch (KeyNotFoundException) { } catch (InvalidCastException) { }
            return result.OrderBy(p => p.Name).ToList();
        }
        public static BrowserProfile Identify(WindowTarget window) { return Read(window.Executable, window.ProcessName).FirstOrDefault(p => !string.IsNullOrEmpty(window.AppId) && p.AppId.Equals(window.AppId, StringComparison.Ordinal)); }
    }
    public sealed class WindowTargetResolver {
        readonly Dictionary<string, WindowTarget> tracked = new Dictionary<string, WindowTarget>();
        string Id(AppRule rule) { return rule.TargetId ?? rule.Name; }
        public void Associate(AppRule rule, WindowTarget window) { tracked[Id(rule)] = window; }
        public IntPtr Find(AppRule rule) {
            var windows = WindowIdentity.Windows(rule.ProcessName); IntPtr foreground = Native.GetForegroundWindow(); IEnumerable<WindowTarget> candidates = windows;
            if (rule.TargetType == TargetType.Profile) {
                // Exact per-window identity, never PID or page title, and never fall back to another profile.
                if (string.IsNullOrWhiteSpace(rule.WindowAppId)) return Tracked(rule, windows);
                candidates = windows.Where(w => w.AppId.Equals(rule.WindowAppId, StringComparison.Ordinal));
            } else if (rule.TargetType == TargetType.SpecificWindow) {
                IntPtr known = Tracked(rule, windows); if (known != IntPtr.Zero) return known;
                var exact = windows.FirstOrDefault(w => w.Handle.ToInt64() == rule.WindowHandle && w.ProcessId == (uint)rule.WindowProcessId && w.ProcessStart == rule.WindowProcessStart);
                return exact == null ? IntPtr.Zero : exact.Handle;
            } else if (!string.IsNullOrEmpty(rule.TitleContains)) candidates = candidates.Where(w => w.Title.IndexOf(rule.TitleContains, StringComparison.OrdinalIgnoreCase) >= 0);
            // Exact executable path when configured, except stable version-independent launchers.
            string path = Environment.ExpandEnvironmentVariables(rule.Executable ?? "");
            if (Path.IsPathRooted(path) && File.Exists(path) && !Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) {
                var exactPath = candidates.Where(w => w.Executable.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList(); if (exactPath.Count > 0 || rule.TargetType == TargetType.Profile) candidates = exactPath;
            }
            var list = candidates.ToList(); var target = list.FirstOrDefault(w => WindowIdentity.SameForeground(w.Handle, foreground));
            if (target == null) { WindowTarget known; if (tracked.TryGetValue(Id(rule), out known)) target = list.FirstOrDefault(w => w.Handle == known.Handle && w.ProcessStart == known.ProcessStart); }
            if (target == null) target = list.FirstOrDefault(); if (target != null) Associate(rule, target); return target == null ? IntPtr.Zero : target.Handle;
        }
        IntPtr Tracked(AppRule rule, List<WindowTarget> windows) { WindowTarget known; if (!tracked.TryGetValue(Id(rule), out known)) return IntPtr.Zero; var match = windows.FirstOrDefault(w => w.Handle == known.Handle && w.ProcessId == known.ProcessId && w.ProcessStart == known.ProcessStart); return match == null ? IntPtr.Zero : match.Handle; }
        public string LaunchArguments(AppRule rule) {
            if (rule.TargetType != TargetType.Profile) return rule.Arguments ?? "";
            // Canonical profile options replace any stale selectors retained by a shortcut.
            string extra = Regex.Replace(rule.Arguments ?? "", "--(?:profile-directory|user-data-dir)(?:=|\\s+)(?:\"[^\"]*\"|[^\\s]+)", "", RegexOptions.IgnoreCase).Trim();
            return extra + " --profile-directory=" + BrowserProfile.Quote(rule.ProfileDirectory) + (string.IsNullOrWhiteSpace(rule.UserDataDirectory) ? "" : " --user-data-dir=" + BrowserProfile.Quote(rule.UserDataDirectory));
        }
    }
}
