param([Parameter(Mandatory=$true)][string]$RuntimePackage)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$package = (Resolve-Path -LiteralPath $RuntimePackage).Path
if (!(Test-Path -LiteralPath (Join-Path $package 'vendor\satloot-chromium\runtime-manifest.json'))) {
    throw 'RuntimePackage must be the extracted SATLOOT Lite folder containing vendor/.'
}
$build = Join-Path $project 'build'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory (Join-Path $build 'manager') -QaOutputDirectory (Join-Path $build 'qa')
$testRoot = Join-Path $build ('tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
Set-Content -LiteralPath (Join-Path $testRoot '.satloot-lite-test') -Value 'Isolated synthetic test data only' -Encoding UTF8
$qa = Join-Path $build 'qa\SATLOOT Lite QA.exe'
# QA creates no visible windows and uses only marked synthetic data.
$arguments = '--self-test "' + $testRoot + '" "' + $package + '"'
$process = Start-Process -FilePath $qa -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw ('Checks failed. Inspect ' + (Join-Path $testRoot 'error.txt')) }
$report = Get-Content -LiteralPath (Join-Path $testRoot 'checks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$report.passed -or $report.realProfilesUsed -or $report.systemInputUsed) { throw 'Test report failed its isolation checks.' }
Write-Output ('Passed ' + $report.checks.Count + ' checks. Report: ' + (Join-Path $testRoot 'checks.json'))
