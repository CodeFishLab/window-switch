# 窗口切换 · Window Switch

给常用应用绑定全局快捷键：按一下打开或切到前台，再按一下最小化。Windows 桌面工具，WinForms / .NET Framework 4.8。

## 功能

- **应用快捷键**：每个应用可设置多个快捷键，第一个显示在预览中；支持区分左右 Ctrl / Alt / Shift（例如 `Left Alt+2`）。
- **窗口目标**：整个应用、指定窗口，或 Chrome / Edge 的某个浏览器用户（按窗口身份精确匹配）。
- **长按预览**：长按设定的按键，显示所有快捷键；每个应用可单独设置是否显示。在预览中点击应用图标或名称即可打开它，点击状态胶囊（「已生效」/「已停用」）可停用或启用该应用的快捷键；右上角齿轮打开设置。预览位置可选紧贴 / 适中 / 较高 / 居中，多显示屏时可跟随当前窗口或鼠标。
- **打开窗口快捷键**、**最小化当前窗口快捷键**。
- 拖动排序应用与快捷键；亮色 / 暗色 / 跟随系统；托盘常驻（可关闭托盘图标）、随 Windows 登录启动。

版本变化见 [CHANGELOG.md](CHANGELOG.md)。

## 构建

需要 Windows 10/11 自带的 .NET Framework 4.8 编译器，无需 Visual Studio。

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1                      # 输出到当前目录
powershell -ExecutionPolicy Bypass -File build.ps1 -OutputDirectory out  # 输出到其他目录
```

## 运行

运行 `WindowSwitch.exe`。`WindowSwitch.exe.config` 必须和 exe 放在同一目录（开启 Per-Monitor V2 高 DPI，否则界面会模糊）。

首次运行会在 exe 旁生成 `config.json`（个人配置，已在 `.gitignore` 中排除）。

命令行参数：

| 参数 | 作用 |
| --- | --- |
| `--config <路径>` | 使用指定的配置文件 |
| `--background` | 启动后只在托盘运行 |
| `--toggle <应用名>` | 切换指定应用 |
| `--preview` | 显示快捷键预览 |
| `--stop` | 退出正在运行的实例 |
| `--enable-startup` | 启用随 Windows 登录启动 |

## 源码结构

| 文件 | 内容 |
| --- | --- |
| `WindowSwitch.cs` | 入口、主窗口逻辑、配置、窗口切换引擎 |
| `MainUi.cs` | 主窗口界面、托盘菜单、全局快捷键设置 |
| `Ui.cs` | 编辑面板（应用 / 快捷键）、应用选择 |
| `Hint.cs` | 长按预览浮层与设置 |
| `Hotkeys.cs` / `KeyHook.cs` | 快捷键注册；区分左右键的键盘钩子 |
| `Targets.cs` / `Catalog.cs` | 窗口识别、浏览器用户、应用列表 |
| `Theme.cs` / `Components.cs` / `FormControls.cs` | 主题与界面组件 |
| `Icons.cs` / `TrayIcon.cs` | 应用图标、托盘图标 |
