using System;
using System.Collections.Generic;
using MyQuicker.Services;

namespace MyQuicker.Tests.Fakes;

/// <summary>IProcessLauncher 的轻量级手写 Mock，可记录调用、解析指定路径或抛出指定异常。</summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly Exception? _exceptionToThrow;
    private readonly Dictionary<string, string> _resolvedPaths = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<(string FileName, string Arguments)> Launched => _launched;
    private readonly List<(string FileName, string Arguments)> _launched = new();

    public FakeProcessLauncher() { }

    public FakeProcessLauncher(Exception exceptionToThrow)
    {
        _exceptionToThrow = exceptionToThrow;
    }

    /// <summary>注册非绝对路径文件名解析后的绝对路径，供 TryResolveExecutablePath 返回。</summary>
    public void RegisterResolvedPath(string fileName, string resolvedPath)
    {
        _resolvedPaths[fileName] = resolvedPath;
    }

    public void Launch(string fileName, string arguments)
    {
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        _launched.Add((fileName, arguments));
    }

    public void Launch(string fileName, IEnumerable<string> argumentList)
    {
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        string joined = string.Join(" ", argumentList);
        _launched.Add((fileName, joined));
    }

    public bool TryResolveExecutablePath(string fileName, out string? executablePath)
    {
        if (_resolvedPaths.TryGetValue(fileName, out string? resolved))
        {
            executablePath = resolved;
            return true;
        }

        executablePath = null;
        return false;
    }
}
