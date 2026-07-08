namespace Aurora.Domain.DTO;

/// <summary>触发器类型枚举。</summary>
public enum TriggerType
{
    /// <summary>瞬时硬件输入（中键、侧键等）。</summary>
    Button,

    /// <summary>纯轨迹画圈手势。</summary>
    CircleGesture,
}
