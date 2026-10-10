param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [Parameter(Mandatory = $true)]
    [string]$StorageRuntimeHost,
    [Parameter(Mandatory = $true)]
    [string]$ProviderEntrypoint,
    [ValidateSet("sqlite", "memory")]
    [string]$Provider = "sqlite",
    [int]$HttpPort = 18120,
    [int]$HealthPort = 18121,
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$storageRuntimeHostPath = (Resolve-Path -LiteralPath $StorageRuntimeHost).Path
$providerEntrypointPath = (Resolve-Path -LiteralPath $ProviderEntrypoint).Path
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$ownedRoot = Join-Path ([IO.Path]::GetTempPath()) "junexis-m11-golden"
$runRoot = Join-Path $ownedRoot ([Guid]::NewGuid().ToString("N"))
$databasePath = Join-Path $runRoot "data\home.db"
$migrationPath = Join-Path $projectRoot "migrations"
$baseUrl = "http://127.0.0.1:$HttpPort"
$server = $null

function Start-GoldenServer {
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
    $start.Environment["JUNEXIS_STORAGE_PROVIDER"] = $Provider
    $start.Environment["JUNEXIS_STORAGE_RUNTIME_HOST"] = $storageRuntimeHostPath
    $start.Environment["JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT"] = $providerEntrypointPath
    $configuration = if ($Provider -eq "sqlite") {
        @{
            path = $databasePath
            migrationsPath = $migrationPath
            busyTimeoutMs = "2000"
            operationTimeoutMs = "5000"
        }
    } else {
        @{
            maxRecords = "1000"
            maxPayloadBytes = "1048576"
        }
    }
    $start.Environment["JUNEXIS_STORAGE_CONFIG_JSON"] = ($configuration | ConvertTo-Json -Compress)
    $start.Environment["JUNEXIS_BUILD_ID"] = "m12.9-laboratory-build"
    $start.Environment["JUNEXIS_BUNDLE_ID"] = "m12.9-laboratory-bundle"
    return [Diagnostics.Process]::Start($start)
}

function Stop-GoldenServer {
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
            # Serwer może jeszcze otwierać port i bazę.
        } catch [System.Net.WebException] {
            # Windows PowerShell zgłasza brak gotowości jako WebException.
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Serwer laboratorium storage nie osiągnął gotowości w ${TimeoutSeconds}s."
}

try {
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

    $server = Start-GoldenServer
    Wait-ForApi

    $observation = @{
        observation_id = "living-room-0001"
        room = "living-room"
        status = "air-quality-high"
        note = "wentylacja została uruchomiona"
        sequence = 1
    } | ConvertTo-Json
    $saved = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body $observation -Uri "$baseUrl/api/home/observations"
    if (-not $saved.stored -or $saved.provider -ne $Provider) {
        throw "Zapis przez zadanie serwera nie został potwierdzony."
    }

    $rollback = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body $observation -Uri "$baseUrl/api/home/storage/rollback"
    if ($rollback -ne $true) {
        throw "Transakcja kontrolna pozostawiła skutek albo nie została wycofana."
    }

    $beforeRestart = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/home/living-room"
    if ($beforeRestart.count -ne 1 -or $beforeRestart.restored_after_restart) {
        throw "Niepoprawny punkt kontrolny przed restartem procesu."
    }

    Stop-GoldenServer
    $server = Start-GoldenServer
    Wait-ForApi

    $afterRestart = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/home/living-room"
    $expectedPersistence = $Provider -eq "sqlite"
    if ($expectedPersistence) {
        if ($afterRestart.count -ne 1 -or -not $afterRestart.restored_after_restart) {
            throw "Nowy proces nie odtworzył zatwierdzonej obserwacji z SQLite."
        }
        if ($afterRestart.latest[0].observation_id -ne "living-room-0001") {
            throw "API serwera zwróciło inną obserwację niż zapisana przed restartem."
        }
    } elseif ($afterRestart.count -ne 0 -or $afterRestart.restored_after_restart) {
        throw "Provider memory zachował dane pomimo restartu procesu."
    }
    if ($afterRestart.build_id -ne "m12.9-laboratory-build" -or
        $afterRestart.bundle_id -ne "m12.9-laboratory-bundle") {
        throw "Dowód nie zachował Build ID lub Bundle ID."
    }

    $moduleLock = Get-Content -Raw -LiteralPath (Join-Path $projectRoot "out\debug\junexis.modules.lock.json") | ConvertFrom-Json
    $storageManifest = Get-Content -Raw -LiteralPath (Join-Path $projectRoot "out\server\storage\junexis.storage.contracts.json") | ConvertFrom-Json
    $migrationNames = Get-ChildItem -LiteralPath $migrationPath -Filter "*.sql" | Sort-Object Name | Select-Object -ExpandProperty Name
    if ($moduleLock.modules.Count -lt 4) {
        throw "Dowód zawiera mniej niż cztery moduły Junexis."
    }

    [pscustomobject]@{
        result = "PASS"
        provider = $afterRestart.provider
        restored_after_restart = $afterRestart.restored_after_restart
        expected_persistence = $expectedPersistence
        restored_observation = if ($expectedPersistence) { $afterRestart.latest[0].observation_id } else { $null }
        rollback_clean = $rollback
        build_id = $afterRestart.build_id
        bundle_id = $afterRestart.bundle_id
        modules = @($moduleLock.modules | ForEach-Object { $_.identity })
        migrations = @($migrationNames)
        storage_contract = $storageManifest.schemaVersion
        panel_boundary = "PublishHomeSnapshot -> HomeStoragePanel"
    } | ConvertTo-Json -Depth 6
} finally {
    Stop-GoldenServer
    if (Test-Path -LiteralPath $runRoot) {
        $resolvedRunRoot = (Resolve-Path -LiteralPath $runRoot).Path
        $resolvedOwnedRoot = [IO.Path]::GetFullPath($ownedRoot)
        if (-not $resolvedRunRoot.StartsWith($resolvedOwnedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Odmowa usunięcia nieoczekiwanego katalogu: $resolvedRunRoot"
        }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}
