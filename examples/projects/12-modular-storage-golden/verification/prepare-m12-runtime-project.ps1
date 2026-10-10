param(
    [Parameter(Mandatory = $true)]
    [string]$ProviderPackage,
    [string]$Destination
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$providerPackagePath = (Resolve-Path -LiteralPath $ProviderPackage).Path
$providerManifestPath = Join-Path $providerPackagePath "junexis.provider.toml"
if (-not (Test-Path -LiteralPath $providerManifestPath -PathType Leaf)) {
    throw "Pakiet nie zawiera junexis.provider.toml: $providerPackagePath"
}
$providerManifest = Get-Content -Raw -LiteralPath $providerManifestPath
$providerIdMatch = [regex]::Match($providerManifest, '(?m)^id\s*=\s*"(?<value>[a-z0-9.-]+)"\s*$')
$providerVersionMatch = [regex]::Match($providerManifest, '(?m)^version\s*=\s*"(?<value>[^"]+)"\s*$')
if (-not $providerIdMatch.Success -or -not $providerVersionMatch.Success) {
    throw "Manifest providera nie zawiera poprawnego id lub version."
}
$providerId = $providerIdMatch.Groups["value"].Value
$providerVersion = $providerVersionMatch.Groups["value"].Value
if ($providerId -notin @("memory", "sqlite", "postgresql")) {
    throw "Laboratorium M12.9 obsługuje memory, sqlite i postgresql; otrzymano: $providerId"
}
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path ([IO.Path]::GetTempPath()) ("junexis-m128-golden-" + [Guid]::NewGuid().ToString("N"))
}
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) {
    throw "Katalog docelowy już istnieje: $destinationPath"
}

New-Item -ItemType Directory -Path $destinationPath | Out-Null
foreach ($name in @("src", "user", "migrations")) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $destinationPath -Recurse
}
$providerRoot = Join-Path $destinationPath ("providers\" + $providerId)
New-Item -ItemType Directory -Path (Split-Path -Parent $providerRoot) -Force | Out-Null
Copy-Item -LiteralPath $providerPackagePath -Destination $providerRoot -Recurse

$databasePath = (Join-Path $destinationPath "data\home.db").Replace("\", "/")
$migrationsPath = (Join-Path $destinationPath "migrations").Replace("\", "/")
$manifest = @(
    'name = "modular-storage-golden-m129"',
    'entry = "src/main.jnx"',
    'sourceRoots = ["src"]',
    'targets = ["server", "report"]',
    'outDir = "out"',
    ('providerPackages = ["providers/' + $providerId + '"]'),
    '',
    '[storage.home]',
    'declaration = "HomeStore"',
    ('provider = "' + $providerId + '"'),
    ('providerVersion = "' + $providerVersion + '"')
)
switch ($providerId) {
    "memory" {
        $manifest += 'config.maxRecords = "1000"'
        $manifest += 'config.maxPayloadBytes = "1048576"'
    }
    "sqlite" {
        $manifest += ('config.path = "literal:' + $databasePath + '"')
        $manifest += ('config.migrationsPath = "literal:' + $migrationsPath + '"')
        $manifest += 'config.busyTimeoutMs = "2000"'
        $manifest += 'config.operationTimeoutMs = "5000"'
    }
    "postgresql" {
        $manifest += 'config.connectionString = "secret:JUNEXIS_STORAGE_CONNECTION_STRING"'
        $manifest += ('config.migrationsPath = "literal:' + $migrationsPath + '"')
        $manifest += 'config.operationTimeoutMs = "5000"'
    }
}
[IO.File]::WriteAllLines((Join-Path $destinationPath "junexis.project.toml"), $manifest)

Write-Output $destinationPath
