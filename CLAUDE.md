# CLAUDE.md

MyQuicker —— 基于 WPF 的个人快捷启动器。鼠标唤醒键触发无框菜单，支持自定义动作与原生截屏。
Spec 驱动开发（SDD），权威规范见 `SPEC.md`，本文件补充实现层面的架构约束与现状。

## 技术栈
- .NET 8.0-windows / WPF（`UseWPF`）+ WinForms（`UseWindowsForms`，仅用于 `Screen` / `NotifyIcon`）
- P/Invoke 调用 `user32.dll` / `kernel32.dll` / `gdi32.dll` / `dwmapi.dll`
- 配置持久化：`appsettings.json`（运行时由 `SettingsManager` 自动生成，源码不纳管）

## 项目架构规范（严格分层）
三层职责分离，原生调用与 UI 渲染必须隔离（SPEC §2 / §5）。

- **`/Interop`** —— 底层 API。`NativeMethods.cs` 集中存放全部 P/Invoke 签名、常量、`[StructLayout]` 结构体。
  **禁止把 P/Invoke 写进 UI code-behind。** 新增原生 API 一律加到此文件并附 XML 注释。
- **`/Services`** —— 核心逻辑，无 UI 依赖。
  - `GlobalHookService` —— 全局低级鼠标钩子（`WH_MOUSE_LL`）。
  - `ScreenshotService` —— 多屏截图采集 + GDI 回收。
  - `ActionExecutor` —— 动作分发，含 `sys:` 协议路由。
  - `SettingsManager` —— `appsettings.json` 读写，缺失/脏文件自动回填默认动作。
- **`/UI`** —— WPF 视图（XAML + code-behind）。
  - `MainWindow` —— 唤醒弹出的无框菜单。
  - `SettingsWindow` —— Fluent 风格侧边栏设置中心。
  - `ScreenshotWindow` —— 全屏截屏覆盖层（智能寻边 + 拖拽选区）。
  - `PinWindow` —— 贴图常驻窗口（拖拽/缩放/旋转/镜像/透明度/右键菜单）。
- **`/Models`** —— 数据契约。`ActionItem`（实现 `INotifyPropertyChanged` 供 DataGrid 双向绑定）、`AppSettings`。

## 核心技术约束

### 1. GDI 对象手动回收
`ScreenshotService.Capture()` 中 `Bitmap.GetHbitmap()` 创建的非托管 `HBITMAP` **必须**手动释放，否则 GDI 句柄泄漏：
1. `Imaging.CreateBitmapSourceFromHBitmap(...)` 后立即 `source.Freeze()` —— 强制 WPF 拷贝像素，使 HBITMAP 可安全释放；
2. 在 `finally` 块中调用 `NativeMethods.DeleteObject(hBitmap)`。
> 任何新增的 GDI 对象（`Bitmap`/`HBITMAP`/`HICON` 等）都遵循同一规则：`Freeze` 拷贝 → `DeleteObject` 释放。`NativeMethods.DeleteObject` 已就绪。

### 2. 多屏坐标系基于 VirtualBounds
多屏环境下主屏左侧/上方的显示器会使原点为负。所有跨屏坐标计算必须以 `ScreenshotService.ComputeVirtualBounds()` 为基准（取所有屏幕 `Min(X/Y)` 与 `Max(Right/Bottom)`，X/Y 可能为负）：
- `ScreenshotWindow` 的 `Left/Top/Width/Height` 直接绑定 `bounds`（物理像素）；
- 鼠标物理坐标转窗口局部坐标统一用 `pt - _bounds.X/Y`，窗口局部坐标与底图像素 1:1 对应，`CroppedBitmap` 即按此裁剪；
- `MainWindow` 定位用 `PresentationSource.CompositionTarget.TransformFromDevice` 把物理坐标转逻辑坐标后再居中。
> 不要用 `SystemParameters.PrimaryScreenWidth` 或单屏 `Bounds` 做跨屏计算。

### 3. 全局钩子线程与委托存活
- 钩子必须在消息泵线程（主 UI 线程）上安装；`GlobalHookService.Start()` 由 `App.OnStartup` 调用。
- 钩子委托 **必须**保存在字段（`_hookProc`）中防止 GC 回收，否则 user32 回调野指针会崩进程。
- 退出时 `App.OnExit` 必须显式 `Stop()` + `Dispose()` 卸载钩子。

### 4. 无框菜单不抢焦点
`MainWindow` 在 `OnSourceInitialized` 中给 HWND 加 `WS_EX_NOACTIVATE`，确保弹出时不夺取当前应用焦点；动作执行前先 `Hide()` 再恢复原前台窗口。

## UI 风格规范

### Fluent Design 侧边栏（`SettingsWindow.xaml`）
- 布局：左 160px 侧边栏 + 右内容区（`Grid.ColumnDefinitions`）。侧边栏背景 `#F7F7F7`，右侧分隔线 `#EAEAEA`。
- 导航项样式 `NavRadioButton`：`RadioButton` 模板去掉单选圆点，做成菜单项外观；选中时左侧出现 3px 宽 `#0066CC` 竖条指示器（`Indicator`），文字变蓝，背景转白。
- 页面切换：`常规` / `动作管理` 两个 `RadioButton`，通过 `BooleanToVisibilityConverter` 绑定各自 `IsChecked` 控制对应 `StackPanel`/`Grid` 显隐（无需代码后置）。
- 动作按钮样式 `ActionButton`：扁平蓝（`#0066CC`，hover `#0055AA`），圆角 6。
- 动作管理：`DataGrid` 双向绑定 `ActionItem`，列头浅灰加粗，交替行 `#FAFAFA`。
- 置顶穿透小技巧：`OpenSettings` 中 `Topmost=true` 后立即 `false`，绕过系统前台锁。
> 新增设置页：加一个 `RadioButton` + 对应内容区，沿用 `NavRadioButton` / `ActionButton` 样式，保持视觉一致。

### ScreenshotWindow 寻边红框绘制逻辑（`ScreenshotWindow.xaml(.cs)`）
覆盖层分三层（`RootGrid`）：
1. `BackgroundImage` —— 全屏底图（`Stretch="None"`）；
2. 暗罩 `Path` —— `#66000000`，用 `CombinedGeometry(Exclude)` 在 `ScreenGeometry`（整屏）中挖出 `CutoutGeometry`（选区）形成镂空；
3. `HighlightBorder` —— 选区红框（`BorderBrush="Red"` / `BorderThickness="2"`），默认 `Hidden`。

寻边模式（未拖拽时，`OnMouseMove`）：
1. `WindowUnderCursor` 临时给自身 HWND 加 `WS_EX_TRANSPARENT`（使 `WindowFromPoint` 穿透覆盖层），取到光标下窗口后**立即还原** ex style；
2. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 取真实物理窗口矩形（Win11 阴影修正），失败回退 `GetWindowRect`；
3. 转窗口局部坐标后 `ApplySelection`：同时设置 `CutoutGeometry.Rect`（镂空）与 `HighlightBorder` 的 `Margin`/`Width`/`Height`（红框）。

拖拽模式（左键按下后）：用 `min/abs` 归一化起止点矩形，支持反向拖拽。松开时按窗口局部坐标 `CroppedBitmap` 裁剪底图 → `Freeze` → `Clipboard.SetImage`。
ESC / 右键取消关闭。

## 指令协议（SDD）：`sys:` 前缀
`ActionItem.Command` 以 `sys:` 开头者为**内部协议指令**，由 `ActionExecutor.Execute` 拦截，不走 `Process.Start`。当前已实现：

| 指令 | 行为 |
|------|------|
| `sys:snipping` | 调 `ScreenshotService.Capture()` 取全屏底图，`new ScreenshotWindow(source, bounds).ShowDialog()` 打开截屏覆盖层 |

> 新增内置功能：在 `ActionExecutor.Execute` 中加 `if (item.Command == "sys:xxx")` 分支，并在 `SettingsManager` 默认动作或文档中登记。`sys:` 之外的命令一律按外部进程启动（`UseShellExecute=true`）。

## 当前进度
- ✅ **8A 阶段已完成**：多屏全屏底图采集（`ScreenshotService`，含 GDI 回收与 VirtualBounds）。
- ✅ **8B 阶段已完成（智能寻边）**：`ScreenshotWindow` 悬停寻边 + 双模态交互（`DragThreshold=5` 解耦点击/拖拽：未跨阈值且红框在则智能快照、跨阈值则手动拖拽）+ 桌面背景过滤（`Progman`/`WorkerW` 视为空点不截图）+ 剪贴板输出；已验证寻边精准、无内存泄漏。
- ✅ **8C 阶段已完成（贴图引擎 PinEngine）**：`PinWindow` 贴图常驻，左键拖拽 / 双击关闭 / 右键菜单（置顶·阴影·重置大小·不透明度·旋转·镜像·复制·另存为·作为文件打开·关闭）；`ScreenshotWindow` 结算时 `new PinWindow(crop).Show()` 联动钉图，截图罩关闭后贴图窗口存活。

## 开发约定
- 一次只实现一个模块（SPEC §5）；先验证 `StructLayout` 内存对齐再测钩子。
- 配置热重载：`MainWindow` 每次唤醒都从磁盘重新加载动作列表，编辑 `appsettings.json` 无需重启。
- 注释风格：公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节/步骤（如 `Per SPEC §4.1 / step 6`）。
- 调试日志（**允许并推荐用于排查**）：本项目为 `WinExe`、无内建控制台；`App.OnStartup` 调 `NativeMethods.AttachConsole(ATTACH_PARENT_PROCESS)` 挂接父终端，使 `Console.WriteLine`（建议紧跟 `Console.Out.Flush()`）在 `dotnet run` 时直接输出到终端。排查事件流/坐标时可临时加 `Console.WriteLine("DEBUG: ...")`——`ScreenshotWindow` 已内置 `DEBUG: MouseDown / MouseUp / Capture Rect / Mode A / Mode B / Empty Click / WS_EX_TRANSPARENT cleared` 等日志。双击启动等无父控制台场景 `AttachConsole` 静默失败，不影响功能；正式发布前应移除调试输出与 `AttachConsole` 调用。

## Git Rules

### Commit Format

```text
<type>(<scope>): <description>
````

### Rules

- 一次 commit 只做一件逻辑变更
    
- 禁止混合 feature / fix / refactor
    
- 禁止使用模糊提交（update / fix bug / test）

- 禁止提交信息携带 Agent、AI信息