# aurora MSI 安装包 + 自动更新配置

> 方案：WiX Toolset v4 生成 PerUser MSI（无需 UAC，装到 LocalAppData）+ 应用内检查 GitHub Releases，下载新 MSI 用 `msiexec` 静默升级。
>
> **权威实现以仓库文件为准**：`installer/aurora.wxs`、`installer/build-msi.ps1`、`src/Services/UpdateChecker.cs`、`.github/workflows/release.yml`。本文档只做方案说明与原理拆解，示例代码与权威实现保持一致；如发现本文与上述文件冲突，以文件为准。
>
> MSI 的 `MajorUpgrade` 允许新版本覆盖旧版本，实现"装新即升级"；`RegistrySearch` 路径自愈确保升级时用户自定义安装路径不丢失。

---

## 1. 前置：WiX Toolset v4

WiX v4 以 `dotnet tool` 形式分发，本地与 CI 一致（锁版本 `4.0.5`，v7 引入 OSMF EULA，个人项目用 v4）。

```bash
dotnet tool install -g wix --version 4.0.5
wix extension add WixToolset.UI.wixext/4.0.5   # WixUI_InstallDir 目录选择向导
```

> WiX v4 文档：https://wixtoolset.org/docs/v4/

---

## 2. MSI 配置：`installer/aurora.wxs`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">

  <Package
    Name="aurora"
    Language="2052"
    Codepage="936"
    Version="$(var.Version)"
    Manufacturer="aurora"
    UpgradeCode="71AB04B5-95B5-4006-9AAF-962F6672A7E9"
    Scope="perUser">

    <!-- 允许新版本覆盖旧版本（升级）；阻止降级。未设 AllowSameVersion → 同版本不属升级范围 -->
    <MajorUpgrade
      DowngradeErrorMessage="已安装更新版本的 aurora，无法降级。" />

    <!-- 路径自愈：升级/静默安装前，AppSearch 从 HKCU\Software\aurora\InstallPath 捞回上一次
         安装目录，绑定给 INSTALLFOLDER 属性。同名 Property 在 CostInitialize 前覆盖 Directory
         默认值，确保 MajorUpgrade 覆盖时路径恒定、用户自定义路径不丢失。
         Type="directory"：旧路径目录不存在时置空，回退默认，避免装到无效路径。 -->
    <Property Id="INSTALLFOLDER">
      <RegistrySearch Id="SearchInstallPath"
        Root="HKCU" Key="Software\aurora" Name="InstallPath"
        Type="directory" />
    </Property>

    <!-- 卸载/升级前强制关闭正在运行的 aurora.exe，避免文件占用（ACCESS_DENIED） -->
    <CustomAction Id="SetTaskkillPath" Property="TASKKILL_EXE" Value="[System64Folder]taskkill.exe" Execute="immediate" />
    <CustomAction Id="KillAurora" Property="TASKKILL_EXE" ExeCommand="/f /im aurora.exe" Execute="immediate" Return="ignore" />

    <!-- 纯卸载（非升级）时清理运行时生成的用户数据；升级时 UPGRADINGPRODUCTCODE 被设置，跳过以保留设置 -->
    <CustomAction Id="SetCmdPath" Property="CMD_EXE" Value="[System64Folder]cmd.exe" Execute="immediate" />
    <CustomAction Id="CleanupRuntimeFiles" Property="CMD_EXE"
      ExeCommand='/c cd /d "[INSTALLFOLDER]" &amp;&amp; del /q /f "settings.json" "settings.json.tmp" "debug.log" "appsettings.json" 2>nul'
      Execute="immediate" Return="ignore" />

    <InstallExecuteSequence>
      <!-- 时序（先杀后清）：KillAurora 先强杀进程释放句柄，CleanupRuntimeFiles 链在其后，
           确保纯卸载时 del 不因文件占用失败、0 字节残留。 -->
      <Custom Action="SetTaskkillPath" Before="KillAurora" />
      <Custom Action="KillAurora" After="InstallValidate" />
      <Custom Action="SetCmdPath" After="KillAurora" />
      <Custom Action="CleanupRuntimeFiles" After="SetCmdPath" Condition="REMOVE=&quot;ALL&quot; AND NOT UPGRADINGPRODUCTCODE" />
    </InstallExecuteSequence>

    <Media Id="1" Cabinet="aurora.cab" EmbedCab="yes" />

    <Property Id="WIXUI_INSTALLDIR" Value="INSTALLFOLDER" />
    <ui:WixUI Id="WixUI_InstallDir" />

    <Feature Id="Complete" Title="aurora" Level="1">
      <ComponentGroupRef Id="HarvestComponents" />
      <ComponentGroupRef Id="ProductComponents" />
      <ComponentGroupRef Id="StartMenuShortcuts" />
    </Feature>
  </Package>

  <Fragment>
    <!-- PerUser：装到 %LocalAppData%\aurora，无需管理员 -->
    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="INSTALLFOLDER" Name="aurora" />
    </StandardDirectory>
  </Fragment>

  <Fragment>
    <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">
      <!-- 持久化安装路径，供下次升级 RegistrySearch 读取（路径自愈） -->
      <Component Id="InstallPathReg" Guid="8F5C83D2-6EDE-4D8A-991C-A5D69621F783">
        <RegistryValue Root="HKCU" Key="Software\aurora" Name="InstallPath"
          Value="[INSTALLFOLDER]" Type="string" KeyPath="yes" />
      </Component>
      <!-- 开机自启：写 HKCU Run 键 -->
      <Component Id="AutostartReg" Guid="C5487722-B855-4319-82F2-31E7764F6988">
        <RegistryValue Root="HKCU" Key="Software\Microsoft\Windows\CurrentVersion\Run"
          Name="aurora" Value="[INSTALLFOLDER]aurora.exe" Type="string" KeyPath="yes" />
      </Component>
      <!-- 卸载快捷方式：调 msiexec /x [ProductCode] -->
      <Component Id="UninstallShortcut" Guid="45515FEA-8379-4644-BA25-E1199A8558A1">
        <Shortcut Id="UninstallShortcut" Name="卸载 aurora" Description="卸载 aurora"
          Target="[System64Folder]msiexec.exe" Arguments="/x [ProductCode]"
          WorkingDirectory="INSTALLFOLDER" />
        <RegistryValue Root="HKCU" Key="Software\aurora" Name="UninstallShortcut"
          Type="string" Value="1" KeyPath="yes" />
      </Component>
    </ComponentGroup>
  </Fragment>

  <!-- 开始菜单快捷方式（独立目录） -->
  <Fragment>
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="StartMenuAuroraDir" Name="aurora" />
    </StandardDirectory>
  </Fragment>
  <Fragment>
    <ComponentGroup Id="StartMenuShortcuts" Directory="StartMenuAuroraDir">
      <Component Id="StartMenuShortcut" Guid="69A0868A-11E5-454C-B991-B3E94DE733C4">
        <Shortcut Id="StartMenuShortcut" Name="aurora"
          Target="[INSTALLFOLDER]aurora.exe" WorkingDirectory="INSTALLFOLDER" />
        <RegistryValue Root="HKCU" Key="Software\aurora" Name="Shortcut"
          Type="string" Value="1" KeyPath="yes" />
      </Component>
    </ComponentGroup>
  </Fragment>
</Wix>
```

### 关键元素说明

| 元素 | 作用 |
|------|------|
| `UpgradeCode` | **固定不变**的 GUID（`71AB04B5-...`），标识同一产品族，升级靠它匹配旧版本。一旦发布永不更改 |
| `Version="$(var.Version)"` | MSI 三段版本（如 `1.0.0`），由构建脚本注入 |
| `Scope="perUser"` | 装到用户目录，无需 UAC（WiX v4 用 `Scope`，非 v3 的 `InstallScope`+`InstallPrivileges`） |
| `MajorUpgrade` | 新版本覆盖旧版本；未设 `AllowSameVersion`（同版本不属升级范围）；`DowngradeErrorMessage` 阻降级 |
| `Property INSTALLFOLDER` + `RegistrySearch` | **路径自愈**：升级前从注册表捞回旧安装路径，绑定给 INSTALLFOLDER |
| `InstallPathReg` Component | 持久化 `[INSTALLFOLDER]` 到 `HKCU\Software\aurora\InstallPath`，供下次升级读取 |
| `KillAurora` CustomAction | `taskkill /f /im aurora.exe` 强杀残留进程，`Return="ignore"` 吞错 |
| `CleanupRuntimeFiles` CustomAction | 纯卸载时清理运行时文件，条件 `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE` |
| `HarvestComponents` | 由 `build-msi.ps1` 脚本 harvest `publish/` 生成（~241 文件 + zh-Hans），`Guid="*"` 自动派生 |
| `ui:WixUI Id="WixUI_InstallDir"` | 安装向导含目录选择，`WIXUI_INSTALLDIR` 指向 `INSTALLFOLDER` |

> Component GUID 均为已生成的真实 GUID（见 `installer/aurora.wxs`），勿再替换。

### 安装后目录结构（默认路径）

**安装位置**：默认 `%LocalAppData%\aurora\`，用户可在安装向导的目录选择步改为任意路径（`WixUI_InstallDir`）。PerUser 下选需管理员的目录（如 `C:\Program Files\`）会因无 UAC 提升而失败。

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
├── System.*.dll / Microsoft.*.dll   ┘ BCL + 框架库（根目录共约 241 个文件）
└── zh-Hans\        ← 仅简中卫星资源（17 个 *.resources.dll）
```

`build-msi.ps1` 采用 **self-contained 展开式**：`--self-contained true` 把 .NET Runtime / WPF / WinForms / BCL 全部随包输出，用户机器无需预装 .NET Runtime，开箱即用；每个 dll 独立可见。MSI 体积约 54 MB。

- 语言包：`SatelliteResourceLanguages=zh-Hans` 仅保留简中，其余 12 种语言目录不输出。
- `createdump.exe`（.NET 崩溃诊断工具）在 publish 后删除，不进安装目录。

**安装时附带的非文件项**：

| 项 | 位置 | 作用 |
|------|------|------|
| 安装路径键 | `HKCU\Software\aurora\InstallPath = [INSTALLFOLDER]` | 路径自愈（升级时 RegistrySearch 读取） |
| 开机自启键 | `HKCU\…\Run\aurora = [INSTALLFOLDER]aurora.exe` | 登录自启 |
| 开始菜单快捷方式 | `…\Start Menu\Programs\aurora\aurora.lnk` | 指向 aurora.exe |
| 卸载快捷方式 | 安装目录内 `卸载 aurora.lnk` | 调 `msiexec /x [ProductCode]` |
| 控制面板条目 | 程序和功能 | 标准 MSI 卸载入口 |

自启键、路径键与快捷方式均用 `[INSTALLFOLDER]aurora.exe` 引用，用户改安装目录后自动跟随，不写死路径。

**运行时生成（不在 MSI 内）**：`settings.json`（首次启动由 `SettingsManager` 在 exe 同目录生成）、`settings.json.tmp`（原子写临时文件）、`debug.log`（仅 Debug 配置生成，Release 不产生）、旧版 `appsettings.json`。这些运行时文件**纯卸载时由 `CleanupRuntimeFiles` CustomAction 自动清理**（条件 `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE`）；**升级时保留**，以延续用户设置（见 §7）。

> ✅ **升级路径已自愈**：`UpdateChecker` 走 `msiexec /i … /qb` 静默升级时，MSI 的 `RegistrySearch` 会先从 `HKCU\Software\aurora\InstallPath` 捞回上一次安装路径并绑定给 `INSTALLFOLDER`，用户自定义路径不再丢失。

---

## 3. 构建脚本：`installer/build-msi.ps1`

```powershell
# Build aurora MSI installer (PerUser, self-contained, expanded file structure).
# Usage: ./installer/build-msi.ps1 -Version 1.0.0
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishDir = "$root/publish"

# 1. Publish self-contained expanded（展开式：每个 dll 独立可见，免装 .NET Runtime）
#    - SatelliteResourceLanguages=zh-Hans：只保留简中卫星资源
#    - 先清空 publish 目录，避免上次构建产物残留被 harvest 打进新 MSI
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root/aurora.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true `
  -p:SatelliteResourceLanguages=zh-Hans `
  -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Remove-Item "$publishDir/createdump.exe" -Force -ErrorAction SilentlyContinue

# 2. Ensure wix tool + UI extension (v4.0.5)
if ((dotnet tool list -g) -notmatch '\bwix\b') { dotnet tool install -g wix --version 4.0.5 }
if ((wix extension list) -notmatch 'WixToolset\.UI\.wixext\s+4\.0\.5') { wix extension add WixToolset.UI.wixext/4.0.5 }

# 3. Harvest publish directory into harvest.wxs (wix v4 has no heat command)
#    每个 File 一个 Component，Guid="*"（WiX 按路径派生稳定 GUID）
$rootPath = (Resolve-Path $publishDir).Path
$files = Get-ChildItem $publishDir -Recurse -File
# …（遍历生成 harvest.wxs，详见 installer/build-msi.ps1 原文）

# 4. Build MSI (PerUser, WixUI_InstallDir directory picker)
wix build "$root/installer/aurora.wxs" "$root/installer/harvest.wxs" `
  -o "$publishDir/aurora-$Version.msi" `
  -d "Version=$Version" -d "PublishDir=$publishDir/" `
  -ext WixToolset.UI.wixext
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

# 5. Checksum
$msi = "$publishDir/aurora-$Version.msi"
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash
"$hash  aurora-$Version.msi" | Out-File "$msi.sha256" -Encoding ascii
```

本地用法：`./installer/build-msi.ps1 -Version 1.0.0`。harvest 步骤的完整 PS 脚本见 `installer/build-msi.ps1`（WiX v4 取消了 `heat` 命令，改用脚本遍历 `publish/` 生成 `harvest.wxs`，该文件被 `.gitignore` 忽略，每次构建重新生成）。

---

## 4. 自动更新：`src/Services/UpdateChecker.cs`

应用内检查 GitHub Releases，发现新版则下载 MSI 并 `msiexec` 静默安装（依赖第 2 节的 `MajorUpgrade` 覆盖升级 + `RegistrySearch` 路径自愈）。

```csharp
namespace Aurora.Services;   // 源码命名空间 Aurora.*（PascalCase），发布元数据 aurora（lowercase）

public sealed class UpdateChecker
{
    // 真实发布仓库地址（owner/repo = chengjp0825/aurora）；与 CI 产物 aurora-<v>.msi 命名一致。
    private const string ReleasesUrl =
        "https://api.github.com/repos/chengjp0825/aurora/releases/latest";

    private static readonly HttpClient _http = new HttpClient();

    public async Task<UpdateInfo?> CheckAsync()
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        req.Headers.UserAgent.ParseAdd("aurora-Updater");   // UA 与应用名对齐
        using var resp = await _http.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var tag = doc.RootElement.GetProperty("tag_name").GetString(); // "v1.0.0"
        var latest = ParseTag(tag);
        if (latest <= current) return null;

        string? msiUrl = null;
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name is not null && name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            { msiUrl = asset.GetProperty("browser_download_url").GetString(); break; }
        }
        return msiUrl is null ? null : new UpdateInfo(latest, msiUrl);
    }

    public async Task ApplyAsync(UpdateInfo info)
    {
        // 临时文件名与 aurora 对齐
        var tmpMsi = Path.Combine(Path.GetTempPath(), $"aurora-{info.Version}.msi");
        using var resp = await _http.GetAsync(info.MsiUrl).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(tmpMsi);
        await resp.Content.CopyToAsync(fs).ConfigureAwait(false);

        // 先启动 msiexec 再立即退出当前进程，让出 exe 文件锁；
        // MSI 侧 KillAurora CustomAction（taskkill /f /im aurora.exe）兜底强杀残留。
        // /qb = 基本 UI（进度条）；/norestart = 不重启系统；MajorUpgrade 覆盖旧版本。
        // 路径自愈：MSI 的 RegistrySearch 自动捞回旧 INSTALLFOLDER，无需在此传参。
        Process.Start(new ProcessStartInfo("msiexec", $"/i \"{tmpMsi}\" /qb /norestart")
        { UseShellExecute = false });
        Environment.Exit(0);
    }

    private static Version ParseTag(string? tag)
    {
        var s = (tag ?? "0.0.0").TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
    }
}

public sealed record UpdateInfo(Version Version, string MsiUrl);
```

> `ApplyAsync` 仅负责网络与安装；UI 提示（弹框）由 `App.OnStartup` 调用方处理，避免 Services 层引用 WPF。

### 集成到 `App.OnStartup`（后台非阻塞，仅 Release）

```csharp
#if !DEBUG
    _ = CheckForUpdatesAsync();
#endif

private async Task CheckForUpdatesAsync()
{
    try
    {
        var info = await new UpdateChecker().CheckAsync().ConfigureAwait(false);
        if (info is null) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (System.Windows.MessageBox.Show(
                    $"发现新版本 {info.Version}，是否立即更新？",
                    "aurora 更新", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _ = new UpdateChecker().ApplyAsync(info);
            }
        });
    }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Update] check failed: {ex.Message}"); }
}
```

> 更新流程：启动后台查 → 有新版弹框 → 用户同意 → 下载 MSI → 当前进程 `Environment.Exit(0)` → `msiexec /qb` 静默覆盖安装 → `KillAurora` 兜底杀残留 → `MajorUpgrade` 替换旧版本（路径自愈保留安装目录，`settings.json` 保留）→ 新版自启。

---

## 5. CI：`.github/workflows/release.yml`

tag 推送触发：构建 → 测试 → 发布 exe（展开式）→ harvest → WiX 打包 MSI → 上传 Release。

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
        with: { dotnet-version: '8.0.x' }

      - name: Build
        run: dotnet build --configuration Release
      - name: Test
        run: dotnet test --configuration Release --no-build

      - name: Publish exe (expanded, self-contained)
        run: >-
          dotnet publish aurora.csproj -c Release -r win-x64
          --self-contained true
          -p:PublishReadyToRun=true
          -p:SatelliteResourceLanguages=zh-Hans
          -o publish

      - name: Install WiX v4 + UI extension
        shell: pwsh
        run: |
          dotnet tool install -g wix --version 4.0.5
          wix extension add WixToolset.UI.wixext/4.0.5

      - name: Harvest publish directory
        shell: pwsh
        run: |
          # 遍历 publish/ 生成 installer/harvest.wxs（与 build-msi.ps1 同逻辑）
          # 详见 .github/workflows/release.yml 原文

      - name: Build MSI
        shell: pwsh
        run: |
          $v = "${{ github.ref_name }}".TrimStart('v')
          if ([string]::IsNullOrWhiteSpace($v) -or $v -eq '${{ github.ref_name }}') { $v = "0.0.0" }
          wix build installer/aurora.wxs installer/harvest.wxs -o "publish/aurora-$v.msi" `
            -d "Version=$v" -d "PublishDir=$PWD/publish/" -ext WixToolset.UI.wixext
          $h = (Get-FileHash "publish/aurora-$v.msi" -Algorithm SHA256).Hash
          "$h  aurora-$v.msi" | Out-File "publish/aurora-$v.msi.sha256" -Encoding ascii

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with: { name: aurora-msi, path: publish/*.msi* }

      - name: Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v2
        with:
          files: |
            publish/*.msi
            publish/*.msi.sha256
```

发布流程：`git tag v1.0.0 && git push origin v1.0.0` → CI 自动构建 MSI 并发布到 GitHub Releases → 用户应用启动时检测到新版本自动升级。完整 harvest 步骤见 `.github/workflows/release.yml`。

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

## 7. 升级流程（MajorUpgrade + 路径自愈 + 文件锁）

### 7.1 升级原理

1. 用户装 v1.0.0 → MSI 记录 ProductCode + UpgradeCode 到 `HKCU\Software\...\Uninstall`，`InstallPathReg` 写入 `HKCU\Software\aurora\InstallPath = <实际路径>`。
2. 发布 v1.1.0 → 用户应用启动检查到新版 → 下载 v1.1.0.msi。
3. `msiexec /i v1.1.0.msi /qb` → **AppSearch 阶段**：`RegistrySearch` 读 `InstallPath` → `INSTALLFOLDER` 属性 = 旧路径。
4. MSI 检测到同 UpgradeCode、更高 Version → `MajorUpgrade` 先卸载 v1.0.0 再装 v1.1.0，安装路径沿用 `INSTALLFOLDER`（旧路径，不变）。
5. 旧 exe 被替换；开机自启键、`InstallPath` 键均指向同路径 `[INSTALLFOLDER]aurora.exe`，路径不变。

### 7.2 路径自愈机制（防自定义路径丢失）

`WixUI_InstallDir` 静默安装（`/qb`）默认不读旧安装路径，`INSTALLFOLDER` 会回到 wxs 默认值 `%LocalAppData%\aurora`。本项目通过 `<Property Id="INSTALLFOLDER">` + `<RegistrySearch>` 解决：

- **读**：`AppSearch` 阶段（早于 `CostInitialize` 与 UI）从 `HKCU\Software\aurora\InstallPath` 捞回旧路径，写入 `INSTALLFOLDER` 属性。同名 Property 覆盖 Directory 默认值。
- **写**：`InstallPathReg` 组件在安装时持久化 `[INSTALLFOLDER]` 到注册表。
- **回退**：`Type="directory"` 验证目录存在；旧路径无效（用户手动删了）则置空，回退默认值，避免装到无效路径。

这样无论 CLI、自动更新触发 `msiexec /i`，还是用户手动双击新版 MSI，都会先从注册表捞回上一次路径，**MajorUpgrade 覆盖时路径恒定**。

### 7.3 文件锁与进程强杀（先杀后清）

升级/卸载时旧 `aurora.exe` 若正在运行会锁文件，导致覆盖失败。双保险：

1. **应用侧**（`UpdateChecker.ApplyAsync`）：`Process.Start(msiexec)` 后立即 `Environment.Exit(0)` 自杀。
2. **MSI 侧**（`KillAurora` CustomAction）：`taskkill /f /im aurora.exe` 强杀残留，`Return="ignore"` 吞错（进程不在也不报错）。

`KillAurora` 锚定 `After="InstallValidate"`（`InstallExecuteSequence` 最早可用标准锚点），先于 `InstallInitialize` 执行，确保后续 `ProcessComponents`（MajorUpgrade 卸旧版、覆盖 exe）时文件已解锁。

### 7.4 纯卸载时序（先杀后清，0 字节残留）

```
InstallValidate
  → SetTaskkillPath → KillAurora          ← 强杀 aurora.exe，释放 debug.log 句柄
  → SetCmdPath → CleanupRuntimeFiles      ← del settings.json/.tmp/debug.log/appsettings.json
                                            （条件 REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE）
→ InstallInitialize → ProcessComponents → InstallFinalize
```

**关键修正**：早期实现 `CleanupRuntimeFiles`（`After="InstallValidate"`）跑在 `KillAurora`（`Before="InstallInitialize"`）之前，导致 aurora.exe 尚未强杀、仍持有 `debug.log` 句柄时 `del` 提前触发并失败。现调整为链式 `KillAurora → SetCmdPath → CleanupRuntimeFiles`，确保先杀进程后清文件，纯卸载 0 字节残留。升级时 `UPGRADINGPRODUCTCODE` 被设置（旧产品被 MajorUpgrade 卸载时的 MSI 内置属性），跳过清理保留 `settings.json`。**注意**：必须用 `UPGRADINGPRODUCTCODE` 而非 WiX 的 `WIX_UPGRADE_DETECTED`——后者仅新产品 session 可见，旧产品卸载上下文读不到，会误判为纯卸载从而删掉 `settings.json`（此 bug 由生命周期测试在阶段 2 捕获并修复）。

---

## 8. 测试清单

- [ ] 本地 `./installer/build-msi.ps1 -Version 1.0.0` 生成 MSI
- [ ] 双击 MSI 安装 → `%LocalAppData%\aurora\aurora.exe` 存在
- [ ] 开始菜单出现 aurora 快捷方式
- [ ] 开机自启键写入 `HKCU\...\Run\aurora`
- [ ] 安装路径键写入 `HKCU\Software\aurora\InstallPath`
- [ ] 控制面板「程序和功能」出现 aurora 条目
- [ ] 安装后 exe 运行，钩子/托盘/截图/设置全功能
- [ ] **自定义路径升级测试**：装 v1.0.0 到自定义路径 → 用 v1.1.0 MSI 静默升级 → 路径保持不变、设置保留
- [ ] 升级测试：装 v1.0.0 → v1.1.0 MSI 覆盖 → 旧版本被替换，`settings.json` 保留
- [ ] 降级测试：v1.1.0 已装 → 装 v1.0.0 → 提示无法降级
- [ ] 自动更新：GitHub 发新 Release → 旧版启动检测到 → 下载 MSI 静默升级
- [ ] 卸载：exe、快捷方式、自启键、路径键、注册表条目全部清除，运行时文件（settings.json/debug.log）0 残留
- [ ] 干净 Windows（无 .NET）环境验证

---

## 9. 可选增强

| 增强 | 说明 |
|------|------|
| 数字签名 | `signtool sign` 签 MSI + exe，消除 SmartScreen 警告（见 publish-guide §5） |
| 增量更新 | MSI 不支持 delta；若需小体积增量，改用 Velopack（但放弃 MSI） |
| 后台静默更新 | `msiexec /qn`（无 UI），不弹框直接升级 |
| 更新通道 | stable/beta：GitHub Releases 用 prerelease 标记区分 |
| 多语言 | WiX `ui:WixUI` 多语种资源（当前简中 2052/936） |

---

## 附：与 publish-guide 的关系

本文档是 `docs/publish-guide.md` 第 4 节（安装包）和第 6 节（自动更新）的 **MSI 方案落地**。其余（版本号、签名、CI 渠道、许可证、检查清单）仍以 publish-guide 为准。

发布前补齐：
1. csproj 版本号 + `ApplicationIcon`
2. `LICENSE`
3. `installer/aurora.wxs`（GUID 已就位）
4. `src/Services/UpdateChecker.cs`（仓库地址已对齐 `chengjp0825/aurora`）
5. `.github/workflows/release.yml`
