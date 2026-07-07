namespace MyQuicker.Domain.DTO;

/// <summary>像素放大镜相对鼠标的初始方向。</summary>
public enum MagnifierPosition
{
    BottomRight = 0,
    BottomLeft = 1,
    TopRight = 2,
    TopLeft = 3,
}

/// <summary>像素放大镜放大倍率预设。</summary>
public enum MagnifierZoomPreset
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>截屏覆盖层参数。仅包含纯数据。</summary>
public sealed class SnippingSettings
{
    /// <summary>判定"点击 vs 拖拽"的位移阈值（DIP）。超过即升级为拖拽。</summary>
    public double DragThreshold { get; set; } = 5.0;

    /// <summary>暗罩浓度（0~1，越大越暗）。暗罩恒为黑色，仅透明度可配。</summary>
    public double MaskAlpha { get; set; } = 0.4;

    /// <summary>选区寻边红框颜色。</summary>
    public string BorderColor { get; set; } = "#FF0000";

    /// <summary>截图结算后的动作（钉贴图 / 写剪贴板 / 两者）。</summary>
    public SnippingAfterScreenshot AfterScreenshot { get; set; } = SnippingAfterScreenshot.PinAndCopy;

    /// <summary>截图范围：所有显示器（跨屏拼接）/ 当前光标所在显示器。</summary>
    public SnippingCaptureScope CaptureScope { get; set; } = SnippingCaptureScope.AllMonitors;

    /// <summary>像素放大镜相对鼠标的初始方向。</summary>
    public MagnifierPosition MagnifierPosition { get; set; } = MagnifierPosition.BottomRight;

    /// <summary>是否在放大镜中显示当前像素坐标。</summary>
    public bool ShowMagnifierCoordinates { get; set; } = true;

    /// <summary>是否在放大镜中显示 RGB/Hex 颜色信息。</summary>
    public bool ShowMagnifierColor { get; set; } = true;

    /// <summary>放大镜放大倍率预设：偏小 / 适中 / 偏大。</summary>
    public MagnifierZoomPreset MagnifierZoomPreset { get; set; } = MagnifierZoomPreset.Medium;
}
