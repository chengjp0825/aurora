using System;
using Aurora.Domain.DTO;
using Aurora.Interop;

namespace Aurora.Domain.Runtime;

/// <summary>
/// 将 <see cref="TriggerBinding"/> DTO 转换为运行时 <see cref="ITrigger"/> 的单向工厂。
/// 负责注入触发器所需的运行时服务（如 <see cref="ITimeProvider"/>）。
/// </summary>
public static class TriggerFactory
{
    /// <summary>
    /// 根据 <paramref name="binding"/> 创建对应的运行时触发器。
    /// </summary>
    /// >xception cref="ArgumentNullException">
    /// <paramref name="binding"/> 或 <paramref name="timeProvider"/> 为 null。
    /// </exception>
    /// >xception cref="NotSupportedException">遇到不支持的 <see cref="TriggerBinding.Type"/>。</exception>
    public static ITrigger Create(TriggerBinding binding, ITimeProvider timeProvider)
    {
        if (binding is null)
            throw new ArgumentNullException(nameof(binding));
        if (timeProvider is null)
            throw new ArgumentNullException(nameof(timeProvider));

        return binding.Type switch
        {
            TriggerType.Button => CreateButtonTrigger(binding, timeProvider),
            TriggerType.CircleGesture => CreateCircleGestureTrigger(binding, timeProvider),
            _ => throw new NotSupportedException($"Unsupported trigger type: {binding.Type}")
        };
    }

    private static ITrigger CreateButtonTrigger(TriggerBinding binding, ITimeProvider timeProvider)
    {
        int wakeupMessage = binding.WakeupMessage ?? NativeMethods.WM_MBUTTONDOWN;
        int? xButton = wakeupMessage == NativeMethods.WM_XBUTTONDOWN
            ? binding.XButtonData
            : null;

        string sourceName = wakeupMessage switch
        {
            NativeMethods.WM_MBUTTONDOWN => "MiddleButton",
            NativeMethods.WM_XBUTTONDOWN => $"XButton{xButton ?? 1}",
            _ => $"Button{wakeupMessage}"
        };

        return new ButtonTrigger(sourceName, wakeupMessage, xButton);
    }

    private static ITrigger CreateCircleGestureTrigger(TriggerBinding binding, ITimeProvider timeProvider)
    {
        return new CircleGestureTrigger("CircleGesture", binding.CircleSensitivity, timeProvider);
    }
}
