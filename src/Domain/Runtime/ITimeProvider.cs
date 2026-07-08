namespace Aurora.Domain.Runtime;

/// <summary>时间抽象：为核心逻辑提供单调递增的物理时钟，避免 NTP 同步导致的时间回跳或漂移。</summary>
public interface ITimeProvider
{
    /// <summary>单调递增的时间戳（由 <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> 提供）。</summary>
    long MonotonicTimestamp { get; }
}
