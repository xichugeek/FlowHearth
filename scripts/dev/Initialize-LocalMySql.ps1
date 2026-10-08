[CmdletBinding()]
param(
    [string]$MySqlLocalRoot = (Join-Path $env:LOCALAPPDATA 'FlowHearth')
)

$ErrorActionPreference = 'Stop'

function New-SecureDevelopmentPassword {
    return [Convert]::ToBase64String(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
}

function Set-PrivateFileAcl {
    param([Parameter(Mandatory)][string]$Path)

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $accessRule = [Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.AccessControlType]::Allow)
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule($accessRule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

$mysqlHome = Join-Path $MySqlLocalRoot 'mysql-8.4.11-winx64'
$mysqlServer = Join-Path $mysqlHome 'bin\mysqld.exe'
$mysqlClient = Join-Path $mysqlHome 'bin\mysql.exe'
$optionFile = Join-Path $MySqlLocalRoot 'my.ini'
$dataDirectory = Join-Path $MySqlLocalRoot 'mysql-data'
$credentialFile = Join-Path $MySqlLocalRoot 'local-dev-credentials.json'
$bootstrapFile = Join-Path $MySqlLocalRoot 'mysql-bootstrap-once.sql'

foreach ($requiredPath in @($mysqlServer, $mysqlClient, $optionFile, $dataDirectory)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required local MySQL path does not exist: $requiredPath"
    }
}

if (Test-Path -LiteralPath $credentialFile) {
    throw "Local development credentials already exist: $credentialFile"
}

if (Get-NetTCPConnection -State Listen -LocalPort 3306 -ErrorAction SilentlyContinue) {
    throw 'TCP 3306 is already in use. Stop that process before initialization.'
}

$credentials = [ordered]@{
    RootPassword = New-SecureDevelopmentPassword
    AppPassword = New-SecureDevelopmentPassword
    MigratorPassword = New-SecureDevelopmentPassword
}

$credentials | ConvertTo-Json | Set-Content -LiteralPath $credentialFile -Encoding utf8NoBOM
Set-PrivateFileAcl -Path $credentialFile

$bootstrapSql = @"
ALTER USER 'root'@'localhost' IDENTIFIED BY '$($credentials.RootPassword)';
CREATE USER 'root'@'127.0.0.1' IDENTIFIED BY '$($credentials.RootPassword)';
GRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION;
CREATE DATABASE IF NOT EXISTS flowhearth CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'flowhearth_app'@'127.0.0.1' IDENTIFIED BY '$($credentials.AppPassword)';
CREATE USER 'flowhearth_migrator'@'127.0.0.1' IDENTIFIED BY '$($credentials.MigratorPassword)';
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON flowhearth.* TO 'flowhearth_app'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES, CREATE VIEW, SHOW VIEW, TRIGGER ON flowhearth.* TO 'flowhearth_migrator'@'127.0.0.1';
"@

Set-Content -LiteralPath $bootstrapFile -Value $bootstrapSql -Encoding utf8NoBOM
Set-PrivateFileAcl -Path $bootstrapFile

try {
    $serverProcess = Start-Process `
        -FilePath $mysqlServer `
        -ArgumentList @(
            "--defaults-file=$optionFile",
            "--init-file=$bootstrapFile") `
        -WindowStyle Hidden `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if ($serverProcess.HasExited) {
            throw "MySQL exited during bootstrap with code $($serverProcess.ExitCode)."
        }

        $listener = Get-NetTCPConnection `
            -State Listen `
            -LocalAddress 127.0.0.1 `
            -LocalPort 3306 `
            -ErrorAction SilentlyContinue

        if ($listener) {
            break
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    if (-not $listener) {
        throw 'MySQL did not listen on 127.0.0.1:3306 within 30 seconds.'
    }

    Start-Sleep -Seconds 2

    $env:MYSQL_PWD = $credentials.AppPassword
    try {
        & $mysqlClient `
            --defaults-file="$optionFile" `
            --user=flowhearth_app `
            --batch `
            --skip-column-names `
            --execute='SELECT 1;' `
            flowhearth | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Application account verification failed with code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
    }
}
finally {
    if (Test-Path -LiteralPath $bootstrapFile) {
        Remove-Item -LiteralPath $bootstrapFile -Force
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apiProject = Join-Path $repositoryRoot 'src\FlowHearth.Api\FlowHearth.Api.csproj'
$migratorProject = Join-Path $repositoryRoot 'src\FlowHearth.DbMigrator\FlowHearth.DbMigrator.csproj'
$userLocalDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnetExecutable = if (Test-Path -LiteralPath $userLocalDotnet) {
    $userLocalDotnet
}
else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$appConnectionString = "Server=127.0.0.1;Port=3306;Database=flowhearth;User ID=flowhearth_app;Password=$($credentials.AppPassword);SslMode=None;Connection Timeout=5;Default Command Timeout=30"
$migratorConnectionString = "Server=127.0.0.1;Port=3306;Database=flowhearth;User ID=flowhearth_migrator;Password=$($credentials.MigratorPassword);SslMode=None;Connection Timeout=5;Default Command Timeout=30"

& $dotnetExecutable user-secrets set 'ConnectionStrings:FlowHearth' $appConnectionString --project $apiProject | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not store the API development connection string.'
}

& $dotnetExecutable user-secrets set 'ConnectionStrings:FlowHearth' $migratorConnectionString --project $migratorProject | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not store the migrator development connection string.'
}

Write-Output 'Local MySQL initialized on 127.0.0.1:3306.'
Write-Output 'Runtime and migrator connection strings were stored in .NET user secrets.'
Write-Output 'No database credential was written to the repository.'
