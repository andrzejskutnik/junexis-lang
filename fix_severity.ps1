param()
$ErrorActionPreference = 'Stop'
$file = 'compiler\JunexisLang.Compiler.Core\Semantics\SemanticAnalyzer.cs'
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
$list = [System.Collections.Generic.List[string]]::new($lines)

# Change E0020 from Error to Warning (line 410, 0-indexed 409)
Write-Host "Line 410: $($list[409])"
$list[409] = $list[409] -replace 'DiagnosticLevel.Error', 'DiagnosticLevel.Warning'
Write-Host "After: $($list[409])"

# Change E0022 from Error to Warning  
for ($i = 0; $i -lt $list.Count; $i++) {
    if ($list[$i] -match '"E0022".*DiagnosticLevel.Error') {
        $list[$i] = $list[$i] -replace 'DiagnosticLevel.Error', 'DiagnosticLevel.Warning'
        Write-Host "Changed E0022 at line $($i+1)"
    }
}

$allText = $list -join "`r`n"
$outBytes = [System.Text.Encoding]::UTF8.GetBytes($allText)
[System.IO.File]::WriteAllBytes($file, $outBytes)
Write-Host "Done."
