param()
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $sourceRoot 'test-output'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testExe = Join-Path $testOutput 'CoreTests.exe'
$sources = @('GuardEngine.cs', 'ModelsAndStorage.cs', 'Audio.cs', 'Localization.cs') |
    ForEach-Object { Join-Path $sourceRoot $_ }
& $compiler /nologo /codepage:65001 /target:exe /platform:anycpu "/out:$testExe" (Join-Path $PSScriptRoot 'CoreTests.cs') @sources
if ($LASTEXITCODE -ne 0) { throw 'Core-test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
