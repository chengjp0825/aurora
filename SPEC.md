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
- `/Services` —— 核心逻辑，无 UI 依赖：`GlobalHookService`（全局低级鼠标钩子 + 画圈旁观）、`GestureHelper`（画圈几何识别，纯函数）、`ScreenshotService`（多屏截图采集 + GDI 回收）、`ActionExecutor`（动作分发 + `sys:` 协议路由）、`SettingsManager`（配置单例）、`ActionStore`（动作域静态门面）。
- `/UI` —— WPF 视图（XAML + code-behind）：`MainWindow`（唤醒菜单，方块矩阵布局）、`SettingsWindow`（5 页设置中心）、`ScreenshotWindow`（截屏覆盖层）、`PinWindow`（贴图窗口）、`BrushHelper`（颜色串转 Brush）。
- `/Models` —— 数据契约：`ActionItem`（实现 `INotifyPropertyChanged`，供 DataGrid 双向绑定）、`SettingsModel`（多层级 POCO：`ActionSettings`（含 `WAKEUP_CIRCLE_GESTURE = -1`）/ `SnippingSettings` / `MenuSettings` / `PinSettings`，默认值对齐重构前硬编码）。
- `/Resources` —— 共享 XAML 资源：`ThemeStyles.xaml`（主题画刷 / 尺寸 / 公共样式，含 Fluent 扁平控件样式），由 `App.xaml` 合并。

## 3. Low-Level API (P/Invoke) Specifications
`Interop/NativeMethods.cs` 静态类集中定义以下非托管 API。

### 3.1 Hook Definitions
- `SetWindowsHookEx`：注册钩子过程，目标 `WH_MOUSE_LL` (id=14)。
- `UnhookWindowsHookEx`：卸载钩子，退出时显式调用防泄漏。
- `CallNextHookEx`：传递给下一钩子。
- `LowLevelMouseProc` 委托类型；`GetModuleHandle` 取当前模块句柄。

### 3.2 Coordinate Control
- `GetCursorPos(out POINT)`：光标物理屏幕坐标。
> 焦点 API（`GetForegroundWindow` / `SetForegroundWindow`）已删除：菜单走 `WS_EX_NOACTIVATE` 全程不抢焦点，无需恢复前台窗口。

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

### 3.6 Structs and Constants
- 消息常量：`WM_MOUSEMOVE` / `WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` / `WM_NCLBUTTONDOWN` / `WM_MBUTTONDOWN` / `WM_XBUTTONDOWN`。
- `POINT` / `MSLLHOOKSTRUCT` / `RECT`：`[StructLayout(LayoutKind.Sequential)]` 精确映射内存。

## 4. Core Module Logic

### 4.1 Global Mouse Hook Service (`GlobalHookService`)
- **初始化**：`Process.GetCurrentProcess().MainModule.ModuleName` → `GetModuleHandle` → `SetWindowsHookEx`。钩子委托必须存字段（`_hookProc`）防 GC 回收野指针崩溃。
- **线程约束**：必须在消息泵线程（主 UI 线程）安装；`App.OnStartup` 调 `Start()`，`App.OnExit` 调 `Stop()` + `Dispose()`。
- **拦截逻辑**（`HookCallback`）：
  1. **`WM_MOUSEMOVE` 旁观分支**（永不拦截，始终 `CallNextHookEx`）：仅在 `WakeupMessage == WAKEUP_CIRCLE_GESTURE` 时入队 `(POINT, Timestamp=Environment.TickCount64)` 到 `_moveHistory`，`PruneMoveHistory` 剔除 >800ms 老点；样本数 ≥8 时复制到复用缓冲 `_pointsBuffer` 调 `GestureHelper.IsCircle`，命中即清空队列并 `Dispatcher.BeginInvoke(OnWakeupClick)`。
  2. 跟踪左/右/非客户区/中/侧键按下；任一按下即 `Dispatcher.BeginInvoke(OnAnyMouseDown)`（供 UI 检测菜单外点击）。
  3. 若 `msg == ActionSettings.WakeupMessage`：侧键（`WM_XBUTTONDOWN`）还需高位字 `mouseData >> 16` 等于 `XButtonData`；命中即 `Dispatcher.BeginInvoke(OnWakeupClick)` 并 `return (IntPtr)1` 吞键；否则 `CallNextHookEx`。
- **回调时效**：除"吞键"同步返回外，所有 UI 变化与磁盘 IO 一律异步派发，保证 <100ms 返回，规避 `LowLevelHooksTimeout` 静默摘钩。
- **可配置唤醒方式**：中键 / 侧键后退 (XButton1) / 单纯画圈（`WAKEUP_CIRCLE_GESTURE = -1`），由 `ActionSettings.WakeupMessage` + `XButtonData` 决定，`SettingsWindow` 编辑后经 `UpdateSettings` 即时生效。

### 4.2 Frameless Wake-up Menu (`MainWindow`)
- **窗口属性**：`WindowStyle=None` / `AllowsTransparency=True` / `Background=Transparent` / `Topmost=True` / `ShowInTaskbar=False`。
- **不抢焦点**：`OnSourceInitialized` 给 HWND 加 `WS_EX_NOACTIVATE`，弹出与点击均不夺取当前应用焦点。菜单全程不抢焦点，故**无需** `Activate()` / `OnDeactivated` / 显式恢复前台窗口。
- **方块矩阵布局**：`ScrollViewer` + `ItemsControl`(ItemsPanel=`WrapPanel`)，每个动作为 72×72 图标方块（`MenuButtonStyle` + 内含图标 `TextBlock` + 名称 `TextBlock`）；底部工具区含齿轮按钮。
- **定位**：钩子事件给出物理坐标，用 `ToLogical(POINT)`（封装 `TransformFromDevice`）转逻辑坐标后令窗口中心对齐光标。`ToLogical` 同时供 `OnAnyMouseDown` 复用，统一 DPI 处理。
- **显隐与防重入**：`OnHookWakeupClick` 开头 `if (IsVisible) return;`（菜单已可见时唤醒无效）；`if (Application.Current.Windows.OfType<ScreenshotWindow>().Any()) return;`（截屏覆盖层开启时不抢唤醒）。通过后才 `PositionAtCursor` + 热重载动作 + `Show()`。`OnAnyMouseDown` 检测点击落在窗口外则 `Hide()`。
- **动作执行**：按钮点击先 `Hide()` 再 `ActionExecutor.Execute`。
- **热重载**：每次唤醒经 `ActionExecutor.GetActions()` → `ActionStore` → `SettingsManager.Load()` 重读磁盘动作列表。`Menu` 视觉参数由 `ApplyMenuSettings` 即时刷新（构造时与设置页"应用设置"后共用此路径），编辑 `settings.json` 无需重启。
- **设置入口**：齿轮按钮 `Hide()` 后回调 `OpenSettingsAction`（由 `App` 接到 `SettingsWindow`）。

### 4.3 Action Execution Engine (`ActionExecutor`)
- 动作列表经 `ActionStore` 从 `settings.json` 加载。
- `sys:` 前缀为内部协议指令，由 `Execute` 拦截，不走 `Process.Start`。当前已实现：

  | 指令 | 行为 |
  |------|------|
  | `sys:snipping` | `ScreenshotService.Capture()` 取全屏底图，`new ScreenshotWindow(source, bounds).ShowDialog()` 打开截屏覆盖层 |

- 其余命令按外部进程启动：`ProcessStartInfo { UseShellExecute = true }`。
- **校验与兜底**：`IsNullOrWhiteSpace(item.Command)` 拦空命令；`Process.Start` 入 try-catch 拦 `Win32Exception`（错填命令不闪退）。
- 新增内置功能：在 `Execute` 加 `if (item.Command == "sys:xxx")` 分支，并在 `SettingsModel.Action` 默认动作或文档中登记。

### 4.4 Configuration System (`SettingsManager` / `ActionStore`)
- `SettingsManager`：全局单例（`Instance`），统一配置中心。`Load()` 读 `settings.json`，文件不存在时默认值 + 迁移旧 `appsettings.json`（`JsonDocument` 解析）并落盘；`ReadSettings` 捕获 `JsonException` 时把坏文件 `File.Move` 为 `settings.json.bak` 再回退默认值，其他异常回退默认值。`Save()` **原子写**：先写 `settings.json.tmp` 再 `File.Move(overwrite:true)` 覆盖。同步 IO。`App.OnStartup` 加载；保存由 `SettingsWindow` 应用设置触发（`App.OnExit` 不保存——所有变更经 `ActionStore.Save` 即时落盘）。
- `SettingsModel`：`Action` / `Snipping` / `Menu` / `Pin` 四组，默认值对齐重构前硬编码。
- `ActionStore`：**静态门面**（`internal static class`），封装唤醒键与动作列表读写，全部委托单例，自身不做 IO。供 `App` / `SettingsWindow` / `ActionExecutor` / `MainWindow` 使用。
- **`SettingsWindow` 5 页编辑**：常规（唤醒键）/ 动作管理（DataGrid）/ 截屏 / 菜单 / 贴图，覆盖四组全部字段。"应用设置"时四组写回 `SettingsManager.Instance.Settings` + `Save()`（reattach 全组防被唤醒 `Load` 替换），并调 `MainWindow.ApplyMenuSettings` 即时刷新菜单。颜色字段 hex 文本框 + 实时预览；`Validate` 全字段校验，非法 `MessageBox` 拦截。
- **热重载 / 编辑隔离**：`Load()` 每次重读磁盘；`SettingsWindow` 的未保存编辑持有独立对象引用，不受他处 `Load` 影响。

### 4.5 Circle Gesture Recognition (`GestureHelper`)
- **纯几何纯函数** `IsCircle(List<POINT> recentPoints)`，无状态、无副作用，O(n) 单趟。
- 调用方（`GlobalHookService`）负责维护 ≤800ms 滑动时间窗；本方法只做空间几何判定。
- 判定闸门（防误触）：
  1. 样本数 `< 8` → false。
  2. Bounding Box 宽高均 `≥80px`，宽高比 `0.5~2.0` → 否则 false（排除抖动/长条拖拽）。
  3. **有符号偏转角累加（转向数）**：相邻向量 `atan2` 差归一化到 `[-π,π]` 后求和；`|总和| ≥ 300°` → true。一致方向画圈 ≈ ±360°；直线/折返正负抵消 ≈ 0°。
  4. 向量长度 `<2px` 跳过（过滤亚像素抖动）。

## 5. Execution Directives for LLM
- 一次只实现一个模块。
- 先验证 `StructLayout` 内存对齐再测钩子。
- 任何 P/Invoke 签名不得放进 UI code-behind，一律加到 `NativeMethods.cs`。
- 任何新增 GDI 对象遵循 `Freeze 拷贝 → DeleteObject 释放`。
- 钩子回调内禁止做 IO 或 UI 变化，一律 `Dispatcher.BeginInvoke` 异步；`WM_MOUSEMOVE` 永不拦截。
- 公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节。
- 调试日志用 `System.Diagnostics.Debug.WriteLine`（Release 自动剥离），不得 reintroduce `Console.WriteLine` / `AttachConsole`。

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
- **结算**（`SettleSelection`，松开左键时）：模式 A 智能快照（未拖拽且有红框 → 截红框）/ 模式 B 手动拖拽（截拖拽选区）/ 空点（无红框且未拖拽 → 不操作）。`Math.Clamp` 把裁剪矩形夹取到 base-image 边界 → `CroppedBitmap` 裁剪 → `Freeze` → `Clipboard.SetImage`（try-catch，剪贴板被独占不阻断）→ `new PinWindow(crop, screenX, screenY).Show()` 联动钉图。`OnMouseLeftButtonUp` 用 try/finally 保证 `Close()` 与 `ReleaseMouseCapture`（无论是否抛异常）。
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

## 7. Unified Configuration & Theming
重构后所有"硬编码"按职责分三层，**严禁再散落到 code-behind / XAML 字面量**：
- **JSON（`SettingsModel` / `settings.json`）**：关键视觉与交互参数（`Snipping` / `Menu` / `Pin` 的颜色、尺寸、阈值、阴影模糊半径、旋转步进等）。各窗口构造函数 `InitializeComponent()` 后读 `SettingsManager.Instance.Settings.{组}` 动态赋值给命名控件属性；按钮背景色等 Style 内部值经 `{DynamicResource}` 注入（窗口 `Resources[key] = BrushHelper.ToBrush(...)`）。
- **ThemeStyles.xaml（`StaticResource`）**：纯布局 / 公共样式 + Fluent 扁平控件（`MenuButtonStyle` / `NavRadioButton` / `ActionButton` / `FlatTextBox` / `FlatComboBox` / `ColorSwatch` / `DataGridColumnHeader` / `DataGridRow` / `DataGridCell` / `FieldTitle` / `FieldDesc`）。`NavRadioButton`：选中浅蓝底 + Accent 指示条 + 文字变 Accent，hover 0.15s 淡入。由 `App.xaml` 合并。**不写入 JSON**。
- **保留内联**：窗口独有视觉物理反馈（如 `PinWindow` 阴影 Depth/Opacity/Direction/Color、`PinBorderThickness=2`、不透明度菜单预设、`MainWindow` 方块矩阵的 72×72 / 图标字号等定制布局值）与唯一面板布局约束（如 `SettingsWindow` 750×500），不提取。

> `SettingsWindow` 内容区 `#FAFAFA` 画布，5 页无卡片紧凑表单，行距 12px。`DataGrid` 扁平：透明底、仅横向网格线、列头仅下边框、行 hover/选中浅蓝、单元格去焦点框、编辑态 `FlatTextBox`。颜色字段 hex 文本框 + `ColorSwatch` 实时预览。

> 新增可配置项：关键视觉/交互参数 → 加到 `SettingsModel` 对应组 + 默认值 + code-behind 注入；公共样式 → 加到 `ThemeStyles.xaml`；否则保留内联。

## 8. Robustness
- **全局崩溃兜底**：`App` 注册 `DispatcherUnhandledException`，未捕获异常记 `Debug.WriteLine` 后 `e.Handled = true`，保活常驻托盘进程（StackOverflow/OOM 等不可恢复异常不触发此事件）。
- **越界防御**：`ScreenshotWindow.SettleSelection` 用 `Math.Clamp` 把裁剪矩形夹取到 base-image 边界，防 `CroppedBitmap` 越界抛 `ArgumentException`（寻边窗口可能超出虚拟屏）。
- **剪贴板/进程容错**：`Clipboard.SetImage` try-catch（剪贴板被独占不阻断）；`ActionExecutor` 空命令校验 + `Process.Start` try-catch。
- **资源释放**：`OnMouseLeftButtonUp` try/finally 保证 `Close()` / `ReleaseMouseCapture`。
- **原子配置**：`SettingsManager.Save` tmp+`File.Move` 原子覆盖，防断电/崩溃截断；脏 JSON 备份 `.bak` 后回退默认值，不丢失坏文件。
- **钩子时效**：`HookCallback` 异步派发 UI/IO，<100ms 返回，防 `LowLevelHooksTimeout` 静默摘钩。
