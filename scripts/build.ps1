[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'CodexLimitReminder\CodexLimitReminder.slnx'
$tests = Join-Path $repoRoot 'CodexLimitReminder.Tests\CodexLimitReminder.Tests.csproj'
$app = Join-Path $repoRoot 'CodexLimitReminder\CodexLimitReminder.csproj'
$output = Join-Path $repoRoot 'artifacts\win-x64'

dotnet build $solution -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project $tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $app -c Release -r win-x64 -o $output --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$executable = Get-Item -LiteralPath (Join-Path $output 'CodexLimitReminder.exe')
$packageDirectory = Join-Path $repoRoot 'artifacts\package'
$packagePath = Join-Path $repoRoot 'artifacts\CodexLimitReminder-win-x64.zip'

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDirectory | Out-Null
Copy-Item -LiteralPath $executable.FullName -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\install.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\uninstall.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $packageDirectory
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $packagePath -Force
Remove-Item -LiteralPath $packageDirectory -Recurse -Force

Write-Host ("Published {0} ({1:N2} MiB)" -f $executable.FullName, ($executable.Length / 1MB))
Write-Host "Packaged $packagePath"
