param(
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'src\JitenMPC-BE.App\JitenMPC-BE.App.csproj'
$PublishDir = Join-Path $Root 'publish'
$LocalDotnet = Join-Path $Root '.dotnet'
$SdkVersion = '10.0.302'

function Write-Step([string]$Text) {
    Write-Host "`n==> $Text" -ForegroundColor Cyan
}

function Test-DotnetSdk([string]$ExePath) {
    if (-not (Test-Path $ExePath)) { return $false }
    try {
        $sdks = & $ExePath --list-sdks 2>$null
        if ($LASTEXITCODE -ne 0) { return $false }
        $usable = $sdks | ForEach-Object {
            $token = ($_ -split '\s+')[0]
            try { [version]$token } catch { $null }
        } | Where-Object { $_ -and $_.Major -eq 10 -and $_ -ge [version]$SdkVersion }
        return [bool]$usable
    } catch {
        return $false
    }
}

function Get-DotnetPath {
    $system = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($system -and (Test-DotnetSdk $system.Source)) {
        return [string]$system.Source
    }

    $localExe = Join-Path $LocalDotnet 'dotnet.exe'
    if (Test-DotnetSdk $localExe) {
        return [string]$localExe
    }

    # Reuse a local SDK downloaded by an earlier preview when possible.
    $parent = Split-Path -Parent $Root
    $siblings = Get-ChildItem -Path $parent -Directory -Filter 'JitenMPC-BE-Avalonia-v*' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $Root } |
        Sort-Object LastWriteTime -Descending
    foreach ($sibling in $siblings) {
        $candidate = Join-Path $sibling.FullName '.dotnet\dotnet.exe'
        if (Test-DotnetSdk $candidate) {
            Write-Step "Reusing .NET SDK from $($sibling.Name)"
            return [string]$candidate
        }
    }

    Write-Step ".NET 10 SDK not found; installing SDK $SdkVersion locally (no admin required)"
    New-Item -ItemType Directory -Force -Path $LocalDotnet | Out-Null
    $installer = Join-Path $Root '.dotnet-install.ps1'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer

    # IMPORTANT: dotnet-install writes status lines to the success output stream.
    # Route them to the host so Get-DotnetPath returns ONLY dotnet.exe's path.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Version $SdkVersion -Architecture x64 -InstallDir $LocalDotnet -NoPath 2>&1 | Out-Host
    $installExitCode = $LASTEXITCODE
    if ($installExitCode -ne 0 -or -not (Test-DotnetSdk $localExe)) {
        throw "The local .NET SDK installation failed (exit code $installExitCode)."
    }
    return [string]$localExe
}

if ($Clean) {
    Write-Step 'Cleaning previous build output'
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir
    Get-ChildItem -Path (Join-Path $Root 'src') -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin','obj') } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

$Dotnet = Get-DotnetPath
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$DotnetDir = Split-Path -Parent $Dotnet
if ((Split-Path -Leaf $Dotnet) -ieq 'dotnet.exe') {
    $env:DOTNET_ROOT = $DotnetDir
    $env:PATH = "$DotnetDir;$env:PATH"
}

Write-Step "Using $(& $Dotnet --version)"
Write-Step 'Restoring Avalonia/.NET dependencies'
& $Dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Write-Step 'Publishing Windows x64 self-contained single-file preview'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir
& $Dotnet publish $Project -c Release -r win-x64 --self-contained true -o $PublishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

foreach ($name in @('README.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md','LICENSE-JitenMPV.txt')) {
    $src = Join-Path $Root $name
    if (Test-Path $src) { Copy-Item $src $PublishDir -Force }
}

$exe = Join-Path $PublishDir 'JitenMPC-BE.exe'
if (-not (Test-Path $exe)) { throw "Build completed without the expected executable: $exe" }

Write-Host "`nBuild succeeded:" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "`nThis is the JitenMPC-BE Avalonia feature-parity preview with mining."

if ($Run) {
    Write-Step 'Launching JitenMPC-BE Avalonia preview'
    Start-Process -FilePath $exe -WorkingDirectory $PublishDir
}
