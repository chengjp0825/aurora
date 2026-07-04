using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using MyQuicker.Interop;
using MyQuicker.Models;

namespace MyQuicker.Services;

/// <summary>
/// Captures a full base image spanning all physical monitors and returns
/// it as a WPF BitmapSource along with the virtual-screen bounds. Per
/// SPEC step 8A.
/// </summary>
internal sealed class ScreenshotService
{
    /// <summary>
    /// Captures every screen into a single bitmap and converts it to a
    /// frozen BitmapSource, freeing the intermediate GDI handle.
    /// </summary>
    public (BitmapSource Source, Rectangle Bounds) Capture()
    {
        Rectangle bounds = ComputeBounds();

        using var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            // Copy the full virtual screen (source origin may be negative).
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        }

        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            source.Freeze(); // force a copy so the HBITMAP can be released safely
            return (source, bounds);
        }
        finally
        {
            // Core memory constraint: release the unmanaged GDI handle immediately.
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// 按 <see cref="SnippingSettings.CaptureScope"/> 计算截图范围：
    /// <see cref="SnippingCaptureScope.AllMonitors"/> 取所有显示器并集（跨屏拼接），
    /// <see cref="SnippingCaptureScope.CurrentMonitor"/> 取光标所在显示器。X/Y 可能为负
    /// （显示器在主屏左/上方时）。
    /// </summary>
    private static Rectangle ComputeBounds()
    {
        Screen[] screens = Screen.AllScreens;

        if (SettingsManager.Instance.Settings.Snipping.CaptureScope == SnippingCaptureScope.CurrentMonitor)
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            foreach (Screen s in screens)
                if (s.Bounds.Contains(cursor))
                    return s.Bounds;
            // 光标不在任何屏幕上（罕见，如刚切换显示器）：回退虚拟屏。
        }

        return ComputeVirtualBounds(screens);
    }

    /// <summary>
    /// 计算包围所有屏幕的最小矩形（跨屏虚拟屏）。X/Y 可能为负。
    /// </summary>
    private static Rectangle ComputeVirtualBounds(Screen[] screens)
    {
        int xMin = screens.Min(s => s.Bounds.X);
        int yMin = screens.Min(s => s.Bounds.Y);
        int xMax = screens.Max(s => s.Bounds.Right);
        int yMax = screens.Max(s => s.Bounds.Bottom);
        return new Rectangle(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
