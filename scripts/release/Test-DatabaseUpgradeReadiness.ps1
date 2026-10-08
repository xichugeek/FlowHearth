[CmdletBinding()]
param(
    [int]$ApiPort = 52179
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runId = (Get-Date -Format 'yyyyMMddHHmmss') + '_' + ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$databasePrefix = 'flowhearth_upgrade_'
$upgradeDatabase = "${databasePrefix}upgrade_$runId"
$cleanDatabase = "${databasePrefix}clean_$runId"
$restoreDatabase = "${databasePrefix}restore_$runId"
$databaseNames = @($upgradeDatabase, $cleanDatabase, $restoreDatabase)
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "flowhearth-upgrade-$runId"
$v10Migrations = Join-Path $temporaryRoot 'migrations-v10'
$attachmentRoot = Join-Path $temporaryRoot 'attachments'
$dumpPath = Join-Path $temporaryRoot 'v10-backup.sql'
$apiStdOut = Join-Path $temporaryRoot 'api.stdout.log'
$apiStdErr = Join-Path $temporaryRoot 'api.stderr.log'
$reportRoot = Join-Path $repositoryRoot '.artifacts\readiness'
$reportPath = Join-Path $reportRoot "database-upgrade-readiness-$runId.json"
$apiProcess = $null
$createdGrants = $false

function Assert-DatabaseName([string]$Name) {
    if ($Name -notmatch '^flowhearth_upgrade_[a-z0-9_]+$') {
        throw "Unsafe temporary database name: $Name"
    }
}

foreach ($databaseName in $databaseNames) {
    Assert-DatabaseName $databaseName
}

$localRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlowHearth'
$credentialPath = Join-Path $localRoot 'local-dev-credentials.json'
$mysqlBin = Join-Path $localRoot 'mysql-8.4.11-winx64\bin'
$mysql = Join-Path $mysqlBin 'mysql.exe'
$mysqldump = Join-Path $mysqlBin 'mysqldump.exe'
$dotnetCandidates = @(
    $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Microsoft\dotnet\dotnet.exe'),
    $((Get-Command dotnet -ErrorAction SilentlyContinue).Source)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
$dotnet = $dotnetCandidates | Select-Object -First 1

foreach ($requiredPath in @($credentialPath, $mysql, $mysqldump)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required local development dependency is missing: $requiredPath"
    }
}
if (-not $dotnet) {
    throw 'A .NET SDK command could not be found.'
}
if (-not (Get-NetTCPConnection -State Listen -LocalAddress 127.0.0.1 -LocalPort 3306 -ErrorAction SilentlyContinue)) {
    throw 'The isolated readiness drill requires the existing local MySQL listener on 127.0.0.1:3306.'
}

$credentials = Get-Content -LiteralPath $credentialPath -Raw | ConvertFrom-Json
foreach ($propertyName in @('RootPassword', 'AppPassword', 'MigratorPassword')) {
    if ([string]::IsNullOrWhiteSpace($credentials.$propertyName)) {
        throw "Local development credential '$propertyName' is missing."
    }
}

$rootPassword = [string]$credentials.RootPassword
$appPassword = [string]$credentials.AppPassword
$migratorPassword = [string]$credentials.MigratorPassword
$bootstrapUsername = "f9_rc_$($runId.Replace('_', ''))"
$bootstrapPassword = "F9!$([Guid]::NewGuid().ToString('N'))aA1"
$bootstrapDisplayName = 'Database Upgrade Readiness Administrator'
$migrationRoot = Join-Path $repositoryRoot 'database\migrations'
$migratorProject = Join-Path $repositoryRoot 'src\FlowHearth.DbMigrator\FlowHearth.DbMigrator.csproj'
$apiProject = Join-Path $repositoryRoot 'src\FlowHearth.Api\FlowHearth.Api.csproj'
$apiDll = Join-Path $repositoryRoot 'src\FlowHearth.Api\bin\Release\net10.0\FlowHearth.Api.dll'

function Invoke-MySql {
    param(
        [Parameter(Mandatory)] [string]$User,
        [Parameter(Mandatory)] [string]$Password,
        [Parameter(Mandatory)] [string]$Sql,
        [string]$Database
    )

    $previousPassword = $env:MYSQL_PWD
    try {
        $env:MYSQL_PWD = $Password
        $arguments = @(
            '--protocol=tcp',
            '--host=127.0.0.1',
            '--port=3306',
            "--user=$User",
            '--batch',
            '--skip-column-names',
            '--default-character-set=utf8mb4'
        )
        if ($Database) {
            $arguments += "--database=$Database"
        }

        $output = $Sql | & $mysql @arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            $diagnostic = (@($output) | Select-Object -Last 5) -join ' '
            throw "Local MySQL command failed with exit code $LASTEXITCODE. $diagnostic"
        }
        return @($output)
    }
    finally {
        $env:MYSQL_PWD = $previousPassword
    }
}

function Get-ConnectionString {
    param(
        [Parameter(Mandatory)] [string]$Database,
        [Parameter(Mandatory)] [string]$User,
        [Parameter(Mandatory)] [string]$Password
    )

    return "Server=127.0.0.1;Port=3306;Database=$Database;User ID=$User;Password=$Password;SslMode=None;Connection Timeout=5;Default Command Timeout=30"
}

function Invoke-Migrator {
    param(
        [Parameter(Mandatory)] [string]$Database,
        [Parameter(Mandatory)] [string]$MigrationsPath,
        [Parameter(Mandatory)] [ValidateSet('migrate', 'status', 'validate', 'bootstrap-development-admin')] [string]$Command
    )

    $saved = @{
        ConnectionString = $env:ConnectionStrings__FlowHearth
        MigrationsPath = $env:Migrations__Path
        Environment = $env:DOTNET_ENVIRONMENT
        Username = $env:DevelopmentAdministrator__Username
        DisplayName = $env:DevelopmentAdministrator__DisplayName
        Password = $env:DevelopmentAdministrator__Password
    }
    try {
        $env:ConnectionStrings__FlowHearth = Get-ConnectionString $Database 'flowhearth_migrator' $migratorPassword
        $env:Migrations__Path = $MigrationsPath
        $env:DOTNET_ENVIRONMENT = 'Testing'
        $env:DevelopmentAdministrator__Username = $bootstrapUsername
        $env:DevelopmentAdministrator__DisplayName = $bootstrapDisplayName
        $env:DevelopmentAdministrator__Password = $bootstrapPassword
        & $dotnet run --project $migratorProject --configuration Release --no-build -- $Command
        if ($LASTEXITCODE -ne 0) {
            throw "DbMigrator '$Command' failed for an isolated upgrade database."
        }
    }
    finally {
        $env:ConnectionStrings__FlowHearth = $saved.ConnectionString
        $env:Migrations__Path = $saved.MigrationsPath
        $env:DOTNET_ENVIRONMENT = $saved.Environment
        $env:DevelopmentAdministrator__Username = $saved.Username
        $env:DevelopmentAdministrator__DisplayName = $saved.DisplayName
        $env:DevelopmentAdministrator__Password = $saved.Password
    }
}

function Get-V10Snapshot([string]$Database) {
    $sql = @'
SELECT CONCAT_WS('|',
    (SELECT COUNT(*) FROM customers),
    (SELECT COUNT(*) FROM contacts),
    (SELECT COUNT(*) FROM customer_followups),
    (SELECT COUNT(*) FROM opportunities),
    (SELECT COUNT(*) FROM projects),
    (SELECT COUNT(*) FROM equipment),
    (SELECT COUNT(*) FROM service_tickets),
    (SELECT COUNT(*) FROM attachments),
    (SELECT COUNT(*) FROM audit_logs),
    (SELECT CONCAT(id, ':', customer_code, ':', name) FROM customers WHERE id=101),
    (SELECT CONCAT(id, ':', opportunity_code, ':', customer_id) FROM opportunities WHERE id=104),
    (SELECT CONCAT(id, ':', project_code, ':', customer_id, ':', source_opportunity_id) FROM projects WHERE id=105),
    (SELECT CONCAT(id, ':', equipment_code, ':', customer_id, ':', project_id) FROM equipment WHERE id=106),
    (SELECT CONCAT(id, ':', service_code, ':', customer_id, ':', project_id, ':', equipment_id) FROM service_tickets WHERE id=107),
    (SELECT CONCAT(id, ':', entity_type, ':', entity_id, ':', storage_key) FROM attachments WHERE id=108),
    (SELECT CONCAT(id, ':', action, ':', entity_type, ':', entity_id) FROM audit_logs WHERE id=109)
);
'@
    return [string](Invoke-MySql -User 'flowhearth_migrator' -Password $migratorPassword -Database $Database -Sql $sql | Select-Object -First 1)
}

function Assert-SingleValue {
    param(
        [Parameter(Mandatory)] [string]$Database,
        [Parameter(Mandatory)] [string]$Sql,
        [Parameter(Mandatory)] [string]$Expected,
        [Parameter(Mandatory)] [string]$Description
    )

    $actual = [string](Invoke-MySql -User 'flowhearth_migrator' -Password $migratorPassword -Database $Database -Sql $Sql | Select-Object -First 1)
    if ($actual -ne $Expected) {
        throw "$Description failed. Expected '$Expected', actual '$actual'."
    }
}

function Invoke-ApiUpgradeSmoke([string]$Database) {
    if (-not (Test-Path -LiteralPath $apiDll)) {
        throw "Release API assembly is missing: $apiDll"
    }

    $saved = @{
        ConnectionString = $env:ConnectionStrings__FlowHearth
        Environment = $env:ASPNETCORE_ENVIRONMENT
        Urls = $env:ASPNETCORE_URLS
        Storage = $env:FileStorage__RootPath
    }
    try {
        $env:ConnectionStrings__FlowHearth = Get-ConnectionString $Database 'flowhearth_app' $appPassword
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"
        $env:FileStorage__RootPath = $attachmentRoot
        $script:apiProcess = Start-Process -FilePath $dotnet -ArgumentList @($apiDll) -PassThru -WindowStyle Hidden -RedirectStandardOutput $apiStdOut -RedirectStandardError $apiStdErr

        $baseUri = "http://127.0.0.1:$ApiPort"
        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if ($script:apiProcess.HasExited) {
                throw 'The isolated API process exited before becoming ready.'
            }
            try {
                $health = Invoke-RestMethod -Uri "$baseUri/health/ready" -Method Get -TimeoutSec 2
                if ($health.status -eq 'Healthy') {
                    $ready = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }
        if (-not $ready) {
            throw 'The isolated upgraded API did not become ready.'
        }

        $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
        $csrf = Invoke-RestMethod -Uri "$baseUri/api/v1/auth/csrf" -WebSession $session -Method Get
        $headers = @{ 'X-XSRF-TOKEN' = [string]$csrf.token }
        $loginBody = @{ username = $bootstrapUsername; password = $bootstrapPassword } | ConvertTo-Json
        Invoke-RestMethod -Uri "$baseUri/api/v1/auth/login" -WebSession $session -Method Post -Headers $headers -ContentType 'application/json' -Body $loginBody | Out-Null

        $checks = @(
            @{ Path = '/api/v1/customers/101'; Property = 'code'; Expected = 'CU-UP-V10-0001' },
            @{ Path = '/api/v1/projects/105'; Property = 'code'; Expected = 'TN-F9-V10-0001' },
            @{ Path = '/api/v1/equipment/106'; Property = 'code'; Expected = 'EQ-F9-V10-0001' },
            @{ Path = '/api/v1/service-tickets/107'; Property = 'code'; Expected = 'SR-F9-V10-0001' }
        )
        foreach ($check in $checks) {
            $response = Invoke-RestMethod -Uri "$baseUri$($check.Path)" -WebSession $session -Method Get
            if ([string]$response.($check.Property) -ne $check.Expected) {
                throw "Upgraded API data check failed for $($check.Path)."
            }
        }

        $customerPage = Invoke-RestMethod -Uri "$baseUri/api/v1/customers?status=Active&level=Unrated" -WebSession $session -Method Get
        if ([int]$customerPage.pageSize -ne 10 -or @($customerPage.items).Count -ne 1 -or [string]$customerPage.items[0].code -ne 'CU-UP-V10-0001') {
            throw 'Upgraded API customer classification or default page-size check failed.'
        }
    }
    finally {
        if ($script:apiProcess -and -not $script:apiProcess.HasExited) {
            Stop-Process -Id $script:apiProcess.Id -Force
            $script:apiProcess.WaitForExit(5000) | Out-Null
        }
        $script:apiProcess = $null
        $env:ConnectionStrings__FlowHearth = $saved.ConnectionString
        $env:ASPNETCORE_ENVIRONMENT = $saved.Environment
        $env:ASPNETCORE_URLS = $saved.Urls
        $env:FileStorage__RootPath = $saved.Storage
    }
}

$report = [ordered]@{
    runId = $runId
    startedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    migrationBaseline = '0010'
    migrationTarget = '0017'
    upgrade = 'not-run'
    cleanInstall = 'not-run'
    backupRestore = 'not-run'
    apiSmoke = 'not-run'
    migrationCount = 0
    backupSha256 = $null
}

try {
    New-Item -ItemType Directory -Path $v10Migrations, $attachmentRoot, $reportRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $migrationRoot -Filter '*.sql' |
        Where-Object Name -Match '^000[1-9]_|^0010_' |
        Copy-Item -Destination $v10Migrations
    if ((Get-ChildItem -LiteralPath $v10Migrations -Filter '*.sql').Count -ne 10) {
        throw 'The isolated V1.0 migration set must contain exactly 10 files.'
    }
    if ((Get-ChildItem -LiteralPath $migrationRoot -Filter '*.sql').Count -ne 17) {
        throw 'The release migration set must contain exactly 17 files.'
    }

    & $dotnet build $migratorProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'DbMigrator Release build failed.' }
    & $dotnet build $apiProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'API Release build failed.' }

    $createSql = ($databaseNames | ForEach-Object {
        "CREATE DATABASE ``$_`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
    }) -join "`n"
    $grantSql = ($databaseNames | ForEach-Object {
        @"
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES, CREATE VIEW, SHOW VIEW, TRIGGER ON ``$_``.* TO 'flowhearth_migrator'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON ``$_``.* TO 'flowhearth_app'@'127.0.0.1';
"@
    }) -join "`n"
    Invoke-MySql -User 'root' -Password $rootPassword -Sql ($createSql + "`n" + $grantSql + "`nFLUSH PRIVILEGES;") | Out-Null
    $createdGrants = $true

    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $v10Migrations -Command migrate
    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $v10Migrations -Command validate
    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $v10Migrations -Command bootstrap-development-admin

    $fixtureSql = @'
SET @now = '2026-09-02 01:00:00.000000';
INSERT INTO customers
    (id,customer_code,name,notes,version,created_at_utc,updated_at_utc)
VALUES
    (101,'CU-UP-V10-0001','F9 V1.0 升级客户','F9 isolated upgrade fixture',1,@now,@now);
INSERT INTO contacts
    (id,customer_id,name,mobile,is_primary,version,created_at_utc,updated_at_utc)
VALUES
    (102,101,'F9 联系人','13900000001',1,1,@now,@now);
INSERT INTO customer_followups
    (id,customer_id,contact_id,method,occurred_at_utc,summary,created_at_utc)
VALUES
    (103,101,102,'Phone',@now,'F9 升级前跟进',@now);
INSERT INTO opportunities
    (id,opportunity_code,customer_id,title,stage,expected_amount,probability_percent,
     expected_close_date,version,created_at_utc,updated_at_utc)
VALUES
    (104,'OP-F9-V10-0001',101,'F9 升级商机','Won',200000.00,100,
     '2026-09-30',1,@now,@now);
INSERT INTO projects
    (id,project_code,customer_id,source_opportunity_id,name,status,contract_amount,
     progress_percent,description,planned_start_date,planned_end_date,version,
     created_at_utc,updated_at_utc)
VALUES
    (105,'TN-F9-V10-0001',101,104,'F9 升级项目','Active',200000.00,
     35.00,'F9 V1.0 representative project','2026-09-01','2026-12-31',1,@now,@now);
INSERT INTO equipment
    (id,equipment_code,customer_id,project_id,name,category,manufacturer,model,
     serial_number,version,created_at_utc,updated_at_utc)
VALUES
    (106,'EQ-F9-V10-0001',101,105,'F9 升级设备','PLC','FlowHearth','F9',
     'F9-V10-SN-0001',1,@now,@now);
INSERT INTO service_tickets
    (id,service_code,customer_id,project_id,equipment_id,title,description,priority,
     status,reported_at_utc,version,created_at_utc,updated_at_utc)
VALUES
    (107,'SR-F9-V10-0001',101,105,106,'F9 升级售后','F9 V1.0 service fixture','P2',
     'New',@now,1,@now,@now);
INSERT INTO attachments
    (id,entity_type,entity_id,entity_code,original_file_name,storage_key,content_type,
     size_bytes,sha256,description,version,uploaded_at_utc)
VALUES
    (108,'Customer',101,'CU-UP-V10-0001','upgrade-v10.txt','f9/v10/fixture',
     'text/plain',15,REPEAT('a',64),'F9 metadata-only upgrade fixture',1,@now);
INSERT INTO audit_logs
    (id,occurred_at_utc,action,entity_type,entity_id,entity_code,summary,after_json)
VALUES
    (109,@now,'CustomerCreated','Customer',101,'CU-UP-V10-0001',
     'F9 V1.0 audit fixture',JSON_OBJECT('customerCode','CU-UP-V10-0001'));
'@
    Invoke-MySql -User 'flowhearth_migrator' -Password $migratorPassword -Database $upgradeDatabase -Sql $fixtureSql | Out-Null
    Assert-SingleValue -Database $upgradeDatabase -Sql 'SELECT COUNT(*) FROM schema_migrations;' -Expected '10' -Description 'V1.0 migration count'
    $beforeSnapshot = Get-V10Snapshot $upgradeDatabase
    if ([string]::IsNullOrWhiteSpace($beforeSnapshot) -or $beforeSnapshot.Split('|').Count -ne 16) {
        throw 'The V1.0 fixture snapshot is incomplete.'
    }

    $previousPassword = $env:MYSQL_PWD
    try {
        $env:MYSQL_PWD = $migratorPassword
        $dumpLines = & $mysqldump --protocol=tcp --host=127.0.0.1 --port=3306 --user=flowhearth_migrator --single-transaction --skip-lock-tables --skip-add-locks --no-tablespaces --routines --triggers --default-character-set=utf8mb4 $upgradeDatabase 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'The local V1.0-style mysqldump failed.' }
        [IO.File]::WriteAllText($dumpPath, (($dumpLines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
    }
    finally {
        $env:MYSQL_PWD = $previousPassword
    }
    if ((Get-Item -LiteralPath $dumpPath).Length -lt 1024) {
        throw 'The V1.0-style database backup is unexpectedly small.'
    }
    $report.backupSha256 = (Get-FileHash -LiteralPath $dumpPath -Algorithm SHA256).Hash.ToLowerInvariant()

    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $migrationRoot -Command migrate
    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $migrationRoot -Command status
    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $migrationRoot -Command validate
    Invoke-Migrator -Database $upgradeDatabase -MigrationsPath $migrationRoot -Command migrate
    Assert-SingleValue -Database $upgradeDatabase -Sql 'SELECT COUNT(*) FROM schema_migrations;' -Expected '17' -Description 'Upgraded migration count'
    Assert-SingleValue -Database $upgradeDatabase -Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('suppliers','receivables','receipts','receipt_allocations','purchase_orders','purchase_receipts','payables','payments','payment_allocations','shipments','shipment_items');" -Expected '11' -Description 'finance table set'
    Assert-SingleValue -Database $upgradeDatabase -Sql "SELECT CONCAT(business_status, '|', customer_level) FROM customers WHERE id=101;" -Expected 'Active|Unrated' -Description 'Upgraded customer classification defaults'
    Assert-SingleValue -Database $upgradeDatabase -Sql "SELECT setting_value FROM system_settings WHERE setting_key='ui.default_page_size';" -Expected '10' -Description 'Upgraded default page size'
    Assert-SingleValue -Database $upgradeDatabase -Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='customers' AND column_name IN ('province_code','city_code','district_code');" -Expected '3' -Description 'Upgraded customer region columns'
    $afterSnapshot = Get-V10Snapshot $upgradeDatabase
    if ($afterSnapshot -ne $beforeSnapshot) {
        throw 'V1.0 row counts, keys, codes, or relationships changed during the 0010 to 0017 upgrade.'
    }
    $report.upgrade = 'pass'

    Invoke-ApiUpgradeSmoke $upgradeDatabase
    $report.apiSmoke = 'pass'

    Invoke-Migrator -Database $cleanDatabase -MigrationsPath $migrationRoot -Command migrate
    Invoke-Migrator -Database $cleanDatabase -MigrationsPath $migrationRoot -Command validate
    Invoke-Migrator -Database $cleanDatabase -MigrationsPath $migrationRoot -Command bootstrap-development-admin
    Invoke-Migrator -Database $cleanDatabase -MigrationsPath $migrationRoot -Command status
    Invoke-Migrator -Database $cleanDatabase -MigrationsPath $migrationRoot -Command migrate
    Assert-SingleValue -Database $cleanDatabase -Sql 'SELECT COUNT(*) FROM schema_migrations;' -Expected '17' -Description 'Clean-install migration count'
    Assert-SingleValue -Database $cleanDatabase -Sql 'SELECT COUNT(*) FROM users;' -Expected '1' -Description 'Clean-install bootstrap administrator'
    $report.cleanInstall = 'pass'

    $dumpSql = Get-Content -LiteralPath $dumpPath -Raw
    Invoke-MySql -User 'flowhearth_migrator' -Password $migratorPassword -Database $restoreDatabase -Sql $dumpSql | Out-Null
    Assert-SingleValue -Database $restoreDatabase -Sql 'SELECT COUNT(*) FROM schema_migrations;' -Expected '10' -Description 'Restored V1.0 migration count'
    $restoredSnapshot = Get-V10Snapshot $restoreDatabase
    if ($restoredSnapshot -ne $beforeSnapshot) {
        throw 'The restored V1.0 backup does not match the pre-upgrade fixture snapshot.'
    }
    $report.backupRestore = 'pass'
    $report.migrationCount = 17
    $report.completedAtUtc = (Get-Date).ToUniversalTime().ToString('O')

    $report | ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Output "Database upgrade readiness PASS. Report: $reportPath"
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($createdGrants) {
        $cleanupSql = ($databaseNames | ForEach-Object {
            @"
REVOKE ALL PRIVILEGES ON ``$_``.* FROM 'flowhearth_migrator'@'127.0.0.1';
REVOKE ALL PRIVILEGES ON ``$_``.* FROM 'flowhearth_app'@'127.0.0.1';
DROP DATABASE IF EXISTS ``$_``;
"@
        }) -join "`n"
        try {
            Invoke-MySql -User 'root' -Password $rootPassword -Sql ($cleanupSql + "`nFLUSH PRIVILEGES;") | Out-Null
        }
        catch {
            Write-Warning 'Automatic isolated-database cleanup failed; inspect only databases with the flowhearth_upgrade_ prefix.'
        }
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
    $rootPassword = $null
    $appPassword = $null
    $migratorPassword = $null
    $bootstrapPassword = $null
}
