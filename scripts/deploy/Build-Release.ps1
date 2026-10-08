[CmdletBinding()]
param(
    [string]$ReleaseId = (Get-Date -Format 'yyyyMMddHHmmss'),
    [string]$Version = 'V1.0.1',
    [string]$BaseProductionVersion = 'none',
    [string]$MigrationFrom = '0000',
    [string]$MigrationTo = '0017',
    [int]$MigrationCount = 17,
    [int]$UnitTestCount = 178,
    [int]$IntegrationTestCount = 95,
    [int]$FrontendTestFileCount = 34,
    [int]$FrontendTestCount = 112,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
if ($ReleaseId -notmatch '^[0-9]{14}(?:-[a-z0-9]+)?$') {
    throw 'ReleaseId must be a 14-digit timestamp with an optional safe suffix.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dotnetCandidates = @(
    $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Microsoft\dotnet\dotnet.exe'),
    $((Get-Command dotnet -ErrorAction SilentlyContinue).Source)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
$dotnet = $dotnetCandidates | Select-Object -First 1
if (-not $dotnet) {
    throw 'A .NET SDK command could not be found.'
}
$npm = Get-Command npm -ErrorAction Stop
$artifactsRoot = Join-Path $repositoryRoot '.artifacts'
$releaseRoot = Join-Path $artifactsRoot "releases\$ReleaseId"
$archivePath = Join-Path $artifactsRoot "FlowHearth-$ReleaseId.tar.gz"
$manifestPath = Join-Path $artifactsRoot "FlowHearth-$ReleaseId-manifest.json"
$gitCommit = (& git -C $repositoryRoot rev-parse --verify HEAD 2>$null)
$dirty = [bool](& git -C $repositoryRoot status --short)

if ($dirty -and -not $AllowDirty) {
    throw 'Release candidates must be built from a clean Git worktree. Commit the validated RC first.'
}

if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release output already exists: $releaseRoot"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

& $dotnet publish `
    (Join-Path $repositoryRoot 'src\FlowHearth.Api') `
    --configuration Release `
    --output (Join-Path $releaseRoot 'api') `
    --no-restore
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }

& $dotnet publish `
    (Join-Path $repositoryRoot 'src\FlowHearth.DbMigrator') `
    --configuration Release `
    --output (Join-Path $releaseRoot 'migrator') `
    --no-restore
if ($LASTEXITCODE -ne 0) { throw 'DbMigrator publish failed.' }

Push-Location (Join-Path $repositoryRoot 'web\flowhearth-web')
try {
    & $npm.Source run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    Copy-Item -Recurse -Force 'dist' (Join-Path $releaseRoot 'web')
}
finally {
    Pop-Location
}

$builtAtUtc = (Get-Date).ToUniversalTime().ToString('O')
$metadata = [ordered]@{
    project = 'FlowHearth'
    version = $Version
    releaseId = $ReleaseId
    builtAtUtc = $builtAtUtc
    gitCommit = $gitCommit
    dirty = $dirty
    baseProductionVersion = $BaseProductionVersion
    databaseUpgrade = "$MigrationFrom -> $MigrationTo"
    migrationCount = $MigrationCount
}
$metadata | ConvertTo-Json | Set-Content `
    -LiteralPath (Join-Path $releaseRoot 'release.json') `
    -Encoding utf8NoBOM

$checksumLines = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File |
    Where-Object Name -ne 'SHA256SUMS' |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = ([IO.Path]::GetRelativePath($releaseRoot, $_.FullName)).Replace(
            '\',
            '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS'
[IO.File]::WriteAllText(
    $checksumPath,
    (($checksumLines -join "`n") + "`n"),
    [Text.Encoding]::ASCII)

if (Test-Path -LiteralPath $archivePath) {
    throw "Release archive already exists: $archivePath"
}

& tar -C (Split-Path $releaseRoot -Parent) -czf $archivePath $ReleaseId
if ($LASTEXITCODE -ne 0) { throw 'Release archive creation failed.' }

$archiveSha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    project = 'FlowHearth'
    version = $Version
    releaseId = $ReleaseId
    builtAtUtc = $builtAtUtc
    gitCommit = $gitCommit
    baseProductionVersion = $BaseProductionVersion
    productionDatabaseUpgrade = [ordered]@{
        from = $MigrationFrom
        to = $MigrationTo
        migrationCount = $MigrationCount
    }
    tests = [ordered]@{
        backend = [ordered]@{
            unit = $UnitTestCount
            integration = $IntegrationTestCount
            total = $UnitTestCount + $IntegrationTestCount
        }
        frontend = [ordered]@{
            files = $FrontendTestFileCount
            tests = $FrontendTestCount
        }
    }
    package = [ordered]@{
        file = [IO.Path]::GetFileName($archivePath)
        sha256 = $archiveSha256
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content `
    -LiteralPath $manifestPath `
    -Encoding utf8NoBOM

Write-Host "Release directory: $releaseRoot"
Write-Host "Release archive: $archivePath"
Write-Host "Release manifest: $manifestPath"
Write-Host "Archive SHA256: $archiveSha256"
