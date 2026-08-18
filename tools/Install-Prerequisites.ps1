<#
.SYNOPSIS
Checks for everything GST Inspect needs and installs whatever is missing.

.DESCRIPTION
The app cannot install these itself: it needs the .NET runtime just to start, and a GUI
that silently installs system-wide software is hard to trust and harder to debug. So the
app only detects and reports, and this script does the installing.

Checked:

  * .NET 9 Desktop Runtime - required to run either front end
  * .NET 9 SDK             - only with -IncludeSdk, for building from source
  * Google Chrome          - the GST portal is driven through a real Chrome window

Spreadsheets are read in-process (ClosedXML for .xlsx/.xlsm, ExcelDataReader for legacy
.xls), so there is no Python or interpreter dependency to install.

Installs go through winget, so packages come from their publishers rather than an ad-hoc
download, and each one is reported before it runs. winget may raise a UAC prompt.

.PARAMETER Check
Report what is missing and change nothing. Exit code 0 when everything is present, 1 when
something is missing - usable as a CI or pre-flight gate.

.PARAMETER IncludeSdk
Also require the .NET SDK, needed to build from source rather than just run a published
build.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\Install-Prerequisites.ps1 -Check

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\Install-Prerequisites.ps1 -IncludeSdk
#>

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$IncludeSdk
)

$ErrorActionPreference = 'Stop'

$script:Missing = @()
$script:Installed = @()
$script:Failed = @()

function Write-Section {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host ''
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ('-' * $Text.Length) -ForegroundColor Cyan
}

function Test-Winget {
    return $null -ne (Get-Command winget.exe -ErrorAction SilentlyContinue)
}

<#
Installs one winget package, unless -Check was passed. Every path is reported so a run is
auditable from its output alone.
#>
function Install-Dependency {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$WingetId
    )

    $script:Missing += $Name

    if ($Check) {
        Write-Host "  MISSING  $Name  (winget install --id $WingetId)" -ForegroundColor Yellow
        return
    }

    if (-not (Test-Winget)) {
        Write-Host "  MISSING  $Name - winget is unavailable, install manually: winget install --id $WingetId" -ForegroundColor Red
        $script:Failed += $Name
        return
    }

    Write-Host "  Installing $Name (winget id $WingetId)..." -ForegroundColor Yellow

    # --silent keeps installers unattended; winget still surfaces UAC when a package needs it.
    & winget.exe install --id $WingetId --exact --silent `
        --accept-package-agreements --accept-source-agreements

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Installed $Name." -ForegroundColor Green
        $script:Installed += $Name
    }
    else {
        Write-Host "  Failed to install $Name (winget exit code $LASTEXITCODE)." -ForegroundColor Red
        $script:Failed += $Name
    }
}

function Test-DotnetRuntime {
    param([Parameter(Mandatory)][string]$Pattern)

    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { return $false }

    $runtimes = & dotnet.exe --list-runtimes
    return [bool]($runtimes | Select-String -SimpleMatch $Pattern -Quiet)
}

# ---------------------------------------------------------------------------
# .NET
# ---------------------------------------------------------------------------

Write-Section '.NET'

if (Test-DotnetRuntime -Pattern 'Microsoft.WindowsDesktop.App 9.') {
    Write-Host '  OK       .NET 9 Desktop Runtime' -ForegroundColor Green
}
else {
    # The WinForms front end needs the desktop runtime; it also carries the base runtime
    # the console app needs, so one package covers both.
    Install-Dependency -Name '.NET 9 Desktop Runtime' -WingetId 'Microsoft.DotNet.DesktopRuntime.9'
}

if ($IncludeSdk) {
    $sdks = if (Get-Command dotnet.exe -ErrorAction SilentlyContinue) { & dotnet.exe --list-sdks } else { @() }

    # Any SDK from 9 upwards can build net9.0 - a 10.x SDK is not a missing prerequisite.
    $newestSdk = $sdks |
        ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } } |
        Sort-Object -Descending |
        Select-Object -First 1

    if ($null -ne $newestSdk -and $newestSdk -ge 9) {
        Write-Host "  OK       .NET SDK $newestSdk.x (builds net9.0)" -ForegroundColor Green
    }
    else {
        Install-Dependency -Name '.NET 9 SDK' -WingetId 'Microsoft.DotNet.SDK.9'
    }
}

# ---------------------------------------------------------------------------
# Chrome
# ---------------------------------------------------------------------------

Write-Section 'Google Chrome'

# Kept in step with Prerequisites.FindChrome in the Core library.
$chromeCandidates = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
)

$chrome = $chromeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($chrome) {
    Write-Host "  OK       Chrome at $chrome" -ForegroundColor Green
}
else {
    Install-Dependency -Name 'Google Chrome' -WingetId 'Google.Chrome'
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Section 'Summary'

if ($script:Missing.Count -eq 0) {
    Write-Host '  Everything GST Inspect needs is already installed.' -ForegroundColor Green
    exit 0
}

if ($Check) {
    Write-Host "  $($script:Missing.Count) missing: $($script:Missing -join ', ')" -ForegroundColor Yellow
    Write-Host '  Re-run without -Check to install.'
    exit 1
}

if ($script:Installed.Count -gt 0) {
    Write-Host "  Installed: $($script:Installed -join ', ')" -ForegroundColor Green
}

if ($script:Failed.Count -gt 0) {
    Write-Host "  Still missing: $($script:Failed -join ', ')" -ForegroundColor Red
    Write-Host '  Install those manually, then re-run with -Check to confirm.'
    exit 1
}

Write-Host '  All prerequisites are now in place.' -ForegroundColor Green
Write-Host '  Open a new shell so PATH changes from the installers take effect.' -ForegroundColor Yellow
exit 0
