namespace MyQuicker.Domain.Runtime;

/// <summary>显示器几何与 DPI 缩放信息。</summary>
public sealed record ScreenInfo(ScreenBounds Bounds, double ScaleX, double ScaleY);
