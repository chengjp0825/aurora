using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// 贴图窗口 seam：把一张已裁剪的截图钉在指定屏幕位置。
/// </summary>
public interface IScreenshotPinService
{
    /// <summary>
    /// 将 <paramref name="source"/> 钉在 <paramref name="physicalBounds"/> 所描述的屏幕位置。
    /// </summary>
    /// <param name="source">要钉的位图源（已按 <paramref name="physicalBounds"/> 裁剪）。</param>
    /// <param name="physicalBounds">贴图左上角目标位置与尺寸（物理像素）。</param>
    Task PinAsync(BitmapSource source, Rectangle physicalBounds);
}
