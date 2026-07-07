namespace MyQuicker.Domain.Runtime;

/// <summary>触发器匹配结果。</summary>
public sealed record TriggerMatchResult(bool IsMatch, WakeContext? Context)
{
    public static TriggerMatchResult NoMatch { get; } = new(false, null);
}
