using System.Linq;
using System.Windows;
using Aurora.Domain.Runtime;
using Aurora.UI;
using Application = System.Windows.Application;

namespace Aurora.Services;

/// <summary>
/// 基于当前打开覆盖层窗口的唤醒阻塞策略。
/// 当截图或设置窗口处于打开状态时，阻塞新的菜单唤醒。
/// </summary>
public sealed class OverlayWakeBlockPolicy : IWakeBlockPolicy
{
    /// <inheritdoc/>
    public bool IsBlocked()
    {
        return Application.Current?.Windows.OfType<ScreenshotWindow>().Any() == true ||
               Application.Current?.Windows.OfType<SettingsWindow>().Any() == true;
    }
}
