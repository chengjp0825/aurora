using System;
using Aurora.Domain.Runtime.Commands;

namespace Aurora.Services;

/// <summary>注册不可变更的系统内建命令，确保 <see cref="CommandRegistry"/> 拥有稳定的 <code>sys:*</code> 命令。</summary>
public static class BuiltInCommandProvider
{
    /// <summary>将系统命令注册到指定注册中心。</summary>
    public static void Register(CommandRegistry registry)
    {
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));

        registry.Register("sys:snipping", new SnippingCommand());
    }
}
