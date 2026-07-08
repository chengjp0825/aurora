using System;
using Aurora.Domain.DTO;
using Aurora.Domain.Runtime;
using Aurora.Tests.Fakes;
using Xunit;

namespace Aurora.Tests.Domain.Runtime;

public class CircleGestureTriggerTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Evaluate_NonMouseMoveEvent_ReturnsNoMatch()
    {
        var trigger = new CircleGestureTrigger("CircleGesture", CircleSensitivity.Medium, _time);

        var result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(0, 0),
            _time.MonotonicTimestamp,
            0,
            null));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_StraightLine_ReturnsNoMatch()
    {
        var trigger = new CircleGestureTrigger("CircleGesture", CircleSensitivity.Medium, _time);

        TriggerMatchResult result = TriggerMatchResult.NoMatch;
        for (int i = 0; i < 50; i++)
        {
            _time.AdvanceMilliseconds(10);
            result = trigger.Evaluate(new TriggerEvent(
                TriggerEventType.MouseMove,
                new Point(i * 10, i * 10),
                _time.MonotonicTimestamp,
                0,
                null));
        }

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_ValidCircle_ReturnsMatchAndClearsHistory()
    {
        // High sensitivity has a 1000ms window and 270° threshold, giving the test more headroom.
        var trigger = new CircleGestureTrigger("CircleGesture", CircleSensitivity.High, _time);
        var center = new Point(200, 200);
        const int radius = 50;
        const int count = 32;

        TriggerMatchResult result = TriggerMatchResult.NoMatch;
        for (int i = 0; i < count; i++)
        {
            _time.AdvanceMilliseconds(20);
            double angle = 2 * Math.PI * i / count;
            var point = new Point(
                center.X + (int)(radius * Math.Cos(angle)),
                center.Y + (int)(radius * Math.Sin(angle)));
            result = trigger.Evaluate(new TriggerEvent(
                TriggerEventType.MouseMove,
                point,
                _time.MonotonicTimestamp,
                0,
                null));

            if (result.IsMatch)
                break;
        }

        Assert.True(result.IsMatch);
        Assert.Equal("CircleGesture", result.Context?.TriggerSource);

        // After a match the history is cleared; continuing movement should not immediately re-match.
        _time.AdvanceMilliseconds(100);
        var afterMatch = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseMove,
            center,
            _time.MonotonicTimestamp,
            0,
            null));
        Assert.False(afterMatch.IsMatch);
    }

    [Fact]
    public void Evaluate_PointsOutsideTimeWindow_ArePruned()
    {
        var trigger = new CircleGestureTrigger("CircleGesture", CircleSensitivity.Medium, _time);

        // Feed a valid circle but with points spaced beyond the 800ms Medium window.
        var center = new Point(200, 200);
        const int radius = 80;
        const int count = 64;
        for (int i = 0; i < count; i++)
        {
            _time.AdvanceMilliseconds(50); // 50ms * 64 = 3200ms > 800ms window
            double angle = 2 * Math.PI * i / count;
            var point = new Point(
                center.X + (int)(radius * Math.Cos(angle)),
                center.Y + (int)(radius * Math.Sin(angle)));
            trigger.Evaluate(new TriggerEvent(
                TriggerEventType.MouseMove,
                point,
                _time.MonotonicTimestamp,
                0,
                null));
        }

        TriggerMatchResult result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseMove,
            center,
            _time.MonotonicTimestamp,
            0,
            null));

        Assert.False(result.IsMatch);
    }
}