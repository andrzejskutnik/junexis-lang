[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$LspPath,
    [ValidateRange(1, 100)]
    [int]$Iterations = 5,
    [ValidateRange(0, 20)]
    [int]$Warmup = 1,
    [ValidateRange(1, 120)]
    [int]$TimeoutSeconds = 15,
    [ValidateRange(0, 60000)]
    [int]$BudgetMs = 0
)

$ErrorActionPreference = 'Stop'
$resolvedLsp = (Resolve-Path -LiteralPath $LspPath).Path
$timeoutMs = $TimeoutSeconds * 1000

function Wait-Task {
    param(
        [Parameter(Mandatory)]
        [System.Threading.Tasks.Task]$Task,
        [Parameter(Mandatory)]
        [string]$Operation
    )

    if (-not $Task.Wait($timeoutMs)) {
        throw "Timed out while waiting for $Operation after $TimeoutSeconds seconds."
    }

    return $Task.GetAwaiter().GetResult()
}

function Measure-Initialize {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    if ([System.IO.Path]::GetExtension($resolvedLsp) -ieq '.dll') {
        $startInfo.FileName = 'dotnet'
        $startInfo.ArgumentList.Add($resolvedLsp)
    } else {
        $startInfo.FileName = $resolvedLsp
    }

    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        if (-not $process.Start()) {
            throw "Could not start LspHost: $resolvedLsp"
        }

        $payload = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":null,"rootUri":null,"capabilities":{}}}'
        $contentLength = [System.Text.Encoding]::UTF8.GetByteCount($payload)
        $process.StandardInput.Write("Content-Length: $contentLength`r`n`r`n$payload")
        $process.StandardInput.Flush()

        $responseLength = -1
        while ($true) {
            $lineTask = $process.StandardOutput.ReadLineAsync()
            $line = Wait-Task -Task $lineTask -Operation 'the LSP response header'
            if ($null -eq $line) {
                $stderr = $process.StandardError.ReadToEnd()
                throw "LspHost closed stdout before initialize response. stderr: $stderr"
            }
            if ($line.Length -eq 0) {
                break
            }
            if ($line -match '^Content-Length:\s*(\d+)$') {
                $responseLength = [int]$Matches[1]
            }
        }

        if ($responseLength -lt 0) {
            throw 'LspHost initialize response did not contain Content-Length.'
        }

        $buffer = [char[]]::new($responseLength)
        $read = 0
        while ($read -lt $responseLength) {
            $readTask = $process.StandardOutput.ReadAsync($buffer, $read, $responseLength - $read)
            $count = Wait-Task -Task $readTask -Operation 'the LSP initialize response body'
            if ($count -le 0) {
                throw 'LspHost closed stdout during initialize response.'
            }
            $read += $count
        }

        $stopwatch.Stop()
        $response = -join $buffer
        $message = $response | ConvertFrom-Json
        if ($message.id -ne 1 -or $null -eq $message.result) {
            throw "Unexpected initialize response: $response"
        }

        return $stopwatch.Elapsed.TotalMilliseconds
    } finally {
        $stopwatch.Stop()
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

for ($index = 0; $index -lt $Warmup; $index++) {
    [void](Measure-Initialize)
}

$samples = @()
for ($index = 0; $index -lt $Iterations; $index++) {
    $duration = Measure-Initialize
    $samples += $duration
    Write-Host ("Run {0}/{1}: {2:N1} ms" -f ($index + 1), $Iterations, $duration)
}

$sorted = @($samples | Sort-Object)
$median = if ($sorted.Count % 2 -eq 1) {
    $sorted[[int][Math]::Floor($sorted.Count / 2)]
} else {
    ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
}
$p95Index = [int][Math]::Ceiling($sorted.Count * 0.95) - 1
$result = [pscustomobject]@{
    lspPath = $resolvedLsp
    iterations = $Iterations
    warmup = $Warmup
    medianMs = [Math]::Round($median, 1)
    p95Ms = [Math]::Round($sorted[$p95Index], 1)
    minMs = [Math]::Round($sorted[0], 1)
    maxMs = [Math]::Round($sorted[-1], 1)
    samplesMs = @($samples | ForEach-Object { [Math]::Round($_, 1) })
}

$result | ConvertTo-Json -Depth 3

if ($BudgetMs -gt 0 -and $median -gt $BudgetMs) {
    throw ("LspHost startup median {0:N1} ms exceeds budget {1} ms." -f $median, $BudgetMs)
}
