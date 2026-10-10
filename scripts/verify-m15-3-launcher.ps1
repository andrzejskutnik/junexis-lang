[CmdletBinding()]
param([string] $Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$platformRoot = Split-Path $langRoot -Parent
$sdkRoot = Join-Path $platformRoot 'junexis-sdk'
$sdkArtifacts = Join-Path $sdkRoot 'artifacts/storage-sdk'
$gateRoot = Join-Path $langRoot '.m15-3-launcher-gate'
$generated = Join-Path $gateRoot 'generated'
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$cli = Join-Path $langRoot "compiler/JunexisLang.Compiler.CLI/bin/$Configuration/net10.0/JunexisLang.Compiler.CLI.dll"

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Remove-Gate {
    if (-not (Test-Path -LiteralPath $gateRoot)) { return }
    $resolved = (Resolve-Path -LiteralPath $gateRoot).Path
    if (-not $resolved.StartsWith($langRoot + '\.m15-3-launcher-gate', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected M15.3 launcher gate path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$savedAppData = $env:APPDATA
$savedPackages = $env:NUGET_PACKAGES
$savedSdkTool = $env:JUNEXIS_SDK_TOOL
try {
    Remove-Gate
    $env:APPDATA = New-Item -ItemType Directory -Force (Join-Path $gateRoot '.junexis/appdata') |
        Select-Object -ExpandProperty FullName
    $env:NUGET_PACKAGES = New-Item -ItemType Directory -Force (Join-Path $gateRoot '.junexis/packages') |
        Select-Object -ExpandProperty FullName
    $toolPath = Join-Path $gateRoot '.junexis/tools'
    dotnet tool install Junexis.SDK.Tool --tool-path $toolPath --version 0.1.0 `
        --add-source $sdkArtifacts --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "SDK Tool install failed with exit code $LASTEXITCODE." }
    $env:JUNEXIS_SDK_TOOL = Join-Path $toolPath ($IsWindows ? 'junexis-sdk.exe' : 'junexis-sdk')

    dotnet build $cliProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Junexis launcher build failed with exit code $LASTEXITCODE." }
    $responseText = & dotnet $cli sdk new provider Acme.Storage.Launcher `
        --output $generated --sdk-source $sdkArtifacts --json
    if ($LASTEXITCODE -ne 0) { throw "Junexis SDK delegation failed with exit code $LASTEXITCODE." }
    $response = $responseText | ConvertFrom-Json
    Assert-True ($response.providerId -eq 'Acme.Storage.Launcher') 'Launcher returned the wrong provider ID.'
    Assert-True (Test-Path -LiteralPath (Join-Path $generated 'src/Acme.Storage.Launcher/LauncherStorageProvider.cs')) `
        'Launcher did not materialize the provider template.'
}
finally {
    $env:APPDATA = $savedAppData
    $env:NUGET_PACKAGES = $savedPackages
    $env:JUNEXIS_SDK_TOOL = $savedSdkTool
    Remove-Gate
}

Write-Output 'M15.3 LAUNCHER VERIFIED: junexis sdk new provider delegates to the packaged SDK Tool.'
