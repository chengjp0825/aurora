# CLAUDE.md

MyQuicker —— 基于 WPF 的个人快捷启动器。鼠标唤醒手势（中键 / 侧键 / 纯轨迹画圈）触发无框菜单，支持自定义动作与原生截屏。
Spec 驱动开发（SDD），权威规范见 `SPEC.md`，本文件补充实现层面的架构约束与现状。

## 技术栈
- .NET 8.0-windows / WPF（`UseWPF`）+ WinForms（`UseWindowsForms`，仅用于 `Screen` / `NotifyIcon`）
- P/Invoke 调用 `user32.dll` / `kernel32.dll` / `gdi32.dll` / `dwmapi.dll`
- 配置持久化：`settings.json`（由 `SettingsManager` 单例运行时生成/读写，源码不纳管）；首次启动自动从旧 `appsettings.json` 迁移唤醒键与动作列表

## 项目架构规范（严格分层）
三层职责分离，原生调用与 UI 渲染必须隔离（SPEC §2 / §5）。

- **`/Interop`** —— 底层 API。`NativeMethods.cs` 集中存放全部 P/Invoke 签名、常量、`[StructLayout]` 结构体。
  **禁止把 P/Invoke 写进 UI code-behind。** 新增原生 API 一律加到此文件并附 XML 注释。
- **`/Services`** —— 核心逻辑，无 UI 依赖。
  - `GlobalHookService` —— 全局低级鼠标钩子（`WH_MOUSE_LL`）。回调内仅同步"吞键"，UI 变化/磁盘 IO 一律 `Dispatcher.BeginInvoke` 异步；`WM_MOUSEMOVE` 旁观分支做画圈识别（800ms 滑动窗口）。
  - `GestureHelper` —— **纯几何纯函数**：转向数法判定画圈（bbox≥80 + 宽高比 0.5~2.0 + \|有符号偏转角累加\|≥300°），无状态、无副作用。
  - `ScreenshotService` —— 多屏截图采集 + GDI 回收。
  - `ActionExecutor` —— 动作分发，含 `sys:` 协议路由；空命令校验 + `Process.Start` try/catch。
  - `SettingsManager` —— **全局单例**（`Instance`），统一配置中心：读写 `SettingsModel` 到 `settings.json`，**原子写**（tmp + `File.Move(overwrite:true)`）+ 脏 JSON 备份 `.bak`，首次加载用 `JsonDocument` 从旧 `appsettings.json` 迁移。同步 IO。`App.OnStartup` 加载；保存由 `SettingsWindow` 应用设置触发（`App.OnExit` 不保存）。
  - `ActionStore` —— **静态门面**（`internal static class`）：封装唤醒键与动作列表读写，全部委托 `SettingsManager.Instance`，自身不做 IO。供 `App` / `SettingsWindow` / `ActionExecutor` / `MainWindow` 使用。
- **`/UI`** —— WPF 视图（XAML + code-behind）。
  - `MainWindow` —— 唤醒弹出的无框菜单（方块矩阵布局：`WrapPanel` + 72×72 图标方块 + 底部工具区）；`ApplyMenuSettings` 供设置页即时刷新；`ToLogical(POINT)` 统一物理→逻辑坐标。
  - `SettingsWindow` —— Fluent 风格侧边栏设置中心，5 页：常规 / 动作管理 / 截屏 / 菜单 / 贴图。
  - `ScreenshotWindow` —— 全屏截屏覆盖层（智能寻边 + 拖拽选区）。
  - `PinWindow` —— 贴图常驻窗口（拖拽/双击关闭/旋转/镜像/边框/透明度/右键菜单；`ResizeMode=CanResize` 但图片 `Stretch=None` 不随窗口缩放，"重置大小"仅重算窗口外接矩形）。
  - `BrushHelper` —— JSON 颜色串（`"#AARRGGBB"` / 命名色）转 WPF `Brush` 的静态转换器，供各窗口从 `SettingsModel` 赋值时复用。
- **`/Resources`** —— `ThemeStyles.xaml` 公共主题资源字典：主题画刷 / 尺寸 + `MenuButtonStyle` / `NavRadioButton`（Fluent：浅蓝选中底 + Accent 指示条 + hover 淡入）/ `ActionButton` / `FlatTextBox` / `FlatComboBox` / `ColorSwatch` / `DataGridColumnHeader` / `DataGridRow` / `DataGridCell` / `FieldTitle` / `FieldDesc`。由 `App.xaml` 合并，各窗口以 `StaticResource` 引用。**不写入 JSON**。
- **`/Models`** —— 数据契约。`ActionItem`（实现 `INotifyPropertyChanged` 供 DataGrid 双向绑定）、`SettingsModel`（多层级 POCO：`ActionSettings`（含 `public const int WAKEUP_CIRCLE_GESTURE = -1;`）/ `SnippingSettings` / `MenuSettings` / `PinSettings`，默认值对齐重构前硬编码）。

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
- `MainWindow` 定位用 `ToLogical(POINT)`（封装 `PresentationSource.CompositionTarget.TransformFromDevice`）把物理坐标转逻辑坐标后再居中。
> 不要用 `SystemParameters.PrimaryScreenWidth` 或单屏 `Bounds` 做跨屏计算。
> **DPI 注意**：`ScreenshotWindow` / `PinWindow` 直接用物理像素设 `Left/Top/Width/Height`，依赖 96 DPI（100% 缩放）下 DIP 与物理像素 1:1 的假设；非 100% 缩放下覆盖层/贴图定位会偏移。`MainWindow` 走 `TransformFromDevice` 不受影响。

### 3. 全局钩子线程与委托存活
- 钩子必须在消息泵线程（主 UI 线程）上安装；`GlobalHookService.Start()` 由 `App.OnStartup` 调用。
- 钩子委托 **必须**保存在字段（`_hookProc`）中防止 GC 回收，否则 user32 回调野指针会崩进程。
- 退出时 `App.OnExit` 必须显式 `Stop()` + `Dispose()` 卸载钩子。
- **回调内禁止做 IO 或 UI 变化**：`HookCallback` 仅同步执行"吞键"（`return (IntPtr)1`）；`OnAnyMouseDown` / `OnWakeupClick` 一律 `Dispatcher.BeginInvoke` 异步派发，保证 <100ms 返回，规避 `LowLevelHooksTimeout` 静默摘钩。
- **`WM_MOUSEMOVE` 永不拦截**：画圈识别为旁观模式，始终 `return CallNextHookEx`；仅在 `WakeupMessage == WAKEUP_CIRCLE_GESTURE` 时记账（`_moveHistory` 800ms 滑窗 + `_pointsBuffer` 复用缓冲），命中即清空队列并异步 `OnWakeupClick`。

### 4. 无框菜单不抢焦点
`MainWindow` 在 `OnSourceInitialized` 中给 HWND 加 `WS_EX_NOACTIVATE`，确保弹出与点击均不夺取当前应用焦点。菜单全程不抢焦点，原前台窗口始终持焦，故动作执行前只需 `Hide()`，无需显式恢复前台窗口。

### 5. 唤醒手势防重入
`MainWindow.OnHookWakeupClick` 开头两道闸（任一命中即 `return`，不弹菜单）：
1. `if (IsVisible) return;` —— 菜单已可见时，后续唤醒动作（画圈/按键）一律无效（不重弹、不关闭）；关闭靠点外面或点动作。
2. `if (Application.Current.Windows.OfType<ScreenshotWindow>().Any()) return;` —— 截屏覆盖层开启时不抢唤醒，避免截图选区时画圈/按键误触菜单。

### 6. 崩溃兜底与原子配置
- `App` 注册 `DispatcherUnhandledException`：未捕获异常记 `Debug.WriteLine` 后 `e.Handled = true`，保活常驻托盘进程。
- `ScreenshotWindow.SettleSelection`：`Math.Clamp` 把裁剪矩形夹取到 base-image 边界（防 `CroppedBitmap` 越界抛 `ArgumentException`）；`Clipboard.SetImage` 入 try-catch（剪贴板被独占不阻断）；`OnMouseLeftButtonUp` 用 try/finally 保证 `Close()` 与 `ReleaseMouseCapture`。
- `ActionExecutor.Execute`：`IsNullOrWhiteSpace(Command)` 拦空命令；`Process.Start` 入 try-catch 拦 `Win32Exception`。
- `SettingsManager.Save`：先写 `settings.json.tmp` 再 `File.Move(overwrite:true)` 原子覆盖；`ReadSettings` 捕获 `JsonException` 时把坏文件 `File.Move` 为 `settings.json.bak` 再回退默认值。

### 7. 统一配置系统：JSON / ThemeStyles / 内联三层
重构后所有"硬编码"按职责分三层，**严禁再散落到 code-behind / XAML 字面量**：
- **JSON（`SettingsModel`，`settings.json`）**：关键视觉与交互参数（`SnippingSettings` / `MenuSettings` / `PinSettings` 的颜色、尺寸、阈值、阴影模糊半径、旋转步进等）。各窗口构造函数 `InitializeComponent()` 后读 `SettingsManager.Instance.Settings.{组}` 动态赋值给命名控件属性；按钮背景色等 Style 内部值经 `{DynamicResource}` 注入（窗口 `Resources[key] = BrushHelper.ToBrush(...)`）。
- **ThemeStyles.xaml（`StaticResource`）**：纯布局 / 公共样式（主题画刷、字号、边距、圆角、`MenuButtonStyle` / `NavRadioButton` / `ActionButton` / `FlatTextBox` / `FlatComboBox` / `ColorSwatch` / DataGrid 系列 / `FieldTitle` / `FieldDesc`）。由 `App.xaml` 合并。**不写入 JSON**。
- **保留内联**：窗口独有视觉物理反馈（如 `PinWindow` 阴影 Depth/Opacity/Direction/Color、`PinBorderThickness=2`、不透明度菜单预设）与唯一面板布局约束（如 `SettingsWindow` 750×500），不提取。

> 新增可配置项：关键视觉/交互参数 → 加到 `SettingsModel` 对应组 + 默认值 + code-behind 注入；公共样式 → 加到 `ThemeStyles.xaml`；否则保留内联。
> `SettingsWindow` 5 页编辑全部四组字段；"应用设置"时四组写回 `SettingsManager` 落盘，并调 `MainWindow.ApplyMenuSettings` 即时刷新菜单（`Menu` 组无需重启）。`Snipping`/`Pin` 分别在下次截图/钉图时生效；`Action` 每次唤醒热重载。`SettingsManager.Load()` 每次重读磁盘，保留唤醒热重载与 `SettingsWindow` 编辑隔离。

## UI 风格规范

### SettingsWindow（Fluent，5 页）
- 内容区 `#FAFAFA` 画布；左侧边栏 `NavRadioButton`（Fluent：选中浅蓝底 `NavSelectedBrush` + 3px Accent 指示条 + 文字变 Accent；hover 0.15s 淡入）。
- 5 个页签：常规（唤醒键 `FlatComboBox`）/ 动作管理（扁平 `DataGrid`）/ 截屏 / 菜单 / 贴图。页面切换由 `BooleanToVisibilityConverter` 绑定 `RadioButton.IsChecked`，无代码后置。
- 表单：左标题+说明（`FieldTitle` / `FieldDesc`）、右控件（`FlatTextBox` / `FlatComboBox` + `ColorSwatch` 圆角色块），行距 12px，无卡片。
- `DataGrid` 扁平：透明底、仅横向网格线、列头仅下边框、行 hover `RowHoverBrush`/选中 `NavSelectedBrush`、单元格去焦点虚线框、编辑态 `EditingElementStyle=FlatTextBox`。
- 颜色字段：十六进制文本框 + 实时预览色块（`WireColorPreview` 在 `TextChanged` 刷色，无效值清空预览）；应用前 `Validate` 全字段校验，非法 `MessageBox` 拦截。
- 唤醒键下拉框 3 项：鼠标中键 / 侧键后退 (XButton1) / 单纯画圈 (无按键，对应 `WAKEUP_CIRCLE_GESTURE = -1`)；`ToIndex`/`SaveButton_Click` 适配 -1。
- 置顶穿透小技巧：`OpenSettings` 中 `Topmost=true` 后立即 `false`，绕过系统前台锁。
> 新增设置页：加一个 `RadioButton` + 对应内容区，沿用 `NavRadioButton` / `FlatTextBox` 等样式。

### ScreenshotWindow 寻边红框绘制逻辑（`ScreenshotWindow.xaml(.cs)`）
> 覆盖层颜色/厚度由 `SettingsModel.Snipping` 注入（构造函数读 `SettingsManager.Instance.Settings.Snipping`，赋值 `MaskPath.Fill` / `HighlightBorder.BorderBrush` / `BorderThickness` / 窗口 `Background`）；`DragThreshold` 取自 `SnippingSettings.DragThreshold`（readonly 字段，双模态状态机逻辑不变）。下述为默认值。
覆盖层分三层（`RootGrid`）：
1. `BackgroundImage` —— 全屏底图（`Stretch="None"`）；
2. 暗罩 `Path`（`MaskPath`）—— `MaskColor=#66000000`，用 `CombinedGeometry(Exclude)` 在 `ScreenGeometry`（整屏）中挖出 `CutoutGeometry`（选区）形成镂空；
3. `HighlightBorder` —— 选区红框（`BorderColor=#FF0000` / `BorderThickness=2`），默认 `Hidden`。

寻边模式（未拖拽时，`OnMouseMove`）：
1. `WindowUnderCursor` 临时给自身 HWND 加 `WS_EX_TRANSPARENT`（使 `WindowFromPoint` 穿透覆盖层），取到光标下窗口后**立即还原** ex style；
2. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 取真实物理窗口矩形（Win11 阴影修正），失败回退 `GetWindowRect`；
3. 转窗口局部坐标后 `ApplySelection`：同时设置 `CutoutGeometry.Rect`（镂空）与 `HighlightBorder` 的 `Margin`/`Width`/`Height`（红框）。

拖拽模式（左键按下后）：用 `min/abs` 归一化起止点矩形，支持反向拖拽。松开时 `SettleSelection`：`Math.Clamp` 夹取到 base-image 边界 → `CroppedBitmap` 裁剪 → `Freeze` → `Clipboard.SetImage`（try-catch）→ `new PinWindow(crop, screenX, screenY).Show()` 联动钉图（截图罩关闭后贴图存活）。`OnMouseLeftButtonUp` 用 try/finally 保证 `Close()`。
ESC / 右键取消关闭。

## 指令协议（SDD）：`sys:` 前缀
`ActionItem.Command` 以 `sys:` 开头者为**内部协议指令**，由 `ActionExecutor.Execute` 拦截，不走 `Process.Start`。当前已实现：

| 指令 | 行为 |
|------|------|
| `sys:snipping` | 调 `ScreenshotService.Capture()` 取全屏底图，`new ScreenshotWindow(source, bounds).ShowDialog()` 打开截屏覆盖层 |

> 新增内置功能：在 `ActionExecutor.Execute` 中加 `if (item.Command == "sys:xxx")` 分支，并在 `SettingsModel.Action` 默认动作（`SettingsManager` 首次生成 `settings.json` 时写入）或文档中登记。`sys:` 之外的命令一律按外部进程启动（`UseShellExecute=true`）。

## 当前进度
- ✅ **8A/8B/8C + 统一配置重构**：多屏截图（`ScreenshotService`，GDI 回收 + VirtualBounds）/ 智能寻边 + 双模态（`ScreenshotWindow`）/ 贴图引擎（`PinWindow`）/ `SettingsManager` 单例 + `SettingsModel` 四组 + 旧 `appsettings.json` 迁移 / `ThemeStyles.xaml` 集中公共样式。
- ✅ **健壮性加固**：`App.DispatcherUnhandledException` 兜底；`SettleSelection` 越界夹取 + 剪贴板 try-catch + finally `Close`；`ActionExecutor` 空命令校验 + `Process.Start` try-catch；`SettingsManager` 原子写 + `.bak` 备份；`GlobalHookService` 回调异步化。
- ✅ **技术债清理**：`Console.WriteLine` → `Debug.WriteLine`（Release 自动剥离）+ 移除 `AttachConsole`；删除死代码（`GetForegroundWindow` / `SetForegroundWindow` / `WM_MBUTTONUP` / `AttachConsole`）；`ActionStore` 改 static；`MainWindow` 提取 `ToLogical`；`SettingsWindow` `#333`/`#888` 入 ThemeStyles。
- ✅ **设置中心 5 页**：补全 截屏/菜单/贴图 全部 17 字段，颜色 hex 文本框 + 实时预览，应用即刷新菜单（`ApplyMenuSettings`）。
- ✅ **Fluent UI**：`FlatTextBox` / `FlatComboBox` / `ColorSwatch` / 扁平 `DataGrid` / `NavRadioButton` 升级；去卡片紧凑表单 + `#FAFAFA` 画布。
- ✅ **纯轨迹画圈唤醒**：`GestureHelper` 转向数法 + `GlobalHookService` 800ms 滑窗旁观 `WM_MOUSEMOVE`，`WAKEUP_CIRCLE_GESTURE = -1`。
- ✅ **唤醒菜单方块矩阵布局**：`WrapPanel` + 72×72 图标方块 + 底部工具区。
- ✅ **唤醒防重入**：菜单可见时唤醒无效；截屏覆盖层开启时不触发唤醒。

## 开发约定
- 一次只实现一个模块（SPEC §5）；先验证 `StructLayout` 内存对齐再测钩子。
- 配置热重载：`MainWindow` 每次唤醒都经 `ActionStore` → `SettingsManager.Instance.Load()` 从磁盘重新加载动作列表；`Menu` 组经 `ApplyMenuSettings` 即时刷新，编辑 `settings.json` 无需重启。
- 注释风格：公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节/步骤（如 `Per SPEC §4.1`）。
- 调试日志：用 `System.Diagnostics.Debug.WriteLine`（`[Conditional("DEBUG")]`，Debug 保留、Release 自动剥离）。**不要再加 `Console.WriteLine` 或 `AttachConsole`**（已全部移除）。

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
