namespace Aurora.Domain.Runtime;

/// <summary>触发器匹配成功后携带的唤醒上下文，解耦输入层与表现层。</summary>
/// <param name="Location">唤醒位置（物理像素）。</param>
/// <param name="Timestamp">单调递增的物理时间戳（Stopwatch ticks）。</param>
/// <param name="TriggerSource">触发源标识。</param>
public sealed record WakeContext(Point Location, long Timestamp, string TriggerSource);
