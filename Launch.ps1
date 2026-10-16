param([ValidateSet('Settings', 'Stop')][string]$Action = 'Settings')
$ErrorActionPreference = 'Stop'
$installedExe = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex\Utilities\MicNeverDown\MicNeverDown.exe'
$programExe = if (Test-Path -LiteralPath $installedExe) { $installedExe } else { Join-Path $PSScriptRoot 'MicNeverDown.exe' }
$programArgs = if ($Action -eq 'Stop') { '--disable-startup --stop' } else { '--settings' }
Start-Process -FilePath $programExe -ArgumentList $programArgs -WindowStyle Hidden
