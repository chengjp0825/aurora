using System.Windows.Threading;
using MyQuicker.Services;

namespace MyQuicker.UI;

/// <summary>
/// <see cref="ISynchronizationContext"/> 的 WPF Dispatcher 适配器。
/// 必须在目标 Dispatcher 线程构造（例如 App.OnStartup 主 UI 线程）。
/// </summary>
internal sealed class WpfSynchronizationContext : ISynchronizationContext
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public void Post(Action action) => _dispatcher.BeginInvoke(action);
}
