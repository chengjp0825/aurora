using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using MyQuicker.Interop;

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
        Rectangle bounds = ComputeVirtualBounds();

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
    /// Computes the smallest rectangle that encloses every screen. X/Y may
    /// be negative when a monitor extends to the left/above the primary.
    /// </summary>
    private static Rectangle ComputeVirtualBounds()
    {
        Screen[] screens = Screen.AllScreens;
        int xMin = screens.Min(s => s.Bounds.X);
        int yMin = screens.Min(s => s.Bounds.Y);
        int xMax = screens.Max(s => s.Bounds.Right);
        int yMax = screens.Max(s => s.Bounds.Bottom);
        return new Rectangle(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
