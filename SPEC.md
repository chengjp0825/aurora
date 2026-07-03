# Spec-Driven Development (SDD): Personal Quicker Architecture

## 1. System Requirements & Stack
- Target Framework: .NET 8.0-windows
- UI Framework: WPF (Windows Presentation Foundation)
- Execution Context: Windows 10/11 (Requires specific Windows API calls)

## 2. Project Structure Constraints
Strictly separate native OS calls from UI rendering.
- `/Interop`: Contains all `user32.dll` and `kernel32.dll` P/Invoke signatures.
- `/Services`: Contains the global hook logic and action execution logic.
- `/UI`: Contains WPF XAML and code-behind files.
- `/Models`: Data contracts for `actions.json`.

## 3. Low-Level API (P/Invoke) Specifications
The `Interop/NativeMethods.cs` static class MUST define the following unmanaged APIs exactly:

### 3.1 Hook Definitions
- `SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId)`: Registers a hook procedure. Target `WH_MOUSE_LL` (id: 14).
- `UnhookWindowsHookEx(IntPtr hhk)`: Unregisters the hook. Must be called explicitly on application exit to prevent memory leaks.
- `CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam)`: Passes hook information to the next hook procedure in the current hook chain.

### 3.2 Coordinate & Focus Control
- `GetCursorPos(out POINT lpPoint)`: Retrieves the cursor's position in physical screen coordinates.
- `GetForegroundWindow()`: Returns a handle (`IntPtr`) to the foreground window (the window with which the user is currently working).
- `SetForegroundWindow(IntPtr hWnd)`: Puts the thread that created the specified window into the foreground and activates the window.

### 3.3 Structs and Constants
- Define `WM_MBUTTONDOWN = 0x0207` and `WM_MBUTTONUP = 0x0208`.
- Define `POINT` and `MSLLHOOKSTRUCT` structs with correct `[StructLayout(LayoutKind.Sequential)]` attributes to map memory exactly.

## 4. Module Physical Logic

### 4.1 Global Mouse Hook Service
- **Initialization:** Obtain the current module handle via `Process.GetCurrentProcess().MainModule.ModuleName` and pass it to `SetWindowsHookEx`.
- **Interception Logic:** Inside the `LowLevelMouseProc` callback:
  1. Check if `wParam` equals `WM_MBUTTONDOWN`.
  2. If matched, trigger a .NET event with the current `POINT` coordinates.
  3. Return `(IntPtr)1` to block the middle-click message from being processed by the OS or other applications.
  4. If not matched, call and return `CallNextHookEx`.
- **Thread Constraints:** The hook MUST be established on a thread that pumps messages (the main WPF UI thread).

### 4.2 Frameless UI Module (MainWindow)
- **Window Properties:**
  - `WindowStyle="None"`
  - `AllowsTransparency="True"`
  - `Background="Transparent"`
  - `Topmost="True"`
  - `ShowInTaskbar="False"`
- **Positioning Logic:**
  - When the hook event fires, the window receives absolute screen coordinates (Physical Pixels).
  - Calculate DPI scaling factor using `PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice`.
  - Convert absolute coordinates to logical coordinates.
  - Set `Window.Left` and `Window.Top` so the center of the window aligns with the cursor.
- **Focus Cycle:**
  - Set `Visibility = Visibility.Visible` and call `this.Activate()`.
  - Override `OnDeactivated`. When the window loses focus, set `Visibility = Visibility.Hidden` immediately.

### 4.3 Action Execution Engine
- Parse `actions.json` on startup.
- Execute actions via `System.Diagnostics.Process.Start(new ProcessStartInfo { ... })`.
- When an action is triggered, immediately hide the UI window, restore focus to the previously active window handle (captured via `GetForegroundWindow` before the UI was shown), and then execute the action.

## 5. Execution Directives for LLM
- Implement one module at a time.
- Verify memory structure alignment (`StructLayout`) before testing hooks.
- Never place P/Invoke signatures inside UI code-behind files.