namespace Aurora.Domain.Runtime;

/// <summary>唤醒触发器策略抽象。</summary>
public interface ITrigger
{
    /// <summary>触发源标识，用于 WakeContext.TriggerSource。</summary>
    string SourceName { get; }

    /// <summary>评估输入事件是否匹配本触发器。</summary>
    TriggerMatchResult Evaluate(TriggerEvent triggerEvent);
}
