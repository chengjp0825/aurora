using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using MyQuicker.Services;

namespace MyQuicker.Domain.Runtime.Commands;

/// <summary>启动本地应用程序的命令。</summary>
public sealed class LaunchApplicationCommand : ICommand
{
    // 禁止出现在可执行路径中的 shell 元字符与危险字符。
    // 这些字符在 ArgumentList 模式下本无 shell 语义，但出现在路径中通常意味着配置被篡改或注入。
    private static readonly char[] DangerousPathChars = new[]
    {
        '&', '|', ';', '<', '>', '$', '`', '\n', '\r', '\0'
    };

    public ActionResult Execute(CommandContext context, Dictionary<string, string> parameters)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (!parameters.TryGetValue("target", out string? path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("缺少启动目标路径（parameters['target']）。", nameof(parameters));

        string trimmed = path.Trim();

        if (trimmed.IndexOfAny(DangerousPathChars) >= 0)
            throw new SecurityException($"启动路径包含非法字符：{trimmed}");

        if (!Path.IsPathFullyQualified(trimmed))
            throw new SecurityException($"启动路径必须是绝对路径：{trimmed}");

        parameters.TryGetValue("arguments", out string? arguments);

        context.ProcessLauncher.Launch(trimmed, arguments ?? string.Empty);
        return new ActionResult(ActionOutcomeKind.StartedProcess);
    }
}
