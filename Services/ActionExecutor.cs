using System;
using System.Collections.Generic;
using System.Diagnostics;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime.Commands;

namespace MyQuicker.Services;

/// <summary>
/// 动作执行调度中心：根据 <see cref="ActionItem.CommandId"/> 从 <see cref="CommandRegistry"/> 中检索命令，
/// 将 DTO 字段转换为纯粹参数后执行，并统一捕获异常包装为 <see cref="ActionResult"/>。
/// </summary>
public sealed class ActionExecutor
{
    private readonly CommandRegistry _registry;
    private readonly Dictionary<string, CommandDefinition> _commandCatalog;

    public ActionExecutor(CommandRegistry registry, IEnumerable<CommandDefinition> commands)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (commands is null)
            throw new ArgumentNullException(nameof(commands));

        _commandCatalog = new Dictionary<string, CommandDefinition>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            if (!string.IsNullOrWhiteSpace(command.Id))
                _commandCatalog[command.Id] = command;
        }
    }

    /// <summary>执行动作。</summary>
    public ActionResult Execute(CommandContext ctx, ActionItem item)
    {
        return ResolveAsync(ctx, item).GetAwaiter().GetResult();
    }

    /// <summary>异步执行动作，避免 UI 线程被截图/启动等物理操作阻塞。</summary>
    public Task<ActionResult> ExecuteAsync(CommandContext ctx, ActionItem item)
    {
        return ResolveAsync(ctx, item);
    }

    private async Task<ActionResult> ResolveAsync(CommandContext ctx, ActionItem item)
    {
        if (string.IsNullOrWhiteSpace(item.CommandId))
        {
            return new ActionResult(
                ActionOutcomeKind.EmptyCommand,
                string.IsNullOrWhiteSpace(item.Name)
                    ? "动作未配置命令"
                    : $"动作「{item.Name}」未配置命令");
        }

        ICommand? command = _registry.Lookup(item.CommandId);
        if (command is null)
        {
            // 保留 sys: 前缀的语义：未注册的内建指令视为未知内建指令。
            if (item.CommandId.StartsWith("sys:", StringComparison.Ordinal))
            {
                return new ActionResult(
                    ActionOutcomeKind.UnknownSystemCommand,
                    $"未知指令：{item.CommandId}");
            }

            return new ActionResult(
                ActionOutcomeKind.LaunchFailed,
                $"无法启动：{item.CommandId}",
                ErrorCommand: item.CommandId);
        }

        try
        {
            Dictionary<string, string> parameters = BuildParameters(item);
            return await command.ExecuteAsync(ctx, parameters).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 执行动作失败 ({item.CommandId}): {ex.Message}");
            return new ActionResult(
                ActionOutcomeKind.LaunchFailed,
                $"无法启动：{item.CommandId}",
                ErrorCommand: item.CommandId);
        }
    }

    private Dictionary<string, string> BuildParameters(ActionItem item)
    {
        _commandCatalog.TryGetValue(item.CommandId, out CommandDefinition? definition);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = item.Name ?? string.Empty,
            ["commandId"] = item.CommandId,
            ["target"] = definition?.Target ?? item.CommandId,
            ["arguments"] = item.Arguments ?? string.Empty,
            ["icon"] = item.Icon ?? string.Empty,
        };
    }
}
