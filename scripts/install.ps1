[CmdletBinding()]
param(
    [string]$SourcePath
)

$ErrorActionPreference = 'Stop'
$releaseExecutable = Join-Path $PSScriptRoot 'CodexLimitReminder.exe'
$repositoryExecutable = Join-Path $PSScriptRoot '..\artifacts\win-x64\CodexLimitReminder.exe'

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = if (Test-Path -LiteralPath $releaseExecutable) { $releaseExecutable } else { $repositoryExecutable }
}

$source = (Resolve-Path -LiteralPath $SourcePath).Path
$localPrograms = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'
$installDirectory = Join-Path $localPrograms 'CodexLimitReminder'
$installedExecutable = Join-Path $installDirectory 'CodexLimitReminder.exe'

$running = Get-Process -Name 'CodexLimitReminder' -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $installedExecutable -Force

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name 'CodexLimitReminder' -Value ('"{0}" --background' -f $installedExecutable) -PropertyType String -Force | Out-Null

Start-Process -FilePath $installedExecutable -ArgumentList '--show-settings'
Write-Host "Installed Codex Limit Reminder to $installDirectory"
