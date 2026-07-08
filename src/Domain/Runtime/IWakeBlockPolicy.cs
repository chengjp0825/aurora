namespace Aurora.Domain.Runtime;

/// <summary>
/// 唤醒阻塞策略：当某些覆盖层窗口（如截图、设置）处于打开状态时，
/// 阻止新的菜单唤醒，避免多个模态/覆盖 UI 同时竞争焦点。
/// </summary>
public interface IWakeBlockPolicy
{
    /// <summary>当前是否存在阻塞唤醒的覆盖层。</summary>
    bool IsBlocked();
}
