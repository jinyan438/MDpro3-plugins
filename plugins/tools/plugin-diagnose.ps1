<#
    Runs the built MDPro3 player with the plugin self test and reports the result.

    The player is started with the plugin argument "-mdpro3-plugin-selftest", which makes
    the plugin check the release date data and the sort popup integration and quit again.
    Used by Run_MDPro3.bat --diagnose.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,

    [string]$LogPath,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Output "PLUGINDIAGNOSE=ERROR"
    Write-Output "PLUGINMESSAGE=the built player was not found: $Exe"
    exit 2
}

$builtDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $Exe).Path
if ([string]::IsNullOrEmpty($LogPath)) {
    $LogPath = Join-Path $builtDirectory 'plugin-selftest.log'
}

if (Test-Path -LiteralPath $LogPath) {
    Remove-Item -LiteralPath $LogPath -Force
}

$arguments = @(
    '-mdpro3-plugin-selftest',
    '-screen-fullscreen', '0',
    '-screen-width', '960',
    '-screen-height', '540',
    '-logFile', ('"' + $LogPath + '"')
)

Write-Output "PLUGINMESSAGE=starting the player for the plugin self test"
$process = Start-Process -FilePath $Exe -ArgumentList $arguments -WorkingDirectory $builtDirectory -PassThru

$exited = $process.WaitForExit($TimeoutSeconds * 1000)
if (-not $exited) {
    try { $process.Kill() } catch { }
    Write-Output "PLUGINDIAGNOSE=TIMEOUT"
    Write-Output "PLUGINMESSAGE=the player did not finish the self test within $TimeoutSeconds seconds"
    exit 2
}

# A Unity player leaves a crash handler process behind for a moment and that process keeps
# the built assemblies mapped, which makes the next player build fail. Wait for it.
$cleanupDeadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $cleanupDeadline) {
    $handlers = @(Get-Process -Name 'UnityCrashHandler64' -ErrorAction SilentlyContinue)
    if ($handlers.Count -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Output "PLUGINDIAGNOSE=ERROR"
    Write-Output "PLUGINMESSAGE=the player wrote no log file: $LogPath"
    exit 2
}

$lines = @(Get-Content -LiteralPath $LogPath -Encoding UTF8)
$result = $lines | Where-Object { $_ -match 'MDPro3Plugins self test:' } | Select-Object -Last 1
$details = $lines | Where-Object { $_ -match 'MDPro3PluginsSelfTest' } | Select-Object -Last 25

foreach ($line in $details) {
    Write-Output ("PLUGINDETAIL=" + ($line -replace '\r', ''))
}

if ([string]::IsNullOrEmpty($result)) {
    Write-Output "PLUGINDIAGNOSE=ERROR"
    Write-Output "PLUGINMESSAGE=the self test did not report a result, can the plugin start? (log: $LogPath)"
    exit 2
}

if ($result -match 'PASS') {
    Write-Output "PLUGINDIAGNOSE=PASS"
    Write-Output "PLUGINMESSAGE=self test finished, log: $LogPath"
    exit 0
}

Write-Output "PLUGINDIAGNOSE=FAIL"
Write-Output "PLUGINMESSAGE=self test reported a problem, log: $LogPath"
exit 1
