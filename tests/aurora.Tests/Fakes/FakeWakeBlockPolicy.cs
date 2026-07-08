using Aurora.Domain.Runtime;

namespace Aurora.Tests.Fakes;

/// <summary>WakeOrchestrator 测试用手动阻塞策略。</summary>
internal sealed class FakeWakeBlockPolicy : IWakeBlockPolicy
{
    public bool Blocked { get; set; }

    public bool IsBlocked() => Blocked;
}
