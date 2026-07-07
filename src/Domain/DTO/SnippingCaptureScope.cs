namespace MyQuicker.Domain.DTO;

/// <summary>截图范围。</summary>
public enum SnippingCaptureScope
{
    /// <summary>所有显示器（跨屏拼接，默认）。</summary>
    AllMonitors = 0,

    /// <summary>仅当前光标所在显示器。</summary>
    CurrentMonitor = 1,
}
