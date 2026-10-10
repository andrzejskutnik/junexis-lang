[CmdletBinding()]
param(
    [string] $PlatformVersion = '0.19.0-preview.1',
    [string] $GoExecutable = 'go',
    [string] $CompilerExecutable,
    [string] $ProjectSource,
    [string] $EvidenceDirectory,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectSource = if ([string]::IsNullOrWhiteSpace($ProjectSource)) {
    Join-Path $repositoryRoot 'examples/projects/10-smart-home-lab'
} else {
    [IO.Path]::GetFullPath($ProjectSource)
}
$compilerProject = Join-Path $repositoryRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$compilerAssembly = Join-Path $repositoryRoot 'compiler/JunexisLang.Compiler.CLI/bin/Debug/net10.0/JunexisLang.Compiler.CLI.dll'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "junexis-m19-7-1-$([Guid]::NewGuid().ToString('N'))"
$project = Join-Path $temporaryRoot 'przeniesiony-projekt'
$server = $null
$previousGoOs = $env:GOOS
$previousGoArch = $env:GOARCH
$previousCgo = $env:CGO_ENABLED
$previousGoCache = $env:GOCACHE

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-NativeRid {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    if ($IsWindows -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) { return 'win-x64' }
    if ($IsLinux -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) { return 'linux-x64' }
    if ($IsMacOS -and $architecture -eq [Runtime.InteropServices.Architecture]::Arm64) { return 'osx-arm64' }
    return $null
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

if (-not $NoBuild) {
    & dotnet build $compilerProject --no-restore -v q
    if ($LASTEXITCODE -ne 0) { throw 'Nie udało się zbudować CLI dla próby M19.7.1.' }
}
$nativeRid = Get-NativeRid
Assert-Condition ($null -ne $nativeRid) 'Bieżący system nie należy do macierzy M19.'
if ([string]::IsNullOrWhiteSpace($CompilerExecutable)) {
    Assert-Condition (Test-Path -LiteralPath $compilerAssembly -PathType Leaf) 'Brakuje zbudowanego CLI Junexis.'
    $compilerCommand = 'dotnet'
    $compilerPrefix = @($compilerAssembly)
} else {
    $compilerCommand = [IO.Path]::GetFullPath($CompilerExecutable)
    Assert-Condition (Test-Path -LiteralPath $compilerCommand -PathType Leaf) 'Brakuje samodzielnego Kompilatora Junexis.'
    $compilerPrefix = @()
}

try {
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    foreach ($file in @('junexis.project.toml', 'laboratory.json', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $projectSource $file) -Destination (Join-Path $project $file)
    }
    foreach ($directory in @('src', 'user', 'verification')) {
        Copy-Item -LiteralPath (Join-Path $projectSource $directory) -Destination $project -Recurse
    }

    $definition = Get-Content -LiteralPath (Join-Path $project 'laboratory.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($definition.execution.mode -ceq 'simulation') 'Laboratorium nie deklaruje jawnego trybu simulation.'
    Assert-Condition ($definition.execution.deterministic -eq $true) 'Scenariusz symulacji nie jest deterministyczny.'
    Assert-Condition ($definition.execution.hardwareFallback -eq $false) 'Laboratorium dopuszcza ukryty powrót do sprzętu.'
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$definition.execution.scenarioSeed)) `
        'Laboratorium nie przypina identyfikatora scenariusza.'
    Assert-Condition ($definition.hardwareRequired -eq $false) 'Przenośny demonstrator nie może wymagać sprzętu.'

    Push-Location $project
    try {
        & $compilerCommand @compilerPrefix check
        if ($LASTEXITCODE -ne 0) { throw 'Przeniesiony projekt nie przeszedł junexis check.' }
        & $compilerCommand @compilerPrefix analyze --target server
        if ($LASTEXITCODE -ne 0) { throw 'Przeniesiony projekt nie przeszedł junexis analyze.' }
        & $compilerCommand @compilerPrefix build --target server
        if ($LASTEXITCODE -ne 0) { throw 'Przeniesiony projekt nie przeszedł junexis build.' }
    }
    finally {
        Pop-Location
    }

    $serverSource = Join-Path $project 'out/server'
    $env:GOCACHE = Join-Path $temporaryRoot 'go-cache'
    $builds = @(
        [ordered]@{ rid = 'win-x64'; goos = 'windows'; goarch = 'amd64'; file = 'smart-home.exe' },
        [ordered]@{ rid = 'linux-x64'; goos = 'linux'; goarch = 'amd64'; file = 'smart-home' },
        [ordered]@{ rid = 'osx-arm64'; goos = 'darwin'; goarch = 'arm64'; file = 'smart-home' }
    )
    Push-Location $serverSource
    try {
        foreach ($variant in $builds) {
            $variantRoot = Join-Path $temporaryRoot $variant.rid
            New-Item -ItemType Directory -Path $variantRoot -Force | Out-Null
            $env:GOOS = $variant.goos
            $env:GOARCH = $variant.goarch
            $env:CGO_ENABLED = '0'
            & $GoExecutable build -trimpath -o (Join-Path $variantRoot $variant.file) .
            if ($LASTEXITCODE -ne 0) { throw "Budowanie demonstratora dla $($variant.rid) nie powiodło się." }
        }
    }
    finally {
        Pop-Location
    }

    $httpPort = Get-FreePort
    $healthPort = Get-FreePort
    $tcpPort = Get-FreePort
    Assert-Condition (@(@($httpPort, $healthPort, $tcpPort) | Select-Object -Unique).Count -eq 3) `
        'Nie udało się przydzielić trzech różnych portów próby.'
    $nativeVariant = @($builds | Where-Object rid -ceq $nativeRid)[0]
    $nativeExecutable = Join-Path $temporaryRoot "$nativeRid/$($nativeVariant.file)"
    $start = [Diagnostics.ProcessStartInfo]::new($nativeExecutable)
    $start.WorkingDirectory = Split-Path $nativeExecutable -Parent
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @(
        '--server-mode', 'persistent',
        '--http-port', [string]$httpPort,
        '--health-port', [string]$healthPort,
        '--tcp-port', [string]$tcpPort)) {
        $start.ArgumentList.Add($argument)
    }
    $start.Environment['JUNEXIS_LAB_STATE_DIR'] = (Join-Path $temporaryRoot 'state')
    $server = [Diagnostics.Process]::Start($start)
    Assert-Condition ($null -ne $server) "Nie udało się uruchomić demonstratora $nativeRid."

    $state = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($server.HasExited) { throw "Demonstrator zakończył się przed gotowością: $($server.ExitCode)." }
        try {
            $candidate = Invoke-RestMethod -Uri "http://127.0.0.1:$httpPort/api/laboratory/state" -TimeoutSec 2
            if ($candidate.execution_mode -ceq 'simulation' -and $candidate.home_started) {
                $state = $candidate
                break
            }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.Net.WebException] { }
        Start-Sleep -Milliseconds 200
    }
    Assert-Condition ($null -ne $state) 'Demonstrator nie osiągnął działającego stanu symulacji.'
    Assert-Condition ($state.hardware_connected -eq $false) 'Symulacja zgłosiła nieistniejące połączenie ze sprzętem.'
    Assert-Condition (@($state.rooms).Count -eq 3) 'Demonstrator nie uruchomił trzech pomieszczeń.'
    $scenarioOutput = & (Join-Path $project 'verification/verify-running-laboratory.ps1') `
        -BaseUrl "http://127.0.0.1:$httpPort" -TimeoutSeconds 15
    if ($LASTEXITCODE -ne 0) { throw 'Pełny scenariusz demonstratora zakończył się błędem.' }
    $scenarioProof = $scenarioOutput | ConvertFrom-Json
    Assert-Condition ($scenarioProof.result -ceq 'PASS') 'Pełny scenariusz demonstratora nie zwrócił PASS.'
    Assert-Condition ($scenarioProof.execution_mode -ceq 'simulation' `
        -and $scenarioProof.hardware_connected -eq $false) 'Scenariusz utracił jawną granicę symulacji.'

    $server.Kill($true)
    $server.WaitForExit()
    $server.Dispose()
    $server = $null
    $restartPorts = @()
    while ($restartPorts.Count -lt 3) {
        $candidatePort = Get-FreePort
        if ($candidatePort -notin $restartPorts) { $restartPorts += $candidatePort }
    }
    $restartOutput = & (Join-Path $project 'verification/verify-restart-recovery.ps1') `
        -Executable $nativeExecutable `
        -HttpPort $restartPorts[0] `
        -HealthPort $restartPorts[1] `
        -TcpPort $restartPorts[2] `
        -TimeoutSeconds 15
    if ($LASTEXITCODE -ne 0) { throw 'Próba trwałego stanu po restarcie zakończyła się błędem.' }
    $restartProof = $restartOutput | ConvertFrom-Json
    Assert-Condition ($restartProof.result -ceq 'PASS' `
        -and $restartProof.execution_mode -ceq 'simulation' `
        -and $restartProof.hardware_connected -eq $false) `
        'Restart procesu utracił jawną granicę symulacji.'

    $bundle = Get-Content -LiteralPath (Join-Path $project 'out/junexis.bundle.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $output = Join-Path $repositoryRoot ".artifacts/m19-7-1/$PlatformVersion/$nativeRid"
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $receipt = [ordered]@{
        schemaVersion = 'junexis.portable-demonstrator-trial/0.1'
        platformVersion = $PlatformVersion
        project = 'smart-home-laboratory'
        sourceRelocated = $true
        buildId = $bundle.buildId
        execution = [ordered]@{
            mode = $state.execution_mode
            deterministic = $true
            hardwareConnected = $state.hardware_connected
            hardwareFallback = $false
            rooms = @($state.rooms).Count
            scenario = 'full-smart-home-and-restart-pass'
        }
        variants = @($builds | ForEach-Object {
            [ordered]@{ rid = $_.rid; result = if ($_.rid -ceq $nativeRid) { 'native-simulation' } else { 'cross-build' } }
        })
    }
    $receiptJson = $receipt | ConvertTo-Json -Depth 8
    $receiptJson | Set-Content -LiteralPath (Join-Path $output 'junexis.portable-demonstrator-trial.json') -Encoding utf8NoBOM
    if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        $resolvedEvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
        New-Item -ItemType Directory -Path $resolvedEvidenceDirectory -Force | Out-Null
        $receiptJson | Set-Content -LiteralPath (Join-Path $resolvedEvidenceDirectory "$nativeRid.json") -Encoding utf8NoBOM
    }

    Write-Output 'M19.7.1 ZALICZONO: przeniesiony Dom działa jawnie w symulacji i buduje się dla całej macierzy.'
}
finally {
    if ($null -ne $server) {
        if (-not $server.HasExited) {
            $server.Kill($true)
            $server.WaitForExit()
        }
        $server.Dispose()
    }
    $env:GOOS = $previousGoOs
    $env:GOARCH = $previousGoArch
    $env:CGO_ENABLED = $previousCgo
    $env:GOCACHE = $previousGoCache
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolved = (Resolve-Path -LiteralPath $temporaryRoot).Path
        $systemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
        Assert-Condition ($resolved.StartsWith(
            $systemTemporary + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) 'Odmowa usunięcia nieoczekiwanego katalogu próby.'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$global:LASTEXITCODE = 0
