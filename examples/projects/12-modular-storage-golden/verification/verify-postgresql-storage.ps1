param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,
    [Parameter(Mandatory = $true)]
    [string]$StorageRuntimeHost,
    [Parameter(Mandatory = $true)]
    [string]$ProviderEntrypoint,
    [int]$HttpPort = 18220,
    [int]$HealthPort = 18221,
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$storageRuntimeHostPath = (Resolve-Path -LiteralPath $StorageRuntimeHost).Path
$providerEntrypointPath = (Resolve-Path -LiteralPath $ProviderEntrypoint).Path
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$migrationPath = Join-Path $projectRoot "migrations"
$baseUrl = "http://127.0.0.1:$HttpPort"
$testSuffix = [Guid]::NewGuid().ToString("N")
$room = "postgresql-$testSuffix"
$observationId = "postgresql-$testSuffix-0001"
$server = $null

function Start-PostgresqlServer {
    $start = [Diagnostics.ProcessStartInfo]::new($executablePath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.ArgumentList.Add("--server-mode")
    $start.ArgumentList.Add("persistent")
    $start.ArgumentList.Add("--http-port")
    $start.ArgumentList.Add([string]$HttpPort)
    $start.ArgumentList.Add("--health-port")
    $start.ArgumentList.Add([string]$HealthPort)
    $start.Environment["JUNEXIS_STORAGE_PROVIDER"] = "postgresql"
    $start.Environment["JUNEXIS_STORAGE_RUNTIME_HOST"] = $storageRuntimeHostPath
    $start.Environment["JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT"] = $providerEntrypointPath
    $start.Environment["JUNEXIS_STORAGE_CONFIG_JSON"] = (@{
        connectionString = $ConnectionString
        migrationsPath = $migrationPath
        operationTimeoutMs = "5000"
        maxPoolSize = "20"
    } | ConvertTo-Json -Compress)
    $start.Environment["JUNEXIS_BUILD_ID"] = "postgresql-provider-build"
    $start.Environment["JUNEXIS_BUNDLE_ID"] = "postgresql-provider-bundle"
    return [Diagnostics.Process]::Start($start)
}

function Stop-PostgresqlServer {
    if ($null -ne $script:server -and -not $script:server.HasExited) {
        $script:server.Kill($true)
        $script:server.WaitForExit()
    }
    if ($null -ne $script:server) {
        $script:server.Dispose()
        $script:server = $null
    }
}

function Wait-ForApi {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $health = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/home/storage/health"
            if ($health -eq $true) {
                return
            }
        } catch [System.Net.Http.HttpRequestException] {
            # Proces może jeszcze otwierać połączenie z PostgreSQL i port HTTP.
        } catch [System.Net.WebException] {
            # Windows PowerShell zgłasza brak gotowości jako WebException.
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Serwer z providerem PostgreSQL nie osiągnął gotowości w ${TimeoutSeconds}s."
}

try {
    $server = Start-PostgresqlServer
    Wait-ForApi

    $observation = @{
        observation_id = $observationId
        room = $room
        status = "postgresql-connected"
        note = "rekord kontrolny providera PostgreSQL"
        sequence = 1
    } | ConvertTo-Json
    $saved = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body $observation -Uri "$baseUrl/api/home/observations"
    if (-not $saved.stored -or $saved.provider -ne "postgresql") {
        throw "Zapis przez provider PostgreSQL nie został potwierdzony."
    }

    $rollback = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body $observation -Uri "$baseUrl/api/home/storage/rollback"
    if ($rollback -ne $true) {
        throw "Transakcja kontrolna pozostawiła skutek albo nie została wycofana."
    }

    $beforeRestart = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/home/$room"
    if ($beforeRestart.count -ne 1 -or $beforeRestart.latest[0].observation_id -ne $observationId) {
        throw "Provider nie odczytał rekordu zapisanego przed restartem."
    }

    Stop-PostgresqlServer
    $server = Start-PostgresqlServer
    Wait-ForApi

    $afterRestart = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/home/$room"
    if ($afterRestart.count -ne 1 -or -not $afterRestart.restored_after_restart) {
        throw "Nowy proces nie odtworzył rekordu z PostgreSQL."
    }
    if ($afterRestart.latest[0].observation_id -ne $observationId) {
        throw "API zwróciło inny rekord niż zapisany przed restartem."
    }

    [pscustomobject]@{
        result = "PASS"
        provider = $afterRestart.provider
        restored_after_restart = $afterRestart.restored_after_restart
        restored_observation = $afterRestart.latest[0].observation_id
        rollback_clean = $rollback
        storage_contract = "junexis.storage/0.1"
        database_cleanup = "rekord kontrolny pozostawiony celowo"
    } | ConvertTo-Json -Depth 4
} finally {
    Stop-PostgresqlServer
}
