[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ApplicationPath,
    [string] $OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'test-output')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$application = (Resolve-Path -LiteralPath $ApplicationPath).Path
if ([IO.Path]::GetFileName($application) -ne 'MicNeverDown.exe') {
    throw 'ApplicationPath must point to the built MicNeverDown.exe.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$runRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('localization-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$harness = Join-Path $runRoot 'LocalizationTests.exe'
& $compiler /nologo /codepage:65001 /target:exe /platform:anycpu "/out:$harness" "/reference:$application" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'LocalizationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Localization-test compilation failed.' }
$cases = @('missing', 'en', 'ru', 'invalid', 'empty', 'malformed', 'oversized', 'dtd', 'transitions', 'write-failure', 'legacy-settings', 'tray', 'ui')
foreach ($case in $cases) {
    $caseRoot = Join-Path $runRoot $case
    [IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    Copy-Item -LiteralPath $application -Destination (Join-Path $caseRoot 'MicNeverDown.exe')
    Copy-Item -LiteralPath $harness -Destination (Join-Path $caseRoot 'LocalizationTests.exe')
    & (Join-Path $caseRoot 'LocalizationTests.exe') $case
    if ($LASTEXITCODE -ne 0) { throw "Localization scenario failed: $case. Evidence: $caseRoot" }
}
Write-Host "All $($cases.Count) localization scenarios passed. Isolated evidence: $runRoot"
Write-Host 'No live guard was started and no microphone, installed settings or startup shortcut was changed.'
