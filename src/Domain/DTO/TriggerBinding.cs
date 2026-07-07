namespace MyQuicker.Domain.DTO;

/// <summary>
/// 触发器配置 DTO。仅包含纯静态标量与数据，
/// 不含运行时服务引用或 WPF 依赖。
/// </summary>
public sealed class TriggerBinding
{
    /// <summary>触发器类型。</summary>
    public TriggerType Type { get; set; } = TriggerType.Button;

    /// <summary>
    /// 鼠标消息（WM_MBUTTONDOWN / WM_XBUTTONDOWN）。
    /// 仅当 <see cref="Type"/> 为 <see cref="TriggerType.Button"/> 时有效。
    /// </summary>
    public int? WakeupMessage { get; set; }

    /// <summary>
    /// 侧键标识（1=后退/XBUTTON1, 2=前进/XBUTTON2）。
    /// 仅当 <see cref="WakeupMessage"/> 为 WM_XBUTTONDOWN 时有效。
    /// </summary>
    public int? XButtonData { get; set; }

    /// <summary>
    /// 画圈手势灵敏度预设。
    /// 仅当 <see cref="Type"/> 为 <see cref="TriggerType.CircleGesture"/> 时有效。
    /// </summary>
    public CircleSensitivity CircleSensitivity { get; set; } = CircleSensitivity.Medium;

    /// <summary>触发时是否拦截（吞掉）唤醒键，不传递给当前前台应用。</summary>
    public bool InterceptWakeupKey { get; set; } = true;
}
