<#
.SYNOPSIS
    Installs RevitAI Bridge into Revit 2025 and/or Revit 2026 on this PC.

.DESCRIPTION
    Run this once. It:
      1. finds which Revit versions you have (2025, 2026),
      2. makes the add-in program from the source code in this folder,
      3. puts it under %LOCALAPPDATA%\RevitAIBridge\bin\<year>\,
      4. tells Revit about it by writing a small .addin file into
         %APPDATA%\Autodesk\Revit\Addins\<year>\.
    No administrator rights are needed. Close Revit first.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1

.EXAMPLE
    .\install.ps1 -Versions 2026        # only Revit 2026
#>
[CmdletBinding()]
param(
    # Which Revit years to install for. Default: every supported year found on this PC.
    [int[]]$Versions,

    # Advanced: where the program files go, and where the .addin files go.
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'RevitAIBridge\bin'),
    [string]$AddinsRoot  = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins'),

    # Advanced: skip the "is Revit open?" check (only for testing).
    [switch]$SkipRevitCheck
)

$ErrorActionPreference = 'Stop'
$repo      = $PSScriptRoot
$supported = 2025, 2026

function Fail($message) {
    Write-Host ""
    Write-Host "Could not install: $message" -ForegroundColor Red
    exit 1
}

# --- Is Revit closed? (Revit locks the add-in file while it is running.) ---
if (-not $SkipRevitCheck -and (Get-Process Revit -ErrorAction SilentlyContinue)) {
    Fail "Revit is open. Save your work, close Revit, then run this again."
}

# --- Which Revit versions? ---
if (-not $Versions) {
    $Versions = @($supported | Where-Object { Test-Path "C:\Program Files\Autodesk\Revit $_\Revit.exe" })
}
if (-not $Versions -or $Versions.Count -eq 0) {
    Fail ("Revit 2025 or 2026 was not found in C:\Program Files\Autodesk. " +
          "If Revit is installed somewhere else, run:  .\install.ps1 -Versions 2026")
}
foreach ($y in $Versions) {
    if ($supported -notcontains $y) { Fail "Revit $y is not supported yet. Supported: $($supported -join ', ')." }
}

# --- Is the free .NET SDK installed? (It turns the source code into the add-in program.) ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail ("Your PC is missing a free Microsoft tool called .NET. Paste this line, press Enter,`n" +
          "then run this installer again:`n" +
          "    winget install Microsoft.DotNet.SDK.8")
}
$sdkOk = & dotnet --list-sdks | Where-Object { ([int](($_ -split '\.')[0])) -ge 8 }
if (-not $sdkOk) {
    Fail ("Your .NET tool is too old. Paste this line, press Enter,`n" +
          "then run this installer again:`n" +
          "    winget install Microsoft.DotNet.SDK.8")
}

Write-Host "Installing RevitAI Bridge for Revit: $($Versions -join ', ')" -ForegroundColor Cyan
Write-Host "(The first time, this downloads some files, so it can take a few minutes.)"

foreach ($y in $Versions) {
    Write-Host ""
    Write-Host "--- Revit $y ---" -ForegroundColor Cyan

    $project  = Join-Path $repo "RevitAIBridge.$y\RevitAIBridge.$y.csproj"
    $template = Join-Path $repo "addins\RevitAIBridge.$y.addin"
    $dest     = Join-Path $InstallRoot "$y"
    if (-not (Test-Path $project))  { Fail "Missing $project. Is this the full RevitAIBridge folder?" }
    if (-not (Test-Path $template)) { Fail "Missing $template." }

    New-Item -ItemType Directory -Force $dest | Out-Null

    Write-Host "Getting it ready (please wait)..."
    & dotnet build $project -c Release -o $dest -p:SatelliteResourceLanguages=en --nologo -v:q
    if ($LASTEXITCODE -ne 0) { Fail "Getting it ready for Revit $y did not work. The messages above say why." }

    $dll = Join-Path $dest 'RevitAIBridge.dll'
    if (-not (Test-Path $dll)) { Fail "Expected $dll but it was not created." }

    # Write the .addin file Revit reads, pointing at the program we just made.
    $xml = (Get-Content -Raw $template) -replace '<Assembly>.*?</Assembly>', "<Assembly>$dll</Assembly>"
    $addinDir = Join-Path $AddinsRoot "$y"
    New-Item -ItemType Directory -Force $addinDir | Out-Null
    $addinFile = Join-Path $addinDir "RevitAIBridge.$y.addin"
    [IO.File]::WriteAllText($addinFile, $xml, (New-Object Text.UTF8Encoding $false))

    Write-Host "Program:  $dll"
    Write-Host "Revit is told about it in:  $addinFile"
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host ""
Write-Host "Next:"
Write-Host "  1. Start Revit and open any project."
Write-Host "  2. Revit will show a box saying it can't check who made 'RevitAI Bridge'."
Write-Host "     Click  Always Load.  (Revit asks this about every new add-on. It is normal.)"
Write-Host "  3. Test it. Come back to this window and paste:"
Write-Host "       .\scripts\Run-RevitScript.ps1 'Doc.Title'"
Write-Host "     You should see OK and the name of your project."
