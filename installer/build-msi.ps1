# Build aurora MSI installer (PerUser, self-contained, expanded file structure).
# Usage: ./installer/build-msi.ps1 -Version 1.0.0
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishDir = "$root/publish"

# 1. Publish self-contained expanded（展开式：每个 dll 独立可见，免装 .NET Runtime）
#    - SatelliteResourceLanguages=zh-Hans：只保留简中卫星资源（其余 12 种语言目录不输出）
#    - 先清空 publish 目录，避免上次构建产物（msi/pdb 等）残留被 harvest 打进新 MSI
Write-Host "[1/5] Publishing self-contained exe..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root/aurora.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true `
  -p:SatelliteResourceLanguages=zh-Hans `
  -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# createdump.exe 是 .NET 崩溃诊断工具，个人项目用不到，删掉保持主目录干净
Remove-Item "$publishDir/createdump.exe" -Force -ErrorAction SilentlyContinue

# 2. Ensure wix tool + UI extension (v4 — v7 引入 OSMF EULA，个人项目用 v4)
Write-Host "[2/5] Ensuring WiX v4 tool + UI extension..."
if ((dotnet tool list -g) -notmatch '\bwix\b') {
  dotnet tool install -g wix --version 4.0.5
  if ($LASTEXITCODE -ne 0) { throw "wix install failed" }
}
if ((wix extension list) -notmatch 'WixToolset\.UI\.wixext\s+4\.0\.5') {
  wix extension add WixToolset.UI.wixext/4.0.5
  if ($LASTEXITCODE -ne 0) { throw "wix UI extension install failed" }
}

# 3. Harvest publish directory into harvest.wxs (wix v4 has no heat command)
Write-Host "[3/5] Harvesting publish directory..."
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

# 4. Build MSI (PerUser, WixUI_InstallDir directory picker)
Write-Host "[4/5] Building MSI..."
wix build "$root/installer/aurora.wxs" "$root/installer/harvest.wxs" `
  -o "$publishDir/aurora-$Version.msi" `
  -d "Version=$Version" -d "PublishDir=$publishDir/" `
  -ext WixToolset.UI.wixext
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

# 5. Checksum
Write-Host "[5/5] Generating checksum..."
$msi = "$publishDir/aurora-$Version.msi"
if (-not (Test-Path $msi)) { throw "MSI not found: $msi" }
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash
"$hash  aurora-$Version.msi" | Out-File "$msi.sha256" -Encoding ascii

Write-Host ""
Write-Host "MSI built: publish/aurora-$Version.msi"
Write-Host "SHA256:    $hash"
