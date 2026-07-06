using System;
using System.Collections.Generic;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime.Commands;

namespace MyQuicker.Services;

/// <summary>
/// Loads user-defined commands from the stable <see cref="Settings.Commands"/> catalog
/// and registers them in the <see cref="CommandRegistry"/>.
/// </summary>
public static class UserCommandStore
{
    public static void Register(CommandRegistry registry, IEnumerable<CommandDefinition> commands)
    {
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));
        if (commands is null)
            throw new ArgumentNullException(nameof(commands));

        foreach (CommandDefinition command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id))
                continue;

            ICommand runtimeCommand = command.Type switch
            {
                CommandType.OpenUrl => new OpenUrlCommand(),
                _ => new LaunchApplicationCommand(),
            };

            registry.Register(command.Id, runtimeCommand);
        }
    }
}
