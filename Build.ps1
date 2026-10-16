#Requires -Version 7.0
<#
Build with the Roslyn libraries bundled in a Windows PowerShell 7 installation.
Example: pwsh -NoProfile -File .\Build.ps1
No downloads, package restore, installation, application execution, or startup changes.
All framework references are the local Microsoft .NET Framework 4.x assemblies.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This build targets Windows .NET Framework and requires Windows.' }

$sourceRoot = $PSScriptRoot
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$compilerPaths = @(
    (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'),
    (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
)
$supportPath = Join-Path $sourceRoot 'BuildSupport.cs'
$referenceDirectory = Join-Path $PSHOME 'ref'
foreach ($required in @($compilerPaths) + @($supportPath, $referenceDirectory)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing local compiler component: $required. Use a standard PowerShell 7 distribution."
    }
}

# Add-Type compiles the helper against PowerShell's .NET reference assemblies.
# These are build-time dependencies; the application never references them.
if (-not ('MicNeverDown.Build.Compiler' -as [type])) {
    $helperReferences = @(
        Get-ChildItem -LiteralPath $referenceDirectory -Filter '*.dll' -File |
            Sort-Object Name | ForEach-Object FullName
    ) + $compilerPaths
    # PowerShell may bundle Roslyn built against the preceding .NET version.
    # Its host provides runtime unification; suppress only that helper warning.
    Add-Type -Path $supportPath -ReferencedAssemblies $helperReferences -CompilerOptions '/nowarn:1701,1702'
}

$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $frameworkDirectory)) {
    $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$frameworkNames = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll')
$frameworkReferences = @($frameworkNames | ForEach-Object { Join-Path $frameworkDirectory $_ })
foreach ($reference in $frameworkReferences) {
    if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) {
        throw "Missing Microsoft .NET Framework assembly: $reference"
    }
}

$inputNames = @([MicNeverDown.Build.Compiler]::ApplicationFiles) + @('Cat.ico', 'app.manifest')
foreach ($name in $inputNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $name) -PathType Leaf)) {
        throw "Missing application input: $name"
    }
}

[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$outputPath = Join-Path $outputRoot 'MicNeverDown.exe'
$diagnostics = [MicNeverDown.Build.Compiler]::Compile($sourceRoot, $outputPath, [string[]] $frameworkReferences)
if ($diagnostics) { Write-Warning $diagnostics }

function Get-FileRecord([string] $Name, [string] $Path) {
    [ordered]@{ name = $Name; sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$compilerVersion = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation].Assembly.GetName().Version.ToString()
$buildInfo = [ordered]@{
    application = 'MicNeverDown'
    version = '1.3.0'
    target = '.NET Framework 4.8; WinExe; AnyCPU; Release; C# 7.3'
    deterministic = $true
    compilerAssemblyVersion = $compilerVersion
    compilerFileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($compilerPaths[1]).FileVersion
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    compilerFiles = @($compilerPaths | ForEach-Object { Get-FileRecord ([System.IO.Path]::GetFileName($_)) $_ })
    frameworkFiles = @($frameworkReferences | ForEach-Object { Get-FileRecord ([System.IO.Path]::GetFileName($_)) $_ })
    sourceFiles = @($inputNames + @('Build.ps1', 'BuildSupport.cs') | ForEach-Object { Get-FileRecord $_ (Join-Path $sourceRoot $_) })
    output = Get-FileRecord 'MicNeverDown.exe' $outputPath
}
$json = $buildInfo | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText((Join-Path $outputRoot 'build-info.json'), $json + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Built: $outputPath"
Write-Host "Roslyn: $compilerVersion"
Write-Host "SHA256: $($buildInfo.output.sha256)"
Write-Host 'Reproducibility requires identical input files, compiler files, and framework files (see build-info.json).'
