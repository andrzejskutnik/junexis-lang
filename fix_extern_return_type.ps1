param()
$ErrorActionPreference = 'Stop'
$file = 'compiler\JunexisLang.Compiler.CodeGen\GoCodeGenerator.cs'
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
$list = [System.Collections.Generic.List[string]]::new($lines)

# Find "var retGoType = returnType is not null ? TypeMappings.ToGo(returnType)"
$targetLine = -1
for ($i = 0; $i -lt $list.Count; $i++) {
    if ($list[$i] -match 'var retGoType = returnType is not null \? TypeMappings\.ToGo\(returnType\)') {
        $targetLine = $i
        break
    }
}
if ($targetLine -lt 0) { Write-Error "Target line not found!"; exit 1 }
Write-Host "Found target at line $($targetLine + 1): $($list[$targetLine])"

# Replace the two-line retGoType calculation with extern-aware version
# Original lines (0-indexed targetLine and targetLine+1):
#   var retGoType = returnType is not null ? TypeMappings.ToGo(returnType)
#                   : inValuePosition ? "interface{}"
#                   : "";
# We want to insert extern task lookup before the fallback to "interface{}"

# Find closing line of the expression
$endLine = $targetLine
for ($i = $targetLine; $i -lt [Math]::Min($targetLine + 5, $list.Count); $i++) {
    if ($list[$i] -match ': ""') { $endLine = $i; break }
}
Write-Host "Expression ends at line $($endLine + 1): $($list[$endLine])"

# Remove old lines
$removeCount = $endLine - $targetLine + 1
$list.RemoveRange($targetLine, $removeCount)

# Insert new lines with extern task lookup
$newLines = [string[]]@(
    '        // Check if the callee has an explicit extern task declaration with a return type',
    '        if (returnType is null && inValuePosition)',
    '        {',
    '            var externDecl = ir.ExternTasks.FirstOrDefault(e => e.Name == name);',
    '            if (externDecl?.OutputType is not null)',
    '                returnType = externDecl.OutputType;',
    '        }',
    '        var retGoType = returnType is not null ? TypeMappings.ToGo(returnType)',
    '                        : inValuePosition ? "interface{}"',
    '                        : "";'
)
$list.InsertRange($targetLine, $newLines)
Write-Host "Inserted extern lookup. Lines now: $($list.Count)"

$allText = $list -join "`r`n"
$outBytes = [System.Text.Encoding]::UTF8.GetBytes($allText)
[System.IO.File]::WriteAllBytes($file, $outBytes)
Write-Host "Done."
