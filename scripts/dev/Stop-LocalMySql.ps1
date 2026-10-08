[CmdletBinding()]
param(
    [string]$MySqlLocalRoot = (Join-Path $env:LOCALAPPDATA 'FlowHearth')
)

$ErrorActionPreference = 'Stop'
$mysqlAdmin = Join-Path $MySqlLocalRoot 'mysql-8.4.11-winx64\bin\mysqladmin.exe'
$optionFile = Join-Path $MySqlLocalRoot 'my.ini'
$credentialFile = Join-Path $MySqlLocalRoot 'local-dev-credentials.json'

foreach ($requiredPath in @($mysqlAdmin, $optionFile, $credentialFile)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required local MySQL path does not exist: $requiredPath"
    }
}

if (-not (Get-NetTCPConnection -State Listen -LocalPort 3306 -ErrorAction SilentlyContinue)) {
    Write-Output 'Local MySQL is not listening on TCP 3306.'
    return
}

$credentials = Get-Content -LiteralPath $credentialFile -Raw | ConvertFrom-Json
$env:MYSQL_PWD = $credentials.RootPassword

try {
    & $mysqlAdmin `
        --defaults-file="$optionFile" `
        --user=root `
        shutdown
    if ($LASTEXITCODE -ne 0) {
        throw "mysqladmin shutdown failed with code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
}

Write-Output 'Local MySQL stopped cleanly.'
