using System;
using System.Collections.Generic;
using System.Diagnostics;
using MyQuicker.Domain.DTO;

namespace MyQuicker.Domain.Runtime;

/// <summary>纯轨迹画圈触发器：维护滑动时间窗并做几何判定。</summary>
public sealed class CircleGestureTrigger : ITrigger
{
    private readonly string _sourceName;
    private readonly double _minBoxSide;
    private readonly double _minTotalTurnDeg;
    private readonly int _windowMs;

    private readonly Queue<(Point Position, long Timestamp)> _history = new();
    private readonly List<Point> _buffer = new();
    private long _lastCheckTick;
    private const int CheckIntervalMs = 30;

    public string SourceName => _sourceName;

    public CircleGestureTrigger(string sourceName, CircleSensitivity sensitivity, ITimeProvider timeProvider)
    {
        _sourceName = sourceName;
        (_minBoxSide, _minTotalTurnDeg, _windowMs) = sensitivity switch
        {
            CircleSensitivity.Low  => (100.0, 330.0, 600),
            CircleSensitivity.High => (60.0,  270.0, 1000),
            _                      => (80.0,  300.0, 800), // Medium
        };
    }

    private static long MillisecondsToTicks(int milliseconds) =>
        (long)(milliseconds * Stopwatch.Frequency / 1000.0);

    public TriggerMatchResult Evaluate(TriggerEvent triggerEvent)
    {
        if (triggerEvent.EventType != TriggerEventType.MouseMove)
            return TriggerMatchResult.NoMatch;

        long now = triggerEvent.Timestamp;
        _history.Enqueue((triggerEvent.Location, now));
        PruneHistory(now);

        if (_history.Count < 8 || now - _lastCheckTick < MillisecondsToTicks(CheckIntervalMs))
            return TriggerMatchResult.NoMatch;

        _lastCheckTick = now;
        _buffer.Clear();
        foreach (var item in _history)
            _buffer.Add(item.Position);

        if (!CircleGestureEvaluator.IsCircle(_buffer, _minBoxSide, _minTotalTurnDeg))
            return TriggerMatchResult.NoMatch;

        _history.Clear();
        return new TriggerMatchResult(true, new WakeContext(
            triggerEvent.Location,
            triggerEvent.Timestamp,
            _sourceName));
    }

    private void PruneHistory(long now)
    {
        long windowTicks = MillisecondsToTicks(_windowMs);
        while (_history.Count > 0 && now - _history.Peek().Timestamp > windowTicks)
            _history.Dequeue();
    }
}
