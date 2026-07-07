namespace MyQuicker.Services;

/// <summary>
/// 外部进程启动抽象：让 Command 执行逻辑可通过测试替身替换，
/// 并避免在核心服务中直接耦合 <see cref="System.Diagnostics.Process"/>。
/// </summary>
public interface IProcessLauncher
{
    /// <summary>启动指定程序，参数为空字符串时表示无参数。</summary>
    void Launch(string fileName, string arguments);

    /// <summary>启动指定程序，参数以显式数组传递。</summary>
    void Launch(string fileName, IEnumerable<string> argumentList);

    /// <summary>
    /// 尝试将 <paramref name="fileName"/> 解析为可安全启动的绝对路径。
    /// 支持绝对路径或 PATH 环境变量中的白名单可执行文件。
    /// </summary>
    /// <returns>解析成功返回 true 并输出绝对路径；否则返回 false。</returns>
    bool TryResolveExecutablePath(string fileName, out string? executablePath);
}
