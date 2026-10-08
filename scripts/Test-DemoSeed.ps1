param(
    [Parameter(Mandatory = $true)][string]$Database,
    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
if ($Database -notmatch '^flowhearth_demo_[a-z0-9_]+$') {
    throw 'An explicit isolated flowhearth_demo_* database is required.'
}
if (-not $env:FLOWHEARTH_DEMO_MYSQL) {
    throw 'Set FLOWHEARTH_DEMO_MYSQL locally; connection strings are never printed.'
}
$seedAssembly = Join-Path $PSScriptRoot '../src/FlowHearth.DemoSeed/bin/Release/net10.0/FlowHearth.DemoSeed.dll'
if (-not (Test-Path -LiteralPath $seedAssembly)) { throw 'Build Release before this check.' }
$originalEnvironment = $env:DOTNET_ENVIRONMENT
$originalConnection = $env:FLOWHEARTH_DEMO_MYSQL

function Assert-SeedResult([string]$Name, [string[]]$SeedArguments, [int]$ExpectedExit, [string]$ExpectedMessage) {
    $result = (& $DotNet $seedAssembly @SeedArguments 2>&1 | Out-String)
    if ($LASTEXITCODE -ne $ExpectedExit -or $result -notlike "*$ExpectedMessage*") {
        throw "Demo Seed check failed: $Name (exit $LASTEXITCODE)."
    }
    Write-Output "PASS: $Name"
}

try {
    $env:DOTNET_ENVIRONMENT = 'Development'
    # A verified baseline must exist. This check never creates or clears a database.
    Assert-SeedResult 'verified repeat run' @('initialize', $Database, '--confirm-isolated-demo') 0 'already present and verified'
    Assert-SeedResult 'missing explicit confirmation' @('initialize', $Database) 2 'Usage:'
    $env:DOTNET_ENVIRONMENT = 'Production'
    Assert-SeedResult 'production environment refused' @('initialize', $Database, '--confirm-isolated-demo') 2 'requires DOTNET_ENVIRONMENT'
    $env:DOTNET_ENVIRONMENT = 'Development'
    Assert-SeedResult 'database identity mismatch refused' @('initialize', 'flowhearth_demo_wrong_target', '--confirm-isolated-demo') 1 'Target must be'
    $env:FLOWHEARTH_DEMO_MYSQL = "Server=192.0.2.1;Port=3307;Database=$Database;User ID=demo;Password=invalid;"
    Assert-SeedResult 'non-loopback host refused before connection' @('initialize', $Database, '--confirm-isolated-demo') 1 'Target must be'
    $env:FLOWHEARTH_DEMO_MYSQL = "Server=127.0.0.1;Port=3306;Database=$Database;User ID=demo;Password=invalid;"
    Assert-SeedResult 'default database port refused before connection' @('initialize', $Database, '--confirm-isolated-demo') 1 'Target must be'
    $env:FLOWHEARTH_DEMO_MYSQL = 'Server=127.0.0.1;Port=3307;Database=business;User ID=demo;Password=invalid;'
    Assert-SeedResult 'business database name refused before connection' @('initialize', 'business', '--confirm-isolated-demo') 1 'Target must be'
    $env:FLOWHEARTH_DEMO_MYSQL = $originalConnection
    Assert-SeedResult 'baseline still verified after refusal checks' @('initialize', $Database, '--confirm-isolated-demo') 0 'already present and verified'
}
finally {
    $env:DOTNET_ENVIRONMENT = $originalEnvironment
    $env:FLOWHEARTH_DEMO_MYSQL = $originalConnection
}
