namespace Aurora.Domain.DTO;

/// <summary>
/// 应用通用偏好设置 DTO。仅包含纯数据子对象。
/// </summary>
public sealed class Preferences
{
    /// <summary>截屏覆盖层参数。</summary>
    public SnippingSettings Snipping { get; set; } = new();

    /// <summary>唤醒菜单外观参数。</summary>
    public MenuSettings Menu { get; set; } = new();

    /// <summary>贴图窗口参数。</summary>
    public PinSettings Pin { get; set; } = new();
}
