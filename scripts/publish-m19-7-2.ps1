[CmdletBinding()]
param(
    [string] $PlatformVersion = '0.19.0-preview.1',
    [string] $GoExecutable = 'go',
    [string] $JunexisExecutable,
    [string] $ProjectSource,
    [string] $OutputRoot,
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
$output = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $repositoryRoot ".artifacts/m19-7-2/$PlatformVersion"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
$temporaryGoCache = Join-Path ([IO.Path]::GetTempPath()) "junexis-m19-7-2-go-$([Guid]::NewGuid().ToString('N'))"
$temporaryProjectRoot = Join-Path ([IO.Path]::GetTempPath()) "junexis-m19-7-2-project-$([Guid]::NewGuid().ToString('N'))"
$project = Join-Path $temporaryProjectRoot 'smart-home-lab'
$previousGoCache = $env:GOCACHE

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

if (-not $NoBuild) {
    & dotnet build $compilerProject --no-restore -v q
    if ($LASTEXITCODE -ne 0) { throw 'Nie udało się zbudować CLI dla publikacji M19.7.2.' }
}
if ([string]::IsNullOrWhiteSpace($JunexisExecutable)) {
    Assert-Condition (Test-Path -LiteralPath $compilerAssembly -PathType Leaf) 'Brakuje zbudowanego CLI Junexis.'
    $junexisCommand = 'dotnet'
    $junexisPrefix = @($compilerAssembly)
} else {
    $junexisCommand = [IO.Path]::GetFullPath($JunexisExecutable)
    Assert-Condition (Test-Path -LiteralPath $junexisCommand -PathType Leaf) 'Brakuje publicznego programu junexis.'
    $junexisPrefix = @()
}

try {
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    New-Item -ItemType Directory -Path $temporaryGoCache -Force | Out-Null
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    foreach ($file in @('junexis.project.toml', 'laboratory.json', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $projectSource $file) -Destination (Join-Path $project $file)
    }
    foreach ($directory in @('src', 'user', 'verification')) {
        Copy-Item -LiteralPath (Join-Path $projectSource $directory) -Destination $project -Recurse
    }
    $env:GOCACHE = $temporaryGoCache
    $variants = @(
        [ordered]@{ rid = 'win-x64'; entrypoint = 'bin/junexis-laboratory.exe' },
        [ordered]@{ rid = 'linux-x64'; entrypoint = 'bin/junexis-laboratory' },
        [ordered]@{ rid = 'osx-arm64'; entrypoint = 'bin/junexis-laboratory' }
    )
    $published = @()

    foreach ($variant in $variants) {
        $archiveName = "smart-home-preview-1-$($variant.rid).junexis-lab.zip"
        $archive = Join-Path $output $archiveName
        & $junexisCommand @junexisPrefix lab package $project --runtime $variant.rid --go-exe $GoExecutable --out $archive
        if ($LASTEXITCODE -ne 0) { throw "Nie udało się utworzyć paczki Laboratorium dla $($variant.rid)." }

        $verificationText = (& $junexisCommand @junexisPrefix lab verify $archive --json | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Paczka $($variant.rid) nie przeszła sprawdzenia offline." }
        $verification = $verificationText | ConvertFrom-Json
        Assert-Condition ($verification.status -ceq 'ok') "Paczka $($variant.rid) nie ma statusu ok."
        Assert-Condition ($verification.runtimeIdentifier -ceq $variant.rid) "Manifest paczki ma niewłaściwy RID."

        $published += [ordered]@{
            runtimeIdentifier = $variant.rid
            archive = $archiveName
            sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            bytes = (Get-Item -LiteralPath $archive).Length
            entrypoint = $variant.entrypoint
            buildId = $verification.buildId
            bundleId = $verification.bundleId
            verifiedFiles = $verification.verifiedFileCount
            proof = 'cross-package-and-contract'
        }
    }

    $receipt = [ordered]@{
        schemaVersion = 'junexis.laboratory-matrix-release/0.1'
        platformVersion = $PlatformVersion
        laboratoryId = 'smart-home'
        packageContract = 'junexis.laboratory-package/0.1'
        sourceRelocated = $true
        cliExecution = if ([string]::IsNullOrWhiteSpace($JunexisExecutable)) { 'repository-build' } else { 'standalone-package' }
        variants = $published
        nativeExecutionEvidence = 'required-separately-on-each-runtime'
    }
    $receiptPath = Join-Path $output 'junexis.laboratory-matrix-release.json'
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
    Write-Output "M19.7.2: utworzono i sprawdzono trzy paczki w $output"
}
finally {
    $env:GOCACHE = $previousGoCache
    if (Test-Path -LiteralPath $temporaryGoCache) {
        $resolved = (Resolve-Path -LiteralPath $temporaryGoCache).Path
        $systemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
        Assert-Condition ($resolved.StartsWith(
            $systemTemporary + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) 'Odmowa usunięcia nieoczekiwanego katalogu cache Go.'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    if (Test-Path -LiteralPath $temporaryProjectRoot) {
        $resolved = (Resolve-Path -LiteralPath $temporaryProjectRoot).Path
        $systemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
        Assert-Condition ($resolved.StartsWith(
            $systemTemporary + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) 'Odmowa usunięcia nieoczekiwanej kopii projektu.'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$global:LASTEXITCODE = 0
