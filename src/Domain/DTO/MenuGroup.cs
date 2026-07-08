using System.Collections.Generic;

namespace Aurora.Domain.DTO;

/// <summary>
/// 菜单分组 DTO。仅包含视觉与动作引用数据。
/// </summary>
public sealed class MenuGroup
{
    /// <summary>稳定标识符。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>分组显示名称。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>分组图标。</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>分组内的动作列表。</summary>
    public List<ActionItem> Actions { get; set; } = new();
}
