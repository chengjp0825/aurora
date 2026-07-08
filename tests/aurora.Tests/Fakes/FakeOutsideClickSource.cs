using System;
using Aurora.Domain.Runtime;

namespace Aurora.Tests.Fakes;

/// <summary>WakeOrchestrator 测试用手动外部点击源。</summary>
internal sealed class FakeOutsideClickSource : IOutsideClickSource
{
    public event EventHandler? OutsideClick;

    public void RaiseOutsideClick() => OutsideClick?.Invoke(this, EventArgs.Empty);
}
