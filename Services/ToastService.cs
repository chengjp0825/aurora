using System.Windows.Threading;
using MyQuicker.Domain.Runtime;
using MyQuicker.UI;

namespace MyQuicker.Services;

/// <summary>
/// <see cref="IToastService"/> 的 WPF 实现：委托给 <see cref="Toast.Show"/>。
/// 构造时捕获 UI Dispatcher，确保非 UI 线程调用也能安全弹出。
/// </summary>
internal sealed class ToastService : IToastService
{
    private readonly Dispatcher _dispatcher;

    public ToastService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <inheritdoc/>
    public void Show(string message, int durationMs)
    {
        _dispatcher.InvokeAsync(() => Toast.Show(message, durationMs));
    }
}
