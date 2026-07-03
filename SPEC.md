# Spec-Driven Development (SDD): MyQuicker Architecture

> 权威规范。本文件描述系统必须满足的架构与行为契约；实现层面的约束与现状见 `CLAUDE.md`。

## 1. System Requirements & Stack
- Target Framework: .NET 8.0-windows
- UI Framework: WPF（`UseWPF`），辅以 WinForms（`UseWindowsForms`，仅用于 `Screen` / `NotifyIcon`）
- Native interop: P/Invoke 调用 `user32.dll` / `kernel32.dll` / `gdi32.dll` / `dwmapi.dll`
- Execution Context: Windows 10/11
- 配置持久化：`settings.json`（运行时由 `SettingsManager` 单例生成/读写，源码不纳管）；首次启动自动从旧 `appsettings.json` 迁移唤醒键与动作列表

## 2. Project Structure & Layering
严格三层职责分离，原生调用与 UI 渲染必须隔离。
- `/Interop` —— 底层 P/Invoke。`NativeMethods.cs` 集中存放全部原生 API 签名、常量、`[StructLayout]` 结构体。**禁止把 P/Invoke 写进 UI code-behind。** 新增原生 API 一律加到此文件并附 XML 注释。
- `/Services` —— 核心逻辑，无 UI 依赖：`GlobalHookService`（全局低级鼠标钩子）、`ScreenshotService`（多屏截图采集 + GDI 回收）、`ActionExecutor`（动作分发 + `sys:` 协议路由）、`SettingsManager`（配置单例）、`ActionStore`（动作域门面）。
- `/UI` —— WPF 视图（XAML + code-behind）：`MainWindow`（唤醒菜单）、`SettingsWindow`（设置中心）、`ScreenshotWindow`（截屏覆盖层）、`PinWindow`（贴图窗口）、`BrushHelper`（颜色串转 Brush）。
- `/Models` —— 数据契约：`ActionItem`（实现 `INotifyPropertyChanged`，供 DataGrid 双向绑定）、`SettingsModel`（多层级 POCO：`ActionSettings` / `SnippingSettings` / `MenuSettings` / `PinSettings`，默认值对齐重构前硬编码）。
- `/Resources` —— 共享 XAML 资源：`ThemeStyles.xaml`（主题画刷 / 尺寸 / 公共样式），由 `App.xaml` 合并。

## 3. Low-Level API (P/Invoke) Specifications
`Interop/NativeMethods.cs` 静态类集中定义以下非托管 API。

### 3.1 Hook Definitions
- `SetWindowsHookEx`：注册钩子过程，目标 `WH_MOUSE_LL` (id=14)。
- `UnhookWindowsHookEx`：卸载钩子，退出时显式调用防泄漏。
- `CallNextHookEx`：传递给下一钩子。
- `LowLevelMouseProc` 委托类型；`GetModuleHandle` 取当前模块句柄。

### 3.2 Coordinate & Focus Control
- `GetCursorPos(out POINT)`：光标物理屏幕坐标。
- `GetForegroundWindow` / `SetForegroundWindow`：前台窗口句柄（声明保留；当前菜单走 `WS_EX_NOACTIVATE` 全程不抢焦点，故未实际调用）。

### 3.3 Window Styles (No-Activate / Hit-Test-Transparent)
- `GetWindowLong` / `SetWindowLong`（32/64 位自适配分发）。
- `WS_EX_NOACTIVATE`：窗口不抢焦点（`MainWindow` 在 `OnSourceInitialized` 加注）。
- `WS_EX_TRANSPARENT`：命中测试穿透（`ScreenshotWindow` 寻边时临时加上、取目标后立即还原）。

### 3.4 GDI Memory Management
- `DeleteObject`：释放 `Bitmap.GetHbitmap()` 创建的非托管 `HBITMAP`。任何新增 GDI 对象遵循 `Freeze 拷贝 → DeleteObject 释放`。

### 3.5 Window Edge Detection (Smart Snipping)
- `WindowFromPoint`：取光标下窗口。
- `GetWindowRect` / `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`：取窗口物理矩形（Win11 阴影修正，失败回退 `GetWindowRect`）。
- `GetClassName`：过滤桌面背景（`Progman` / `WorkerW`），使空点不截图。

### 3.6 Console Attachment (Debug)
- `AttachConsole(ATTACH_PARENT_PROCESS)`：WinExe 挂接父终端，使 `Console.WriteLine` 调试输出可见；无父控制台时静默失败。

### 3.7 Structs and Constants
- 消息常量：`WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` / `WM_NCLBUTTONDOWN` / `WM_MBUTTONDOWN` / `WM_MBUTTONUP` / `WM_XBUTTONDOWN`。
- `POINT` / `MSLLHOOKSTRUCT` / `RECT`：`[StructLayout(LayoutKind.Sequential)]` 精确映射内存。

## 4. Core Module Logic

### 4.1 Global Mouse Hook Service (`GlobalHookService`)
- **初始化**：`Process.GetCurrentProcess().MainModule.ModuleName` → `GetModuleHandle` → `SetWindowsHookEx`。钩子委托必须存字段（`_hookProc`）防 GC 回收野指针崩溃。
- **线程约束**：必须在消息泵线程（主 UI 线程）安装；`App.OnStartup` 调 `Start()`，`App.OnExit` 调 `Stop()` + `Dispose()`。
- **拦截逻辑**（`HookCallback`）：
  1. 跟踪左/右/非客户区/中/侧键按下；任一按下即触发 `OnAnyMouseDown`（供 UI 检测菜单外点击）。
  2. 若 `msg == ActionSettings.WakeupMessage` 触发 `OnWakeupClick`；侧键（`WM_XBUTTONDOWN`）还需高位字 `mouseData >> 16` 等于 `XButtonData`。
  3. 命中唤醒键时 `return (IntPtr)1` 吞掉消息；否则 `CallNextHookEx`。
- **可配置唤醒键**：中键 / 侧键1（后退）/ 侧键2（前进），由 `ActionSettings.WakeupMessage` + `XButtonData` 决定，`SettingsWindow` 编辑后经 `UpdateSettings` 即时生效。

### 4.2 Frameless Wake-up Menu (`MainWindow`)
- **窗口属性**：`WindowStyle=None` / `AllowsTransparency=True` / `Background=Transparent` / `Topmost=True` / `ShowInTaskbar=False`。
- **不抢焦点**：`OnSourceInitialized` 给 HWND 加 `WS_EX_NOACTIVATE`，弹出与点击均不夺取当前应用焦点。菜单全程不抢焦点，故**无需** `Activate()` / `OnDeactivated` / 显式恢复前台窗口。
- **定位**：钩子事件给出物理坐标，用 `PresentationSource.CompositionTarget.TransformFromDevice` 转逻辑坐标后令窗口中心对齐光标。
- **显隐与外部点击关闭**：`OnWakeupClick` 中 `Show()`；`OnAnyMouseDown` 检测到点击落在窗口外则 `Hide()`（点击本身不阻断，继续到达底层应用）。
- **动作执行**：按钮点击先 `Hide()` 再 `ActionExecutor.Execute`（菜单未抢焦点，原前台窗口仍持焦，无需恢复）。
- **热重载**：每次唤醒经 `ActionExecutor.GetActions()` → `ActionStore` → `SettingsManager.Load()` 重读磁盘动作列表，编辑 `settings.json` 无需重启。
- **设置入口**：齿轮按钮 `Hide()` 后回调 `OpenSettingsAction`（由 `App` 接到 `SettingsWindow`）。

### 4.3 Action Execution Engine (`ActionExecutor`)
- 动作列表经 `ActionStore` 从 `settings.json` 加载。
- `sys:` 前缀为内部协议指令，由 `Execute` 拦截，不走 `Process.Start`。当前已实现：

  | 指令 | 行为 |
  |------|------|
  | `sys:snipping` | `ScreenshotService.Capture()` 取全屏底图，`new ScreenshotWindow(source, bounds).ShowDialog()` 打开截屏覆盖层 |

- 其余命令按外部进程启动：`ProcessStartInfo { UseShellExecute = true }`。
- 新增内置功能：在 `Execute` 加 `if (item.Command == "sys:xxx")` 分支，并在 `SettingsModel.Action` 默认动作或文档中登记。

### 4.4 Configuration System (`SettingsManager` / `ActionStore`)
- `SettingsManager`：全局单例（`Instance`），统一配置中心。`Load()` 读 `settings.json`（脏文件/解析失败回退默认值），文件不存在时默认值 + 迁移旧 `appsettings.json`（`JsonDocument` 解析，不依赖已删除的 `AppSettings` 类型）并落盘；`Save()` 同步写盘。`App.OnStartup` 加载 / `App.OnExit` 保存。
- `SettingsModel`：`Action` / `Snipping` / `Menu` / `Pin` 四组，默认值对齐重构前硬编码。
- `ActionStore`：动作域门面，封装唤醒键与动作列表读写，全部委托单例，自身不做 IO。供 `App` / `SettingsWindow` / `ActionExecutor` / `MainWindow` 使用。
- **热重载 / 编辑隔离**：`Load()` 每次重读磁盘；`SettingsWindow` 的未保存编辑持有独立对象引用，不受他处 `Load` 影响。

## 5. Execution Directives for LLM
- 一次只实现一个模块。
- 先验证 `StructLayout` 内存对齐再测钩子。
- 任何 P/Invoke 签名不得放进 UI code-behind，一律加到 `NativeMethods.cs`。
- 任何新增 GDI 对象遵循 `Freeze 拷贝 → DeleteObject 释放`。
- 公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节。

## 6. Screenshot & Pin Subsystem

### 6.1 (8A) Multi-Monitor Full-Screen Capture (`ScreenshotService`)
- `Capture()`：`ComputeVirtualBounds()` 取所有屏幕 `Min(X/Y)` 与 `Max(Right/Bottom)` 构成虚拟屏矩形（X/Y 可能为负）→ `Bitmap` (32bppArgb) → `CopyFromScreen` → `GetHbitmap` → `Imaging.CreateBitmapSourceFromHBitmap` → `Freeze()` 强制拷贝像素 → `finally` 中 `DeleteObject(hBitmap)` 释放非托管句柄。返回 `(BitmapSource, Rectangle)`。
- **多屏坐标基准**：所有跨屏计算以 `ComputeVirtualBounds()` 为基准，禁止用 `SystemParameters.PrimaryScreenWidth` 或单屏 `Bounds`。
- **DPI 假设**：`ScreenshotWindow` / `PinWindow` 直接用物理像素设 `Left/Top/Width/Height`，依赖 96 DPI（100% 缩放）下 DIP 与物理像素 1:1；非 100% 缩放下覆盖层/贴图定位会偏移。`MainWindow` 走 `TransformFromDevice` 不受影响。

### 6.2 (8B) Smart Snipping Overlay (`ScreenshotWindow`)
- 全屏覆盖层，三层（`RootGrid`）：`BackgroundImage`（底图 `Stretch=None`）/ `MaskPath`（暗罩，`CombinedGeometry(Exclude)` 在整屏 `ScreenGeometry` 中挖出 `CutoutGeometry` 选区镂空）/ `HighlightBorder`（选区红框，默认 `Hidden`）。
- 窗口 `Left/Top/Width/Height` 直接绑定 `bounds`（物理像素）；鼠标物理坐标转窗口局部坐标统一用 `pt - bounds.X/Y`，窗口局部坐标与底图像素 1:1 对应，`CroppedBitmap` 即按此裁剪。
- **双模态状态机**（`DragThreshold` 解耦点击/拖拽，默认 5 DIP）：
  - **寻边模式**（未跨阈值）：`WindowUnderCursor` 临时给自身 HWND 加 `WS_EX_TRANSPARENT` 使 `WindowFromPoint` 穿透覆盖层，取光标下窗口后立即还原 ex style；`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 取真实矩形（失败回退 `GetWindowRect`）；桌面背景（`Progman`/`WorkerW`）视为空点不截图。转窗口本地坐标后 `ApplySelection`（同时设镂空 `CutoutGeometry.Rect` 与红框 `Margin`/`Width`/`Height`）。
  - **拖拽模式**（位移超阈值）：`CaptureMouse`，用 `min/abs` 归一化起止点矩形，支持反向拖拽。
- **结算**（`SettleSelection`，松开左键时）：模式 A 智能快照（未拖拽且有红框 → 截红框）/ 模式 B 手动拖拽（截拖拽选区）/ 空点（无红框且未拖拽 → 不操作）。按窗口本地坐标 `CroppedBitmap` 裁剪底图 → `Freeze` → `Clipboard.SetImage` → `new PinWindow(crop, screenX, screenY).Show()` 联动钉图（截图罩 `Close()` 后贴图窗口存活）。随后 `Close()`。
- ESC / 右键取消关闭。
- 覆盖层颜色/厚度由 `SettingsModel.Snipping` 注入（`MaskColor` / `BorderColor` / `BorderThickness` / `OverlayBackground`）；`DragThreshold` 取自 `SnippingSettings.DragThreshold`。

### 6.3 (8C) Pin Engine (`PinWindow`)
- 贴图常驻窗口：`WindowStyle=None` / `AllowsTransparency=True` / `Topmost=True` / `ShowInTaskbar=True` / `ResizeMode=CanResize`。
- 两层结构：`PinBorder`（边框层，向外生长）+ `PinImage`（`Stretch=None`，`Margin=border` 向内缩，内容面积恒为 imgW×imgH）；两层 `IsHitTestVisible=False`，命中测试落到 Window。
- 交互：左键 `DragMove`（系统模态移动，无抖动）/ 左键双击关闭 / 右键菜单。
- 右键菜单：置顶 / 显示阴影 / 显示边界 / 重置大小 / 不透明度（0.3/0.5/0.8/1.0）/ 旋转 / 镜像 / 复制图片 / 另存为… / 作为文件打开 / 关闭。
- 旋转：`_rotationStep = (_rotationStep + 1) % 4`，`RotationAngle = step * RotationStepDegrees`（默认 90°），90/270 时窗口宽高互换（`ApplyWindowSize`）。
- 镜像：`ScaleTransform.ScaleX = -1/1`（水平翻转，`RenderTransformOrigin=0.5,0.5` 居中）。
- 显示边界：`PinBorderThickness` 0↔2，边框向外生长（窗口左上角反向偏移保持图片屏幕坐标不变）。
- 不透明度：直接设 `Window.Opacity`。
- 另存为 / 作为文件打开：`PngBitmapEncoder` 落盘；后者写临时文件后 `UseShellExecute=true` 打开。
- 参数由 `SettingsModel.Pin` 注入（`MinWidth` / `MinHeight` / `BorderColor` / `ShadowBlurRadius` / `RotationStepDegrees` / `DefaultOpacity`）；阴影 Depth/Opacity/Direction/Color、`PinBorderThickness=2`、不透明度菜单预设保留内联。

## 7. Unified Configuration & Theming（重构）
重构后所有"硬编码"按职责分三层，**严禁再散落到 code-behind / XAML 字面量**：
- **JSON（`SettingsModel` / `settings.json`）**：关键视觉与交互参数（`Snipping` / `Menu` / `Pin` 的颜色、尺寸、阈值、阴影模糊半径、旋转步进等）。各窗口构造函数 `InitializeComponent()` 后读 `SettingsManager.Instance.Settings.{组}` 动态赋值给命名控件属性；按钮背景色等 Style 内部值经 `{DynamicResource}` 注入（窗口 `Resources[key] = BrushHelper.ToBrush(...)`）。
- **ThemeStyles.xaml（`StaticResource`）**：纯布局 / 公共样式（主题画刷、字号、边距、圆角、`MenuButtonStyle` / `NavRadioButton` / `ActionButton` / `DataGridColumnHeader`）。由 `App.xaml` 合并。**不写入 JSON**。
- **保留内联**：窗口独有视觉物理反馈（如 `PinWindow` 阴影 Depth/Opacity/Direction/Color、`PinBorderThickness=2`、不透明度菜单预设）与唯一面板布局约束（如 `SettingsWindow` 750×500、ComboBox 宽 320），不提取。

> 新增可配置项：关键视觉/交互参数 → 加到 `SettingsModel` 对应组 + 默认值 + code-behind 注入；公共样式 → 加到 `ThemeStyles.xaml`；否则保留内联。
