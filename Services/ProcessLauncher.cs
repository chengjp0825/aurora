using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace Aurora.Services;

/// <summary><see cref="IProcessLauncher"/> 的默认实现：安全地启动外部程序。</summary>
/// <remarks>
/// 安全边界：
/// 1. 禁止 <see cref="UseShellExecute"/> = true 执行任意字符串（封杀命令注入）。
/// 2. 对可执行文件使用 <see cref="UseShellExecute"/> = false + <see cref="ProcessStartInfo.ArgumentList"/>。
/// 3. FileName 必须是：现有文件的绝对路径、白名单中的可执行文件名，或 http/https URL。
/// 4. 参数通过安全解析拆分为数组，避免 shell 元字符（&amp;、|、;、$ 等）被解释。
/// </remarks>
internal sealed class ProcessLauncher : IProcessLauncher
{
    // 允许直接通过文件名启动的安全 GUI 程序白名单。脚本解释器（cmd、powershell 等）不得加入，
    // 否则通过 ArgumentList 传入 /c 或 -EncodedCommand 仍可恢复任意代码执行。
    private static readonly HashSet<string> AllowedExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "notepad.exe",
        "mspaint.exe",
        "calc.exe",
        "explorer.exe",
    };

    private static readonly HashSet<string> AllowedExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".com",
    };

    private static readonly HashSet<string> AllowedUrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
    };

    /// <summary>启动指定程序，参数为空字符串时表示无参数。</summary>
    public void Launch(string fileName, string arguments)
    {
        IReadOnlyList<string> argumentList = ParseArguments(arguments);
        Launch(fileName, argumentList);
    }

    /// <summary>启动指定程序，参数以显式数组传递。</summary>
    public void Launch(string fileName, IEnumerable<string> argumentList)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("启动命令不能为空。", nameof(fileName));

        string trimmedFileName = fileName.Trim();

        // 1. 严格校验的 URL Scheme：唯一允许 UseShellExecute=true 的场景，
        //    因为操作系统需要调用默认浏览器/协议处理程序。
        if (Uri.TryCreate(trimmedFileName, UriKind.Absolute, out Uri? uri) && AllowedUrlSchemes.Contains(uri.Scheme))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            return;
        }

        // 2. 可执行文件路径校验。
        if (!TryResolveExecutablePath(trimmedFileName, out string? executablePath) || executablePath is null)
            throw new SecurityException($"禁止启动未经验证的程序：{trimmedFileName}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };

        foreach (string arg in argumentList ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(arg))
                startInfo.ArgumentList.Add(arg);
        }

        Process.Start(startInfo);
    }

    /// <inheritdoc/>
    public bool TryResolveExecutablePath(string fileName, out string? executablePath)
    {
        executablePath = ResolveExecutablePath(fileName);
        return executablePath is not null;
    }

    /// <summary>
    /// 将 FileName 解析为可安全启动的绝对路径；无法解析或校验失败时返回 null。
    /// </summary>
    private static string? ResolveExecutablePath(string fileName)
    {
        // 必须是绝对路径或白名单中的文件名；拒绝相对路径与 shell 元字符命令。
        string? candidatePath = null;

        if (Path.IsPathFullyQualified(fileName))
        {
            candidatePath = fileName;
        }
        else if (AllowedExecutableNames.Contains(fileName))
        {
            candidatePath = FindInPath(fileName);
        }

        if (candidatePath is null)
            return null;

        if (!File.Exists(candidatePath))
            return null;

        string extension = Path.GetExtension(candidatePath);
        if (!AllowedExecutableExtensions.Contains(extension))
            return null;

        return candidatePath;
    }

    private static string? FindInPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.Combine(dir, fileName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    /// <summary>
    /// 将参数字符串安全解析为数组，供 <see cref="ProcessStartInfo.ArgumentList"/> 使用。
    /// 不依赖 shell，因此 &amp;、|、; 等字符只是普通字符。
    /// </summary>
    private static IReadOnlyList<string> ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return Array.Empty<string>();

        var result = new List<string>();
        var sb = new StringBuilder();
        int i = 0;

        while (i < arguments!.Length)
        {
            // 跳过前导空白
            while (i < arguments.Length && char.IsWhiteSpace(arguments[i]))
                i++;

            if (i >= arguments.Length)
                break;

            sb.Clear();
            bool inQuotes = false;
            bool hasToken = false;

            while (i < arguments.Length)
            {
                char c = arguments[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                    i++;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                    hasToken = true;
                    i++;
                }
            }

            if (hasToken)
                result.Add(sb.ToString());
        }

        return result;
    }
}
