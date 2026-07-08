namespace Aurora.Domain.Runtime;

/// <summary>WakeOrchestrator 配置。</summary>
public sealed record WakeOrchestratorSettings(
    TimeSpan DebounceInterval,
    TimeSpan StaleEventThreshold,
    double MenuWidth,
    double MenuHeight);
