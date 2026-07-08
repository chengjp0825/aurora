using System;

namespace Aurora.Domain.Runtime;

/// <summary>
/// 菜单外部点击信号源：当用户点击菜单内容区域之外时触发，
/// 供 <see cref="WakeOrchestrator"/> 决定是否请求关闭菜单。
/// </summary>
public interface IOutsideClickSource
{
    /// <summary>用户在菜单内容区域外按下指针时触发。</summary>
    event EventHandler? OutsideClick;
}
