using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Aurora.Services;

/// <summary>
/// 检查 GitHub Releases 最新版本并下载 MSI 静默升级。
/// 仅负责网络与安装；UI 提示（弹框）由调用方 App 处理，避免 Services 层引用 WPF。
/// 升级依赖 MSI 的 MajorUpgrade：同 UpgradeCode + 高 Version 覆盖旧版本。
/// </summary>
public sealed class UpdateChecker
{
    // 真实发布仓库地址（owner/repo = chengjp0825/aurora）；与 CI 产物 aurora-<v>.msi 命名一致。
    private const string ReleasesUrl =
        "https://api.github.com/repos/chengjp0825/aurora/releases/latest";

    private static readonly HttpClient _http = new HttpClient();

    /// <summary>检查是否有新版本；无新版本返回 null。</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        req.Headers.UserAgent.ParseAdd("aurora-Updater");
        using var resp = await _http.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString(); // "v1.0.0"
        var latest = ParseTag(tag);
        if (latest <= current) return null;

        // 找 .msi 资产
        string? msiUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name is not null && name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                msiUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        return msiUrl is null ? null : new UpdateInfo(latest, msiUrl);
    }

    /// <summary>
    /// 下载新 MSI 并静默安装（覆盖升级），完成后退出当前进程让 msiexec 接管。
    /// 必须先退出旧进程，否则运行中的 exe 锁文件导致覆盖失败。
    /// </summary>
    public async Task ApplyAsync(UpdateInfo info)
    {
        var tmpMsi = Path.Combine(Path.GetTempPath(), $"aurora-{info.Version}.msi");
        using var resp = await _http.GetAsync(info.MsiUrl).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(tmpMsi);
        await resp.Content.CopyToAsync(fs).ConfigureAwait(false);

        // /qb = 基本 UI（进度条）；/norestart = 不重启系统；MajorUpgrade 覆盖旧版本。
        Process.Start(new ProcessStartInfo("msiexec", $"/i \"{tmpMsi}\" /qb /norestart")
        {
            UseShellExecute = false,
        });
        Environment.Exit(0);
    }

    private static Version ParseTag(string? tag)
    {
        var s = (tag ?? "0.0.0").TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
    }
}

/// <summary>更新信息：新版本号 + MSI 下载地址。</summary>
public sealed record UpdateInfo(Version Version, string MsiUrl);
