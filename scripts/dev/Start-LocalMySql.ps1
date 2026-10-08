[CmdletBinding()]
param(
    [string]$MySqlLocalRoot = (Join-Path $env:LOCALAPPDATA 'FlowHearth')
)

$ErrorActionPreference = 'Stop'
$mysqlServer = Join-Path $MySqlLocalRoot 'mysql-8.4.11-winx64\bin\mysqld.exe'
$optionFile = Join-Path $MySqlLocalRoot 'my.ini'

foreach ($requiredPath in @($mysqlServer, $optionFile)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required local MySQL path does not exist: $requiredPath"
    }
}

$existingListener = Get-NetTCPConnection `
    -State Listen `
    -LocalPort 3306 `
    -ErrorAction SilentlyContinue

if ($existingListener) {
    $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($existingListener.OwningProcess)"
    if ($owner.ExecutablePath -and
        [IO.Path]::GetFullPath($owner.ExecutablePath) -eq [IO.Path]::GetFullPath($mysqlServer)) {
        Write-Output 'Local MySQL is already listening on 127.0.0.1:3306.'
        return
    }

    throw 'TCP 3306 is owned by another process; local MySQL was not started.'
}

$serverProcess = Start-Process `
    -FilePath $mysqlServer `
    -ArgumentList @("--defaults-file=$optionFile") `
    -WindowStyle Hidden `
    -PassThru

$deadline = [DateTime]::UtcNow.AddSeconds(30)
do {
    if ($serverProcess.HasExited) {
        throw "MySQL exited during startup with code $($serverProcess.ExitCode)."
    }

    $listener = Get-NetTCPConnection `
        -State Listen `
        -LocalAddress 127.0.0.1 `
        -LocalPort 3306 `
        -ErrorAction SilentlyContinue

    if ($listener) {
        Write-Output 'Local MySQL started on 127.0.0.1:3306.'
        return
    }

    Start-Sleep -Milliseconds 250
} while ([DateTime]::UtcNow -lt $deadline)

throw 'MySQL did not listen on 127.0.0.1:3306 within 30 seconds.'
