# MyQuicker MSI 安装包 + 自动更新配置

> 方案：WiX Toolset v4 生成 PerUser MSI（无需 UAC，装到 LocalAppData）+ 应用内检查 GitHub Releases，下载新 MSI 用 `msiexec` 静默升级。
>
> MSI 的 `MajorUpgrade` 允许新版本覆盖旧版本，实现"装新即升级"。

---

## 1. 前置：WiX Toolset v4

WiX v4 以 `dotnet tool` 形式分发，本地与 CI 一致。

```bash
dotnet tool install -g wix
# 验证
wix --version
```

> WiX v4 文档：https://wixtoolset.org/docs/v4/

---

## 2. MSI 配置：`installer/MyQuicker.wxs`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">

  <Package
    Name="MyQuicker"
    Language="2052"
    Codepage="936"
    Version="$(var.Version)"
    Manufacturer="your-name"
    UpgradeCode="8F2C4D6E-1234-5678-9ABC-DEF012345678"
    InstallScope="perUser"
    InstallPrivileges="limited">

    <!-- 允许新版本覆盖旧版本（升级）；阻止降级 -->
    <MajorUpgrade
      AllowSameVersion="yes"
      DowngradeErrorMessage="已安装更新版本的 MyQuicker，无法降级。" />

    <Media Id="1" Cabinet="MyQuicker.cab" EmbedCab="yes" />

    <!-- 安装目录选择 UI（PerUser 下默认 LocalAppData/MyQuicker） -->
    <Property Id="WIXUI_INSTALLDIR" Value="INSTALLFOLDER" />
    <ui:WixUI Id="WixUI_InstallDir" />
    <ui:WixUIRef Id="WixUI_ErrorProgressText" />

    <Feature Id="Complete" Title="MyQuicker" Level="1">
      <ComponentGroupRef Id="ProductComponents" />
    </Feature>
  </Package>

  <Fragment>
    <!-- PerUser：装到 %LocalAppData%\MyQuicker，无需管理员 -->
    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="INSTALLFOLDER" Name="MyQuicker" />
    </StandardDirectory>
  </Fragment>

  <Fragment>
    <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">

      <!-- 主程序 exe（来自 dotnet publish 输出） -->
      <Component Id="MainExe" Guid="A1B2C3D4-1111-2222-3333-444455556666">
        <File Id="MyQuickerExe" Source="$(var.PublishDir)MyQuicker.exe" KeyPath="yes" />
      </Component>

      <!-- 开机自启：写 HKCU Run 键 -->
      <Component Id="AutostartReg" Guid="A1B2C3D4-7777-8888-9999-AAAABBBBCCCC">
        <RegistryValue Root="HKCU"
          Key="Software\Microsoft\Windows\CurrentVersion\Run"
          Name="MyQuicker"
          Value="[INSTALLFOLDER]MyQuicker.exe"
          Type="string" KeyPath="yes" />
      </Component>

      <!-- 开始菜单快捷方式 -->
      <Component Id="StartMenuShortcut" Guid="A1B2C3D4-DDDD-EEEE-FFFF-111122223333">
        <Shortcut Id="StartMenuShortcut"
          Name="MyQuicker"
          Target="[INSTALLFOLDER]MyQuicker.exe"
          WorkingDirectory="INSTALLFOLDER" />
        <RegistryValue Root="HKCU" Key="Software\MyQuicker" Name="Shortcut"
          Type="string" Value="1" KeyPath="yes" />
      </Component>

    </ComponentGroup>
  </Fragment>
</Wix>
```

### 关键元素说明

| 元素 | 作用 |
|------|------|
| `UpgradeCode` | **固定不变**的 GUID，标识同一产品族，升级靠它匹配旧版本。必须真实生成一个 |
| `Version="$(var.Version)"` | MSI 三段版本（如 `1.0.0`），由构建脚本注入 |
| `InstallScope="perUser"` | 装到用户目录，无需 UAC |
| `MajorUpgrade` | 新版本覆盖旧版本；`AllowSameVersion` 允许同版本重装 |
| `LocalAppDataFolder` | PerUser 安装位置 |
| `AutostartReg` | 注册表 Run 键实现开机自启 |
| `ui:WixUI Id="WixUI_InstallDir"` | 安装向导含目录选择 |

> ⚠️ **3 个 Component GUID 和 UpgradeCode 必须替换为真实 GUID**（用 `uuidgen` 或 VS → 工具→创建 GUID）。UpgradeCode 一旦发布永不更改。

### 安装后目录结构（默认路径）

> 以下基于 `installer/` 实际实现（`AssemblyName=aurora`、self-contained 展开式发布）。下方第 3 节的示例脚本用 `MyQuicker` 命名仅作方案演示，以 `installer/build-msi.ps1` 为权威来源。

**安装位置**：默认 `%LocalAppData%\aurora\`，用户可在安装向导的目录选择步改为任意路径（`WixUI_InstallDir`）。PerUser 下选需管理员的目录（如 `C:\Program Files\`）会因无 UAC 提升而失败。

**目录结构**：

```
%LocalAppData%\aurora\
├── aurora.exe                       ← 主程序（WinExe，Release 无控制台）
├── aurora.dll                       ← 应用主体程序集
├── aurora.deps.json                 ← 依赖描述
├── aurora.runtimeconfig.json        ← 运行时配置
├── coreclr.dll / clrjit.dll /       ┐
│   clrgc.dll / hostfxr.dll /        │ .NET 8 运行时（self-contained，免装 Runtime）
│   hostpolicy.dll / ...             │
├── PresentationFramework.dll /      │ WPF 框架
│   PresentationCore.dll / ...       │
├── System.Windows.Forms.*.dll       │ WinForms（仅 Screen / NotifyIcon 用）
├── System.*.dll / Microsoft.*.dll   ┘ BCL + 框架库
│   （根目录共 241 个文件）
└── zh-Hans\        ← 仅简中卫星资源（17 个 *.resources.dll）
```

`build-msi.ps1` 采用 **self-contained 展开式**：`--self-contained true` 把 .NET Runtime / WPF / WinForms / BCL 全部随包输出，用户机器无需预装 .NET Runtime，开箱即用；每个 dll 独立可见，文件结构清晰。MSI 体积约 54 MB。

- 语言包：`SatelliteResourceLanguages=zh-Hans` 仅保留简中，其余 12 种语言目录（cs/de/es/fr/it/ja/ko/pl/pt-BR/ru/tr/zh-Hant）不输出。
- `createdump.exe`（.NET 崩溃诊断工具）在 publish 后删除，不进安装目录。

**安装时附带的非文件项**：

| 项 | 位置 | 作用 |
|------|------|------|
| 开机自启键 | `HKCU\…\Run\aurora = [INSTALLFOLDER]aurora.exe` | 登录自启 |
| 开始菜单快捷方式 | `…\Start Menu\Programs\aurora\aurora.lnk` | 指向 aurora.exe |
| 卸载快捷方式 | 安装目录内 `卸载 aurora.lnk` | 调 `msiexec /x [ProductCode]` |
| 控制面板条目 | 程序和功能 | 标准 MSI 卸载入口 |

自启键与快捷方式均用 `[INSTALLFOLDER]aurora.exe` 引用，用户改安装目录后自动跟随，不写死路径。

**运行时生成（不在 MSI 内）**：`settings.json`（首次启动由 `SettingsManager` 在 exe 同目录生成）、`settings.json.tmp`（原子写临时文件）、`debug.log`（仅 Debug 配置生成，Release 不产生）、旧版 `appsettings.json`。这些运行时文件**纯卸载时由 `CleanupRuntimeFiles` CustomAction 自动清理**（条件 `REMOVE="ALL" AND NOT WIX_UPGRADE_DETECTED`）；**升级时保留**，以延续用户设置（见 §7 升级流程）。

**升级路径坑**：`WixUI_InstallDir` 默认不读旧安装路径。`UpdateChecker` 走 `msiexec /i … /qb` 静默升级时，`INSTALLFOLDER` 会回到 wxs 默认值 `%LocalAppData%\aurora`。若用户当初改过路径，升级后旧版本被 `MajorUpgrade` 卸载、新版本落到默认路径，自定义选择丢失。要保留需在升级命令显式传 `INSTALLFOLDER=<旧路径>`。

---

## 3. 构建脚本：`installer/build-msi.ps1`

```powershell
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# 1. 发布单文件 exe
dotnet publish "$root/MyQuicker.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `
  -o "$root/publish"

# 2. 确保 wix 工具
$tools = dotnet tool list -g
if ($tools -notmatch '\bwix\b') { dotnet tool install -g wix }

# 3. 构建 MSI（需 UI 扩展）
wix build "$root/installer/MyQuicker.wxs" `
  -o "$root/publish/MyQuicker-$Version.msi" `
  -d "Version=$Version" -d "PublishDir=$root/publish/" `
  -ext WixToolset.UI.wixext

# 4. 校验
if (Test-Path "$root/publish/MyQuicker-$Version.msi") {
  $hash = (Get-FileHash "$root/publish/MyQuicker-$Version.msi" -Algorithm SHA256).Hash
  "$hash  MyQuicker-$Version.msi" | Out-File "$root/publish/MyQuicker-$Version.msi.sha256"
  Write-Host "MSI 生成成功: publish/MyQuicker-$Version.msi"
} else { throw "MSI 构建失败" }
```

本地用法：`./installer/build-msi.ps1 -Version 1.0.0`

---

## 4. 自动更新：`src/Services/UpdateChecker.cs`

应用内检查 GitHub Releases，发现新版则下载 MSI 并 `msiexec` 静默安装（依赖第 2 节的 `MajorUpgrade` 覆盖升级）。

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace MyQuicker.Services;

/// <summary>检查 GitHub Releases 最新版本并静默升级（MSI 覆盖安装）。</summary>
public sealed class UpdateChecker
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/your-name/MyQuicker/releases/latest";

    private static readonly HttpClient _http = new HttpClient();

    /// <summary>检查是否有新版本；无则返回 null。</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version
                      ?? new Version(0, 0, 0);

        var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        req.Headers.UserAgent.ParseAdd("MyQuicker-Updater");
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

    /// <summary>下载新 MSI 并静默安装（覆盖升级），完成后重启应用。</summary>
    public async Task ApplyAsync(UpdateInfo info)
    {
        var tmpMsi = Path.Combine(Path.GetTempPath(), $"MyQuicker-{info.Version}.msi");
        using var resp = await _http.GetAsync(info.MsiUrl).ConfigureAwait(false);
        await using var fs = File.Create(tmpMsi);
        await resp.Content.CopyToAsync(fs).ConfigureAwait(false);

        // /qb = 基本 UI（进度条）；/norestart = 不自动重启系统；新 MSI 的 MajorUpgrade 覆盖旧版本
        Process.Start("msiexec", $"/i \"{tmpMsi}\" /qb /norestart")?.WaitForExit();
    }

    private static Version ParseTag(string? tag)
    {
        var s = (tag ?? "0.0.0").TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
    }
}

public sealed record UpdateInfo(Version Version, string MsiUrl);
```

### 集成到 `App.OnStartup`（后台非阻塞）

在 `App.xaml.cs` 的 `OnStartup` 末尾追加（仅 Release）：

```csharp
#if !DEBUG
    _ = CheckForUpdatesAsync();
#endif

private async Task CheckForUpdatesAsync()
{
    try
    {
        var checker = new UpdateChecker();
        var info = await checker.CheckAsync().ConfigureAwait(false);
        if (info is null) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (System.Windows.MessageBox.Show(
                    $"发现新版本 {info.Version}，是否立即更新？",
                    "MyQuicker 更新", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _ = checker.ApplyAsync(info).ContinueWith(_ => Shutdown());
            }
        });
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Update] check failed: {ex.Message}");
    }
}
```

> 更新流程：启动后台查 → 有新版弹框 → 用户同意 → 下载 MSI → `msiexec /qb` 静默覆盖安装 → 旧进程退出，新版自启（开机自启键或安装后用户启动）。

---

## 5. CI：`.github/workflows/release.yml`

tag 推送触发：测试 → 发布 exe → WiX 打包 MSI → 上传 Release。

```yaml
name: Release
on:
  push:
    tags: ['v*']
  workflow_dispatch:

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build & Test
        run: |
          dotnet build --configuration Release
          dotnet test --configuration Release --no-build

      - name: Publish exe
        run: |
          dotnet publish MyQuicker.csproj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `
            -o publish

      - name: Install WiX
        run: dotnet tool install -g wix

      - name: Build MSI
        run: |
          $version = "${{ github.ref_name }}".TrimStart('v')
          wix build installer/MyQuicker.wxs -o "publish/MyQuicker-$version.msi" `
            -d "Version=$version" -d "PublishDir=publish/" -ext WixToolset.UI.wixext
          $hash = (Get-FileHash "publish/MyQuicker-$version.msi" -Algorithm SHA256).Hash
          "$hash  MyQuicker-$version.msi" | Out-File "publish/MyQuicker-$version.msi.sha256"

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: MyQuicker-msi
          path: publish/*.msi*

      - name: Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v2
        with:
          files: |
            publish/*.msi
            publish/*.msi.sha256
```

发布流程：`git tag v1.0.0 && git push origin v1.0.0` → CI 自动构建 MSI 并发布到 GitHub Releases → 用户应用启动时检测到新版本自动升级。

---

## 6. 版本号管理

MSI `ProductVersion` 只支持三段（`Major.Minor.Build`），与 csproj 的 `VersionPrefix` 对齐。

| 来源 | 格式 | 示例 |
|------|------|------|
| csproj `VersionPrefix` | 三段 | `1.0.0` |
| Git tag | `v` 前缀 | `v1.0.0` |
| MSI `Version` | 三段 | `1.0.0` |
| `AssemblyVersion` | 四段 | `1.0.0.0` |

> CI 从 `${{ github.ref_name }}`（tag）去 `v` 前缀得 MSI 版本，保持一致。

---

## 7. 升级流程（MajorUpgrade 原理）

1. 用户装 v1.0.0 → MSI 记录 ProductCode + UpgradeCode 到 `HKCU\Software\...\Uninstall`。
2. 发布 v1.1.0 → 用户应用启动检查到新版 → 下载 v1.1.0.msi。
3. `msiexec /i v1.1.0.msi /qb` → MSI 检测到同 UpgradeCode、更高 Version → `MajorUpgrade` 先卸载 v1.0.0 再装 v1.1.0。
4. 旧 exe 被替换（开机自启键仍指向同路径 `[INSTALLFOLDER]MyQuicker.exe`，路径不变）。

> ⚠️ 升级时旧 exe 必须能被覆盖。`MyQuicker.exe` 若正在运行会锁文件。`ApplyAsync` 先 `Shutdown()` 退出旧进程，再 `msiexec` 安装。代码里 `ContinueWith(_ => Shutdown())` 即此目的，但时序上建议先 Shutdown 再 msiexec——见下方优化。

### 时序优化（推荐）

```csharp
public async Task ApplyAsync(UpdateInfo info)
{
    var tmpMsi = Path.Combine(Path.GetTempPath(), $"MyQuicker-{info.Version}.msi");
    using var resp = await _http.GetAsync(info.MsiUrl).ConfigureAwait(false);
    await using var fs = File.Create(tmpMsi);
    await resp.Content.CopyToAsync(fs).ConfigureAwait(false);

    // 先退出当前进程，再静默安装（避免 exe 锁文件）
    var psi = new ProcessStartInfo("msiexec", $"/i \"{tmpMsi}\" /qb /norestart")
    {
        UseShellExecute = false,
    };
    Process.Start(psi);
    Environment.Exit(0); // 立即退出，让 msiexec 接管
}
```

---

## 8. 测试清单

- [ ] 本地 `./installer/build-msi.ps1 -Version 1.0.0` 生成 MSI
- [ ] 双击 MSI 安装 → `%LocalAppData%\MyQuicker\MyQuicker.exe` 存在
- [ ] 开始菜单出现 MyQuicker 快捷方式
- [ ] 开机自启键写入 `HKCU\...\Run`
- [ ] 控制面板「程序和功能」出现 MyQuicker 条目
- [ ] 安装后 exe 运行，钩子/托盘/截图/设置全功能
- [ ] 升级测试：装 v1.0.0 → 用 v1.1.0 MSI 覆盖 → 旧版本被替换，设置（settings.json）保留
- [ ] 降级测试：v1.1.0 已装 → 装 v1.0.0 → 提示无法降级
- [ ] 自动更新：GitHub 发新 Release → 旧版启动检测到 → 下载 MSI 静默升级
- [ ] 卸载：exe、快捷方式、自启键、注册表条目全部清除
- [ ] 干净 Windows（无 .NET）环境验证

---

## 9. 可选增强

| 增强 | 说明 |
|------|------|
| 数字签名 | `signtool sign` 签 MSI + exe，消除 SmartScreen 警告（见 publish-guide §5） |
| 增量更新 | MSI 不支持 delta；若需小体积增量，改用 Velopack（但放弃 MSI） |
| 后台静默更新 | `msiexec /qn`（无 UI），不弹框直接升级 |
| 更新通道 | stable/beta：GitHub Releases 用 prerelease 标记区分 |
| 卸载时清理 | ✅ 已实现：纯卸载时 `CleanupRuntimeFiles` 清理运行时文件（settings.json/.tmp/debug.log/appsettings.json），升级时保留（见 §2） |
| 多语言 | WiX `ui:WixUI` 多语种资源（当前简中 2052/936） |

---

## 附：与 publish-guide 的关系

本文档是 `docs/publish-guide.md` 第 4 节（安装包）和第 6 节（自动更新）的 **MSI 方案落地**。其余（版本号、签名、CI 渠道、许可证、检查清单）仍以 publish-guide 为准。

发布前补齐：
1. csproj 版本号 + `ApplicationIcon`
2. `LICENSE`
3. `installer/MyQuicker.wxs`（替换 4 个 GUID）
4. `src/Services/UpdateChecker.cs`
5. `.github/workflows/release.yml`
