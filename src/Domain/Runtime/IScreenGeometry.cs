namespace MyQuicker.Domain.Runtime;

/// <summary>多显示器几何抽象：让边界计算可测试。</summary>
public interface IScreenGeometry
{
    IReadOnlyList<ScreenInfo> Screens { get; }

    /// <summary>返回包含指定点的显示器；不存在时回退第一块屏。</summary>
    ScreenInfo GetScreenContaining(Point point);
}
