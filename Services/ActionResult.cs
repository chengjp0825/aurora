namespace Aurora.Services;

/// <summary>
/// ActionExecutor 的纯数据执行结果。
/// 不实现 <see cref="IDisposable"/>：当 <see cref="CapturedImage"/> 非空时，
/// 其引用所有权转移给调用方，调用方必须在消费完毕后显式释放。
/// </summary>
public sealed record ActionResult(
    ActionOutcomeKind Kind,
    string? ToastMessage = null,
    CapturedImage? CapturedImage = null,
    string? ErrorCommand = null);
