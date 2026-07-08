namespace Aurora.Domain.DTO;

/// <summary>截图结算后的动作。</summary>
public enum SnippingAfterScreenshot
{
    /// <summary>钉为贴图并复制到剪贴板（默认）。</summary>
    PinAndCopy = 0,

    /// <summary>仅复制到剪贴板，不钉贴图。</summary>
    CopyOnly = 1,

    /// <summary>仅钉为贴图，不写剪贴板。</summary>
    PinOnly = 2,
}
