<#
.SYNOPSIS
    Builds the HornetStudio application, compiles the Inno Setup installer, and packages a portable ZIP for Windows x64 and ARM64.
.PARAMETER Version
    The SemVer version string (e.g. 1.0.0).
.PARAMETER Architecture
    Target architecture: 'x64', 'arm64', or 'all'. Defaults to 'all'.
#>
param(
    [string]$Version = "1.0.0",
    [ValidateSet("x64", "arm64", "all")]
    [string]$Architecture = "all"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$artifactsDir = Join-Path $rootDir "artifacts\installer"
$issPath = Join-Path $rootDir "installer\HornetStudio.iss"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " HornetStudio - Windows Installer & Package Builder" -ForegroundColor Cyan
Write-Host " Version: $Version | Architecture: $Architecture" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

# Locate Inno Setup Compiler (ISCC)
$isccPath = $null
$candidatePaths = @(
    "iscc",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

foreach ($candidate in $candidatePaths) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        $isccPath = $candidate
        break
    }
    if (Test-Path $candidate) {
        $isccPath = $candidate
        break
    }
}

if ($isccPath) {
    Write-Host "Found ISCC at: $isccPath" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup Compiler (ISCC.exe) not found. Portable ZIPs will be created, but installer executables will be skipped."
}

$architectures = if ($Architecture -eq "all") { @("x64", "arm64") } else { @($Architecture) }

foreach ($arch in $architectures) {
    Write-Host "`n>>> Processing Architecture: win-$arch <<<" -ForegroundColor Magenta
    $distDir = Join-Path $rootDir "dist\win-$arch"

    if (Test-Path $distDir) {
        Remove-Item $distDir -Recurse -Force
    }

    # 1. Publish self-contained executable
    Write-Host "Publishing self-contained .NET 11 desktop app (win-$arch)..." -ForegroundColor Yellow
    dotnet publish "$rootDir\src\HornetStudio.Desktop\HornetStudio.Desktop.csproj" `
        -c Release `
        -r "win-$arch" `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -o $distDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed for win-$arch."
        exit 1
    }

    # 2. Compile Inno Setup Script
    if ($isccPath) {
        Write-Host "Compiling Inno Setup installer for win-$arch..." -ForegroundColor Yellow
        & $isccPath "/DMyAppVersion=$Version" "/DMyAppArch=$arch" $issPath
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Inno Setup compilation failed for win-$arch."
            exit 1
        }
    }

    # 3. Create Portable ZIP
    Write-Host "Creating portable ZIP for win-$arch..." -ForegroundColor Yellow
    $zipPath = Join-Path $artifactsDir "HornetStudio-Portable-win-$arch-v$Version.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host "`nWindows packaging complete! Artifacts in: $artifactsDir" -ForegroundColor Green
Get-ChildItem $artifactsDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
