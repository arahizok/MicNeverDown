$ErrorActionPreference = 'Stop'
$baseDirectory = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex\Utilities'))
$installDirectory = [IO.Path]::GetFullPath((Join-Path $baseDirectory 'MicNeverDown'))
$expectedDirectory = $baseDirectory.TrimEnd('\') + '\MicNeverDown'
if ($installDirectory -ne $expectedDirectory) { throw 'Unexpected installation directory.' }
$installedExe = Join-Path $installDirectory 'MicNeverDown.exe'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'MicNeverDown.lnk'
if (Test-Path -LiteralPath $installedExe) {
    Start-Process -FilePath $installedExe -ArgumentList '--disable-startup --stop' -WindowStyle Hidden -Wait
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $running = @(Get-Process -Name MicNeverDown -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExe })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($running.Count -gt 0) { throw 'Close MicNeverDown in the tray and retry uninstall.' }
}
if (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink -Force }
if (Test-Path -LiteralPath $installDirectory) {
    $targetItem = Get-Item -LiteralPath $installDirectory -Force
    if ($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove a redirected directory.' }
    # Remove only known files; preserve any other user files in the directory.
    foreach ($fileName in @('MicNeverDown.exe')) {
        $filePath = Join-Path $installDirectory $fileName
        if (Test-Path -LiteralPath $filePath -PathType Leaf) { Remove-Item -LiteralPath $filePath -Force }
    }
    $dataDirectory = Join-Path $installDirectory 'data'
    if (Test-Path -LiteralPath $dataDirectory) {
        $dataItem = Get-Item -LiteralPath $dataDirectory -Force
        if ($dataItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove redirected data.' }
        foreach ($fileName in @('settings.xml', 'status.xml', 'ui.xml', 'last-error.txt', 'settings.xml.tmp', 'status.xml.tmp', 'ui.xml.tmp')) {
            $filePath = Join-Path $dataDirectory $fileName
            if (Test-Path -LiteralPath $filePath -PathType Leaf) { Remove-Item -LiteralPath $filePath -Force }
        }
        if (@(Get-ChildItem -LiteralPath $dataDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $dataDirectory }
    }
    if (@(Get-ChildItem -LiteralPath $installDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $installDirectory }
}
Write-Output 'MicNeverDown removed. The current microphone level was left as-is.'
