namespace MyQuicker.Domain.Runtime;

/// <summary>
/// Toast 通知 seam：把具体 UI 实现（WPF 窗口/系统通知）与调用方解耦。
/// </summary>
public interface IToastService
{
    /// <summary>
    /// 显示一条临时消息，<paramref name="durationMs"/> 毫秒后自动消失。
    /// </summary>
    void Show(string message, int durationMs);
}
