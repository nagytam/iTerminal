# Checks that the .NET SDK that global.json asks for is available, and offers to install it.
#
# Usage: Tools\ensure-sdk.ps1
#
# Exit codes: 0 = a compatible SDK is available (now or after the install),
#             1 = no compatible SDK, and the user declined or the session cannot prompt.
# The prompt needs an interactive console. In CI (or when input is redirected) the script
# never prompts: it prints the install command and exits 1.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$globalJson = Join-Path $root 'global.json'

$requested = 'any'
if (Test-Path $globalJson) {
    $match = Select-String -Path $globalJson -Pattern '"version"\s*:\s*"([^"]*)"' | Select-Object -First 1
    if ($match) { $requested = $match.Matches[0].Groups[1].Value }
}

function Test-SdkAvailable {
    # `dotnet --version` resolves the SDK through global.json (rollForward included), so its exit
    # code is the authority: non-zero means no compatible SDK is installed.
    Push-Location $root
    try {
        & dotnet --version *> $null
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
    finally {
        Pop-Location
    }
}

if (Test-SdkAvailable) {
    Push-Location $root
    $version = & dotnet --version
    Pop-Location
    Write-Host "The .NET SDK $version is available (global.json asks for $requested)."
    exit 0
}

Write-Host "No .NET SDK compatible with global.json (requested $requested) is installed."
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Host 'Installed SDKs:'
    & dotnet --list-sdks | ForEach-Object { Write-Host "  $_" }
}

$interactive = [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and -not $env:CI
if (-not $interactive) {
    Write-Host "Install it with: powershell -ExecutionPolicy Bypass -c `"& ([scriptblock]::Create((iwr https://dot.net/v1/dotnet-install.ps1).Content)) -JsonFile '$globalJson'`""
    exit 1
}

$answer = Read-Host "Download and install the .NET SDK $requested now (into %LOCALAPPDATA%\Microsoft\dotnet)? [y/N]"
if ($answer -notmatch '^(y|yes)$') {
    Write-Host 'Not installed. Install the SDK, or change global.json to an installed version.'
    exit 1
}

$installer = Join-Path ([IO.Path]::GetTempPath()) "dotnet-install-$([guid]::NewGuid()).ps1"
try {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & $installer -JsonFile $globalJson
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'The installer failed.'
        exit 1
    }
}
finally {
    Remove-Item $installer -ErrorAction SilentlyContinue
}

Write-Host 'Add %LOCALAPPDATA%\Microsoft\dotnet to PATH if it is not there yet, then rerun the build.'
$installedDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (Test-Path $installedDotnet) {
    Push-Location $root
    & $installedDotnet --version *> $null
    $ready = ($LASTEXITCODE -eq 0)
    Pop-Location
    if ($ready) {
        Write-Host 'The .NET SDK is ready.'
        exit 0
    }
}
exit 1
