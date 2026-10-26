param(
    [string]$OutputName='SATLOOT Lite.exe',
    [string]$OutputDirectory,
    [string]$QaOutputDirectory
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Windows x64 with the .NET Framework compiler is required.' }
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [string]::IsNullOrWhiteSpace($OutputName)) { throw 'OutputName must be a file name, not a path.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $project 'preview' }
if (!$QaOutputDirectory) { $QaOutputDirectory = Join-Path $project 'evidence' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$qaOutput = [IO.Path]::GetFullPath($QaOutputDirectory)
New-Item -ItemType Directory -Force -Path $output,$qaOutput | Out-Null
$common = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/win32manifest:' + (Join-Path $project 'src\app.manifest')), '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Security.dll','/reference:System.Core.dll','/reference:System.Management.dll', (Join-Path $project 'src\Core.cs'),(Join-Path $project 'src\UI.cs'))
$common += @('/reference:System.Web.dll',(Join-Path $project 'src\Network.cs'),(Join-Path $project 'src\Language.cs'))
$common += @((Join-Path $project 'src\Routes.cs'),(Join-Path $project 'src\NetworkUI.cs'))
$common += @((Join-Path $project 'src\BrowserActions.cs'))
$common += @('/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',(Join-Path $project 'src\Backups.cs'))
& $compiler ('/out:' + (Join-Path $output $OutputName)) @common
if ($LASTEXITCODE -ne 0) {throw 'Production build failed'}
& $compiler ('/out:' + (Join-Path $qaOutput 'SATLOOT Lite QA.exe')) '/define:QA' @common (Join-Path $project 'src\Checks.cs')
if ($LASTEXITCODE -ne 0) {throw 'QA build failed'}
Write-Output ('Manager: ' + (Join-Path $output $OutputName))
Write-Output ('QA (not for distribution): ' + (Join-Path $qaOutput 'SATLOOT Lite QA.exe'))
