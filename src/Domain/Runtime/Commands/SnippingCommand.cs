using System;
using System.Collections.Generic;
using Aurora.Services;

namespace Aurora.Domain.Runtime.Commands;

/// <summary>发起屏幕截图工作流。</summary>
public sealed class SnippingCommand : ICommand
{
    /// <inheritdoc/>
    public ActionResult Execute(CommandContext context, Dictionary<string, string> parameters)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (context.ScreenshotWorkflow is null)
            return new ActionResult(ActionOutcomeKind.LaunchFailed, "截图工作流未配置");

        // fire-and-forget：截图覆盖层是长时间、可取消的 UI 流程，命令立即返回 StartedProcess。
        _ = context.ScreenshotWorkflow.RunAsync();
        return new ActionResult(ActionOutcomeKind.StartedProcess);
    }

    /// <inheritdoc/>
    public Task<ActionResult> ExecuteAsync(CommandContext context, Dictionary<string, string> parameters)
    {
        return Task.FromResult(Execute(context, parameters));
    }
}
