using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;
using MyQuicker.Services;
using Clipboard = System.Windows.Clipboard;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;
using ShapePath = System.Windows.Shapes.Path;
using TextBox = System.Windows.Controls.TextBox;

namespace MyQuicker.UI;

/// <summary>
/// 贴图常驻窗口：把一张截图钉在桌面上，可拖拽、缩放、旋转、镜像、调透明度，
/// 并提供右键菜单与基础批注（画框/画圆/箭头/文字）。左键双击关闭。Per SPEC 8C (PinEngine).
/// 批注状态机与光栅化导出见 docs/02-interaction-engine.md §8。
/// </summary>
public partial class PinWindow : Window
{
    private readonly BitmapSource _source;

    // 旋转累计角度（每次 +90°）与水平镜像开关
    private int _rotationStep;
    private bool _mirrored;

    // 每次旋转的步进角度（度），固定 90°（原 PinSettings.RotationStepDegrees 已移除：
    // 非 90° 会破坏 90/270 宽高互换逻辑）
    private const double _rotationStepDegrees = 90;

    // 原始物理像素尺寸（重置大小用）
    private readonly double _naturalWidth;
    private readonly double _naturalHeight;

    /// <summary>贴图内容左上角目标屏幕物理坐标（截图结算传入，ReapplyMetrics 基位）。</summary>
    private readonly double _screenX;
    private readonly double _screenY;

    /// <summary>DIP↔物理像素 缩放系数。初始为 ScreenshotWindow 传入值，SourceInitialized/DpiChanged 用 TransformToDevice 修正为窗口实际渲染 DPI（docs/02 §5）。</summary>
    private double _scaleX;
    private double _scaleY;

    /// <summary>当前旋转角度（0/90/180/270）。步进固定 90°。</summary>
    private double RotationAngle => (_rotationStep % 4) * _rotationStepDegrees;

    // ----- 批注状态机（docs/02 §8.1）-----
    private enum EditMode { None, Rect, Circle, Arrow, Text }
    private EditMode _editMode = EditMode.None;

    /// <summary>批注模式总开关（右键「批注 ▸ 批注模式」）。关闭时工具栏不存在、Canvas 击穿。</summary>
    private bool _annotationModeEnabled;

    /// <summary>当前批注颜色画刷，由工具栏颜色预设切换。</summary>
    private Brush _currentBrush = Brushes.Red;

    /// <summary>画笔粗细（px），由工具栏粗细预设切换；作用于框/圆/箭头描边。</summary>
    private double _strokeThickness = 2;

    // 拖拽绘制：起点 + 当前临时形状（Rectangle / Ellipse / ShapePath）
    private Point _dragStart;
    private Point _dragStartNatural;
    private Shape? _activeShape;

    /// <summary>箭头头部边长（px）：max(8, 粗细×3)。</summary>
    private double ArrowHeadLen => Math.Max(8, _strokeThickness * 3);

    /// <summary>文本批注自然字体大小（物理像素），Arrange 时按当前 scale 转 DIP。</summary>
    private const double TextFontSizeNatural = 14;

    // ----- 工具栏 Hover 延迟淡出 -----
    private readonly DispatcherTimer _toolbarHideTimer;

    // ----- DPI / HWND -----
    private HwndSource? _hwndSource;
    private bool _suppressSizeChanged;

    // ----- 撤销 / 重做 -----
    private readonly Stack<IAnnotationEdit> _undoStack = new();
    private readonly Stack<IAnnotationEdit> _redoStack = new();

    /// <summary>批注在自然图像坐标系中的记录，挂到每个 Shape/TextBlock/TextBox 的 Tag。</summary>
    private sealed class AnnotationRecord
    {
        public EditMode Kind { get; set; }
        public Rect NaturalRect { get; set; }
        public Point NaturalStart { get; set; }
        public Point NaturalEnd { get; set; }
        public Point NaturalPosition { get; set; }
        public string? Text { get; set; }
        public double NaturalStrokeThickness { get; set; }
        public double NaturalFontSize { get; set; }
        public Brush? Brush { get; set; }
    }

    /// <summary>撤销/重做编辑命令。</summary>
    private interface IAnnotationEdit
    {
        void Do();
        void Undo();
    }

    private sealed class AddAnnotationEdit : IAnnotationEdit
    {
        private readonly Canvas _canvas;
        private readonly UIElement _element;
        private readonly int _index;

        public AddAnnotationEdit(Canvas canvas, UIElement element, int index = -1)
        {
            _canvas = canvas;
            _element = element;
            _index = index;
        }

        public void Do()
        {
            if (_canvas.Children.Contains(_element)) return;
            if (_index >= 0 && _index <= _canvas.Children.Count)
                _canvas.Children.Insert(_index, _element);
            else
                _canvas.Children.Add(_element);
        }

        public void Undo()
        {
            _canvas.Children.Remove(_element);
        }
    }

    private sealed class RemoveAnnotationEdit : IAnnotationEdit
    {
        private readonly Canvas _canvas;
        private readonly UIElement _element;
        private readonly int _index;

        public RemoveAnnotationEdit(Canvas canvas, UIElement element)
        {
            _canvas = canvas;
            _element = element;
            _index = _canvas.Children.IndexOf(element);
        }

        public void Do()
        {
            _canvas.Children.Remove(_element);
        }

        public void Undo()
        {
            if (_canvas.Children.Contains(_element)) return;
            if (_index >= 0 && _index <= _canvas.Children.Count)
                _canvas.Children.Insert(_index, _element);
            else
                _canvas.Children.Add(_element);
        }
    }

    private sealed class ClearAnnotationsEdit : IAnnotationEdit
    {
        private readonly Canvas _canvas;
        private readonly List<UIElement> _children;

        public ClearAnnotationsEdit(Canvas canvas, IEnumerable<UIElement> children)
        {
            _canvas = canvas;
            _children = children.ToList();
        }

        public void Do()
        {
            _canvas.Children.Clear();
        }

        public void Undo()
        {
            _canvas.Children.Clear();
            foreach (var child in _children)
                _canvas.Children.Add(child);
        }
    }

    /// <param name="screenX">贴图左上角目标屏幕横坐标（物理像素，来自截图结算）。</param>
    /// <param name="screenY">贴图左上角目标屏幕纵坐标（物理像素，来自截图结算）。</param>
    /// <param name="scaleX">DIP↔物理像素 X 系数（截图所在显示器 DPI，保证贴图 1:1）。</param>
    /// <param name="scaleY">DIP↔物理像素 Y 系数。</param>
    /// <param name="pinSettings">贴图视觉参数配置。</param>
    public PinWindow(BitmapSource source, double screenX, double screenY, double scaleX, double scaleY, PinSettings pinSettings)
    {
        if (pinSettings is null)
            throw new ArgumentNullException(nameof(pinSettings));

        InitializeComponent();

        _screenX = screenX;
        _screenY = screenY;
        _scaleX = scaleX;
        _scaleY = scaleY;

        // 关键视觉参数从统一配置注入（Per SPEC 重构 Step 3）。
        // 最小宽高（40×40）、阴影模糊半径（14）、旋转步进（90°）已硬编码，不再可配。
        MinWidth = 40;
        MinHeight = 40;
        PinBorder.BorderBrush = BrushHelper.ToBrush(pinSettings.BorderColor);
        ShadowEffect.BlurRadius = 14;
        Opacity = pinSettings.DefaultOpacity;
        SyncOpacityMenu(pinSettings.DefaultOpacity); // 不透明度菜单勾选与 DefaultOpacity 同步

        // 默认置顶 / 默认阴影（Per SPEC 8C）：覆盖 XAML 的 True 默认与菜单勾选状态。
        Topmost = pinSettings.DefaultTopmost;
        TopmostMenuItem.IsChecked = pinSettings.DefaultTopmost;
        ShadowMenuItem.IsChecked = pinSettings.DefaultShowShadow;
        PinImage.Effect = pinSettings.DefaultShowShadow ? ShadowEffect : null;

        _source = source;
        _naturalWidth = source.PixelWidth;
        _naturalHeight = source.PixelHeight;

        PinImage.Source = source;

        // 用传入 scale 初算 PinImage 尺寸/定位；SourceInitialized 再用窗口实际渲染 DPI 修正。
        ReapplyMetrics();

        // 默认外观（docs/03 §6）：默认显示边界 + 默认批注模式，均可配置。
        _annotationModeEnabled = pinSettings.DefaultAnnotationMode;
        AnnotationModeItem.IsChecked = _annotationModeEnabled;
        if (pinSettings.DefaultShowBorder)
        {
            BorderMenuItem.IsChecked = true;
            ApplyBorder(2);
        }
        ApplyAnnotationState();

        _toolbarHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _toolbarHideTimer.Tick += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            FadeOutToolbar();
        };

        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>HWND 创建后：物理坐标强制定位 + 用确定性窗口 DPI 重算尺寸，订阅 DPI 变化。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // SetWindowPos 以物理坐标（贴图内容左上角）强制 HWND 落在目标显示器，触发该屏 DPI 赋值。
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, (int)_screenX, (int)_screenY, 0, 0,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        ReapplyMetrics(hwnd);
    }

    /// <summary>处理原生 WM_DPICHANGED，用系统建议矩形直接定位/定尺寸，避免 WPF 自动缩放冲突。</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            var rc = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

            uint newDpi = (ushort)wParam.ToInt32();
            _scaleX = _scaleY = newDpi / 96.0;
            ApplyMetricsCore(preservePosition: true);

            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>用构造时传入的 scale 初算尺寸（HWND 尚未创建）。</summary>
    private void ReapplyMetrics()
    {
        ApplyMetricsCore();
    }

    /// <summary>
    /// 用确定性窗口 DPI（<see cref="DpiHelper.ScaleForWindow"/>）重算 PinImage 尺寸、Left/Top、窗口外接尺寸。
    /// 裁剪为物理像素，PinImage 用 actualScale 定尺寸 + Stretch=Fill → 渲染 ×actualScale = 物理像素 1:1，
    /// 与框选物理尺寸一致（无论窗口被赋何 DPI）。
    /// </summary>
    private void ReapplyMetrics(IntPtr hwnd)
    {
        var (sx, sy) = DpiHelper.ScaleForWindow(hwnd);
        _scaleX = sx;
        _scaleY = sy;

        // 兜底：API 不可用或返回值异常时回退到实际渲染 DPI。
        if (_scaleX <= 0 || _scaleY <= 0)
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is not null)
            {
                var m = src.CompositionTarget.TransformToDevice;
                _scaleX = m.M11;
                _scaleY = m.M22;
            }
        }

        ApplyMetricsCore();
    }

    private void ApplyMetricsCore(bool preservePosition = false)
    {
        double bx = PinBorder.BorderThickness.Left; // 均匀边框（DIP）
        PinImage.Width = _naturalWidth / _scaleX;
        PinImage.Height = _naturalHeight / _scaleY;

        // AnnotationCanvas 与 PinImage 同尺寸、同变换，批注才能随图片 1:1 对齐。
        AnnotationCanvas.Width = PinImage.Width;
        AnnotationCanvas.Height = PinImage.Height;

        if (!preservePosition)
        {
            // 内容左上角对齐 (screenX, screenY)；边框向外生长，窗口左上角反向偏移 bx。
            Left = _screenX / _scaleX - bx;
            Top = _screenY / _scaleY - bx;
        }

        ApplyTransform(); // 旋转/镜像 + ApplyWindowSize（按 _scaleX/Y 重算窗口外接尺寸）
        ArrangeAnnotations(); // 按新 scale 重映射已有批注

        Debug.WriteLine($"DEBUG: PinWindow ReapplyMetrics renderScale=({_scaleX:F3},{_scaleY:F3}) natural={_naturalWidth}x{_naturalHeight} screen=({_screenX},{_screenY}) border={bx}");
    }

    // -----------------------------------------------------------------------
    // 自然坐标 ↔ DIP 坐标转换
    // -----------------------------------------------------------------------

    private Point ToNaturalPoint(Point canvas) => new(canvas.X * _scaleX, canvas.Y * _scaleY);
    private Point ToCanvasPoint(Point natural) => new(natural.X / _scaleX, natural.Y / _scaleY);
    private double ToNaturalLengthX(double len) => len * _scaleX;
    private double ToNaturalLengthY(double len) => len * _scaleY;
    private double ToCanvasLengthX(double len) => len / _scaleX;
    private double ToCanvasLengthY(double len) => len / _scaleY;

    // -----------------------------------------------------------------------
    // 拖拽与双击关闭
    // -----------------------------------------------------------------------

    /// <summary>
    /// 左键按下：双击关闭，否则交给 WPF 原生 <see cref="Window.DragMove"/>。
    /// 批注模式（Rect/Circle/Arrow/Text）下 Canvas 接管命中并 e.Handled，本回调不触发。
    /// 批注模式开启但为指针工具时，双击不关闭窗口。
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            if (_annotationModeEnabled)
            {
                e.Handled = true;
                return;
            }
            Close();
            return;
        }

        DragMove();
    }

    // -----------------------------------------------------------------------
    // 窗口手动缩放：保持宽高比
    // -----------------------------------------------------------------------

    /// <summary>用户拖拽窗口边框时，根据客户区大小反推缩放并保持宽高比。</summary>
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_suppressSizeChanged || (!e.WidthChanged && !e.HeightChanged)) return;

        double border = PinBorder.BorderThickness.Left;
        double contentW = Math.Max(1, Width - 2 * border);
        double contentH = Math.Max(1, Height - 2 * border);

        bool swapped = (_rotationStep % 2) == 1;
        double natW = swapped ? _naturalHeight : _naturalWidth;
        double natH = swapped ? _naturalWidth : _naturalHeight;

        // 保持宽高比：按能填满客户区的一边计算新缩放。
        double scaleFromW = natW / contentW;
        double scaleFromH = natH / contentH;
        double newScale = Math.Max(scaleFromW, scaleFromH);

        if (newScale > 0)
        {
            _scaleX = _scaleY = newScale;
            _suppressSizeChanged = true;
            try
            {
                ApplyMetricsCore(preservePosition: true);
            }
            finally
            {
                _suppressSizeChanged = false;
            }
        }
    }

    // -----------------------------------------------------------------------
    // 工具栏 Hover 淡入 / 淡出（仅批注模式开启时，docs/03 §6）
    // -----------------------------------------------------------------------

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_annotationModeEnabled) return;
        _toolbarHideTimer.Stop();
        FadeInToolbar();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_annotationModeEnabled) return;
        _toolbarHideTimer.Start();
    }

    private void FadeInToolbar()
    {
        AnnotationToolbar.IsHitTestVisible = true;
        AnnotationToolbar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
    }

    private void FadeOutToolbar()
    {
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        anim.Completed += (_, _) =>
        {
            if (AnnotationToolbar.Opacity <= 0.01)
                AnnotationToolbar.IsHitTestVisible = false;
        };
        AnnotationToolbar.BeginAnimation(OpacityProperty, anim);
    }

    private void ForceHideToolbar()
    {
        AnnotationToolbar.IsHitTestVisible = false;
        AnnotationToolbar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.Zero));
    }

    // -----------------------------------------------------------------------
    // 批注工具栏：模式 / 粗细 / 颜色切换
    // -----------------------------------------------------------------------

    /// <summary>工具切换：按 Tag 解析 EditMode，并按批注模式开关 + 模式决定 Canvas 命中。</summary>
    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s && Enum.TryParse<EditMode>(s, out var m))
        {
            _editMode = m;
            ApplyAnnotationState();
        }
    }

    /// <summary>画笔粗细切换：Tag 为数字串（2/4/6）。</summary>
    private void PenSize_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s
            && double.TryParse(s, CultureInfo.InvariantCulture, out double v))
            _strokeThickness = v;
    }

    /// <summary>颜色切换：Tag 为 hex 串，经 BrushHelper 转 Brush。</summary>
    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s)
            _currentBrush = BrushHelper.ToBrush(s);
    }

    /// <summary>Canvas 命中可见性 = 批注模式开启 且 当前工具非 None。</summary>
    private void ApplyAnnotationState()
    {
        AnnotationCanvas.IsHitTestVisible = _annotationModeEnabled && _editMode != EditMode.None;
    }

    // -----------------------------------------------------------------------
    // 右键「批注」子菜单
    // -----------------------------------------------------------------------

    /// <summary>批注模式开关：切换工具栏存在性与 Canvas 命中。</summary>
    private void AnnotationMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            _annotationModeEnabled = mi.IsChecked;
            if (!_annotationModeEnabled)
            {
                _editMode = EditMode.None;
                ToolPointer.IsChecked = true; // 回到指针，触发 Tool_Checked→ApplyAnnotationState
                ForceHideToolbar();
            }
            else
            {
                ApplyAnnotationState();
                FadeInToolbar();
            }
        }
    }

    /// <summary>清除 Canvas 上所有批注（需确认）。</summary>
    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        if (AnnotationCanvas.Children.Count == 0) return;

        var result = System.Windows.MessageBox.Show(
            "确定要清除所有批注吗？此操作不可恢复。",
            "清除批注",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var children = AnnotationCanvas.Children.Cast<UIElement>().ToList();
            ExecuteEdit(new ClearAnnotationsEdit(AnnotationCanvas, children));
            AnnotationCanvas.Children.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // 批注状态机：Canvas 鼠标事件（docs/02 §8.1）
    // -----------------------------------------------------------------------

    /// <summary>Canvas 左键按下：Rect/Circle 建临时 Rectangle/Ellipse，Arrow 建临时 Path，Text 生成 TextBox。</summary>
    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(AnnotationCanvas);
        _dragStartNatural = ToNaturalPoint(_dragStart);

        if (_editMode == EditMode.Rect || _editMode == EditMode.Circle)
        {
            Shape sh = _editMode == EditMode.Rect
                ? new Rectangle { Stroke = _currentBrush, StrokeThickness = _strokeThickness, Fill = Brushes.Transparent, Stretch = Stretch.Fill }
                : new Ellipse { Stroke = _currentBrush, StrokeThickness = _strokeThickness, Fill = Brushes.Transparent, Stretch = Stretch.Fill };
            Canvas.SetLeft(sh, _dragStart.X);
            Canvas.SetTop(sh, _dragStart.Y);
            AnnotationCanvas.Children.Add(sh);
            _activeShape = sh;
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
        }
        else if (_editMode == EditMode.Arrow)
        {
            var p = new ShapePath
            {
                Stroke = _currentBrush,
                StrokeThickness = _strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = ArrowGeometry(_dragStart, _dragStart, ArrowHeadLen)
            };
            AnnotationCanvas.Children.Add(p);
            _activeShape = p;
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
        }
        else if (_editMode == EditMode.Text)
        {
            SpawnTextEditor(_dragStart);
            e.Handled = true;
        }
    }

    /// <summary>Rect 实时 min/abs 归一化宽高；Circle 取 min(|dx|,|dy|) 作直径（真圆）；Arrow 重建几何。</summary>
    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_activeShape is null) return;
        var pt = e.GetPosition(AnnotationCanvas);
        if (_editMode == EditMode.Rect)
        {
            Canvas.SetLeft(_activeShape, Math.Min(_dragStart.X, pt.X));
            Canvas.SetTop(_activeShape, Math.Min(_dragStart.Y, pt.Y));
            _activeShape.Width = Math.Abs(pt.X - _dragStart.X);
            _activeShape.Height = Math.Abs(pt.Y - _dragStart.Y);
        }
        else if (_editMode == EditMode.Circle)
        {
            // 真圆：直径 = min(|dx|,|dy|)，从起点向拖拽方向扩展
            double dx = pt.X - _dragStart.X;
            double dy = pt.Y - _dragStart.Y;
            double size = Math.Min(Math.Abs(dx), Math.Abs(dy));
            Canvas.SetLeft(_activeShape, dx >= 0 ? _dragStart.X : _dragStart.X - size);
            Canvas.SetTop(_activeShape, dy >= 0 ? _dragStart.Y : _dragStart.Y - size);
            _activeShape.Width = size;
            _activeShape.Height = size;
        }
        else if (_editMode == EditMode.Arrow && _activeShape is ShapePath p)
        {
            p.Data = ArrowGeometry(_dragStart, pt, ArrowHeadLen);
        }
    }

    /// <summary>松开定型：过小（&lt;3px）移除；否则记录自然坐标并入 undo 栈。</summary>
    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeShape is null) return;
        AnnotationCanvas.ReleaseMouseCapture();

        var endPt = e.GetPosition(AnnotationCanvas);
        bool tooSmall = _editMode == EditMode.Arrow
            ? (endPt - _dragStart).Length < 3
            : _activeShape.Width < 3 || _activeShape.Height < 3;

        if (tooSmall)
        {
            AnnotationCanvas.Children.Remove(_activeShape);
        }
        else
        {
            var record = CreateRecordForActiveShape(endPt);
            _activeShape.Tag = record;
            ExecuteEdit(new AddAnnotationEdit(AnnotationCanvas, _activeShape));
        }

        _activeShape = null;
        e.Handled = true;
    }

    /// <summary>把当前临时形状转为自然坐标记录。</summary>
    private AnnotationRecord CreateRecordForActiveShape(Point endPt)
    {
        var record = new AnnotationRecord
        {
            Kind = _editMode,
            NaturalStrokeThickness = ToNaturalLengthX(_strokeThickness),
            Brush = _currentBrush
        };

        switch (_editMode)
        {
            case EditMode.Rect:
                {
                    double x = Math.Min(_dragStartNatural.X, endPt.X * _scaleX);
                    double y = Math.Min(_dragStartNatural.Y, endPt.Y * _scaleY);
                    double w = Math.Abs(endPt.X * _scaleX - _dragStartNatural.X);
                    double h = Math.Abs(endPt.Y * _scaleY - _dragStartNatural.Y);
                    record.NaturalRect = new Rect(x, y, w, h);
                    break;
                }
            case EditMode.Circle:
                {
                    double dx = (endPt.X - _dragStart.X) * _scaleX;
                    double dy = (endPt.Y - _dragStart.Y) * _scaleY;
                    double size = Math.Min(Math.Abs(dx), Math.Abs(dy));
                    double x = dx >= 0 ? _dragStartNatural.X : _dragStartNatural.X - size;
                    double y = dy >= 0 ? _dragStartNatural.Y : _dragStartNatural.Y - size;
                    record.NaturalRect = new Rect(x, y, size, size);
                    break;
                }
            case EditMode.Arrow:
                {
                    record.NaturalStart = _dragStartNatural;
                    record.NaturalEnd = ToNaturalPoint(endPt);
                    break;
                }
        }

        return record;
    }

    /// <summary>按当前 scale 与变换重排所有已有批注。</summary>
    private void ArrangeAnnotations()
    {
        foreach (UIElement child in AnnotationCanvas.Children)
        {
            if (child is not FrameworkElement fe || fe.Tag is not AnnotationRecord rec) continue;

            switch (rec.Kind)
            {
                case EditMode.Rect:
                case EditMode.Circle:
                    if (child is Shape sh)
                    {
                        var r = rec.NaturalRect;
                        Canvas.SetLeft(sh, r.X / _scaleX);
                        Canvas.SetTop(sh, r.Y / _scaleY);
                        sh.Width = r.Width / _scaleX;
                        sh.Height = r.Height / _scaleY;
                        sh.StrokeThickness = rec.NaturalStrokeThickness / Math.Max(_scaleX, _scaleY);
                    }
                    break;

                case EditMode.Arrow:
                    if (child is ShapePath p)
                    {
                        var start = ToCanvasPoint(rec.NaturalStart);
                        var end = ToCanvasPoint(rec.NaturalEnd);
                        double headLen = Math.Max(8, rec.NaturalStrokeThickness * 3) / Math.Max(_scaleX, _scaleY);
                        p.Data = ArrowGeometry(start, end, headLen);
                        p.StrokeThickness = rec.NaturalStrokeThickness / Math.Max(_scaleX, _scaleY);
                    }
                    break;

                case EditMode.Text:
                    double fontSize = rec.NaturalFontSize / ((_scaleX + _scaleY) / 2);
                    var pt = ToCanvasPoint(rec.NaturalPosition);
                    if (child is TextBlock blk)
                    {
                        Canvas.SetLeft(blk, pt.X);
                        Canvas.SetTop(blk, pt.Y);
                        blk.FontSize = fontSize;
                    }
                    else if (child is TextBox tb)
                    {
                        Canvas.SetLeft(tb, pt.X);
                        Canvas.SetTop(tb, pt.Y);
                        tb.FontSize = fontSize;
                    }
                    break;
            }
        }
    }

    /// <summary>撤销 / 重做快捷键。</summary>
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
    }

    private void ExecuteEdit(IAnnotationEdit edit)
    {
        edit.Do();
        _undoStack.Push(edit);
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        var edit = _undoStack.Pop();
        edit.Undo();
        _redoStack.Push(edit);
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var edit = _redoStack.Pop();
        edit.Do();
        _undoStack.Push(edit);
    }

    // -----------------------------------------------------------------------
    // 文本批注
    // -----------------------------------------------------------------------

    /// <summary>Text 模式：在点击处生成无边框可编辑 TextBox，失焦转固定 TextBlock；Enter 提交，Esc 取消。</summary>
    private void SpawnTextEditor(Point pt)
    {
        var ptNatural = ToNaturalPoint(pt);
        var tb = new TextBox
        {
            Foreground = _currentBrush,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(178, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            CaretBrush = _currentBrush,
            FontSize = TextFontSizeNatural / ((_scaleX + _scaleY) / 2),
            MinWidth = 30,
            Padding = new Thickness(0) // 显式消除 Padding 带来的 1-2px 位移
        };

        Canvas.SetLeft(tb, pt.X);
        Canvas.SetTop(tb, pt.Y);
        AnnotationCanvas.Children.Add(tb);

        tb.PreviewKeyDown += TextBox_PreviewKeyDown;
        tb.LostFocus += (s, _) => CommitTextEditor((TextBox)s!, null);
        tb.Focus();
        Keyboard.Focus(tb);

        var record = new AnnotationRecord
        {
            Kind = EditMode.Text,
            NaturalPosition = ptNatural,
            Text = string.Empty,
            NaturalFontSize = TextFontSizeNatural,
            Brush = _currentBrush
        };
        tb.Tag = record;
    }

    private void TextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        if (e.Key == Key.Enter)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            var record = tb.Tag as AnnotationRecord;
            tb.Text = record?.Text ?? string.Empty;
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    /// <summary>TextBox 失焦：非空则转为同位置同样式的 TextBlock；空则移除。双击 TextBlock 可回编。</summary>
    private void CommitTextEditor(TextBox tb, TextBlock? replace)
    {
        var record = tb.Tag as AnnotationRecord;
        if (string.IsNullOrWhiteSpace(tb.Text))
        {
            if (replace != null)
            {
                int replaceIdx = AnnotationCanvas.Children.IndexOf(replace);
                if (replaceIdx >= 0)
                    AnnotationCanvas.Children.RemoveAt(replaceIdx);
            }
            AnnotationCanvas.Children.Remove(tb);
            return;
        }

        var bg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(178, 0, 0, 0));
        var blk = new TextBlock
        {
            Text = tb.Text,
            Foreground = tb.Foreground,
            Background = bg,
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            Padding = new Thickness(0), // 与 TextBox 保持一致，避免视觉跳动
            TextWrapping = TextWrapping.NoWrap
        };

        blk.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2 && _annotationModeEnabled)
            {
                EnterTextEdit((TextBlock)s!);
                e.Handled = true;
            }
        };

        if (record != null)
        {
            record.Text = tb.Text;
            blk.Tag = record;
        }

        // 继承 TextBox 的 Canvas 位置，避免文本跳回左上角。
        Canvas.SetLeft(blk, Canvas.GetLeft(tb));
        Canvas.SetTop(blk, Canvas.GetTop(tb));

        int idx;
        if (replace != null)
        {
            idx = AnnotationCanvas.Children.IndexOf(replace);
            AnnotationCanvas.Children.Remove(replace);
        }
        else
        {
            idx = AnnotationCanvas.Children.IndexOf(tb);
        }
        AnnotationCanvas.Children.Remove(tb);

        if (idx >= 0 && idx <= AnnotationCanvas.Children.Count)
            AnnotationCanvas.Children.Insert(idx, blk);
        else
            AnnotationCanvas.Children.Add(blk);
    }

    private void EnterTextEdit(TextBlock blk)
    {
        var record = blk.Tag as AnnotationRecord;
        var bg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(178, 0, 0, 0));
        var tb = new TextBox
        {
            Text = record?.Text ?? blk.Text,
            Foreground = blk.Foreground,
            Background = bg,
            FontSize = blk.FontSize,
            FontFamily = blk.FontFamily,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CaretBrush = blk.Foreground,
            MinWidth = 30
        };

        tb.PreviewKeyDown += TextBox_PreviewKeyDown;
        tb.LostFocus += (s, _) => CommitTextEditor((TextBox)s!, blk);

        // 继承 TextBlock 的 Canvas 位置，避免编辑时文本跳回左上角。
        Canvas.SetLeft(tb, Canvas.GetLeft(blk));
        Canvas.SetTop(tb, Canvas.GetTop(blk));

        int idx = AnnotationCanvas.Children.IndexOf(blk);
        AnnotationCanvas.Children.RemoveAt(idx);
        AnnotationCanvas.Children.Insert(idx, tb);
        tb.Focus();
        Keyboard.Focus(tb);

        if (record != null)
            tb.Tag = record;
    }

    /// <summary>箭头几何：主线 + 末端 V 形箭头（箭头长 = headLen，张角 60°）。</summary>
    private static PathGeometry ArrowGeometry(Point start, Point end, double headLen)
    {
        var geo = new PathGeometry();
        var main = new PathFigure { StartPoint = start };
        main.Segments.Add(new LineSegment(end, true));
        geo.Figures.Add(main);

        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double Back = System.Math.PI * 5.0 / 6.0; // 150°
        var p1 = new Point(end.X + headLen * Math.Cos(angle + Back), end.Y + headLen * Math.Sin(angle + Back));
        var p2 = new Point(end.X + headLen * Math.Cos(angle - Back), end.Y + headLen * Math.Sin(angle - Back));
        var head = new PathFigure { StartPoint = p1 };
        head.Segments.Add(new LineSegment(end, true));
        head.Segments.Add(new LineSegment(p2, true));
        geo.Figures.Add(head);
        return geo;
    }

    // -----------------------------------------------------------------------
    // 右键菜单：置顶 / 显示阴影 / 显示边界 / 重置大小 / 不透明度 / 旋转 / 镜像
    // -----------------------------------------------------------------------

    /// <summary>置顶：切换 Topmost。</summary>
    private void Topmost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            Topmost = mi.IsChecked;
    }

    /// <summary>显示阴影：在 DropShadowEffect 与 null 之间切换。</summary>
    private void Shadow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            PinImage.Effect = mi.IsChecked ? ShadowEffect : null;
    }

    /// <summary>显示边界：在无边框与 2px 灰边框之间切换，边框向外生长。</summary>
    private void Border_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            ApplyBorder(mi.IsChecked ? 2 : 0);
    }

    /// <summary>应用边框厚度：设置 PinBorder，经 ReapplyMetrics 重算 Left/Top（边框向外生长）与窗口外接尺寸。</summary>
    private void ApplyBorder(double thickness)
    {
        PinBorder.BorderThickness = new Thickness(thickness);
        var hwnd = new WindowInteropHelper(this).Handle;
        // suppress SizeChanged：防止 ApplyWindowSize 设 Width 触发的 SizeChanged 反推 _scaleX
        // 并用 preservePosition=true 的 ApplyMetricsCore 覆盖刚算好的 Left/Top（开关边界时贴图错位）。
        _suppressSizeChanged = true;
        try
        {
            ReapplyMetrics(hwnd);
            UpdateLayout();
        }
        finally { _suppressSizeChanged = false; }
    }

    /// <summary>重置大小：恢复当前 DPI 下的 1:1 像素比例，以窗口中心为锚点。</summary>
    private void ResetSize_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var center = new Point(Left + Width / 2, Top + Height / 2);

        ReapplyMetrics(hwnd);

        Left = center.X - Width / 2;
        Top = center.Y - Height / 2;
    }

    /// <summary>不透明度子菜单：0.3 / 0.5 / 0.8 / 1.0。点击设 Opacity 并同步勾选。</summary>
    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s && double.TryParse(s, out double op))
        {
            Opacity = op;
            SyncOpacityMenu(op);
        }
    }

    /// <summary>按当前不透明度同步菜单勾选（构造时与点击时共用，Tag 值精确匹配）。</summary>
    private void SyncOpacityMenu(double opacity)
    {
        foreach (var child in OpacityMenuItem.Items)
            if (child is MenuItem m && m.Tag is string s && double.TryParse(s, out double v))
                m.IsChecked = Math.Abs(v - opacity) < 0.001;
    }

    /// <summary>旋转：每次顺时针 90 度，窗口宽高随外接矩形互换。</summary>
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        _rotationStep = (_rotationStep + 1) % 4;
        ApplyTransform();
    }

    /// <summary>镜像：水平翻转。</summary>
    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        _mirrored = !_mirrored;
        ApplyTransform();
    }

    /// <summary>
    /// 把旋转角度与镜像状态同步到 ScaleTransform / RotateTransform，
    /// 并随即重算窗口尺寸，使其始终紧贴图片旋转后的外接矩形。
    /// </summary>
    private void ApplyTransform()
    {
        RotateTransform.Angle = RotationAngle;
        ScaleTransform.ScaleX = _mirrored ? -1 : 1;
        ScaleTransform.ScaleY = 1;
        ApplyWindowSize();
    }

    /// <summary>
    /// 按当前旋转角度与边框厚度计算窗口外接尺寸：
    /// 90/270 度时图片外接矩形宽高互换。窗口 = 图片外接矩形 + 两侧边框，
    /// 而 ContentRoot 通过 Margin=border 向内缩，图片内容面积恒为 imgW×imgH，
    /// 边框向外生长、不侵占图片内容。窗口边缘始终紧贴（边框 + 图片），无多余留白。
    /// imgW/imgH 为物理像素，需除以 DPI 系数转 DIP 后再设窗口尺寸（docs/02 §5）。
    /// </summary>
    private void ApplyWindowSize()
    {
        bool swapped = (_rotationStep % 2) == 1;
        double imgW = (swapped ? _naturalHeight : _naturalWidth) / _scaleX;
        double imgH = (swapped ? _naturalWidth : _naturalHeight) / _scaleY;
        double border = PinBorder.BorderThickness.Left; // 均匀边框（DIP）

        Width = imgW + 2 * border;
        Height = imgH + 2 * border;

        // ContentRoot 内缩 border = 图片视觉区 = AnnotationCanvas 铺满区 = RenderTargetBitmap 导出根。
        ContentRoot.Margin = new Thickness(border);
    }

    // -----------------------------------------------------------------------
    // 光栅化导出：复制 / 另存为 / 作为文件打开（docs/02 §8.2）
    // -----------------------------------------------------------------------

    /// <summary>
    /// 把 ContentRoot（图片 + 批注）光栅化为 BitmapSource：
    /// 1. 摘阴影——渲染前 PinImage.Effect=null + InvalidateVisual，渲染后恢复，防阴影烤入；
    /// 2. DPI 缩放——按窗口当前 DPI 放大像素维度，避免高 DPI 屏糊；
    /// 3. 渲染根 ContentRoot 天然排除 AnnotationToolbar（在其外）与 PinBorder。
    /// </summary>
    private BitmapSource RenderComposite()
    {
        var savedEffect = PinImage.Effect;
        PinImage.Effect = null;
        try
        {
            // 强制 flush 阴影摘除，避免 Effect=null 未即时生效把阴影烤入位图
            PinImage.InvalidateVisual();
            ContentRoot.UpdateLayout();

            double dpiX = _scaleX * 96.0;
            double dpiY = _scaleY * 96.0;
            int w = Math.Max(1, (int)(ContentRoot.ActualWidth * _scaleX));
            int h = Math.Max(1, (int)(ContentRoot.ActualHeight * _scaleY));

            var rtb = new RenderTargetBitmap(w, h, dpiX, dpiY, PixelFormats.Pbgra32);
            rtb.Render(ContentRoot);
            var result = (BitmapSource)rtb;
            result.Freeze();
            return result;
        }
        finally
        {
            PinImage.Effect = savedEffect;
        }
    }

    /// <summary>复制图片：光栅化复合图写入剪贴板（剪贴板被独占时静默）。批注保存即走此路径。</summary>
    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(RenderComposite()); }
        catch { /* 剪贴板被独占不阻断 */ }
    }

    /// <summary>另存为...：光栅化复合图按所选格式（PNG/JPEG）保存到用户选择的路径。</summary>
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveAsAsync();
    }

    private async Task SaveAsAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            FileName = "screenshot.png",
        };
        if (dlg.ShowDialog() != true)
            return;

        string fileName = dlg.FileName;
        BitmapSource composite = RenderComposite();
        await Task.Run(() =>
        {
            try
            {
                bool isJpeg = fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
                BitmapEncoder encoder = isJpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(composite));
                using var fs = new FileStream(fileName, FileMode.Create);
                encoder.Save(fs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: 保存图片失败: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>作为文件打开：光栅化复合图写临时缓存文件后用系统默认程序打开。</summary>
    private void OpenAsFile_Click(object sender, RoutedEventArgs e)
    {
        _ = OpenAsFileAsync();
    }

    private async Task OpenAsFileAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"myquicker_pin_{System.Guid.NewGuid():N}.png");
        BitmapSource composite = RenderComposite();
        await Task.Run(() =>
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(composite));
                using var fs = new FileStream(path, FileMode.Create);
                encoder.Save(fs);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: 作为文件打开失败: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>窗口关闭时释放图片源、Brush、批注元素、鼠标捕获与事件订阅，避免资源泄漏。</summary>
    protected override void OnClosed(EventArgs e)
    {
        PinImage.Source = null;
        PinImage.ClearValue(System.Windows.Controls.Image.EffectProperty);
        PinBorder.BorderBrush = null;
        AnnotationCanvas.Children.Clear();
        _currentBrush = null!;
        _activeShape = null;
        _undoStack.Clear();
        _redoStack.Clear();

        if (AnnotationCanvas.IsMouseCaptured)
            AnnotationCanvas.ReleaseMouseCapture();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        SourceInitialized -= OnSourceInitialized;

        base.OnClosed(e);
    }

    /// <summary>关闭。</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
