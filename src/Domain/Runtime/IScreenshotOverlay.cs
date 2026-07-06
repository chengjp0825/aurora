using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// 截图覆盖层 seam：在全屏底图上让用户选择目标区域，返回物理屏幕坐标系中的矩形。
/// 实现类负责把 WPF 窗口的 DIP 选区转换回物理像素并落回虚拟屏坐标系。
/// </summary>
public interface IScreenshotOverlay
{
    /// <summary>
    /// 显示截图覆盖层并异步等待用户完成一次区域选择。
    /// </summary>
    /// <param name="fullImage">全屏底图（物理像素 1:1）。</param>
    /// <param name="fullBounds">底图在虚拟屏幕坐标系中的边界（物理像素）。</param>
    /// <returns>
    /// 用户确认的选区（物理屏幕坐标，与 <paramref name="fullBounds"/> 同坐标系）；
    /// 取消或无效选区时返回 <c>null</c>。
    /// </returns>
    Task<Rectangle?> SelectRegionAsync(BitmapSource fullImage, Rectangle fullBounds);
}
