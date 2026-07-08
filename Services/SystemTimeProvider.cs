using System.Diagnostics;
using Aurora.Domain.Runtime;

namespace Aurora.Services;

/// <summary><see cref="ITimeProvider"/> 的系统实现，使用高精度硬件单调时钟。</summary>
internal sealed class SystemTimeProvider : ITimeProvider
{
    public long MonotonicTimestamp => Stopwatch.GetTimestamp();
}
