[CmdletBinding()]
param([string] $Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = Join-Path $root '.m15-7-launcher-gate'
$stub = Join-Path $gate 'stub'
$provider = Join-Path $gate 'provider'
$capture = Join-Path $gate 'arguments.json'
$cliProject = Join-Path $root 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$cli = Join-Path $root "compiler/JunexisLang.Compiler.CLI/bin/$Configuration/net10.0/JunexisLang.Compiler.CLI.dll"

function Remove-Gate {
    if (-not (Test-Path -LiteralPath $gate)) { return }
    $resolved = (Resolve-Path -LiteralPath $gate).Path
    if (-not $resolved.StartsWith($root + '\.m15-7-launcher-gate', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected M15.7 launcher gate path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$savedTool = $env:JUNEXIS_SDK_TOOL
$savedCapture = $env:JUNEXIS_SDK_CAPTURE
$savedAppData = $env:APPDATA
$savedPackages = $env:NUGET_PACKAGES
try {
    Remove-Gate
    New-Item -ItemType Directory -Force $stub, $provider | Out-Null
    $env:APPDATA = New-Item -ItemType Directory -Force (Join-Path $gate 'appdata') | Select-Object -ExpandProperty FullName
    $env:NUGET_PACKAGES = New-Item -ItemType Directory -Force (Join-Path $gate 'packages') | Select-Object -ExpandProperty FullName
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@ | Set-Content (Join-Path $stub 'Stub.csproj')
    @'
using System.Text.Json;
var path = System.Environment.GetEnvironmentVariable("JUNEXIS_SDK_CAPTURE")!;
System.IO.File.WriteAllText(path, JsonSerializer.Serialize(args));
return 0;
'@ | Set-Content (Join-Path $stub 'Program.cs')
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /></packageSources></configuration>
'@ | Set-Content (Join-Path $gate 'NuGet.Config')
    dotnet restore (Join-Path $stub 'Stub.csproj') --configfile (Join-Path $gate 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'SDK delegation stub restore failed.' }
    dotnet build (Join-Path $stub 'Stub.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'SDK delegation stub build failed.' }
    dotnet build $cliProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Junexis CLI build failed.' }
    $env:JUNEXIS_SDK_TOOL = Join-Path $stub 'bin/Release/net10.0/Stub.exe'
    $env:JUNEXIS_SDK_CAPTURE = $capture
    & dotnet $cli sdk test provider $provider --configuration Debug --profile storage-plans --no-build --offline --json
    if ($LASTEXITCODE -ne 0) { throw 'CLI delegation failed.' }
    $arguments = Get-Content -Raw $capture | ConvertFrom-Json
    $expected = @('sdk','test','provider',(Resolve-Path $provider).Path,'--configuration','Debug','--no-build','--offline','--profile','storage-plans','--json')
    if (($arguments -join "`n") -ne ($expected -join "`n")) {
        throw "Unexpected delegated arguments: $($arguments -join ' ')"
    }
}
finally {
    $env:JUNEXIS_SDK_TOOL = $savedTool
    $env:JUNEXIS_SDK_CAPTURE = $savedCapture
    $env:APPDATA = $savedAppData
    $env:NUGET_PACKAGES = $savedPackages
    Remove-Gate
}

Write-Output 'M15.7 LAUNCHER VERIFIED: --profile storage-plans reaches the SDK Tool unchanged.'
