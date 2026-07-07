using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Services;

/// <summary>
/// <see cref="IScreenGeometry"/> 的 WinForms 实现。
/// 使用 System.Windows.Forms.Screen 枚举显示器并按真实 DPI 计算缩放。
/// 不永久缓存屏幕几何：每次查询实时计算，确保热插拔显示器或 DPI 切换后自动自愈。
/// </summary>
internal sealed class FormsScreenGeometry : IScreenGeometry
{
    public IReadOnlyList<ScreenInfo> Screens => Screen.AllScreens.Select(ToScreenInfo).ToList();

    public ScreenInfo GetScreenContaining(Domain.Runtime.Point point)
    {
        var drawingPoint = new System.Drawing.Point(point.X, point.Y);
        Screen screen = Screen.FromPoint(drawingPoint);
        return ToScreenInfo(screen);
    }

    private static ScreenInfo ToScreenInfo(Screen screen)
    {
        Rectangle bounds = screen.Bounds;
        var (sx, sy) = DpiHelper.ScaleForBounds(bounds);
        return new ScreenInfo(
            new Domain.Runtime.ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            sx,
            sy);
    }
}
