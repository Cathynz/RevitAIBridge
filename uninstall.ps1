<#
.SYNOPSIS
    Removes RevitAI Bridge from Revit 2025 and 2026 on this PC.

.DESCRIPTION
    Deletes the .addin files from %APPDATA%\Autodesk\Revit\Addins\<year>\ and the program
    files from %LOCALAPPDATA%\RevitAIBridge\bin. Close Revit first. Your projects are untouched.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'RevitAIBridge\bin'),
    [string]$AddinsRoot  = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins'),
    [switch]$SkipRevitCheck
)

$ErrorActionPreference = 'Stop'

if (-not $SkipRevitCheck -and (Get-Process Revit -ErrorAction SilentlyContinue)) {
    Write-Host "Revit is open. Close it, then run this again." -ForegroundColor Red
    exit 1
}

foreach ($y in 2025, 2026) {
    $addin = Join-Path $AddinsRoot "$y\RevitAIBridge.$y.addin"
    if (Test-Path $addin) {
        Remove-Item -LiteralPath $addin -Force
        Write-Host "Removed $addin"
    }
}

if (Test-Path $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
    Write-Host "Removed $InstallRoot"
}

Write-Host "Done. RevitAI Bridge is uninstalled."
