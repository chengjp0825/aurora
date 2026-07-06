using System;
using System.Windows;
using MyQuicker.Domain.Runtime;
using DomainPoint = MyQuicker.Domain.Runtime.Point;
using WpfPoint = System.Windows.Point;

namespace MyQuicker.UI;

/// <summary>
/// 把 <see cref="MainWindow"/> 的命中测试适配为 <see cref="IOutsideClickSource"/>。
/// 当菜单处于唤醒状态且点击落在内容区外时，向外发出 <see cref="OutsideClick"/>。
/// </summary>
public sealed class MainWindowOutsideClickSource : IOutsideClickSource
{
    private readonly MainWindow _window;

    /// <summary>用户在菜单内容区域外按下指针时触发。</summary>
    public event EventHandler? OutsideClick;

    /// <summary>初始化适配器。</summary>
    public MainWindowOutsideClickSource(MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// 供原始输入源在任何鼠标按下时调用。
    /// 若菜单未唤醒或点击在内容区内，则不触发事件。
    /// </summary>
    public void OnMouseDown(object? sender, DomainPoint point)
    {
        if (!_window.IsAwake)
            return;

        WpfPoint logical = _window.ToLogical(point);
        if (!_window.ContentBounds.Contains(logical))
        {
            OutsideClick?.Invoke(this, EventArgs.Empty);
        }
    }
}
