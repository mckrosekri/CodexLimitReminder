[CmdletBinding()]
param(
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$localPrograms = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'
$installDirectory = Join-Path $localPrograms 'CodexLimitReminder'
$expectedPrefix = [IO.Path]::GetFullPath($localPrograms).TrimEnd('\') + '\'
$resolvedTarget = [IO.Path]::GetFullPath($installDirectory)

if (-not $resolvedTarget.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove an unexpected install path: $resolvedTarget"
}

Get-Process -Name 'CodexLimitReminder' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CodexLimitReminder' -ErrorAction SilentlyContinue
$startupDirectory = [Environment]::GetFolderPath('Startup')
$startupScript = Join-Path $startupDirectory 'CodexLimitReminder.vbs'
$startupPrefix = [IO.Path]::GetFullPath($startupDirectory).TrimEnd('\') + '\'
$resolvedStartupScript = [IO.Path]::GetFullPath($startupScript)
if (-not $resolvedStartupScript.StartsWith($startupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove an unexpected startup path: $resolvedStartupScript"
}
Remove-Item -LiteralPath $resolvedStartupScript -Force -ErrorAction SilentlyContinue

if (-not $KeepSettings) {
    Remove-Item -LiteralPath 'HKCU:\Software\CodexLimitReminder' -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $resolvedTarget) {
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

Write-Host 'Codex Limit Reminder was uninstalled.'
