using System;
using Aurora.Domain.DTO;
using Aurora.Domain.Runtime.Commands;
using Aurora.Services;
using Xunit;

namespace Aurora.Tests.Domain.Runtime.Commands;

public class BuiltInCommandProviderTests
{
    [Fact]
    public void Register_ProvidesSysSnippingCommand()
    {
        var registry = new CommandRegistry();

        BuiltInCommandProvider.Register(registry);

        var command = registry.Lookup("sys:snipping");
        Assert.NotNull(command);
        Assert.IsType<SnippingCommand>(command);
    }

    [Fact]
    public void Register_NullRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BuiltInCommandProvider.Register(null!));
    }
}
