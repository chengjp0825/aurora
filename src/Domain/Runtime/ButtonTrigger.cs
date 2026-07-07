using System;
using MyQuicker.Interop;

namespace MyQuicker.Domain.Runtime;

/// <summary>瞬时硬件输入触发器（中键、侧键等）。</summary>
public sealed class ButtonTrigger : ITrigger
{
    private readonly int _wakeupMessage;
    private readonly int? _xButtonData;

    public string SourceName { get; }

    public ButtonTrigger(string sourceName, int wakeupMessage, int? xButtonData)
    {
        SourceName = sourceName;
        _wakeupMessage = wakeupMessage;
        _xButtonData = xButtonData;
    }

    public TriggerMatchResult Evaluate(TriggerEvent triggerEvent)
    {
        if (triggerEvent.EventType != TriggerEventType.MouseDown)
            return TriggerMatchResult.NoMatch;

        if (triggerEvent.Message != _wakeupMessage)
            return TriggerMatchResult.NoMatch;

        if (_wakeupMessage == NativeMethods.WM_XBUTTONDOWN &&
            triggerEvent.XButtonData != _xButtonData)
        {
            return TriggerMatchResult.NoMatch;
        }

        return new TriggerMatchResult(true, new WakeContext(
            triggerEvent.Location,
            triggerEvent.Timestamp,
            SourceName));
    }
}
