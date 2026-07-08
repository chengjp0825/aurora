using System.Drawing;

namespace Aurora.Services;

/// <summary>
/// 截图子域实体：承载 GDI+ <see cref="Bitmap"/> 与采集元数据。
/// 持有非托管资源，必须显式调用 <see cref="Dispose"/> 或在 using 中使用。
/// </summary>
public sealed class CapturedImage : IDisposable
{
    /// <summary>原始位图（32bppArgb，物理像素）。</summary>
    public Bitmap Bitmap { get; }

    /// <summary>截图范围在虚拟屏幕坐标系中的矩形（物理像素）。</summary>
    public Rectangle Bounds { get; }

    /// <summary>AllMonitors 因主副屏 DPI 不一致而回退到当前屏。</summary>
    public bool FallbackToCurrent { get; }

    public CapturedImage(Bitmap bitmap, Rectangle bounds, bool fallbackToCurrent)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Bounds = bounds;
        FallbackToCurrent = fallbackToCurrent;
    }

    public void Dispose() => Bitmap.Dispose();
}
