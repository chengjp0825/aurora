using System;
using System.Collections.Generic;
using System.Security;
using Aurora.Services;

namespace Aurora.Domain.Runtime.Commands;

/// <summary>使用默认浏览器打开 HTTP/HTTPS 链接的命令。</summary>
public sealed class OpenUrlCommand : ICommand
{
    public ActionResult Execute(CommandContext context, Dictionary<string, string> parameters)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (!parameters.TryGetValue("target", out string? url) || string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("缺少 URL（parameters['target']）。", nameof(parameters));

        string trimmed = url.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException($"不是有效的 URL：{trimmed}", nameof(parameters));

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"禁止的 URL Scheme：{uri.Scheme}");
        }

        context.ProcessLauncher.Launch(trimmed, string.Empty);
        return new ActionResult(ActionOutcomeKind.StartedProcess);
    }
}
