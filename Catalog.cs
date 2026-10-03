using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WindowSwitch {
    public class AppChoice {
        public string Name, ProcessName, Executable, Arguments, WindowTitle, ApplicationId, IconPath;
        public TargetType TargetType;
        public WindowTarget Window;
        public BrowserProfile Profile;
        public AppRule ToRule() {
            if (Profile != null) { var rule = Profile.ToRule(); rule.Name = Name; return rule; }
            return new AppRule { Name = Name, ProcessName = ProcessName, Executable = Executable, Arguments = Arguments ?? "", IconPath = IconPath, TargetType = TargetType, WindowAppId = Window == null ? "" : Window.AppId, WindowHandle = Window == null ? 0 : Window.Handle.ToInt64(), WindowProcessId = Window == null ? 0 : (int)Window.ProcessId, WindowProcessStart = Window == null ? "" : Window.ProcessStart };
        }
    }
    public static class Catalog {
        public static string Identity(uint pid) {
            IntPtr process = Native.OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return "";
            try { uint size = 0; if (Native.GetApplicationUserModelId(process, ref size, null) != 122 || size > 4096) return ""; var id = new StringBuilder((int)size); return Native.GetApplicationUserModelId(process, ref size, id) == 0 ? id.ToString() : ""; }
            finally { Native.CloseHandle(process); }
        }
        public static AppChoice FromFile(string path) {
            string target = path, args = "";
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
                dynamic shell = null, link = null;
                try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); link = shell.CreateShortcut(path); target = (string)link.TargetPath; args = (string)link.Arguments; }
                finally { Release(link); Release(shell); }
                if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("这个快捷方式没有可识别的启动目标，请从正在运行的窗口中选择。");
            }
            string process = Path.GetFileNameWithoutExtension(target), name = Path.GetFileNameWithoutExtension(path);
            Match launched = Regex.Match(args, "--processStart\\s+[\"']?([^\"'\\s]+\\.exe)", RegexOptions.IgnoreCase);
            if (launched.Success) process = Path.GetFileNameWithoutExtension(launched.Groups[1].Value);
            string folder = Path.GetDirectoryName(target), parent = folder == null ? null : Path.GetDirectoryName(folder);
            if (folder != null && parent != null && Path.GetFileName(folder).StartsWith("app-", StringComparison.OrdinalIgnoreCase)) {
                string stable = Path.Combine(parent, Path.GetFileName(target)); if (File.Exists(stable)) target = stable;
            }
            try { string product = FileVersionInfo.GetVersionInfo(target).ProductName; if (!string.IsNullOrWhiteSpace(product) && !product.Equals("Electron", StringComparison.OrdinalIgnoreCase)) name = product; } catch { }
            if (BrowserProfiles.IsBrowser(process)) {
                Match selector = Regex.Match(args, "--profile-directory(?:=|\\s+)(?:\"([^\"]+)\"|([^\\s]+))", RegexOptions.IgnoreCase);
                if (selector.Success) {
                    string directory = selector.Groups[1].Success ? selector.Groups[1].Value : selector.Groups[2].Value;
                    Match data = Regex.Match(args, "--user-data-dir(?:=|\\s+)(?:\"([^\"]+)\"|([^\\s]+))", RegexOptions.IgnoreCase);
                    string root = data.Success ? data.Groups[1].Success ? data.Groups[1].Value : data.Groups[2].Value : null;
                    var profile = BrowserProfiles.Read(target, process, root).FirstOrDefault(p => p.Directory == directory);
                    if (profile == null) throw new ArgumentException("这个浏览器快捷方式的用户目录未能识别，请从当前窗口中选择，或在高级设置里指定数据目录。");
                    return new AppChoice { Name = profile.ToRule().Name, ProcessName = process, Executable = target, Arguments = args, IconPath = target, TargetType = TargetType.Profile, Profile = profile, WindowTitle = "浏览器配置文件" };
                }
            }
            return new AppChoice { Name = name, ProcessName = process, Executable = target, Arguments = args, IconPath = target, WindowTitle = "程序文件" };
        }
        public static List<AppChoice> Running() {
            var choices = new List<AppChoice>(); uint self = (uint)Process.GetCurrentProcess().Id;
            var profileCache = new Dictionary<string, List<BrowserProfile>>(StringComparer.OrdinalIgnoreCase);
            foreach (var window in WindowIdentity.Windows()) {
                if (window.ProcessId == self) continue;
                try {
                        if (string.IsNullOrEmpty(window.Executable)) continue;
                        AppChoice choice = FromFile(window.Executable); choice.ProcessName = window.ProcessName; choice.WindowTitle = window.Title; choice.Window = window;
                        string id = Identity(window.ProcessId); choice.ApplicationId = id;
                        if (!string.IsNullOrWhiteSpace(id)) { choice.Executable = "explorer.exe"; choice.Arguments = "shell:AppsFolder\\" + id; }
                        if (choice.Name.Equals("Microsoft Windows Operating System", StringComparison.OrdinalIgnoreCase)) choice.Name = window.ProcessName;
                        if (BrowserProfiles.IsBrowser(window.ProcessName)) {
                            List<BrowserProfile> profiles; if (!profileCache.TryGetValue(window.ProcessName, out profiles)) { profiles = BrowserProfiles.Read(window.Executable, window.ProcessName); profileCache[window.ProcessName] = profiles; }
                            choice.Profile = profiles.FirstOrDefault(p => !string.IsNullOrEmpty(window.AppId) && p.AppId.Equals(window.AppId, StringComparison.Ordinal));
                            if (choice.Profile != null) { choice.Name = choice.Profile.ToRule().Name; choice.TargetType = TargetType.Profile; }
                        }
                        choices.Add(choice);
                } catch { /* Skip inaccessible/exiting processes, leaving the rest available. */ }
            }
            return choices.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public static List<AppChoice> Installed() {
            var choices = new List<AppChoice>(); dynamic shell = null, folder = null, items = null;
            try {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); folder = shell.NameSpace("shell:AppsFolder");
                if (folder == null) return choices; items = folder.Items();
                for (int i = 0; i < (int)items.Count; i++) {
                    dynamic item = null;
                    try {
                        item = items.Item(i); string name = (string)item.Name, id = (string)item.Path;
                        string target = Convert.ToString(item.ExtendedProperty("System.Link.TargetParsingPath"));
                        if (!string.IsNullOrWhiteSpace(target) && File.Exists(target) && (Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase))) {
                            var choice = FromFile(target); choice.Name = name; choice.ApplicationId = id; choice.WindowTitle = "已安装的应用"; choices.Add(choice);
                        } else if (id.Contains("!")) {
                            string family = Convert.ToString(item.ExtendedProperty("System.AppUserModel.PackageFamilyName"));
                            string process = PackageProcess(family, id.Substring(id.IndexOf('!') + 1));
                            if (string.IsNullOrWhiteSpace(process)) continue;
                            choices.Add(new AppChoice { Name = name, ProcessName = process, Executable = "explorer.exe", Arguments = "shell:AppsFolder\\" + id, ApplicationId = id, WindowTitle = "Windows 应用" });
                        }
                    } catch { } finally { Release(item); }
                }
            } finally { Release(items); Release(folder); Release(shell); }
            return choices.GroupBy(a => a.Name + "|" + a.Executable + "|" + a.Arguments).Select(g => g.First()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        static string PackageProcess(string family, string appId) {
            string root; XElement app = PackageApplication(family, appId, out root);
            string executable = app == null ? null : (string)app.Attribute("Executable");
            return string.IsNullOrWhiteSpace(executable) ? "" : Path.GetFileNameWithoutExtension(executable.Replace('/', '\\'));
        }
        // The manifest <Application> element of an installed package, and the package's install folder.
        public static XElement PackageApplication(string family, string appId, out string root) {
            root = null; if (string.IsNullOrWhiteSpace(family)) return null;
            uint count = 0, length = 0;
            if (Native.GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero) != 122 || count == 0 || count > 100 || length > 100000) return null;
            IntPtr names = Marshal.AllocHGlobal((int)count * IntPtr.Size), buffer = Marshal.AllocHGlobal((int)length * 2);
            try {
                if (Native.GetPackagesByPackageFamily(family, ref count, names, ref length, buffer) != 0) return null;
                for (int i = 0; i < count; i++) {
                    string fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size)); uint size = 0;
                    if (Native.GetPackagePathByFullName(fullName, ref size, null) != 122) continue;
                    var path = new StringBuilder((int)size); if (Native.GetPackagePathByFullName(fullName, ref size, path) != 0) continue;
                    string manifest = Path.Combine(path.ToString(), "AppxManifest.xml"); if (!File.Exists(manifest)) continue;
                    XElement app = XDocument.Load(manifest).Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" && (string)e.Attribute("Id") == appId);
                    if (app != null) { root = path.ToString(); return app; }
                }
            } catch { } finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(names); }
            return null;
        }
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    }
}
