[CmdletBinding()]
param([string] $Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$platformRoot = Split-Path $langRoot -Parent
$sdkArtifacts = Join-Path $platformRoot 'junexis-sdk/artifacts/storage-sdk'
$gateRoot = Join-Path $langRoot '.m15-5-launcher-gate'
$providerRoot = Join-Path $gateRoot 'provider'
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$cli = Join-Path $langRoot "compiler/JunexisLang.Compiler.CLI/bin/$Configuration/net10.0/JunexisLang.Compiler.CLI.dll"

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Remove-Gate {
    if (-not (Test-Path -LiteralPath $gateRoot)) { return }
    $resolved = (Resolve-Path -LiteralPath $gateRoot).Path
    if (-not $resolved.StartsWith($langRoot + '\.m15-5-launcher-gate', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected M15.5 launcher gate path: $resolved"
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
    & dotnet $cli sdk new provider Acme.Storage.LauncherValidation `
        --output $providerRoot --sdk-source $sdkArtifacts --json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Provider generation through launcher failed with exit code $LASTEXITCODE." }
    $resultText = & dotnet $cli sdk validate provider $providerRoot --json
    if ($LASTEXITCODE -ne 0) { throw "Provider validation through launcher failed with exit code $LASTEXITCODE." }
    $result = $resultText | ConvertFrom-Json
    Assert-True ($result.status -eq 'valid' -and $result.summary.errors -eq 0) `
        'Launcher did not return a valid provider manifest result.'
}
finally {
    $env:APPDATA = $savedAppData
    $env:NUGET_PACKAGES = $savedPackages
    $env:JUNEXIS_SDK_TOOL = $savedSdkTool
    Remove-Gate
}

Write-Output 'M15.5 LAUNCHER VERIFIED: junexis sdk validate provider delegates to strict manifest validation.'
