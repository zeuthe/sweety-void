# Builds "Sweety Void Setup.exe":
#   1. publishes the app self-contained, WITHOUT single-file bundling (#20):
#      VoidUI.exe stays a thin launcher and all code lands in VoidUI.dll +
#      dependency DLLs, so an update can replace or drop assemblies
#   2. packs the publish output into payload.zip
#   3. builds a slim installer variant (no payload) - it becomes the
#      Uninstall.exe in the install folder (~15 MB instead of ~120 MB)
#   4. builds the full Avalonia installer with the payload and the slim
#      uninstaller embedded
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
#         powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -SkipApp

param(
    [string]$Configuration = "Release",
    [switch]$SkipApp,
    [string]$Version = "",
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$temp   = Join-Path $env:LOCALAPPDATA "Temp\opencode"
$appPub = Join-Path $temp "voidui-publish"
$payload = Join-Path $temp "payload.zip"
$insOut = Join-Path $temp "voidui-setup-out"

Write-Host "== Sweety Void installer build ==" -ForegroundColor Cyan

# --- Версия (#14: её же сравнивает автообновление с GitHub) ---
# По умолчанию patch-бамп сам: 1.0.0 -> 1.0.1. Явно: -Version 1.1.0.
# Оставить как есть: -NoBump (например, при пересборке того же релиза).
$asmPath = Join-Path $root "Properties\AssemblyInfo.cs"
$asmText = [System.IO.File]::ReadAllText($asmPath)
$vm = [regex]::Match($asmText, 'AssemblyInformationalVersion\("(\d+)\.(\d+)\.(\d+)"\)')
if (-not $vm.Success) { throw "AssemblyInfo.cs: version string not found" }

if (-not $NoBump) {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "{0}.{1}.{2}" -f [int]$vm.Groups[1].Value, [int]$vm.Groups[2].Value, ([int]$vm.Groups[3].Value + 1)
    }
    $asmText = [regex]::Replace($asmText, 'AssemblyVersion\("[^"]*"\)',        ('AssemblyVersion("' + $Version + '.0")'))
    $asmText = [regex]::Replace($asmText, 'AssemblyFileVersion\("[^"]*"\)',    ('AssemblyFileVersion("' + $Version + '.0")'))
    $asmText = [regex]::Replace($asmText, 'AssemblyInformationalVersion\("[^"]*"\)', ('AssemblyInformationalVersion("' + $Version + '")'))
    [System.IO.File]::WriteAllText($asmPath, $asmText, (New-Object System.Text.UTF8Encoding($true)))
}
elseif ([string]::IsNullOrWhiteSpace($Version)) {
    # -NoBump keeps AssemblyInfo.cs as is, but the installer still needs the number.
    $Version = "{0}.{1}.{2}" -f [int]$vm.Groups[1].Value, [int]$vm.Groups[2].Value, [int]$vm.Groups[3].Value
}
Write-Host ("    version: " + $Version) -ForegroundColor Green

Get-Process -Name "VoidUI", "VoidUI.Setup" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (-not $SkipApp) {
    Write-Host "[1/4] Publishing app (self-contained, code split into DLLs)..." -ForegroundColor Yellow
    if (Test-Path $appPub) { Remove-Item $appPub -Recurse -Force }
    # The app's obj is relocated to C: for this build (see -p:BaseIntermediateOutputPath).
    # Remove the stale obj at the default location, otherwise its generated *.g.cs files
    # get picked up by the default **\*.cs compile glob -> duplicate-class errors.
    $staleObj = Join-Path $root "obj"
    if (Test-Path $staleObj) { Remove-Item $staleObj -Recurse -Force -ErrorAction SilentlyContinue }
    $tempObj = Join-Path $temp "voidui-app-obj"
    if (Test-Path $tempObj) { Remove-Item $tempObj -Recurse -Force -ErrorAction SilentlyContinue }
    # Leftover RID build output from previous attempts can fill D:.
    $staleRid = Join-Path $root "_build\bin\net8.0-windows\win-x64"
    if (Test-Path $staleRid) { Remove-Item $staleRid -Recurse -Force -ErrorAction SilentlyContinue }
    $tempBin = Join-Path $temp "voidui-app-bin"
    if (Test-Path $tempBin) { Remove-Item $tempBin -Recurse -Force -ErrorAction SilentlyContinue }
    # PublishSingleFile=false (#20): код остаётся в отдельных DLL, exe только запускает.
    # Так обновление может заменять или удалять ассемблей, а частичный патч одного
    # большого файла дал бы смешанную сборку.
    dotnet publish (Join-Path $root "VoidUI.csproj") `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:BaseIntermediateOutputPath="$temp\voidui-app-obj\" `
        -p:OutputPath="$temp\voidui-app-bin\" `
        -o $appPub
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) failed" }
    # The build disk is nearly full: drop intermediates right after publish.
    foreach ($d in @($tempObj, $tempBin)) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue }
    }

    Write-Host "[2/4] Packing payload.zip..." -ForegroundColor Yellow
    if (Test-Path $payload) { Remove-Item $payload -Force }
    Compress-Archive -Path (Join-Path $appPub "*") -DestinationPath $payload -CompressionLevel Optimal
    # Payload is packed; the publish dir is no longer needed either.
    if (Test-Path $appPub) { Remove-Item $appPub -Recurse -Force -ErrorAction SilentlyContinue }
}
elseif (-not (Test-Path $payload)) {
    throw "payload.zip not found at $payload - run without -SkipApp first"
}

Write-Host "[3/4] Building slim uninstaller (no payload)..." -ForegroundColor Yellow
$slimOut = Join-Path $temp "voidui-setup-slim"
if (Test-Path $slimOut) { Remove-Item $slimOut -Recurse -Force }
dotnet publish (Join-Path $root "Installer\VoidUI.Installer.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:SlimBuild=true `
    -p:Version="$Version" `
    -o $slimOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (slim uninstaller) failed" }
$slimExe = Join-Path $slimOut "VoidUI.Setup.exe"

Write-Host "[4/4] Building installer..." -ForegroundColor Yellow
if (Test-Path $insOut) { Remove-Item $insOut -Recurse -Force }
dotnet publish (Join-Path $root "Installer\VoidUI.Installer.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PayloadZip="$payload" `
    -p:UninstallerExe="$slimExe" `
    -p:Version="$Version" `
    -o $insOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed" }

$setup = Join-Path $insOut "VoidUI.Setup.exe"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
$slimMb = [math]::Round((Get-Item $slimExe).Length / 1MB, 1)
Write-Host ""
Write-Host "DONE: $setup ($sizeMb MB), slim uninstaller $slimMb MB" -ForegroundColor Green

# Try to drop a copy next to the app for convenience.
$deliver = Join-Path $root "_build\installer"
try {
    New-Item -ItemType Directory -Path $deliver -Force | Out-Null
    Copy-Item $setup $deliver -Force
    Write-Host "Copied to: $deliver\VoidUI.Setup.exe" -ForegroundColor Green
}
catch {
    Write-Host "Could not copy to $deliver (disk space?) - installer stays at $setup" -ForegroundColor DarkYellow
}
