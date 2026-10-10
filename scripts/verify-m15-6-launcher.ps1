[CmdletBinding()]
param([string] $Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$platformRoot = Split-Path $langRoot -Parent
$sdkArtifacts = Join-Path $platformRoot 'junexis-sdk/artifacts/storage-sdk'
$gateRoot = Join-Path $langRoot '.m15-6-launcher-gate'
$providerRoot = Join-Path $gateRoot 'provider'
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$cli = Join-Path $langRoot "compiler/JunexisLang.Compiler.CLI/bin/$Configuration/net10.0/JunexisLang.Compiler.CLI.dll"

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Remove-Gate {
    if (-not (Test-Path -LiteralPath $gateRoot)) { return }
    $resolved = (Resolve-Path -LiteralPath $gateRoot).Path
    if (-not $resolved.StartsWith($langRoot + '\.m15-6-launcher-gate', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected M15.6 launcher gate path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$savedAppData = $env:APPDATA
$savedPackages = $env:NUGET_PACKAGES
$savedSdkTool = $env:JUNEXIS_SDK_TOOL
try {
    Remove-Gate
    $env:APPDATA = New-Item -ItemType Directory -Force (Join-Path $gateRoot 'appdata') |
        Select-Object -ExpandProperty FullName
    $env:NUGET_PACKAGES = New-Item -ItemType Directory -Force (Join-Path $gateRoot 'packages') |
        Select-Object -ExpandProperty FullName
    $toolPath = Join-Path $gateRoot 'tools'
    dotnet tool install Junexis.SDK.Tool --tool-path $toolPath --version 0.1.0 `
        --add-source $sdkArtifacts --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "SDK Tool install failed with exit code $LASTEXITCODE." }
    $env:JUNEXIS_SDK_TOOL = Join-Path $toolPath ($IsWindows ? 'junexis-sdk.exe' : 'junexis-sdk')

    dotnet build $cliProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Junexis launcher build failed with exit code $LASTEXITCODE." }
    & dotnet $cli sdk new provider Acme.Storage.LauncherCompatibility `
        --output $providerRoot --sdk-source $sdkArtifacts --json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Provider generation through launcher failed.' }
    $result = & dotnet $cli sdk compatibility provider $providerRoot `
        --runtime-version 0.1.0 --rid win-x64 --require read --require write --json | ConvertFrom-Json
    Assert-True ($LASTEXITCODE -eq 0 -and $result.status -eq 'compatible') `
        'Launcher did not return a compatible provider result.'
}
finally {
    $env:APPDATA = $savedAppData
    $env:NUGET_PACKAGES = $savedPackages
    $env:JUNEXIS_SDK_TOOL = $savedSdkTool
    Remove-Gate
}

Write-Output 'M15.6 LAUNCHER VERIFIED: junexis sdk compatibility provider delegates to SDK resolver.'
