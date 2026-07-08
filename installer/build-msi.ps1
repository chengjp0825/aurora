# Build aurora MSI installer (PerUser, self-contained, expanded file structure).
# Usage: ./installer/build-msi.ps1 -Version 1.0.0
#
# 可选数字签名（门控）：设置以下环境变量后自动签名 exe + MSI，未设置则跳过（产物未签名，SmartScreen 会警告）。
#   AURORA_CERT_PFX        PFX 证书文件路径
#   AURORA_CERT_PASS       PFX 密码
#   AURORA_TIMESTAMP_URL   时间戳服务器（默认 http://timestamp.digicert.com）
# 本地签名需安装 Windows SDK（含 signtool）。
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishDir = "$root/publish"

# --- 签名辅助（门控：无证书/signtool 时跳过，不阻断构建）---
function Find-Signtool {
    $st = Get-Command signtool -ErrorAction SilentlyContinue
    if ($st) { return $st.Source }
    $cand = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($cand) { return $cand.FullName }
    return $null
}
function Invoke-SignIfCert($target) {
    $name = Split-Path $target -Leaf
    $pfx = $env:AURORA_CERT_PFX
    $pass = $env:AURORA_CERT_PASS
    if (-not $pfx -or -not $pass) {
        Write-Host "  [sign] 未设置 AURORA_CERT_PFX/AURORA_CERT_PASS — 跳过（$name 未签名，SmartScreen 会警告）"
        return
    }
    if (-not (Test-Path $pfx)) { Write-Host "  [sign] PFX 不存在: $pfx — 跳过签名 $name"; return }
    $signtool = Find-Signtool
    if (-not $signtool) { Write-Host "  [sign] signtool 未安装（需 Windows SDK）— 跳过签名 $name"; return }
    $ts = if ($env:AURORA_TIMESTAMP_URL) { $env:AURORA_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }
    & $signtool sign /fd SHA256 /tr $ts /td SHA256 /f $pfx /p $pass $target
    if ($LASTEXITCODE -eq 0) { Write-Host "  [sign] 已签名: $name" }
    else { Write-Host "  [sign] 签名失败（exit $LASTEXITCODE）: $name" }
}

# 1. Publish self-contained expanded（展开式：每个 dll 独立可见，免装 .NET Runtime）
#    - SatelliteResourceLanguages=zh-Hans：只保留简中卫星资源（其余 12 种语言目录不输出）
#    - 先清空 publish 目录，避免上次构建产物（msi/pdb 等）残留被 harvest 打进新 MSI
Write-Host "[1/7] Publishing self-contained exe..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root/aurora.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true `
  -p:SatelliteResourceLanguages=zh-Hans `
  -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# createdump.exe 是 .NET 崩溃诊断工具，个人项目用不到，删掉保持主目录干净
Remove-Item "$publishDir/createdump.exe" -Force -ErrorAction SilentlyContinue

# 2. Sign exe（可选，门控；须在 harvest 前签名，使 MSI 内嵌已签 exe）
Write-Host "[2/7] Signing exe (if cert present)..."
Invoke-SignIfCert "$publishDir/aurora.exe"

# 3. Ensure wix tool + UI extension (v4 — v7 引入 OSMF EULA，个人项目用 v4)
Write-Host "[3/7] Ensuring WiX v4 tool + UI extension..."
if ((dotnet tool list -g) -notmatch '\bwix\b') {
  dotnet tool install -g wix --version 4.0.5
  if ($LASTEXITCODE -ne 0) { throw "wix install failed" }
}
if ((wix extension list) -notmatch 'WixToolset\.UI\.wixext\s+4\.0\.5') {
  wix extension add WixToolset.UI.wixext/4.0.5
  if ($LASTEXITCODE -ne 0) { throw "wix UI extension install failed" }
}

# 4. Harvest publish directory into harvest.wxs (wix v4 has no heat command)
Write-Host "[4/7] Harvesting publish directory..."
$rootPath = (Resolve-Path $publishDir).Path
$files = Get-ChildItem $publishDir -Recurse -File
$subdirs = @()
foreach ($f in $files) {
    $relDir = $f.DirectoryName.Substring($rootPath.Length).TrimStart('\','/')
    if ($relDir -and $subdirs -notcontains $relDir) { $subdirs += $relDir }
}
$dirIds = @{ "" = "INSTALLFOLDER" }
foreach ($sd in $subdirs) { $dirIds[$sd] = "d_" + ($sd -replace '[^a-zA-Z0-9]','_') }

$wxs = "<?xml version=""1.0"" encoding=""UTF-8""?>`n<Wix xmlns=""http://wixtoolset.org/schemas/v4/wxs"">`n  <Fragment>`n    <DirectoryRef Id=""INSTALLFOLDER"">`n"
foreach ($sd in $subdirs) {
    $name = Split-Path $sd -Leaf
    $wxs += "      <Directory Id=""$($dirIds[$sd])"" Name=""$name"" />`n"
}
$wxs += "    </DirectoryRef>`n    <ComponentGroup Id=""HarvestComponents"" Directory=""INSTALLFOLDER"">`n"
$i = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($rootPath.Length + 1).Replace('\','/')
    $relDir = $f.DirectoryName.Substring($rootPath.Length).TrimStart('\','/')
    $fileId = "f$i"
    $dirAttr = if ($relDir) { " Directory=""$($dirIds[$relDir])""" } else { "" }
    $wxs += "      <Component Guid=""*""$dirAttr>`n        <File Id=""$fileId"" Source=""`$(var.PublishDir)$rel"" KeyPath=""yes"" />`n      </Component>`n"
    $i++
}
$wxs += "    </ComponentGroup>`n  </Fragment>`n</Wix>"
$wxs | Out-File "$root/installer/harvest.wxs" -Encoding UTF8
Write-Host "  Harvested $i files"

# 5. Build MSI (PerUser, WixUI_InstallDir directory picker)
Write-Host "[5/7] Building MSI..."
wix build "$root/installer/aurora.wxs" "$root/installer/harvest.wxs" `
  -o "$publishDir/aurora-$Version.msi" `
  -d "Version=$Version" -d "PublishDir=$publishDir/" `
  -ext WixToolset.UI.wixext
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

# 6. Sign MSI（可选，门控）
Write-Host "[6/7] Signing MSI (if cert present)..."
Invoke-SignIfCert "$publishDir/aurora-$Version.msi"

# 7. Checksum
Write-Host "[7/7] Generating checksum..."
$msi = "$publishDir/aurora-$Version.msi"
if (-not (Test-Path $msi)) { throw "MSI not found: $msi" }
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash
"$hash  aurora-$Version.msi" | Out-File "$msi.sha256" -Encoding ascii

Write-Host ""
Write-Host "MSI built: publish/aurora-$Version.msi"
Write-Host "SHA256:    $hash"
