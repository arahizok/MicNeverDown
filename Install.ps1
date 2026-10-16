param(
    [string]$DeviceId,
    [Nullable[double]]$LevelPercent,
    [switch]$NoStart,
    [switch]$NoAutostart
)
$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $PSScriptRoot 'MicNeverDown.exe'
$installDirectory = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex\Utilities\MicNeverDown'
$installedExe = Join-Path $installDirectory 'MicNeverDown.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'MicNeverDown.exe is missing from this folder.' }
if ($DeviceId -and $null -eq $LevelPercent) { throw 'Pass both -DeviceId and -LevelPercent, or neither.' }
if ($null -ne $LevelPercent -and (-not $DeviceId -or $LevelPercent -lt 0 -or $LevelPercent -gt 100 -or [double]::IsNaN($LevelPercent) -or [double]::IsInfinity($LevelPercent))) {
    throw 'LevelPercent must be between 0 and 100 and requires DeviceId.'
}
if (Test-Path -LiteralPath $installedExe) {
    $stopProcess = Start-Process -FilePath $installedExe -ArgumentList '--stop' -WindowStyle Hidden -PassThru -Wait
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $running = @(Get-Process -Name MicNeverDown -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExe })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($running.Count -gt 0) { throw 'Close MicNeverDown in the tray before updating it.' }
}
New-Item -Path $installDirectory -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
if ($DeviceId) {
    # Endpoint identifiers never contain quotes; reject invalid input before passing to a process.
    if ($DeviceId.Contains('"')) { throw 'Invalid endpoint identifier.' }
    $scalar = ([double]$LevelPercent / 100.0).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    $configureArgs = @('--configure', ('"' + $DeviceId + '"'), $scalar)
    $configureProcess = Start-Process -FilePath $installedExe -ArgumentList $configureArgs -WindowStyle Hidden -PassThru -Wait
    if ($configureProcess.ExitCode -ne 0) { throw 'Microphone configuration failed.' }
}
$startupArg = if ($NoAutostart) { '--disable-startup' } else { '--enable-startup' }
$startupProcess = Start-Process -FilePath $installedExe -ArgumentList $startupArg -WindowStyle Hidden -PassThru -Wait
if ($startupProcess.ExitCode -ne 0) { throw 'Could not update startup shortcut.' }
if (-not $NoStart) { Start-Process -FilePath $installedExe -ArgumentList '--background' -WindowStyle Hidden }
Write-Output ('Installed: ' + $installedExe)
Write-Output ('Startup: ' + (-not $NoAutostart))
