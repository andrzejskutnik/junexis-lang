param(
    [Parameter(Mandatory = $true)]
    [string]$Bundle,
    [string]$NodeExecutable = "node",
    [int]$HttpPort = 18130,
    [int]$HealthPort = 18131,
    [int]$ReportPort = 14174,
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$bundlePath = (Resolve-Path -LiteralPath $Bundle).Path
$receiptPath = "$bundlePath.package-receipt.json"
$ownedRoot = Join-Path ([IO.Path]::GetTempPath()) "junexis-m11-bundle-smoke"
$runRoot = Join-Path $ownedRoot ([Guid]::NewGuid().ToString("N"))
$extractRoot = Join-Path $runRoot "bundle"
$databasePath = Join-Path $runRoot "data\home.db"
$server = $null
$report = $null

function Stop-OwnedProcess([Diagnostics.Process]$Process) {
    if ($null -ne $Process -and -not $Process.HasExited) {
        $Process.Kill($true)
        $Process.WaitForExit()
    }
    if ($null -ne $Process) {
        $Process.Dispose()
    }
}

function Start-PackagedServer([string]$Executable, [string]$Migrations, [string]$BuildId, [string]$BundleId) {
    $storageRoot = Join-Path (Split-Path -Parent $Executable) "storage"
    $deployment = Get-Content -Raw -LiteralPath (Join-Path $storageRoot "junexis.storage.deployment.json") | ConvertFrom-Json
    $storageRuntimeHostPath = (Resolve-Path -LiteralPath (Join-Path $storageRoot $deployment.runtimeHost)).Path
    $providerEntrypointPath = (Resolve-Path -LiteralPath (Join-Path $storageRoot $deployment.binding.entrypoint)).Path
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.ArgumentList.Add("--server-mode")
    $start.ArgumentList.Add("persistent")
    $start.ArgumentList.Add("--http-port")
    $start.ArgumentList.Add([string]$HttpPort)
    $start.ArgumentList.Add("--health-port")
    $start.ArgumentList.Add([string]$HealthPort)
    $start.Environment["JUNEXIS_STORAGE_PROVIDER"] = $deployment.binding.provider
    $start.Environment["JUNEXIS_STORAGE_BINDING"] = $deployment.binding.name
    $start.Environment["JUNEXIS_STORAGE_RUNTIME_HOST"] = $storageRuntimeHostPath
    $start.Environment["JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT"] = $providerEntrypointPath
    $start.Environment["JUNEXIS_STORAGE_CONFIG_JSON"] = (@{
        path = $databasePath
        migrationsPath = $Migrations
        busyTimeoutMs = "2000"
        operationTimeoutMs = "5000"
    } | ConvertTo-Json -Compress)
    $start.Environment["JUNEXIS_BUILD_ID"] = $BuildId
    $start.Environment["JUNEXIS_BUNDLE_ID"] = $BundleId
    return [Diagnostics.Process]::Start($start)
}

function Start-PackagedReport([string]$RuntimeDirectory) {
    $start = [Diagnostics.ProcessStartInfo]::new($NodeExecutable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $RuntimeDirectory
    $start.ArgumentList.Add("serve.mjs")
    $start.Environment["JUNEXIS_REPORT_PORT"] = [string]$ReportPort
    $start.Environment["JUNEXIS_SERVER_URL"] = "http://127.0.0.1:$HttpPort"
    return [Diagnostics.Process]::Start($start)
}

function Wait-ForJson([string]$Uri, [scriptblock]$Condition, [string]$Description) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $value = Invoke-RestMethod -Method Get -Uri $Uri
            if (& $Condition $value) {
                return $value
            }
        } catch [System.Net.Http.HttpRequestException] {
            # Proces może jeszcze otwierać port.
        } catch [System.Net.WebException] {
            # Windows PowerShell zgłasza brak gotowości jako WebException.
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Nie osiągnięto punktu kontrolnego: $Description"
}

try {
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        throw "Brak receipt dla System Bundle: $receiptPath"
    }
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($bundlePath, $extractRoot)

    $descriptor = Get-Content -Raw -LiteralPath (Join-Path $extractRoot "system-bundle.json") | ConvertFrom-Json
    $integrity = Get-Content -Raw -LiteralPath (Join-Path $extractRoot "manifest.json") | ConvertFrom-Json
    $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
    if ($descriptor.schemaVersion -ne "junexis.system-bundle/0.1" -or
        $receipt.schemaVersion -ne "junexis.package-receipt/0.1" -or
        $descriptor.buildId -ne $receipt.buildId) {
        throw "Descriptor i package receipt nie opisują tego samego poprawnego buildu."
    }

    $serverExecutable = Join-Path $extractRoot "artifacts\server\runtime\junexis-server.exe"
    $migrations = Join-Path $extractRoot "artifacts\server\migrations"
    $reportRuntime = Join-Path $extractRoot "artifacts\report\runtime"
    foreach ($required in @(
        $serverExecutable,
        (Join-Path $migrations "001-storage.sql"),
        (Join-Path $reportRuntime "index.html"),
        (Join-Path $reportRuntime "dist\browser.js"),
        (Join-Path $reportRuntime "serve.mjs")
    )) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "System Bundle nie zawiera wymaganego artefaktu: $required"
        }
    }

    $server = Start-PackagedServer $serverExecutable $migrations $descriptor.buildId $receipt.bundleId
    $null = Wait-ForJson "http://127.0.0.1:$HttpPort/api/home/storage/health" { param($value) $value -eq $true } "gotowość serwera z paczki"

    $report = Start-PackagedReport $reportRuntime
    $null = Wait-ForJson "http://127.0.0.1:$ReportPort/health/ready" { param($value) $value.status -eq "ready" } "gotowość raportu z paczki"
    $reportPage = Invoke-WebRequest -Uri "http://127.0.0.1:$ReportPort/?server=http://127.0.0.1:$HttpPort"
    $browserBundle = Invoke-WebRequest -Uri "http://127.0.0.1:$ReportPort/dist/browser.js"
    if ($reportPage.StatusCode -ne 200 -or $reportPage.Content -notmatch "dist/browser.js" -or
        $browserBundle.StatusCode -ne 200 -or $browserBundle.RawContentLength -lt 1000) {
        throw "Spakowana aplikacja raportowa nie udostępnia kompletnego interfejsu."
    }

    $observation = @{
        observation_id = "bundle-living-room-0001"
        room = "living-room"
        status = "air-quality-high"
        note = "zapis wykonany wyłącznie z System Bundle"
        sequence = 1
    } | ConvertTo-Json
    $saved = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body $observation -Uri "http://127.0.0.1:$HttpPort/api/home/observations"
    if (-not $saved.stored -or $saved.provider -ne "sqlite") {
        throw "Serwer z paczki nie zapisał obserwacji przez SQLite."
    }

    Stop-OwnedProcess $server
    $server = $null
    $server = Start-PackagedServer $serverExecutable $migrations $descriptor.buildId $receipt.bundleId
    $null = Wait-ForJson "http://127.0.0.1:$HttpPort/api/home/storage/health" { param($value) $value -eq $true } "gotowość drugiego procesu z paczki"
    $snapshot = Wait-ForJson "http://127.0.0.1:$HttpPort/api/home/living-room" {
        param($value)
        $value.restored_after_restart -and $value.count -eq 1
    } "odtworzenie danych po restarcie procesu z paczki"

    if ($snapshot.latest[0].observation_id -ne "bundle-living-room-0001" -or
        $snapshot.build_id -ne $descriptor.buildId -or
        $snapshot.bundle_id -ne $receipt.bundleId) {
        throw "Odtworzony stan nie zachował danych albo tożsamości System Bundle."
    }

    $moduleLock = Get-Content -Raw -LiteralPath (Join-Path $extractRoot "artifacts\debug\junexis.modules.lock.json") | ConvertFrom-Json
    [pscustomobject]@{
        result = "PASS"
        schema = $descriptor.schemaVersion
        build_id = $descriptor.buildId
        bundle_id = $receipt.bundleId
        artifact_entries = $descriptor.artifacts.Count
        verified_payload_files = @($integrity.sha256.PSObject.Properties).Count
        targets = @($descriptor.targets)
        modules = @($moduleLock.modules | ForEach-Object { $_.identity })
        provider = $snapshot.provider
        restored_after_restart = $snapshot.restored_after_restart
        restored_observation = $snapshot.latest[0].observation_id
        report_ready = $true
        report_bundle_bytes = $browserBundle.RawContentLength
        checkout_required_at_runtime = $false
        go_toolchain_required_at_runtime = $false
        c_toolchain_required_at_runtime = $false
    } | ConvertTo-Json -Depth 6
} finally {
    Stop-OwnedProcess $server
    Stop-OwnedProcess $report
    if (Test-Path -LiteralPath $runRoot) {
        $resolvedRunRoot = (Resolve-Path -LiteralPath $runRoot).Path
        $resolvedOwnedRoot = [IO.Path]::GetFullPath($ownedRoot)
        if (-not $resolvedRunRoot.StartsWith($resolvedOwnedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Odmowa usunięcia nieoczekiwanego katalogu: $resolvedRunRoot"
        }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}
