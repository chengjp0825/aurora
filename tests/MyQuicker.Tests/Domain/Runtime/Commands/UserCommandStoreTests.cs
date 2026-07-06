using System;
using System.Collections.Generic;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class UserCommandStoreTests
{
    [Fact]
    public void Register_AddsLaunchAndUrlCommands()
    {
        var registry = new CommandRegistry();
        var commands = new List<CommandDefinition>
        {
            new() { Id = "cmd:app", Type = CommandType.LaunchApplication, Target = "C:\\app.exe" },
            new() { Id = "cmd:url", Type = CommandType.OpenUrl, Target = "https://example.com" },
        };

        UserCommandStore.Register(registry, commands);

        Assert.IsType<LaunchApplicationCommand>(registry.Lookup("cmd:app"));
        Assert.IsType<OpenUrlCommand>(registry.Lookup("cmd:url"));
    }

    [Fact]
    public void Register_SkipsEmptyOrNullCommandIds()
    {
        var registry = new CommandRegistry();
        var commands = new List<CommandDefinition>
        {
            new() { Id = "", Type = CommandType.LaunchApplication, Target = "C:\\app.exe" },
            new() { Id = null!, Type = CommandType.OpenUrl, Target = "https://example.com" },
            new() { Id = "   ", Type = CommandType.LaunchApplication, Target = "C:\\app2.exe" },
            new() { Id = "cmd:valid", Type = CommandType.LaunchApplication, Target = "C:\\valid.exe" },
        };

        UserCommandStore.Register(registry, commands);

        Assert.Null(registry.Lookup(""));
        Assert.Null(registry.Lookup(null!));
        Assert.Null(registry.Lookup("   "));
        Assert.NotNull(registry.Lookup("cmd:valid"));
    }

    [Fact]
    public void Register_NullRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UserCommandStore.Register(null!, Array.Empty<CommandDefinition>()));
    }

    [Fact]
    public void Register_NullCommands_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UserCommandStore.Register(new CommandRegistry(), null!));
    }
}
