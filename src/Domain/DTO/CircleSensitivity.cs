namespace Aurora.Domain.DTO;

/// <summary>画圈手势灵敏度预设。低=不易误触，高=易触发。</summary>
public enum CircleSensitivity
{
    /// <summary>不易误触：更大最小圈、更严闭合度、更短时间窗。</summary>
    Low = 0,

    /// <summary>默认平衡值。</summary>
    Medium = 1,

    /// <summary>易触发：更小最小圈、更宽闭合度、更长时间窗。</summary>
    High = 2,
}
