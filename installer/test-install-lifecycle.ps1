#requires -Version 5.1
<#
.SYNOPSIS
  aurora MSI 安装生命周期黑盒测试（跨盘 D 盘）。

.DESCRIPTION
  三阶段黑盒校验，验证发布拓扑的绝对刚度：
    1. 首次安装与跨盘持久化：msiexec /i INSTALLFOLDER=D 盘 → 注册表 InstallPath 持久化
    2. 无感升级与路径自愈：伪造 settings.json → 无参数 msiexec /i → 路径铁打不动落回 D 盘 + 资产保留
    3. 强杀与 0 残留纯卸载：启动 aurora.exe 锁句柄 → msiexec /x → 进程被斩杀 + 目录物理擦除

  测试基准路径硬编码为 D:\AuroraTestPath_Debug（跨盘，远离 C 盘默认 %LocalAppData%\aurora）。

.PARAMETER MsiV1
  低版本 MSI 路径（首次安装）。未指定则从 publish/ 自动发现，或用 -Build 自动构建。

.PARAMETER MsiV2
  高版本 MSI 路径（升级）。未指定则从 publish/ 自动发现，或用 -Build 自动构建。

.PARAMETER Build
  自动调用 installer/build-msi.ps1 构建 1.0.0 与 1.0.1 两个版本到临时目录。
  build-msi.ps1 每次清空 publish/，故构建 v1 后先复制到临时目录再构建 v2。

.PARAMETER LogDir
  msiexec /l*v 详细日志目录。默认 $env:TEMP\aurora-test-logs。

.EXAMPLE
  # 全自动：构建两个版本 + 跑生命周期
  .\installer\test-install-lifecycle.ps1 -Build

  # 手动指定两个已构建的 MSI
  .\installer\test-install-lifecycle.ps1 -MsiV1 publish\aurora-1.0.0.msi -MsiV2 publish\aurora-1.0.1.msi

.NOTES
  副作用：会真实安装/卸载 aurora 到 D:\AuroraTestPath_Debug，并在阶段 3 启动 aurora.exe
  （挂全局鼠标钩子、显示托盘图标）。测试环境应预留鼠标空闲窗口。
  PerUser MSI 无需管理员；但 D 盘根目录需当前用户可写。
#>
param(
    [string]$MsiV1,
    [string]$MsiV2,
    [switch]$Build,
    [string]$LogDir = "$env:TEMP\aurora-test-logs"
)
$ErrorActionPreference = 'Stop'

# ============================================================
# 硬编码测试基准路径（跨盘 D 盘，用户指定）
# ============================================================
$TestPath = "D:\AuroraTestPath_Debug"

$root      = Split-Path $PSScriptRoot -Parent
$pubDir    = Join-Path $root "publish"
$FailCount = 0
$Abort     = $false

# ---------- 输出辅助 ----------
function Step($n, $m) { Write-Host "`n========== [$n] $m ==========" -ForegroundColor Cyan }
function Pass($m)     { Write-Host "  [PASS] $m" -ForegroundColor Green }
function Fail($m)     { Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:FailCount++ }
function Warn($m)     { Write-Host "  [WARN] $m" -ForegroundColor Yellow }
function Info($m)     { Write-Host "       $m" -ForegroundColor DarkGray }
function AbortStep($m){ Write-Host "  [ABORT] $m" -ForegroundColor Magenta; $script:Abort = $true }

# 路径标准化：去尾分隔符 + GetFullPath，用于注册表值与期望值的稳健比较
function Norm($p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }
    try { return ([System.IO.Path]::GetFullPath($p.TrimEnd('\', '/'))).TrimEnd('\', '/') }
    catch { return $p.TrimEnd('\', '/') }
}

# 安全读 HKCU 注册表值（不存在返回 $null）
function Get-RegValue($key, $name) {
    try { return (Get-ItemProperty $key -Name $name -ErrorAction Stop).$name }
    catch { return $null }
}

# 从 MSI 文件名解析版本（aurora-1.0.1.msi → 1.0.1）
function Get-MsiVersion($path) {
    $name = [System.IO.Path]::GetFileName($path)
    if ($name -match 'aurora-(\d+\.\d+\.\d+)\.msi') { return [Version]$Matches[1] }
    return $null
}

# msiexec 封装：全静默 /qn + 详细日志，返回退出码（0 或 3010 视为成功）
function Invoke-Msiexec($argLine) {
    $p = Start-Process -FilePath msiexec -ArgumentList $argLine -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

# ---------- 解析两个 MSI 版本 ----------
function Resolve-Msis {
    if ($Build) {
        Write-Host "`n>> -Build：自动构建两个版本..." -ForegroundColor Cyan
        $tmp = Join-Path $env:TEMP "aurora-test-msis"
        New-Item -ItemType Directory -Force -Path $tmp | Out-Null
        # v1.0.0
        & "$root\installer\build-msi.ps1" -Version "1.0.0"
        if ($LASTEXITCODE -ne 0) { throw "build-msi.ps1 -Version 1.0.0 失败" }
        Copy-Item "$pubDir\aurora-1.0.0.msi" "$tmp\aurora-1.0.0.msi" -Force
        # v1.0.1（build-msi.ps1 会清空 publish/，但 v1 已复制到 $tmp）
        & "$root\installer\build-msi.ps1" -Version "1.0.1"
        if ($LASTEXITCODE -ne 0) { throw "build-msi.ps1 -Version 1.0.1 失败" }
        Copy-Item "$pubDir\aurora-1.0.1.msi" "$tmp\aurora-1.0.1.msi" -Force
        $script:MsiV1 = "$tmp\aurora-1.0.0.msi"
        $script:MsiV2 = "$tmp\aurora-1.0.1.msi"
        return
    }
    if ($MsiV1 -and $MsiV2) {
        $script:MsiV1 = (Resolve-Path $MsiV1).Path
        $script:MsiV2 = (Resolve-Path $MsiV2).Path
        return
    }
    # 自动发现 publish/ 下两个版本
    $found = Get-ChildItem $pubDir -Filter "aurora-*.msi" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'aurora-\d+\.\d+\.\d+\.msi' } |
        Sort-Object { [Version]([regex]::Match($_.Name, 'aurora-(\d+\.\d+\.\d+)\.msi').Groups[1].Value) }
    if (@($found).Count -lt 2) {
        throw "未在 publish/ 发现至少两个不同版本的 aurora-*.msi（当前 $((@($found)).Count) 个）。请用 -Build 自动构建，或 -MsiV1/-MsiV2 指定，或先跑两次 build-msi.ps1 -Version 1.0.0 / 1.0.1 并各自保存 MSI。"
    }
    $script:MsiV1 = $found[0].FullName
    $script:MsiV2 = $found[-1].FullName
}

# ---------- 预清理：卸载已装 aurora + 删测试目录 + 杀残留进程 ----------
function Pre-Cleanup {
    Write-Host "`n>> 预清理：卸载已装 aurora + 删测试目录..." -ForegroundColor Cyan
    if ($MsiV2 -and (Test-Path $MsiV2)) {
        try { Start-Process msiexec -ArgumentList "/x `"$MsiV2`" /qn /norestart" -Wait -NoNewWindow -ErrorAction SilentlyContinue | Out-Null } catch {}
    }
    Start-Sleep -Seconds 1
    Get-Process -Name aurora -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if (Test-Path $TestPath) { Remove-Item $TestPath -Recurse -Force -ErrorAction SilentlyContinue }
}

# ---------- 阶段 1：首次安装与跨盘持久化 ----------
function Phase1-Install {
    Step 1 "首次安装与跨盘持久化（INSTALLFOLDER=$TestPath）"
    if ($Abort) { return }
    $log = Join-Path $LogDir "phase1-install.log"
    $code = Invoke-Msiexec "/i `"$MsiV1`" INSTALLFOLDER=`"$TestPath`" /qn /norestart /l*v `"$log`""
    if ($code -eq 0 -or $code -eq 3010) { Pass "msiexec /i 退出码 $code" }
    else { Fail "msiexec /i 退出码 $code（见 $log）"; AbortStep "安装失败，后续阶段无意义"; return }

    # 黑盒检查 1：注册表 InstallPath 持久化为 D 盘
    $regVal = Get-RegValue "HKCU:\Software\aurora" "InstallPath"
    if ((Norm $regVal) -eq (Norm $TestPath)) { Pass "注册表 InstallPath = $regVal" }
    else { Fail "注册表 InstallPath 期望 $(Norm $TestPath)，实际 $(Norm $regVal)" }

    # 黑盒检查 2：aurora.exe 物理落在 D 盘
    if (Test-Path "$TestPath\aurora.exe") { Pass "aurora.exe 落在 $TestPath\aurora.exe" }
    else { Fail "aurora.exe 不在 $TestPath\aurora.exe" }

    # 黑盒检查 3：未跑回 C 盘默认路径
    $defaultPath = Join-Path $env:LocalAppData "aurora"
    if (Test-Path "$defaultPath\aurora.exe") { Fail "C 盘默认路径 $defaultPath 出现 aurora.exe（跨盘未生效）" }
    else { Pass "C 盘默认路径未被污染" }
}

# ---------- 阶段 2：无感升级与路径自愈 ----------
function Phase2-Upgrade {
    Step 2 "无感升级与路径自愈（不传路径参数）"
    if ($Abort) { return }
    # 伪造用户资产 settings.json（模拟用户配置）
    $fake = '{"source":"lifecycle-test","phase":2,"asset":"user-settings","ts":"placeholder"}'
    Set-Content -Path "$TestPath\settings.json" -Value $fake -Encoding UTF8
    $hashBefore = (Get-FileHash "$TestPath\settings.json" -Algorithm SHA256).Hash
    Pass "伪造 settings.json 写入 $TestPath（SHA256 $(${hashBefore}.Substring(0,8))...）"

    $log = Join-Path $LogDir "phase2-upgrade.log"
    # 关键：不传 INSTALLFOLDER，靠 MSI 的 RegistrySearch 路径自愈捞回 D 盘
    $code = Invoke-Msiexec "/i `"$MsiV2`" /qn /norestart /l*v `"$log`""
    if ($code -eq 0 -or $code -eq 3010) { Pass "msiexec /i 升级退出码 $code" }
    else { Fail "msiexec /i 升级退出码 $code（见 $log）"; AbortStep "升级失败"; return }

    # 黑盒检查 1：新版文件铁打不动落回 D 盘（路径自愈生效）
    if (Test-Path "$TestPath\aurora.exe") { Pass "新版 aurora.exe 仍精准落回 $TestPath（路径自愈生效）" }
    else { Fail "新版 aurora.exe 未落回 $TestPath，路径自愈失败" }

    # 黑盒检查 2：注册表 InstallPath 仍为 D 盘
    $regVal = Get-RegValue "HKCU:\Software\aurora" "InstallPath"
    if ((Norm $regVal) -eq (Norm $TestPath)) { Pass "升级后注册表 InstallPath 仍 = $TestPath" }
    else { Fail "升级后 InstallPath 期望 $(Norm $TestPath)，实际 $(Norm $regVal)（路径跑偏）" }

    # 黑盒检查 3：settings.json 物理保留且内容未变
    if (Test-Path "$TestPath\settings.json") {
        $hashAfter = (Get-FileHash "$TestPath\settings.json" -Algorithm SHA256).Hash
        if ($hashAfter -eq $hashBefore) { Pass "settings.json 物理保留，内容未变（升级跳过清理生效）" }
        else { Fail "settings.json 存在但内容被改写（$hashAfter != $hashBefore）" }
    } else { Fail "settings.json 在升级后丢失（应保留）" }

    # 黑盒检查 4：C 盘默认路径未被污染
    $defaultPath = Join-Path $env:LocalAppData "aurora"
    if (Test-Path "$defaultPath\aurora.exe") { Fail "C 盘默认路径 $defaultPath 出现 aurora.exe（路径跑偏）" }
    else { Pass "C 盘默认路径未被污染" }
}

# ---------- 阶段 3：强杀与 0 残留纯卸载 ----------
function Phase3-Uninstall {
    Step 3 "强杀与 0 残留纯卸载"
    if ($Abort) { return }
    # 物理启动 aurora.exe 常驻内存，锁死文件句柄
    $exe = "$TestPath\aurora.exe"
    if (-not (Test-Path $exe)) { Fail "aurora.exe 不存在，无法启动锁句柄"; return }
    $proc = Start-Process $exe -PassThru
    Start-Sleep -Seconds 3
    $running = Get-Process -Name aurora -ErrorAction SilentlyContinue
    if ($running) { Pass "aurora.exe 已常驻内存（PID $($running.Id -join ',')），锁句柄就位" }
    else { Warn "aurora.exe 启动后未检测到进程（可能自退出），仍继续卸载测试" }

    $log = Join-Path $LogDir "phase3-uninstall.log"
    # 纯卸载：msiexec /x 触发 REMOVE=ALL → KillAurora 强杀 + CleanupRuntimeFiles 清理
    $code = Invoke-Msiexec "/x `"$MsiV2`" /qn /norestart /l*v `"$log`""
    if ($code -eq 0 -or $code -eq 3010) { Pass "msiexec /x 纯卸载退出码 $code" }
    else { Fail "msiexec /x 退出码 $code（见 $log）" }

    Start-Sleep -Seconds 2
    # 黑盒检查 1：进程已被 KillAurora 斩杀
    $still = Get-Process -Name aurora -ErrorAction SilentlyContinue
    if (-not $still) { Pass "aurora.exe 进程已被 KillAurora 强杀" }
    else { Fail "aurora.exe 进程仍存活（PID $($still.Id -join ',')），KillAurora 未生效" }

    # 黑盒检查 2：D 盘目录完全物理擦除（0 字节残留）
    if (-not (Test-Path $TestPath)) { Pass "$TestPath 已完全物理擦除（0 残留）" }
    else {
        $leftover = Get-ChildItem $TestPath -Recurse -Force -ErrorAction SilentlyContinue
        if (-not $leftover) { Warn "$TestPath 目录存在但为空（MSI 未删空目录，非文件残留）" }
        else { Fail "$TestPath 仍有残留：$((@($leftover)).Count) 项（$($leftover.Name -join ', '))" }
    }

    # 黑盒检查 3：自启键清除
    $runVal = Get-RegValue "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" "aurora"
    if (-not $runVal) { Pass "HKCU Run\aurora 自启键已清除" }
    else { Fail "HKCU Run\aurora 自启键仍存在: $runVal" }

    # 黑盒检查 4：InstallPath 键清除
    $ipVal = Get-RegValue "HKCU:\Software\aurora" "InstallPath"
    if (-not $ipVal) { Pass "HKCU Software\aurora\InstallPath 已清除" }
    else { Fail "HKCU Software\aurora\InstallPath 仍存在: $ipVal" }
}

# ============================================================
# 主流程
# ============================================================
# D 盘存在性检查
if (-not (Test-Path "D:\")) { throw "D 盘不存在，测试路径 $TestPath 无效。请挂载 D 盘或修改脚本基准路径。" }

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Resolve-Msis

Write-Host "`n===========================================" -ForegroundColor White
Write-Host " aurora MSI 生命周期黑盒测试" -ForegroundColor White
Write-Host "===========================================" -ForegroundColor White
Write-Host " 测试基准路径 : $TestPath" -ForegroundColor White
Write-Host " MsiV1 (首次安装): $MsiV1" -ForegroundColor White
Write-Host " MsiV2 (升级)    : $MsiV2" -ForegroundColor White
$v1 = Get-MsiVersion $MsiV1; $v2 = Get-MsiVersion $MsiV2
if ($v1 -and $v2) {
    if ($v1 -ge $v2) { Write-Host " ! 警告: MsiV1($v1) >= MsiV2($v2)，升级测试无效（需 v1 < v2）" -ForegroundColor Yellow }
    else { Write-Host " 版本顺序: $v1 → $v2（OK）" -ForegroundColor DarkGray }
}

Pre-Cleanup
Phase1-Install
Phase2-Upgrade
Phase3-Uninstall

# 兜底清理残留进程（避免遗留挂钩子进程）
Get-Process -Name aurora -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# ============================================================
# 汇总
# ============================================================
Write-Host "`n===========================================" -ForegroundColor Cyan
if ($FailCount -eq 0) {
    Write-Host "  全量通关: 0 失败 — 发布验证基建 PASS" -ForegroundColor Green
    Write-Host "===========================================" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "  未通关: $FailCount 项失败 — 见上方 [FAIL]" -ForegroundColor Red
    Write-Host "===========================================" -ForegroundColor Cyan
    exit 1
}
