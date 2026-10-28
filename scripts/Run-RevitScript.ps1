<#
.SYNOPSIS
    Runs a C# script inside your open Revit session via RevitAI Bridge and prints the result.

.DESCRIPTION
    Does the whole folder protocol for you: checks Revit is alive, writes the script
    atomically, triggers it, waits for the result, and prints it.

.EXAMPLE
    .\Run-RevitScript.ps1 'Doc.Title'

.EXAMPLE
    .\Run-RevitScript.ps1 -File ..\examples\list-levels.csx

.NOTES
    Exit code is 0 when the script returned OK, 1 when it returned ERROR or timed out.
    If PowerShell refuses to run the file, start it as:
        powershell -ExecutionPolicy Bypass -File .\Run-RevitScript.ps1 'Doc.Title'
#>
[CmdletBinding(DefaultParameterSetName = 'Inline')]
param(
    [Parameter(Mandatory, Position = 0, ParameterSetName = 'Inline')]
    [string]$Script,

    [Parameter(Mandatory, ParameterSetName = 'File')]
    [string]$File,

    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

$root = if ($env:REVITAI_BRIDGE_ROOT) { $env:REVITAI_BRIDGE_ROOT } else { Join-Path $env:LOCALAPPDATA 'RevitAIBridge' }
$statusPath = Join-Path $root 'bridge_status.json'
$taskTmp    = Join-Path $root 'current_task.tmp'
$taskPath   = Join-Path $root 'current_task.csx'
$flagPath   = Join-Path $root 'run.flag'
$resultPath = Join-Path $root 'last_result.txt'

# 1. Is Revit alive? The add-in rewrites bridge_status.json about every 30 seconds.
if (-not (Test-Path $statusPath)) {
    Write-Error "No bridge_status.json in $root. Is the add-in installed, and has Revit been started at least once?"
    exit 1
}
$raw = Get-Content $statusPath -Raw
if ($raw -match '"updated_utc":\s*"([^"]+)"') {
    $updated = [DateTimeOffset]::Parse($matches[1], [Globalization.CultureInfo]::InvariantCulture)
    $age = [DateTimeOffset]::UtcNow - $updated
    if ($age.TotalSeconds -gt 120) {
        Write-Error ("Revit looks closed: the bridge last reported {0:N0} seconds ago. Start Revit and try again." -f $age.TotalSeconds)
        exit 1
    }
}

# 2. Get the script text.
$content = if ($PSCmdlet.ParameterSetName -eq 'File') { Get-Content -LiteralPath $File -Raw } else { $Script }

# 3. Write it atomically (temp file, then rename) so Revit never reads half a file.
[IO.File]::WriteAllText($taskTmp, $content, (New-Object Text.UTF8Encoding $false))
Move-Item -LiteralPath $taskTmp -Destination $taskPath -Force

# 4. Remember the old result's timestamp, then pull the trigger.
$before = if (Test-Path $resultPath) { (Get-Item $resultPath).LastWriteTimeUtc } else { [datetime]::MinValue }
New-Item -ItemType File -Path $flagPath -Force | Out-Null

# 5. Wait for last_result.txt to change.
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $resultPath) -and ((Get-Item $resultPath).LastWriteTimeUtc -gt $before)) {
        Start-Sleep -Milliseconds 200   # let the atomic move settle
        $text = Get-Content $resultPath -Raw
        Write-Output $text
        if ($text -match '^\s*OK') { exit 0 } else { exit 1 }
    }
    Start-Sleep -Milliseconds 500
}

Write-Error ("Timed out after {0}s. Revit only runs tasks when it is idle: close any open dialog or finish the command you are in, then try again." -f $TimeoutSeconds)
exit 1
