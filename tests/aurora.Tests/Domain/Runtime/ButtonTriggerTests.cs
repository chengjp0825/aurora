using Aurora.Domain.Runtime;
using Aurora.Interop;
using Xunit;

namespace Aurora.Tests.Domain.Runtime;

public class ButtonTriggerTests
{
    [Fact]
    public void Evaluate_MiddleButtonDown_ReturnsMatch()
    {
        var trigger = new ButtonTrigger("MiddleButton", NativeMethods.WM_MBUTTONDOWN, null);

        var result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(10, 20),
            12345,
            NativeMethods.WM_MBUTTONDOWN,
            null));

        Assert.True(result.IsMatch);
        Assert.Equal("MiddleButton", result.Context?.TriggerSource);
        Assert.Equal(new Point(10, 20), result.Context?.Location);
    }

    [Fact]
    public void Evaluate_MouseMove_ReturnsNoMatch()
    {
        var trigger = new ButtonTrigger("MiddleButton", NativeMethods.WM_MBUTTONDOWN, null);

        var result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseMove,
            new Point(10, 20),
            12345,
            NativeMethods.WM_MBUTTONDOWN,
            null));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_WrongMessage_ReturnsNoMatch()
    {
        var trigger = new ButtonTrigger("MiddleButton", NativeMethods.WM_MBUTTONDOWN, null);

        var result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(10, 20),
            12345,
            NativeMethods.WM_RBUTTONDOWN,
            null));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_XButtonWithMatchingData_ReturnsMatch()
    {
        var trigger = new ButtonTrigger("XButton2", NativeMethods.WM_XBUTTONDOWN, 2);

        var result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(0, 0),
            0,
            NativeMethods.WM_XBUTTONDOWN,
            2));

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Evaluate_XButtonWithMismatchedData_ReturnsNoMatch()
    {
        var trigger = new ButtonTrigger("XButton2", NativeMethods.WM_XBUTTONDOWN, 2);

        var result = trigger.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(0, 0),
            0,
            NativeMethods.WM_XBUTTONDOWN,
            1));

        Assert.False(result.IsMatch);
    }
}