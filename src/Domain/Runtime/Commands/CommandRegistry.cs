using System;
using System.Collections.Generic;

namespace MyQuicker.Domain.Runtime.Commands;

/// <summary>
/// 命令注册中心。维护命令 ID 到 <see cref="ICommand"/> 实例的 O(1) 映射。
/// 由 <see cref="MyQuicker.Services.BuiltInCommandProvider"/> 与 <see cref="MyQuicker.Services.UserCommandStore"/> 在启动时填充。
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);

    /// <summary>注册命令。</summary>
    /// <param name="key">命令 ID，与 ActionItem.Command 对应。</param>
    /// <param name="command">无状态命令实例。</param>
    public void Register(string key, ICommand command)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        _commands[key] = command;
    }

    /// <summary>按命令 ID 查找命令；未找到时返回 null。</summary>
    public ICommand? Lookup(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        _commands.TryGetValue(key, out ICommand? command);
        return command;
    }
}
